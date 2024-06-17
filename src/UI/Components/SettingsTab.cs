using System;
using Chronofoil.Utility;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using ImGuiNET;

namespace Chronofoil.UI.Components;

public class SettingsTab
{
    private readonly Configuration _config;
    private readonly RegisterModal _registerModal;
    private readonly LoginModal _loginModal;

    // Local config vars
    private string _storageDirText = "";
    private bool _enableContext;
    private bool _enableUpload;
    private int _metricsTimeValue;
    private TimeSpanIdentifier _metricsTimeSpan;
    private bool _metricsWhenEos;
    private int _publicTimeValue;
    private TimeSpanIdentifier _publicTimeSpan;
    private bool _publicWhenEos;
    private bool _notificationsEnabled;
    private bool _captureBeginNotificationsEnabled;
    private bool _captureEndNotificationsEnabled;
    private bool _uploadCapturesNotificationsEnabled;
    
    private bool _metricsTimeValid;
    private bool _publicTimeValid;
    private bool IsValid => _metricsTimeValid && _publicTimeValid;

    private bool _drewLastFrame;
    
    public SettingsTab(Configuration config, RegisterModal registerModal, LoginModal loginModal)
    {
        _config = config;
        _registerModal = registerModal;
        _loginModal = loginModal;
    }

    public void Update(bool isDrawing)
    {
        if (isDrawing)
        {
            // Wipe settings when the user swaps tabs if they didn't save
            if (!_drewLastFrame)
            {
                _storageDirText = _config.StorageDirectory;
                _enableContext = _config.EnableContext;
                _enableUpload = _config.EnableUpload;
                _metricsTimeValue = _config.MetricsTimeValue;
                _metricsTimeSpan = _config.MetricsTimeSpan;
                _metricsWhenEos = _config.MetricsWhenEos;
                _publicTimeValue = _config.PublicTimeValue;
                _publicTimeSpan = _config.PublicTimeSpan;
                _publicWhenEos = _config.PublicWhenEos;
                _notificationsEnabled = _config.NotificationsEnabled;
                _captureBeginNotificationsEnabled = _config.CaptureBeginNotificationsEnabled;
                _captureEndNotificationsEnabled = _config.CaptureEndNotificationsEnabled;
                _uploadCapturesNotificationsEnabled = _config.UploadCapturesNotificationsEnabled;
            }
            _drewLastFrame = true;
        }
        else if (_drewLastFrame)
        {
            _drewLastFrame = false;
        }
    }

    private void Validate()
    {
        var metricsTimeSpan = Util.Convert(_metricsTimeValue, _metricsTimeSpan);
        var publicTimeSpan = Util.Convert(_publicTimeValue, _publicTimeSpan);

        _metricsTimeValid = metricsTimeSpan >= TimeSpan.FromDays(7) || _metricsWhenEos;
        _publicTimeValid = publicTimeSpan >= TimeSpan.FromDays(14) || _publicWhenEos;
    }
    
