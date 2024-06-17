using System;
using System.Reflection;
using Chronofoil.Capture.IO;
using Chronofoil.CaptureFile;
using Chronofoil.CaptureFile.Generated;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;

namespace Chronofoil.Capture.Session;

public class CaptureSessionManager : IDisposable
{
	private readonly VersionInfo _versionInfo;

	private readonly IPluginLog _log;
	private readonly Configuration _config;
	private readonly IClientState _clientState;
	private readonly INotificationManager _notificationManager;

	private readonly CaptureHookManager _hookManager;
	
	public delegate void CaptureSessionStartedDelegate(Guid captureId, DateTime startTime);
	public event CaptureSessionStartedDelegate CaptureSessionStarted;
	
	public delegate void CaptureSessionFinishedDelegate(Guid captureId, DateTime endTime);
	public event CaptureSessionFinishedDelegate? CaptureSessionFinished;

	public CaptureSession? Session { get; private set; }
	public bool IsCapturing { get; private set; }

	public CaptureSessionManager(
		IPluginLog log,
		Configuration config,
		IClientState clientState,
		INotificationManager notificationManager,
		CaptureHookManager hookManager)
	{
		_log = log;
		_config = config;
		_clientState = clientState;
		_notificationManager = notificationManager;

		_hookManager = hookManager;
		_hookManager.NetworkInitialized += OnNetworkInitialized;

		var gamePath = Environment.ProcessPath!;
		var writerId = "Chronofoil";
		var writerVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString();
		_versionInfo = VersionInfoGenerator.Generate(gamePath, writerId, writerVersion);
	}

	public void Dispose()
	{
		End();
	}

	private void Restart()
	{
		_log.Debug("[CaptureSessionManager] Restart!");

		End();
		Begin();
	}

	private void Begin()
	{
		_log.Debug("[CaptureSessionManager] Begin!");
		var guid = Guid.NewGuid();
		Session = new CaptureSession(_log, _config, _versionInfo, guid);
		CaptureSessionStarted?.Invoke(Session.CaptureId, DateTime.UtcNow);
		_hookManager.NetworkEvent += OnNetworkEvent;
		_clientState.Logout += End;
		if (_config is { NotificationsEnabled: true, CaptureBeginNotificationsEnabled: true })
			_notificationManager.AddNotification(new Notification { Content = $"Capture session started: {guid}!" });
		IsCapturing = true;
	}

	private void End()
	{
		_log.Debug("[CaptureSessionManager] End!");
		_hookManager.NetworkEvent -= OnNetworkEvent;
		_clientState.Logout -= End;
		if (Session != null)
		{
			Session.FinalizeSession();
			CaptureSessionFinished?.Invoke(Session.CaptureId, DateTime.UtcNow);
			if (_config is { NotificationsEnabled: true, CaptureBeginNotificationsEnabled: true })
				_notificationManager.AddNotification(new Notification { Content = $"Capture session finalized: {Session.CaptureId}!" });
			Session = null;
		}
		IsCapturing = false;
	}
	
	private void OnNetworkInitialized()
	{
		_log.Debug("[CaptureSessionManager] OnNetworkInitialized!");

		if (IsCapturing)
			Restart();
		else
			Begin();
	}
	
	private void OnNetworkEvent(Protocol proto, Direction direction, ReadOnlySpan<byte> data)
	{
		if (!IsCapturing) return;
		Session.WriteFrame(proto, direction, data);
	}
}