using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Chronofoil.Common.Auth;
using Chronofoil.Common.Censor;
using Chronofoil.Common.Info;
using Chronofoil.Common.Capture;
using Chronofoil.Utility;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Chronofoil.Web;

public class ChronofoilClient {
    private string _endpoint;

    private string RegisterEndpoint => $"{_endpoint}/register/discord";
    private string LoginEndpoint => $"{_endpoint}/login/discord";
    private string RefreshEndpoint => $"{_endpoint}/token/refresh";
    private string TosAcceptEndpoint => $"{_endpoint}/tos/accept";

    private string UploadEndpoint => $"{_endpoint}/capture/upload";
    private string DeleteEndpoint => $"{_endpoint}/capture/delete";
    private string CaptureListEndpoint => $"{_endpoint}/capture/list";

    private string FoundOpcodeEndpoint => $"{_endpoint}/censor/found";
    private string CensoredOpcodesEndpoint => $"{_endpoint}/censor/opcodes";

    private string TosEndpoint => $"{_endpoint}/info/tos";
    private string FaqEndpoint => $"{_endpoint}/info/faq";

    private static readonly HttpClient Client = new();

    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly JsonSerializerOptions _options;

    public ChronofoilClient(IPluginLog log, Configuration config)
    {
        _log = log;
        _config = config;
        _options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        _endpoint = _config.ApiEndpoint;
    }

    private AccessTokenResponse? HitTokenEndpoint(string endpoint, string code)
    {
        var request = new AuthRequest { AuthorizationCode = code };
        var content = JsonContent.Create(request, null, _options);
        var response = Client.PostAsync(endpoint, content).Result;
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Response code did not indicate success: {response.StatusCode}");
        return JsonConvert.DeserializeObject<AccessTokenResponse>(response.Content.ReadAsStringAsync().Result);
    }

    public bool TryRegister(string authCode, out AccessTokenResponse? response)
    {
        response = HitTokenEndpoint(RegisterEndpoint, authCode);
        return response != null;
    }

    public bool TryLogin(string authCode, out AccessTokenResponse? response)
    {
        response = HitTokenEndpoint(LoginEndpoint, authCode);
        return response != null;
    }

    public bool TryRefresh(string refreshCode, out AccessTokenResponse? response)
    {
        var request = new RefreshRequest { RefreshToken = refreshCode };
        var content = JsonContent.Create(request, null, _options);
        var resp = Client.PostAsync(RefreshEndpoint, content).Result;
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Response code did not indicate success: {resp.StatusCode}");
        response = JsonConvert.DeserializeObject<AccessTokenResponse>(resp.Content.ReadAsStringAsync().Result);
        return response != null;
    }

    public TosResponse GetTos()
    {
        var resp = Client.GetAsync(TosEndpoint).Result;
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Response code did not indicate success: {resp.StatusCode}");
        return JsonConvert.DeserializeObject<TosResponse>(resp.Content.ReadAsStringAsync().Result);
    }

    public FaqResponse GetFaq()
    {
        var resp = Client.GetAsync(FaqEndpoint).Result;
        if (!resp.IsSuccessStatusCode)
        {
            return new FaqResponse(); // not a huge deal
        }

        return JsonConvert.DeserializeObject<FaqResponse>(resp.Content.ReadAsStringAsync().Result);
    }

