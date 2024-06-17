using System;
using System.Threading.Tasks;
using Chronofoil.Common.Auth;
using Dalamud.Plugin.Services;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;

namespace Chronofoil.Web.Auth;

public class AuthManager : IDisposable
{
    private enum AuthType
    {
        None,
        Register,
        Login,
    }
    private const string LoginUri = "https://discord.com/oauth2/authorize?client_id=1237235845736562728&response_type=code&redirect_uri=http%3A%2F%2Flocalhost%3A43595%2Fauth%2Flogin%2Fdiscord&scope=identify";
    
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly ChronofoilClient _client;
    
    private WebServer? _server;
    private IAuthListener? _listener;

    private AuthType _type = AuthType.None;

    public AuthManager(IPluginLog log, Configuration config, ChronofoilClient client)
    {
        _log = log;
        _config = config;
        _client = client;
        Task.Run(RefreshIfNeeded)
            .ContinueWith(task =>
            {
                if (task.Exception == null)
                    _log.Info("Refresh succeeded.");
                else
                    _log.Error($"Refresh failed: {task.Exception}");
            });
    }

    public void Dispose()
    {
        _server?.Dispose();
        _server = null;
    }

    private void RefreshIfNeeded()
    {
        if (string.IsNullOrEmpty(_config.RefreshToken)) return;
        if (DateTime.UtcNow < _config.TokenExpiryTime) return;
        
        try
        {
            var tokenResult = _client.TryRefresh(_config.RefreshToken, out var tokens);
            if (!tokenResult || tokens == null)
            {
                return;
            }
            
            _config.AccessToken = tokens.AccessToken;
            _config.RefreshToken = tokens.RefreshToken;
            _config.TokenExpiryTime = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);
            _config.Save();
        }
        catch (Exception e)
        {
            _log.Error(e, "[AuthManager] [RefreshIfNeeded] Something went wrong!");
            _config.AccessToken = "";
            _config.RefreshToken = "";
            _config.TokenExpiryTime = DateTime.UtcNow.AddYears(2000);
        }
    }

    public void Register(IAuthListener listener)
    {
        _listener = listener;
        _server = CreateWebServer();
        _server.Start();
        _type = AuthType.Register;
        Dalamud.Utility.Util.OpenLink(LoginUri);
    }
    
    public void Login(IAuthListener listener)
    {
        _listener = listener;
        _server = CreateWebServer();
        _server.Start();
        _type = AuthType.Login;
        Dalamud.Utility.Util.OpenLink(LoginUri);
    }
    
    public void AuthCallback(string authCode)
    {
        try
        {
            AccessTokenResponse? tokens = null;
            var tokenResult = _type switch
            {
                AuthType.Login => _client.TryLogin(authCode, out tokens),
                AuthType.Register => _client.TryRegister(authCode, out tokens),
                _ => throw new ArgumentOutOfRangeException()
            };
            
            if (!tokenResult || tokens == null)
            {
                _listener?.Error("Auth failed.");
                return;
            }
            
            _config.AccessToken = tokens.AccessToken;
            _config.RefreshToken = tokens.RefreshToken;
            _config.TokenExpiryTime = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);
            _config.Save();
            
            if (_type == AuthType.Login)
                _listener?.Login();
            else if (_type == AuthType.Register)
                _listener?.Register();
        }
        catch (Exception e)
        {
            _log.Error(e, "[AuthManager] [LoginCallback] Something went wrong!");
            _listener?.Error($"Something went wrong!\n{e.Message}\n{e.StackTrace}");
        }
        finally
        {
            _listener = null;
            _server?.Dispose();
            _server = null;
            _type = AuthType.None;
        }
    }
    
    public bool CheckForNewTos()
    {
        var isUser = !string.IsNullOrEmpty(_config.AccessToken);
        if (!isUser) return false;
        
        try
        {
            var tosResult = _client.GetTos();
            var isNew = tosResult.Version > _config.MaxAcceptedTosVersion;
            _config.MaxKnownTosVersion = tosResult.Version;
            _config.Save();
            return isNew;
        }
        catch (Exception e)
        {
            _log.Error(e, "[AuthManager] [CheckForNewTos] Something went wrong!");
        }
        
        return false;
    }

    public bool AcceptNewTos(int tosVersion)
    {
        var accept = _client.TryAcceptTos(tosVersion);
        if (accept)
        {
            _config.MaxAcceptedTosVersion = tosVersion;
            _config.Save();
        }
        return accept;
    }
    
    private WebServer CreateWebServer()
    {
        var server = new WebServer(o => o
                .WithUrlPrefix("http://localhost:43595")
                .WithMode(HttpListenerMode.EmbedIO))
                .WithWebApi("/auth", module => module.WithController(() => new AuthController(this)));

        return server;
    }
}

internal class AuthController : WebApiController
{
    private readonly AuthManager _authManager;

    public AuthController(AuthManager authManager)
    {
        _authManager = authManager;
    }
    
    [Route(HttpVerbs.Get, "/login/discord")]
    public string Login([QueryField] string code)
    {
        _authManager.AuthCallback(code);
        return "Authentication successful! Please return to Final Fantasy XIV!";
    }
}