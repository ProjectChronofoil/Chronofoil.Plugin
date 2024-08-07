using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Chronofoil.Capture;
using Chronofoil.CaptureFile.Censor;
using Chronofoil.CaptureFile.Generated;
using Chronofoil.Censor;
using Chronofoil.Common.Capture;
using Chronofoil.Utility;
using Chronofoil.Web.Upload;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace Chronofoil.UI.Components;

public class UploadModal : Window
{
    private enum UploadStep
    {
        Settings,
        Prepare,
        CensorNotice,
        Censoring,
        Progress,
        Done,
        Error,
    }
    
    private string _errorText;
    private UploadStep _step;

    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly CaptureManager _captureManager;
    private readonly UploadService _uploadService;
    private readonly OpcodeService _opcodeService;
    
    private Guid _captureId = Guid.Empty;
    private string? _gameVersion = "";
    private DateTime _now;
    private ProgressHolder _holder;
    private Task<CaptureUploadResponse?> _response;

    private Task _opcodeTask;
    private Task _censorTask;

    // Imgui stuff
    private bool _overrideSettings;
    
    // Time settings
    private int _metricsTimeValue;
    private TimeSpanIdentifier _metricsTimeSpan;
    private bool _metricsWhenEos;
    private int _publicTimeValue;
    private TimeSpanIdentifier _publicTimeSpan;
    private bool _publicWhenEos;
    
    private bool _metricsTimeValid;
    private bool _publicTimeValid;
    private bool IsValid => _metricsTimeValid && _publicTimeValid;

    private readonly Vector2 _boxSize = ImGuiHelpers.ScaledVector2(500, 300);

