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

public class NewTosModal : Window
{
    private enum TosStep
    {
        Tos,
        Error,
        Done,
    }
    
    private TosStep _step = TosStep.Tos;
    private string _errorText;
    private TosResponse _tos;
    private int _radio;
    private bool _wasOpen;

    private bool _waiting;

    private readonly IPluginLog _log;
    private readonly AuthManager _authManager;
    private readonly InfoService _infoService;

    private Vector2 BoxSize => new(-1, 300 * ImGuiHelpers.GlobalScale);

    public NewTosModal(IPluginLog log, InfoService infoService, AuthManager authManager) : base("New Chronofoil ToS", ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysVerticalScrollbar, true)
    {
        _log = log;
        _authManager = authManager;
        _infoService = infoService;
        
        _errorText = "";
        _tos = new TosResponse();
        
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

    public void End()
    {
        IsOpen = false;
        _radio = 0;
        _tos = new TosResponse();
        _errorText = "";
        _step = TosStep.Tos;
        _waiting = false;
    }

    public override void Draw()
    {
        switch (_step)
        {
            case TosStep.Tos:
                DrawTos();
                break;
            case TosStep.Error:
                DrawError();
                break;
            case TosStep.Done:
                DrawDone();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void DrawTos()
    {
        ImGuiHelpers.SafeTextWrapped("Please read and accept the new Chronofoil Terms of Service.");
        if (_tos.EnactedDate != default)
            ImGuiHelpers.SafeTextWrapped($"Effective date: {_tos.EnactedDate}");

        using (var _ = ImRaii.PushFont(UiBuilder.MonoFont))
        {
            var text = _tos.Text;
            ImGui.InputTextMultiline("##cf_tos", ref text, (uint)text.Length, BoxSize, ImGuiInputTextFlags.ReadOnly);   
        }
        
        ImGui.Separator();

        ImGui.RadioButton("I do not understand, or do not agree, to the terms and conditions", ref _radio, 0);
        ImGui.RadioButton("I understand and agree to the terms and conditions", ref _radio, 1);

        ImGui.BeginDisabled(_radio == 0 || string.IsNullOrEmpty(_tos.Text));
        if (ImGui.Button("Finish"))
        {
            Task.Run(() =>
            {
                _authManager.AcceptNewTos(_tos.Version);
                _waiting = true;
            }).ContinueWith(task =>
            {
                if (task.Exception != null)
                    Error(task.Exception.ToString());
                else
                    _step = TosStep.Done;
            });
        }

        if (_waiting)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted("Please wait...");
        }
        ImGui.EndDisabled();
    }

    private void DrawError()
    {
        ImGuiHelpers.SafeTextWrapped("Chronofoil encountered an error during registration.");
        ImGuiHelpers.SafeTextWrapped("The error is as follows:");
        ImGui.InputTextMultiline("##cf_new_tos_error", ref _errorText, (uint)_errorText.Length, BoxSize, ImGuiInputTextFlags.ReadOnly);

        if (ImGui.Button("Ok##cf_new_tos_error_ok"))
        {
            _step = TosStep.Tos;
            IsOpen = false;
        }
    }

    private void DrawDone()
    {
        ImGuiHelpers.SafeTextWrapped($"Thank you for accepting the new Chronofoil Terms of Service (effective {_tos.EnactedDate.ToLocalTime()}).");

        if (ImGui.Button("Done##cf_new_tos_modal_done"))
        {
            End();
        }
    }
    
    public void Register()
    {
        End();
    }

    public void Error(string errorText)
    {
        _errorText = errorText;
        _step = TosStep.Error;
    }
}