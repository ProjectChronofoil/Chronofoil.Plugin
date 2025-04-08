using System;
using System.Linq;
using System.Threading.Tasks;
using Chronofoil.Capture;
using Chronofoil.Censor;
using Chronofoil.Utility;
using Dalamud.Plugin.Services;

namespace Chronofoil.Web.Upload;

public class AutoUploadService
{
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly INotificationManager _notificationManager;
    private readonly CaptureManager _captureManager;
    private readonly UploadService _uploadService;
    private readonly OpcodeService _opcodeService;

    public AutoUploadService(
        IPluginLog log,
        INotificationManager notificationManager,
        Configuration config,
        CaptureManager captureManager,
        UploadService uploadService,
        OpcodeService opcodeService)
    {
        _log = log;
        _config = config;
        _captureManager = captureManager;
        _notificationManager = notificationManager;
        _uploadService = uploadService;
        _opcodeService = opcodeService;
    }

    public void Begin()
    {
        if (!_config.EnableAutoUpload) return;
        
        Task.Run(PerformUpload);
    }

    private void PerformUpload()
    {
        var captures = _captureManager.CapturesByTime;
        foreach (var captureId in captures)
        {
            if (_captureManager.GetUploaded(captureId)!.Value) continue;
            if (_captureManager.GetIgnored(captureId)!.Value) continue;
            if (_captureManager.GetCapturing(captureId)!.Value) continue;
            
            UploadOne(captureId);
        }
    }

    private void UploadOne(Guid captureId)
    {
        try
        {
            var upload = new BackgroundUpload(_log, _config, _uploadService, _opcodeService, _captureManager,
                _notificationManager);
            upload.Upload(captureId);
        }
        catch (Exception e)
        {
            _log.Error(e, $"[AutoUploadService] Failed to upload capture {captureId}");
        }
    }
}