    public UploadModal(
        IPluginLog log, 
        Configuration config,
        CaptureManager captureManager,
        UploadService uploadService,
        OpcodeService opcodeService) :
        base("Upload Capture", ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.NoResize, true)
    {
        _log = log;
        _config = config;
        _captureManager = captureManager;
        _uploadService = uploadService;
        _opcodeService = opcodeService;
        
        _errorText = "";
        Size = new Vector2(600, 400);
        SizeCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        switch (_step)
        {
            case UploadStep.Settings:
                DrawSettings();
                break;
            case UploadStep.Prepare:
                DrawPrepare();
                break;
            case UploadStep.CensorNotice:
                DrawCensorNotice();
                break;
            case UploadStep.Censoring:
                DrawCensorProgress();
                break;
            case UploadStep.Progress:
                DrawUploadProgress();
                break;
            case UploadStep.Done:
                DrawDone();
                break;
            case UploadStep.Error:
                DrawError();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Begin(Guid captureIdToUpload)
    {
        _step = UploadStep.Settings;
        IsOpen = true;
        _captureId = captureIdToUpload;
        _now = DateTime.UtcNow;

        _metricsTimeSpan = _config.MetricsTimeSpan;
        _metricsTimeValue = _config.MetricsTimeValue;
        _metricsWhenEos = _config.MetricsWhenEos;
        _publicTimeValue = _config.PublicTimeValue;
        _publicTimeSpan = _config.PublicTimeSpan;
        _publicWhenEos = _config.PublicWhenEos;
    }
    
    private void Validate()
    {
        var metricsTimeSpan = Util.Convert(_metricsTimeValue, _metricsTimeSpan);
        var publicTimeSpan = Util.Convert(_publicTimeValue, _publicTimeSpan);

        _metricsTimeValid = metricsTimeSpan >= TimeSpan.FromDays(7) || _metricsWhenEos;
        _publicTimeValid = publicTimeSpan >= TimeSpan.FromDays(14) || _publicWhenEos;
    }

    private void DrawSettings()
    {
        Validate();
        
        var captureStart = _captureManager.GetStartTime(_captureId);
        var captureEnd = _captureManager.GetEndTime(_captureId);
        if (captureStart == null || captureEnd == null)
        {
            Error("Unable to determine capture start or end time. This capture may be corrupt.");
            return;
        }

		var length = (captureEnd - captureStart).Value;
		var lengthString = $"{Math.Floor(length.TotalHours):00}:{length.Minutes:00}:{length.Seconds:00}";
		var startString = captureStart?.ToString(CultureInfo.InvariantCulture);
        var endString = captureEnd?.ToString(CultureInfo.InvariantCulture);
        
        ImGuiHelpers.SafeTextWrapped($"Preparing to upload capture {_captureId}:");

        ImGui.Indent(10f);
        ImGui.TextUnformatted($"Start: {startString}");
        ImGui.TextUnformatted($"End: {endString}");
        ImGui.TextUnformatted($"Duration: {lengthString}");
        ImGui.Unindent(10f);

        ImGui.TextUnformatted("Override my normal settings for this capture upload: ");
        ImGui.SameLine();
        ImGui.Checkbox("##cf_upload_override_settings", ref _overrideSettings);

        ImGui.BeginDisabled(!_overrideSettings);
        ImGui.TextUnformatted("Metrics time for this capture: ");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("""
                                 The 'metrics time' is how long from the time of upload that it will take for the capture to appear in the
                                 Chronofoil metrics system for developers to query.
                                 This does not prevent an individual from querying the metrics system and recreating your capture, but it
                                 makes it incredibly difficult, while still providing useful information to developers.
                                 """);
        ImGui.BeginDisabled(_metricsWhenEos);
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150f);
        ImGui.InputInt("##cf_upload_metric_time_value", ref _metricsTimeValue);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150f);
        if (ImGui.BeginCombo("##cf_upload_metric_time_identifier", _metricsTimeSpan.ToString()))
        {
            foreach (var tsId in Enum.GetValues<TimeSpanIdentifier>())
                if (ImGui.Selectable(tsId.ToString()))
                    _metricsTimeSpan = tsId;
            ImGui.EndCombo();   
        }
        ImGui.EndDisabled();
        ImGui.Checkbox("Do not add this capture to the metrics system until FFXIV is End of Service##cf_metric_time_eos", ref _metricsWhenEos);
        if (!_metricsTimeValid)
            ImGuiHelpers.SafeTextColoredWrapped(ImGuiColors.DalamudRed, "The minimum Metrics time is 7 days from the date of upload.");
        
        ImGui.TextUnformatted("Public time for this capture: ");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("""
                                 The 'public time' is how long from the time of upload that it will take for the capture to be available for raw download.
                                 This provides the full capture file to individuals, which allows them to do whatever - extract information, places visited,
                                 or even replay the packet capture on their own machine if they so choose.
                                 """);
        ImGui.BeginDisabled(_publicWhenEos);
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150f);
        ImGui.InputInt("##cf_upload_public_time_value", ref _publicTimeValue);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150f);
        if (ImGui.BeginCombo("##cf_upload_public_time_identifier", _publicTimeSpan.ToString()))
        {
            foreach (var tsId in Enum.GetValues<TimeSpanIdentifier>())
                if (ImGui.Selectable(tsId.ToString()))
                    _publicTimeSpan = tsId;
            ImGui.EndCombo();   
        }
        ImGui.EndDisabled();
        ImGui.Checkbox("Do not make this capture public until FFXIV is End of Service##cf_public_time_eos", ref _publicWhenEos);
        if (!_publicTimeValid)
            ImGuiHelpers.SafeTextColoredWrapped(ImGuiColors.DalamudRed, "The minimum Public time is 14 days from the date of upload.");
        ImGui.EndDisabled();

        var metricTimeValue = Util.GetTime(_now, _metricsTimeValue, _metricsTimeSpan);
        var publicTimeValue = Util.GetTime(_now, _publicTimeValue, _publicTimeSpan);

        var metricTimeString = Util.GetTimeString(metricTimeValue, _metricsWhenEos);
        var publicTimeString = Util.GetTimeString(publicTimeValue, _publicWhenEos);
        
        ImGui.TextUnformatted("With your settings, these captures will be available:");
        ImGui.TextUnformatted($"For metrics: {metricTimeString}");
        ImGui.TextUnformatted($"For download: {publicTimeString}");

        if (ImGui.Button("Cancel##cf_upload_cancel"))
        {
            End();
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(!IsValid);
        if (ImGui.Button($"Continue##cf_upload_ok"))
        {
            BeginPrepare();
        }
        ImGui.EndDisabled();
    }

    private void BeginPrepare()
    {
        _step = UploadStep.Prepare;
        
        _gameVersion = _uploadService.GetCaptureGameVersion(_captureId);
        if (_gameVersion == null) throw new Exception("Failed to read game version");

        _opcodeTask = Task.Run(() => _opcodeService.UpdateIfNeeded(_gameVersion));
    }
    
    private void DrawPrepare()
    {
        ImGui.TextUnformatted($"Preparing for upload...");

        if (_opcodeTask.IsCompleted)
        {
            if (!_opcodeService.HaveAllOpcodes(_gameVersion))
            {
                _step = UploadStep.CensorNotice;
            }
            else
            {
                BeginCensor();
            }
        }
        else
        {
            try
            {
                if (_opcodeTask.IsFaulted)
                    Error(_opcodeTask.Exception.ToString());
            }
            catch (Exception e)
            {
                Error(e.ToString());
            }
        }
    }

    private void DrawCensorNotice()
    {
        ImGuiHelpers.SafeTextWrapped($"Some sensitive packet opcodes that Chronofoil censors are not available from your client or the server.");
        ImGuiHelpers.SafeTextWrapped($"What this means is that nobody on the current game version has performed a certain task (for example, sending a letter) - including you.");
        ImGuiHelpers.SafeTextWrapped($"Chronofoil tracks these on your game client - if you've sent a censorable packet, Chronofoil will know its opcode and censor it accordingly.");
        ImGuiHelpers.SafeTextWrapped($"You should be safe to upload, considering if you've performed one of these censorable actions, Chronofoil would know the opcode.");
        ImGui.Dummy(new Vector2(0, 10f));
        ImGuiHelpers.SafeTextWrapped($"However, you can opt to not upload now and try again later when all censorable opcodes are known.");

        if (ImGui.Button($"Cancel##cf_upload_censornotice_cancel"))
        {
            End();
        }
        ImGui.SameLine();
        if (ImGui.Button($"Continue##cf_upload_censornotice_ok"))
        {
            BeginCensor();
        }
    }

    private void BeginCensor()
    {
        _step = UploadStep.Censoring;

        var opcodes = _opcodeService.GetCensoredOpcodes(_gameVersion);
        var toCensor = new List<CensorTarget>();
        foreach (var opcode in opcodes)
        {
            var protocol = Protocol.Zone;
            var direction = opcode.Key.ToString().EndsWith("Down") ? Direction.Rx : Direction.Tx;
            toCensor.Add(new CensorTarget { Protocol = protocol, Direction = direction, Opcode = opcode.Value });
        }

        _censorTask = Task.Run(() => _uploadService.CensorCapture(_captureId, toCensor));
    }

    private void DrawCensorProgress()
    {
        ImGui.TextUnformatted($"Censoring capture...");
        
        if (_censorTask.IsFaulted || _censorTask.IsCanceled)
        {
            Error($"The upload failed. {_censorTask.Exception}");    
        }
        else if (_censorTask is { IsCompleted: true })
        {
            BeginUpload();
        }
    }

    private void BeginUpload()
    {
        _step = UploadStep.Progress;
        _holder = new ProgressHolder();
        var file = _captureManager.GetCaptureFilePath(_captureId);
        if (file == null)
        {
            Error("Capture file not found.");
            return;
        }

        var request = new CaptureUploadRequest
        {
            CaptureId = _captureId,
            MetricTime = Util.GetTime(_now, _metricsTimeValue, _metricsTimeSpan),
            MetricWhenEos = _metricsWhenEos,
            PublicTime = Util.GetTime(_now, _publicTimeValue, _publicTimeSpan),
            PublicWhenEos = _publicWhenEos,
        };

        _response = Task.Run(() => _uploadService.UploadFile(_captureId, request, _holder));
    }

    private void DrawUploadProgress()
    {
        ImGui.TextUnformatted($"Uploading capture...");
        ImGui.ProgressBar(_holder.GetPercent(), new Vector2(-1, 0));

        Console.WriteLine($"{_response}");
        
        if (_response.IsFaulted || _response.IsCanceled || _response is { IsCompleted: true, Result: null })
        {
            Error($"The upload failed. {_response.Exception}");    
        }
        else if (_response is { IsCompleted: true, Result: not null })
        {
            _step = UploadStep.Done;
            _captureManager.SetUploaded(_captureId, true);
        }
    }

    private void DrawError()
    {
        ImGuiHelpers.SafeTextWrapped("Chronofoil encountered an error while uploading the capture.");
        ImGuiHelpers.SafeTextWrapped("The error is as follows:");
        ImGui.InputTextMultiline("##cf_upload_error", ref _errorText, (uint)_errorText.Length, _boxSize, ImGuiInputTextFlags.ReadOnly);

        if (ImGui.Button("Ok##cf_upload_error_ok"))
        {
            End();
        }
    }

    private void DrawDone()
    {
        ImGui.TextUnformatted("Upload successful!");

        var response = _response.Result;

        ImGuiHelpers.SafeTextWrapped($"Your capture {response.CaptureId} will be available at the following times:");
        ImGui.TextUnformatted($"Metrics: {Util.GetTimeString(response.MetricTime, response.MetricWhenEos)}");
        ImGui.TextUnformatted($"Public: {Util.GetTimeString(response.PublicTime, response.PublicWhenEos)}");
        ImGui.TextUnformatted("");
        
        if (ImGui.Button("Done##cf_upload_done"))
        {
            End();
        }
    }

    private void End()
    {
        _captureId = Guid.Empty;
        _gameVersion = "";
        _holder = null;
        _response = null;
        _errorText = "";
        _overrideSettings = false;
        IsOpen = false;
    }
    
    public void Error(string errorText)
    {
        _step = UploadStep.Error;
        _errorText = errorText;
    }
}