using System;
using System.IO;
using System.Text;
using Chronofoil.Common;
using Chronofoil.Common.Auth;
using Chronofoil.Common.Censor;
using Chronofoil.Common.Info;
using Chronofoil.Common.Capture;
using Chronofoil.Utility;
using Dalamud.Plugin.Services;
using Refit;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Chronofoil.Web;

public class ChronofoilClient
{
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly IChronofoilClient _client;

    public ChronofoilClient(IPluginLog log, Configuration config, IChronofoilClient client)
    {
        _log = log;
        _config = config;
        _client = client;
    }
    
    public bool TryRegister(string authCode, out AccessTokenResponse? response, out ApiStatusCode? code)
    {
        var request = new AuthRequest { AuthorizationCode = authCode };
        var result = _client.Register("discord", request).Result;
        LogResult("Register", result.StatusCode);
        response = result.Data;
        code = result.StatusCode;
        return result.IsSuccess;
    }

    public bool TryLogin(string authCode, out AccessTokenResponse? response, out ApiStatusCode? code)
    {
        var request = new AuthRequest { AuthorizationCode = authCode };
        var result = _client.Login("discord", request).Result;
        LogResult("Login", result.StatusCode);
        response = result.Data;
        code = result.StatusCode;
        return result.IsSuccess;
    }
    
    public bool TryRefresh(string refreshCode, out AccessTokenResponse? response)
    {
        var request = new RefreshRequest { RefreshToken = refreshCode };
        var result = _client.RefreshToken(request).Result;
        LogResult("RefreshToken", result.StatusCode);
        response = result.Data;
        return result.IsSuccess;
    }

    public TosResponse? GetTos()
    {
        var result = _client.GetTos().Result;
        return result.Data;
    }

    public FaqResponse? GetFaq()
    {
        var result = _client.GetFaq().Result;
        return !result.IsSuccess ? new FaqResponse() : result.Data;
    }
    
    public bool TrySendOpcodes(FoundOpcodesRequest request)
    {
        var result = _client.FoundOpcodes(_config.AccessToken, request).Result;
        LogResult("FoundOpcodes", result.StatusCode);
        return result.IsSuccess;
    }

    public bool TryGetCensoredOpcodes(string gameVersion, out CensoredOpcodesResponse censoredOpcodes)
    {
        censoredOpcodes = new CensoredOpcodesResponse();
        var result = _client.GetOpcodes(_config.AccessToken, gameVersion).Result;
        LogResult("GetOpcodes", result.StatusCode);
        ThrowIfFailed(result.StatusCode);
        censoredOpcodes = result.Data!;
        
        return true;
    }

    public bool TryDeleteCapture(Guid captureId)
    {
        var result = _client.DeleteCapture(_config.AccessToken, captureId).Result;
        LogResult("DeleteCapture", result.StatusCode);
        return result.IsSuccess;
    }
    
    public bool TryUploadCapture(FileInfo captureFile, CaptureUploadRequest request, ProgressHolder progress, out CaptureUploadResponse captureUploadResponse)
    {
        var metaMemory = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));
        var metaPart = new StreamPart(metaMemory, "meta.json", "application/json");

        using var fileStream = captureFile.OpenRead();
        using var progressStream = new ProgressReportingStream(fileStream, progress);
        var capturePart = new StreamPart(
            progressStream,
            $"{request.CaptureId}.ccfcap", 
            "application/octet-stream"); 
        
        var result = _client.UploadCapture(_config.AccessToken, metaPart, capturePart).Result;
        LogResult("UploadCapture", result.StatusCode);
        
        ThrowIfFailed(result.StatusCode);
        captureUploadResponse = result.Data!;
        return true;
    }

    public bool TryAcceptTos(int tosVersion)
    {
        var result = _client.AcceptTos(_config.AccessToken, tosVersion).Result;
        LogResult("AcceptTos", result.StatusCode);
        
        ThrowIfFailed(result.StatusCode);
        return true;
    }
    
    public CaptureListResponse? GetCaptureList()
    {
        var result = _client.GetCaptureList(_config.AccessToken).Result;
        LogResult("GetCaptureList", result.StatusCode);
        
        return !result.IsSuccess ? null : result.Data;
    }

    private void LogResult(string method, ApiStatusCode code)
    {
        _log.Verbose($"[{method}] Response was {code}");
    }

    private static void ThrowIfFailed(ApiStatusCode code)
    {
        if (code != ApiStatusCode.Success)
            throw new Exception($"Response code did not indicate success: {code}");
    }
}