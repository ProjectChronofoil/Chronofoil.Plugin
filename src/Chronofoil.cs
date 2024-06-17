using System;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Chronofoil.UI;
using Chronofoil.UI.Components;
using Chronofoil.Utility;
using Chronofoil.Web.Auth;
using Dalamud.Interface;
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
        UiBuilder uiBuilder,
        ITitleScreenMenu titleScreenMenu,
        ICommandManager commandManager,
        IPluginLog log,
        AuthManager authManager,
        NewTosModal tosModal,
        TitleScreenWaiter waiter,
        ChronofoilUI ui)
    {
        _log = log;
        _commandManager = commandManager;
        _ui = ui;

        PrepareTitleScreenIcon(uiBuilder, titleScreenMenu, ui);
        waiter.OnTitleScreenAppeared += () => Task.Run(() => CheckAndWarnNewTos(authManager, tosModal));
        
        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Chronofoil UI.",
        });
    }

    private void PrepareTitleScreenIcon(UiBuilder uiBuilder, ITitleScreenMenu titleScreenMenu, ChronofoilUI ui)
    {
        try
        {
            const string resourceName = "Chronofoil.Data.icon_small.png";
            var resource = Util.GetResource(resourceName);
            _log.Verbose($"resource {resourceName} is {resource.Length} bytes");
            var texture = uiBuilder.LoadImage(resource);
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