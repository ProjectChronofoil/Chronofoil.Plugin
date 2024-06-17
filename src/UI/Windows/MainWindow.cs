using System.Numerics;
using System.Reflection;
using Chronofoil.UI.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace Chronofoil.UI.Windows;

public class MainWindow : Window
{
    private readonly Configuration _config;
    private readonly string _verString;

    private readonly CaptureTab _captureTab;
    private readonly UploadsTab _uploadsTab;
    private readonly SettingsTab _settingsTab;
    private readonly FaqTab _faqTab;

    private bool _isDrawingSettings;
    private bool _isDrawingUploads;
	
    public MainWindow(
        Configuration config,
        CaptureTab captureTab,
        UploadsTab uploadsTab,
        SettingsTab settingsTab,
        FaqTab faqTab,
        string name = "Chronofoil", ImGuiWindowFlags flags = ImGuiWindowFlags.None, bool forceMainWindow = false) : base(name, flags, forceMainWindow)
    {
        _config = config;
        _captureTab = captureTab;
        _uploadsTab = uploadsTab;
        _settingsTab = settingsTab;
        _faqTab = faqTab;
		
        var ver = Assembly.GetExecutingAssembly().GetName().Version!.ToString();
        WindowName = $"Chronofoil v{ver}###cf_main_window";

        Size = new Vector2(1200, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Update()
    {
        // _captureTab.Update();
        _settingsTab.Update(_isDrawingSettings);
        _uploadsTab.Update(_isDrawingUploads);
    }

    public override void Draw()
    {
        using var _ = ImRaii.TabBar("cf_tabs");
        DrawCaptureTab();
        DrawUploadsTab();
        DrawSettingsTab();
        DrawFaqTab();
        DrawAboutTab();
    }

    private void DrawCaptureTab()
    {
        using var tab = ImRaii.TabItem("Captures");
        if (!tab.Success) return;
        _captureTab.Draw();
    }
    
    private void DrawFaqTab()
    {
        using var tab = ImRaii.TabItem("Faq");
        if (!tab.Success) return;
        _faqTab.Draw();
    }
    
    private void DrawSettingsTab()
    {
        using var tab = ImRaii.TabItem("Settings");
        if (!tab.Success)
        {
            _isDrawingSettings = false;
            return;
        }
        _isDrawingSettings = true;
        _settingsTab.Draw();
    }
    
    private void DrawUploadsTab()
    {
        _isDrawingUploads = false;
        if (string.IsNullOrEmpty(_config.AccessToken)) return;
        using var tab = ImRaii.TabItem("Uploads");
        if (!tab.Success) return;
        _isDrawingUploads = true;
        _uploadsTab.Draw();
    }

    // private void DrawMainTab()
    // {
    //     using var tab = ImRaii.TabItem("Main");
    //     if (!tab.Success) return;
		  //
    //     ImGui.TextUnformatted("Main tab.");
		  //
    //     ImGui.TextUnformatted("Set default capture output path:");
    //     ImGui.InputText("##cf_path_input", ref _outputPathInput, 255);
    //
    //     if (ImGui.Button("Save"))
    //     {
    //         _config.StorageDirectory = _outputPathInput;
    //         _config.Save();
    //     }
    // }

    private void DrawAboutTab()
    {
        using var tab = ImRaii.TabItem("About");
        if (!tab.Success) return;
		
        ImGui.TextUnformatted("Chronofoil");
        ImGui.TextUnformatted("by perchbird");
    }
}