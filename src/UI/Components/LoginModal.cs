using System;
using Chronofoil.Web.Auth;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace Chronofoil.UI.Components;

public class LoginModal : Window, IAuthListener
{
    private enum LoginStep
    {
        Auth,
        Error,
        Done,
    }
    
    private LoginStep _step = LoginStep.Auth;
    private string _errorText;

    private readonly IPluginLog _log;
    private readonly AuthManager _authManager;

    private readonly Vector2 _boxSize = ImGuiHelpers.ScaledVector2(500, 300);

    public LoginModal(IPluginLog log, AuthManager authManager) : base("Chronofoil Login", ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.NoResize, true)
    {
        _log = log;
        _authManager = authManager;
        _errorText = "";
        Size = new Vector2(600, 400);
        SizeCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        switch (_step)
        {
            case LoginStep.Auth:
                DrawAuth();
                break;
            case LoginStep.Error:
                DrawError();
                break;
            case LoginStep.Done:
                DrawDone();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Begin()
    {
        IsOpen = true;
        _authManager.Login(this);
    }

    public void End()
    {
        IsOpen = false;
        _step = LoginStep.Auth;
    }

    private void DrawAuth()
    {
        ImGuiHelpers.SafeTextWrapped("Please authorize the application with Discord in your web browser.");
    }

    private void DrawError()
    {
        ImGuiHelpers.SafeTextWrapped("Chronofoil encountered an error during login.");
        ImGuiHelpers.SafeTextWrapped("The error is as follows:");
        ImGui.InputTextMultiline("##cf_login_error", ref _errorText, (uint)_errorText.Length, _boxSize, ImGuiInputTextFlags.ReadOnly);

        if (ImGui.Button("Ok##cf_login_error_ok"))
        {
            End();
        }
    }

    private void DrawDone()
    {
        ImGuiHelpers.SafeTextWrapped("Authentication with the Chronofoil server was successful!");

        if (ImGui.Button("Done##cf_login_modal_done"))
        {
            End();
        }
    }
    
    public void Register()
    {
        throw new NotImplementedException();
    }

    public void Error(string errorText)
    {
        _errorText = errorText;
        _step = LoginStep.Error;
    }

    public void Login()
    {
        _step = LoginStep.Done;
    }
}