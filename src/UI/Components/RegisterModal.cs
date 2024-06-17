using System;
using System.Threading.Tasks;
using Chronofoil.Common.Info;
using Chronofoil.Web.Auth;
using Chronofoil.Web.Info;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace Chronofoil.UI.Components;

public class RegisterModal : Window, IAuthListener
{
    private enum RegisterStep
    {
        Tos,
        Auth,
        Error,
        Done,
    }
    
    private RegisterStep _step = RegisterStep.Tos;
    private string _errorText;
    private TosResponse _tos;
    private int _radio;
    private bool _wasOpen;

    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private readonly AuthManager _authManager;
    private readonly InfoService _infoService;

    private Vector2 BoxSize => new(-1, 300 * ImGuiHelpers.GlobalScale);

    public RegisterModal(
        Configuration config,
        IPluginLog log,
        InfoService infoService,
        AuthManager authManager) : base("Chronofoil Registration", ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysVerticalScrollbar, true)
    {
        _config = config;
        _log = log;
        _authManager = authManager;
        _infoService = infoService;
        
        _errorText = "";
        _tos = new TosResponse { Version = 0, EnactedDate = DateTime.UnixEpoch, Text = "" };
        
        Size = new Vector2(1000, 500);
        SizeCondition = ImGuiCond.Appearing;
    }

    public override void PreOpenCheck()
    {
        if (!IsOpen && _wasOpen)
            End();
        _wasOpen = IsOpen;
    }
    
    public void Begin()
    {
        IsOpen = true;
        Task.Run(() => _infoService.GetTos())
            .ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    _tos = task.Result;
                }
                else
                {
                    if (task.Exception == null)
                        Error("Failed to get current Terms of Service.");
                    else
                        Error($"Failed to get current Terms of Service: {task.Exception}");
                }
            });
    }

    private void End()
    {
        IsOpen = false;
        _radio = 0;
        _tos = new TosResponse { Version = 0, EnactedDate = DateTime.UnixEpoch, Text = "" };
        _errorText = "";
        _step = RegisterStep.Tos;
    }

    public override void Draw()
    {
        switch (_step)
        {
            case RegisterStep.Tos:
                DrawTos();
                break;
            case RegisterStep.Auth:
                DrawAuth();
                break;
            case RegisterStep.Error:
                DrawError();
                break;
            case RegisterStep.Done:
                DrawDone();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void DrawTos()
    {
        ImGuiHelpers.SafeTextWrapped("Chronofoil registration is only necessary if you would like to contribute packet captures to the central capture repository.");
        ImGuiHelpers.SafeTextWrapped("Registration is not necessary for capturing packets for your own purposes.");
        ImGuiHelpers.SafeTextWrapped("Please read the terms and conditions carefully. It contains important information regarding your privacy as a Chronofoil user.");

        using (var _ = ImRaii.PushFont(UiBuilder.MonoFont))
        {
            var text = _tos.Text;
            ImGui.InputTextMultiline("##cf_tos", ref text, (uint)text.Length, BoxSize, ImGuiInputTextFlags.ReadOnly);   
        }
        
        ImGui.Separator();

        ImGui.RadioButton("I do not understand, or do not agree, to the terms and conditions", ref _radio, 0);
        ImGui.RadioButton("I understand and agree to the terms and conditions", ref _radio, 1);

        ImGui.BeginDisabled(_radio == 0 || string.IsNullOrEmpty(_tos.Text));
        if (ImGui.Button("Next"))
        {
            BeginAuth();
        }
        ImGui.EndDisabled();
    }

    private void BeginAuth()
    {
        _step = RegisterStep.Auth;
        _authManager.Register(this);
    }

    private void DrawAuth()
    {
        ImGuiHelpers.SafeTextWrapped("Please authorize the application with Discord in your web browser.");
    }

    private void DrawDone()
    {
        _config.MaxAcceptedTosVersion = _tos.Version;
        _config.Save();
        ImGuiHelpers.SafeTextWrapped("Authentication with the Chronofoil server was successful!");

        if (ImGui.Button("Done##cf_register_modal_done"))
        {
            End();
        }
    }
    
    public void Register()
    {
        _step = RegisterStep.Done;
    }

    public void Error(string errorText)
    {
        _errorText = errorText;
        _step = RegisterStep.Error;
    }
    
    private void DrawError()
    {
        ImGuiHelpers.SafeTextWrapped("Chronofoil encountered an error during registration.");
        ImGuiHelpers.SafeTextWrapped("The error is as follows:");
        ImGui.InputTextMultiline("##cf_register_error", ref _errorText, (uint)_errorText.Length, BoxSize, ImGuiInputTextFlags.ReadOnly);

        if (ImGui.Button("Ok##cf_register_error_ok"))
        {
            End();
        }
    }

    public void Login()
    {
        throw new NotImplementedException();
    }
}