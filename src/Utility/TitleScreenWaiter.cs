using System;
using Dalamud.Plugin.Services;

namespace Chronofoil.Utility;

public class TitleScreenWaiter
{
    public delegate void TitleScreenAppeared();
    public event TitleScreenAppeared? OnTitleScreenAppeared;

    private readonly IPluginLog _log;
    private readonly IGameGui _gui;

    private bool _isTitleScreen;
    private bool _wasTitleScreen;

    public TitleScreenWaiter(IPluginLog log, IFramework framework, IGameGui gui)
    {
        _log = log;
        _gui = gui;
        
        framework.Update += _ => CheckForTitleScreen();
    }

    private void CheckForTitleScreen()
    {
        // _log.Debug($"istitlescreen: {_isTitleScreen} wastitlescreen: {_wasTitleScreen}");
        _wasTitleScreen = _isTitleScreen;
        _isTitleScreen = _gui.GetAddonByName("_TitleLogo") != IntPtr.Zero;

        if (!_wasTitleScreen && _isTitleScreen)
            OnTitleScreenAppeared?.Invoke();
    }
}