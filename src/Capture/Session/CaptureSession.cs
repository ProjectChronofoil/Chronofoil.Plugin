using System;
using System.IO;
using Chronofoil.Capture.IO;
using Chronofoil.CaptureFile;
using Chronofoil.CaptureFile.Generated;
using Dalamud.Plugin.Services;

namespace Chronofoil.Capture.Session;

public class CaptureSession
{
	private readonly IPluginLog _log;
	private readonly FileInfo _captureFile;
	private readonly CaptureWriter _writer;
	private readonly CaptureFrame _frame;

	public Guid CaptureId { get; }
	
	public DateTime CaptureBeginTime { get; }
	public DateTime CaptureEndTime { get; set; }

	public CaptureSession(IPluginLog log, Configuration config, VersionInfo versionInfo, Guid id)
	{
		_log = log;
		
		CaptureId = id;
		CaptureBeginTime = DateTime.UtcNow;
		var captureDirectory = new DirectoryInfo(config.StorageDirectory);
		// captureDirectory = captureDirectory.CreateSubdirectory(CaptureId.ToString());
		
		_captureFile = new FileInfo(Path.Combine(captureDirectory.FullName, $"{CaptureId}"));
		_writer = new CaptureWriter(_captureFile.FullName);
		_frame = new CaptureFrame { Header = new CaptureFrameHeader() };
		
		_log.Debug($"[CaptureSession] Capture path is {_captureFile.FullName}");
		
		_writer.WriteVersionInfo(versionInfo);
		_writer.WriteCaptureStart(CaptureId, CaptureBeginTime);
	}

	public void WriteFrame(Protocol proto, Direction direction, ReadOnlySpan<byte> data)
	{
		_writer.AppendCaptureFrame(proto, direction, data);
	}
	
	public void FinalizeSession()
	{
		CaptureEndTime = DateTime.UtcNow;
		_writer.WriteCaptureEnd(CaptureEndTime);
	}
}