using System;
using System.Reflection;
using System.Threading.Tasks;
using Chronofoil.UI;
using Chronofoil.UI.Components;
using Chronofoil.Utility;
using Chronofoil.Web.Auth;
using Chronofoil.Web.Upload;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace Chronofoil;

public class Chronofoil
{
    private const string CommandName = "/chronofoil";
    
    private readonly IPluginLog _log;
    private readonly ICommandManager _commandManager;
    private readonly ChronofoilUI _ui;

    private readonly NewTosModal _tosModal;
    
    public Chronofoil(
        ITextureProvider textureProvider,
        ITitleScreenMenu titleScreenMenu,
        ICommandManager commandManager,
        IPluginLog log,
        AuthManager authManager,
        NewTosModal tosModal,
        TitleScreenWaiter waiter,
        AutoUploadService autoUploadService,
        ChronofoilUI ui)
    {
        _log = log;
        _commandManager = commandManager;
        _ui = ui;

        PrepareTitleScreenIcon(textureProvider, titleScreenMenu, ui);
        waiter.OnTitleScreenAppeared += () => Task.Run(() => CheckAndWarnNewTos(authManager, tosModal));
        waiter.OnTitleScreenAppeared += autoUploadService.Begin;
        
        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Chronofoil UI.",
        });
    }

    private void PrepareTitleScreenIcon(ITextureProvider textureProvider, ITitleScreenMenu titleScreenMenu, ChronofoilUI ui)
    {
        try
        {
            const string resourceName = "Chronofoil.Data.icon_small.png";
            var texture = textureProvider.GetFromManifestResource(Assembly.GetExecutingAssembly(), resourceName);
            titleScreenMenu.AddEntry("Chronofoil", texture, ui.ShowMainWindow);
        }
        catch (Exception e)
        {
            // No icon for you
            _log.Verbose($"{e.Message}");
            _log.Verbose($"{e.StackTrace}");
        }
    }

    private void CheckAndWarnNewTos(AuthManager authManager, NewTosModal tosModal)
    {
        _log.Debug($"checking and opening TOS");
        if (!authManager.CheckForNewTos()) return;
        tosModal.Begin();
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        switch (args)
        {
            // case "monitor":
                // _ui.ShowMonitorWindow();    
                // break;
            // case "config" or "settings":
                // _ui.ShowMainWindow();
                // break;
            default:
                _ui.ShowMainWindow();
                break;
        }
    }
}