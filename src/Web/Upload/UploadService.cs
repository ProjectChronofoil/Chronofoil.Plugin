using System;
using System.Collections.Generic;
using System.IO;
using Chronofoil.Capture;
using Chronofoil.CaptureFile;
using Chronofoil.CaptureFile.Censor;
using Chronofoil.Common.Capture;
using Chronofoil.Utility;
using Dalamud.Plugin.Services;

namespace Chronofoil.Web.Upload;

public class UploadService
{
    private readonly IPluginLog _log;
    private readonly CaptureManager _captureManager;
    private readonly ChronofoilClient _chronofoilClient;

    public UploadService(IPluginLog log, CaptureManager captureManager, ChronofoilClient chronofoilClient)
    {
        _log = log;
        _captureManager = captureManager;
        _chronofoilClient = chronofoilClient;
    }

    public string? GetCaptureGameVersion(Guid captureId)
    {
        var capturePath = _captureManager.GetCaptureFilePath(captureId);
        if (capturePath == null) return null;

        return new CaptureReader(capturePath).VersionInfo.GameVer[0];
    }

    public void CensorCapture(Guid captureId, List<CensorTarget> targets)
    {
        var capturePath = _captureManager.GetCaptureFilePath(captureId);
        if (capturePath == null) return;

        var redactor = new CaptureRedactor(capturePath, targets);
        redactor.Censor();
    }

    public CaptureUploadResponse? UploadFile(Guid captureId, CaptureUploadRequest request, ProgressHolder holder)
    {
        var capturePath = _captureManager.GetCensoredCaptureFilePath(captureId);
        if (capturePath == null) return null;
        return _chronofoilClient.TryUploadCapture(new FileInfo(capturePath), request, holder, out var response) ? response : null;
    }
}