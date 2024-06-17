using Chronofoil.UI.Components;
using Chronofoil.UI.Windows;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace Chronofoil.UI;

public class ChronofoilUI
{
	private readonly IPluginLog _log;
	private readonly UiBuilder _uiBuilder;
	
	private readonly WindowSystem _windowSystem;

	private readonly MainWindow _mainWindow;

	public ChronofoilUI(
		IPluginLog log,
		MainWindow mainWindow,
		RegisterModal registerModal,
		LoginModal loginModal,
		UploadModal uploadModal,
		NewTosModal newTosModal,
		UiBuilder uiBuilder
	)
	{
		_log = log;
		_uiBuilder = uiBuilder;

		_mainWindow = mainWindow;
		
		_windowSystem = new WindowSystem("Chronofoil");
		_windowSystem.AddWindow(mainWindow);
		_windowSystem.AddWindow(registerModal);
		_windowSystem.AddWindow(loginModal);
		_windowSystem.AddWindow(uploadModal);
		_windowSystem.AddWindow(newTosModal);
		
		_uiBuilder.Draw += _windowSystem.Draw;
		_uiBuilder.OpenMainUi += ShowMainWindow;
		_uiBuilder.OpenConfigUi += ShowMainWindow;
	}
	
	public void ShowMainWindow() => _mainWindow.IsOpen = true;
	public void CloseMainWindow() => _mainWindow.IsOpen = false;
	public void ToggleMainWindow() => _mainWindow.IsOpen = !_mainWindow.IsOpen;
}