    public bool TrySendOpcodes(FoundOpcodesRequest request)
    {
        _log.Verbose($"[TrySendOpcodes] Begin");
        var token = _config.AccessToken;

        var message = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri($"{FoundOpcodeEndpoint}"),
            Headers = { { HttpRequestHeader.Authorization.ToString(), $"Bearer {token}" } },
            Content = JsonContent.Create(request)
        };
        var response = Client.SendAsync(message).Result;
        _log.Verbose($"[TrySendOpcodes] Response was {response.StatusCode}");
        return response.IsSuccessStatusCode;
        // throw new Exception($"Response code did not indicate success: {response.StatusCode}");
    }

    public bool TryGetCensoredOpcodes(string gameVersion, out CensoredOpcodesResponse censoredOpcodes)
    {
        _log.Verbose($"[TryGetCensoredOpcodes] Begin");

        censoredOpcodes = new CensoredOpcodesResponse();
        var request = new CensoredOpcodesRequest { GameVersion = gameVersion };

        var message = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri($"{CensoredOpcodesEndpoint}"),
            Headers = { { HttpRequestHeader.Authorization.ToString(), $"Bearer {_config.AccessToken}" } },
            Content = JsonContent.Create(request)
        };
        var response = Client.SendAsync(message).Result;
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Response code did not indicate success: {response.StatusCode}");
        censoredOpcodes = JsonConvert.DeserializeObject<CensoredOpcodesResponse>(response.Content.ReadAsStringAsync().Result);
        return true;
    }

    public bool TryDeleteCapture(Guid captureId)
    {
        _log.Verbose($"[TryDeleteCapture] Begin");

        var request = new CaptureDeletionRequest { CaptureId = captureId };

        var message = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri($"{DeleteEndpoint}"),
            Headers = { { HttpRequestHeader.Authorization.ToString(), $"Bearer {_config.AccessToken}" } },
            Content = JsonContent.Create(request)
        };
        var response = Client.SendAsync(message).Result;
        _log.Verbose($"[TryDeleteCapture] Response was {response}");
        return response.IsSuccessStatusCode;
    }

    public bool TryUploadCapture(FileInfo captureFile, CaptureUploadRequest request, ProgressHolder progress, out CaptureUploadResponse captureUploadResponse)
    {
        _log.Verbose($"[TryUploadCapture] Begin");

        captureUploadResponse = new CaptureUploadResponse();
        var json = JsonSerializer.Serialize(request);
        var captureFileContent = new ProgressableStreamContent(new StreamContent(captureFile.OpenRead()), progress.Set);

        var content = new MultipartFormDataContent(Util.RandomByteString(16));
        content.Add(new StringContent(json), "files", "meta.json");
        content.Add(captureFileContent, "files", $"{captureUploadResponse.CaptureId}.ccfcap");

        var message = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri($"{UploadEndpoint}"),
            Headers = { { HttpRequestHeader.Authorization.ToString(), $"Bearer {_config.AccessToken}" } },
            Content = content
        };
        var response = Client.SendAsync(message).Result;
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Response code did not indicate success: {response.StatusCode} {response.Content.ReadAsStringAsync().Result}");
            // return false;
        }

        captureUploadResponse = JsonConvert.DeserializeObject<CaptureUploadResponse>(response.Content.ReadAsStringAsync().Result);
        return true;
    }

    public bool TryAcceptTos(int tosVersion)
    {
        _log.Verbose("[TryAcceptTos] Begin");

        var request = new AcceptTosRequest { TosVersion = tosVersion};

        var message = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri($"{TosAcceptEndpoint}"),
            Headers = { { HttpRequestHeader.Authorization.ToString(), $"Bearer {_config.AccessToken}" } },
            Content = JsonContent.Create(request)
        };
        var response = Client.SendAsync(message).Result;
        _log.Verbose($"[TryAcceptTos] Response was {response}");
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Response code did not indicate success: {response.StatusCode} {response.Content.ReadAsStringAsync().Result}");
        return true;
    }

    public CaptureListResponse? GetCaptureList()
    {
        _log.Verbose("[GetUploadedCaptureList] Begin");

        var message = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri($"{CaptureListEndpoint}"),
            Headers = { { HttpRequestHeader.Authorization.ToString(), $"Bearer {_config.AccessToken}" } }
        };
        var response = Client.SendAsync(message).Result;
        _log.Verbose($"[GetUploadedCaptureList] Response was {response}");
        if (!response.IsSuccessStatusCode)
        {
            // throw new Exception($"Response code did not indicate success: {response.StatusCode} {response.Content.ReadAsStringAsync().Result}");
            return null;
        }

        return JsonSerializer.Deserialize<CaptureListResponse>(response.Content.ReadAsStringAsync().Result, _options);
    }
}
