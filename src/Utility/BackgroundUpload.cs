using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chronofoil.Capture;
using Chronofoil.CaptureFile.Censor;
using Chronofoil.CaptureFile.Generated;
using Chronofoil.Censor;
using Chronofoil.Common.Capture;
using Chronofoil.Web.Upload;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using Thread = System.Threading.Thread;

namespace Chronofoil.Utility;

public class BackgroundUpload
{
    private enum UploadStep
    {
        Prepare,
        Censoring,
        Progress,
        Done,
        Error,
    }
    
    private string _errorText;
    private UploadStep _step;

    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly UploadService _uploadService;
    private readonly OpcodeService _opcodeService;
    private readonly CaptureManager _captureManager;
    private readonly INotificationManager _notificationManager;
    
    private Guid _captureId = Guid.Empty;
    private string? _gameVersion = "";
    private DateTime _now;
    private ProgressHolder _holder;

    private Notification _notification;
    private IActiveNotification _activeNotification;

    public BackgroundUpload(
        IPluginLog log, 
        Configuration config,
        UploadService uploadService,
        OpcodeService opcodeService,
        CaptureManager captureManager,
        INotificationManager notificationManager)
    {
        _log = log;
        _config = config;
        _uploadService = uploadService;
        _opcodeService = opcodeService;
        _captureManager = captureManager;
        _notificationManager = notificationManager;
    }

    public bool Upload(Guid captureIdToUpload)
    {
        _captureId = captureIdToUpload;
        _now = DateTime.UtcNow;

        _notification = new Notification
        {
            Type = NotificationType.Info,
            Title = $"Uploading {_captureId}",
            Content = "",
            InitialDuration = TimeSpan.FromMinutes(5),
        };
        
        _activeNotification = _notificationManager.AddNotification(_notification);
        SetStep(UploadStep.Prepare);
        
        _holder = new ProgressHolder();

        var progressTask = Task.Run(() =>
        {
            while (true)
            {
                if (_step is UploadStep.Done or UploadStep.Error) return;
                Thread.Sleep(100);
                _activeNotification.Progress = _holder.GetPercent();
            }
        });

        var result = false;
        _gameVersion = _uploadService.GetCaptureGameVersion(_captureId);
        if (_gameVersion == null) throw new Exception("Failed to read game version");

        try
        {
            _opcodeService.UpdateIfNeeded(_gameVersion);
        }
        catch (Exception e)
        {
            // Just log this - there is a disclaimer on the quick upload setting
            _log.Error(e, "Failed to update opcodes. Continuing with quick upload anyways...");   
        }
        
        SetStep(UploadStep.Censoring);
        try
        {
            _uploadService.CensorCapture(_captureId, CreateCensorTargets());
        }
        catch (Exception e)
        {
            Error($"Failed to censor capture {_captureId}", e);
            return result;
        }
        
        var uploadRequest = new CaptureUploadRequest
        {
            CaptureId = _captureId,
            MetricTime = Util.GetTime(_now, _config.MetricsTimeValue, _config.MetricsTimeSpan),
            MetricWhenEos = _config.MetricsWhenEos,
            PublicTime = Util.GetTime(_now, _config.PublicTimeValue, _config.PublicTimeSpan),
            PublicWhenEos = _config.PublicWhenEos
        };

        SetStep(UploadStep.Progress);

        try
        {
            _uploadService.UploadFile(_captureId, uploadRequest, _holder);
        }
        catch (Exception e)
        {
            Error($"Failed to upload capture {_captureId}", e);
            return result;
        }
        
        SetStep(UploadStep.Done);
        _activeNotification.Type = NotificationType.Success;
        _activeNotification.Content = $"Successfully uploaded capture {_captureId}.";
        _activeNotification.HardExpiry = DateTime.Now.Add(TimeSpan.FromSeconds(15));

        _captureManager.SetUploaded(_captureId, true);
        result = true;
        
        return result;
    }

    private void SetStep(UploadStep step)
    {
        _step = step;
        _activeNotification.Content = step switch
        {
            UploadStep.Prepare => "Preparing upload...",
            UploadStep.Censoring => "Censoring upload...",
            UploadStep.Progress => "Uploading...",
            UploadStep.Done => $"Successfully uploaded {_captureId}.",
            _ => "step text"
        };
    }
    
    private List<CensorTarget> CreateCensorTargets()
    {
        var opcodes = _opcodeService.GetCensoredOpcodes(_gameVersion);
        var toCensor = new List<CensorTarget>();
        foreach (var opcode in opcodes)
        {
            var protocol = Protocol.Zone;
            var direction = opcode.Key.EndsWith("Down") ? Direction.Rx : Direction.Tx;
            toCensor.Add(new CensorTarget { Protocol = protocol, Direction = direction, Opcode = opcode.Value });
        }

        return toCensor;
    }

    private void Error(string errorText, Exception? e)
    {
        _activeNotification.Type = NotificationType.Error;
        _activeNotification.Content = $"Failed to upload capture {_captureId}. Please see the log for more information.";

        if (e != null)
        {
            _log.Error(e, $"[BackgroundUpload] {errorText}");
        }
        else
        {
            _log.Error($"[BackgroundUpload] {errorText}");
        }
    }
}