    public void Draw()
    {
        Validate();
        
        ImGui.TextUnformatted("Main Settings");
        
        ImGui.TextUnformatted("Chronofoil storage directory:");
        ImGui.SameLine();
        ImGui.InputText("##cf_storage_dir", ref _storageDirText, 255);
        ImGui.SameLine();
        if (ImGui.Button($"Open Folder##cf_settings_open_folder"))
        {
        	Dalamud.Utility.Util.OpenLink(_config.StorageDirectory);
        }
        
        // ImGui.TextUnformatted("Enable Context:");
        // ImGui.SameLine();
        // ImGui.Checkbox("##cf_context", ref _enableContext);
        
        ImGui.Separator();

        ImGui.TextUnformatted("Notification Settings");
        ImGui.TextUnformatted("Notifications:");
        ImGui.SameLine();
        ImGui.Checkbox("##cf_notifications", ref _notificationsEnabled);
        ImGui.TextUnformatted("Capture Start notifications:");
        ImGui.SameLine();
        ImGui.Checkbox("##cf_start_notifications", ref _captureBeginNotificationsEnabled);
        ImGui.TextUnformatted("Capture End notifications:");
        ImGui.SameLine();
        ImGui.Checkbox("##cf_end_notifications", ref _captureEndNotificationsEnabled);
        ImGui.BeginDisabled(!_enableUpload);
        ImGui.TextUnformatted("Remind me on startup if I have non-ignored, non-uploaded captures:");
        ImGui.SameLine();
        ImGui.Checkbox("##cf_capture_reminder_notifications", ref _uploadCapturesNotificationsEnabled);
        ImGui.EndDisabled();
        
        ImGui.Separator();
        
        ImGui.TextUnformatted("Upload Settings");
        
        ImGui.TextUnformatted("Enable uploading:");
        ImGui.SameLine();
        ImGui.Checkbox("##cf_upload", ref _enableUpload);
        
        ImGui.BeginDisabled(!_enableUpload);
        ImGui.TextUnformatted("Default metrics time for uploads: ");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("The 'metrics time' is how long from the time of upload that it will take for your captures to appear in " +
                                   "the Chronofoil metrics system for developers to query. This does not prevent an individual from querying " +
                                   "the metrics system and recreating your capture, but it makes it incredibly difficult, while still providing " +
                                   "useful information to developers. This setting is your default time and will be set automatically for uploads," +
                                   " but can be overridden at upload time.");
        ImGui.BeginDisabled(_metricsWhenEos);
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150f);
        ImGui.InputInt("##cf_metric_time_value", ref _metricsTimeValue);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150f);
        if (ImGui.BeginCombo("##cf_metric_time_identifier", _metricsTimeSpan.ToString()))
        {
            foreach (var tsId in Enum.GetValues<TimeSpanIdentifier>())
                if (ImGui.Selectable(tsId.ToString()))
                    _metricsTimeSpan = tsId;
            ImGui.EndCombo();   
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.Checkbox("Do not add my captures to the metrics system until FFXIV is End of Service##cf_metric_time_eos", ref _metricsWhenEos);
        if (!_metricsTimeValid)
            ImGuiHelpers.SafeTextColoredWrapped(ImGuiColors.DalamudRed, "The minimum Metrics time is 7 days from the date of upload.");
        
        ImGui.TextUnformatted("Default 'public' time for uploads: ");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("The 'public time' is how long from the time of upload that it will take for your captures to be" +
                                   " available for raw download. This provides the full capture file to individuals, which allows them to do" +
                                   " whatever - extract information, places visited, or even replay the packet capture on their own machine " +
                                   "if they have that capability. This setting is your default time and will be set automatically for uploads, " +
                                   "but can be overridden at upload time");
        ImGui.BeginDisabled(_publicWhenEos);
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150f);
        ImGui.InputInt("##cf_public_time_value", ref _publicTimeValue);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150f);
        if (ImGui.BeginCombo("##cf_public_time_identifier", _publicTimeSpan.ToString()))
        {
            foreach (var tsId in Enum.GetValues<TimeSpanIdentifier>())
                if (ImGui.Selectable(tsId.ToString()))
                    _publicTimeSpan = tsId;
            ImGui.EndCombo();   
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.Checkbox("Do not make my captures public until FFXIV is End of Service##cf_public_time_eos", ref _publicWhenEos);
        if (!_publicTimeValid)
            ImGuiHelpers.SafeTextColoredWrapped(ImGuiColors.DalamudRed, "The minimum Public time is 14 days from the date of upload.");
        ImGui.EndDisabled();
        
        ImGui.Separator();

        if (string.IsNullOrEmpty(_config.AccessToken))
        {
            ImGui.BeginDisabled(!_enableUpload);
            ImGui.TextUnformatted("Register with:");
            ImGui.SameLine();
            if (ImGui.Button($"Discord##cf_register_discord"))
            {
                _registerModal.Begin();
            }
        
            ImGui.TextUnformatted("Log in with:");
            ImGui.SameLine();
            if (ImGui.Button($"Discord##cf_login_discord"))
            {
                _loginModal.Begin();
            }
            ImGui.EndDisabled();
        }
        else
        {
            var (userName, provider) = JwtReader.GetTokenInfo(_config.AccessToken);
            ImGui.TextUnformatted($"Authenticated as {userName} via {provider}.");

            if (ImGui.Button("Log out##cf_logout"))
            {
                // TODO: Logout popup modal?
                _config.AccessToken = "";
                _config.RefreshToken = "";
                _config.TokenExpiryTime = DateTime.UnixEpoch;
                _config.Save();
            }
        }
        
        ImGui.BeginDisabled(!IsValid);
        if (ImGui.Button("Save##cf_settings_save"))
        {
            SaveSettings();
        }
        ImGui.EndDisabled();
        if (!IsValid)
        {
            ImGuiHelpers.SafeTextColoredWrapped(ImGuiColors.DalamudRed, "Settings are invalid.");
        }
    }

    private void SaveSettings()
    {
         _config.StorageDirectory = _storageDirText;
         _config.EnableContext = _enableContext;
         _config.EnableUpload = _enableUpload;
         _config.MetricsTimeValue = _metricsTimeValue;
         _config.MetricsTimeSpan = _metricsTimeSpan;
         _config.MetricsWhenEos = _metricsWhenEos;
         _config.PublicTimeValue = _publicTimeValue;
         _config.PublicTimeSpan = _publicTimeSpan;
         _config.PublicWhenEos = _publicWhenEos;
         _config.NotificationsEnabled = _notificationsEnabled;
         _config.CaptureBeginNotificationsEnabled = _captureBeginNotificationsEnabled;
         _config.CaptureEndNotificationsEnabled = _captureEndNotificationsEnabled;
         _config.UploadCapturesNotificationsEnabled = _uploadCapturesNotificationsEnabled;
        _config.Save();
    }
}