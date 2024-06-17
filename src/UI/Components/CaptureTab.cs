using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chronofoil.Capture;
using Chronofoil.Web;
using Dalamud.Plugin.Services;
using ImGuiNET;

namespace Chronofoil.UI.Components;

public class CaptureTab
{
    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private readonly CaptureManager _captureManager;
    private readonly UploadModal _uploadModal;
    private readonly ChronofoilClient _client;

    private HashSet<Guid> _remoteDeletions;
    
    public CaptureTab(
	    Configuration config,
	    IPluginLog log,
	    CaptureManager captureManager,
	    UploadModal uploadModal,
	    ChronofoilClient chronofoilClient)
    {
        _config = config;
        _log = log;
        _captureManager = captureManager;
        _uploadModal = uploadModal;
        _client = chronofoilClient;

        _remoteDeletions = new HashSet<Guid>();
    }

    public void Draw()
    {
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX;// | ImGuiTableFlags.SizingFixedFit;
		
		if (ImGui.BeginTable("CapturesTable##cf_capturetab", 8, tableFlags))
		{
			ImGui.TableSetupColumn("Capture ID", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableSetupColumn("Start Time", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableSetupColumn("End Time", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableSetupColumn("Uploaded", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableSetupColumn("Ignored", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableSetupColumn("Upload", ImGuiTableColumnFlags.WidthFixed);
			// ImGui.TableSetupColumn("Folder", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableSetupColumn("Delete", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableSetupColumn("Delete from Server", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableSetupScrollFreeze(0, 1);
			
			ImGui.TableHeadersRow();

			var canUpload = !string.IsNullOrEmpty(_config.AccessToken) && _config.TokenExpiryTime > DateTime.UtcNow;

			foreach (var guid in _captureManager.CapturesByTime)
			{
				var captureStartTime = _captureManager.GetStartTime(guid)!.Value;
				var captureEndTime = _captureManager.GetEndTime(guid)!.Value;
				var uploaded = _captureManager.GetUploaded(guid)!.Value;
				var ignored = _captureManager.GetIgnored(guid)!.Value;
				var capturing = _captureManager.GetCapturing(guid)!.Value;
				
				ImGui.TableNextRow();
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(guid.ToString());
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(captureStartTime.ToLocalTime().ToString(CultureInfo.CurrentCulture));
				ImGui.TableNextColumn();
				var captureEndString = captureEndTime == DateTime.UnixEpoch ? "In Progress" : captureEndTime.ToLocalTime().ToString(CultureInfo.CurrentCulture); 
				ImGui.TextUnformatted(captureEndString);
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(uploaded ? "Yes" : "No");
				ImGui.TableNextColumn();
				if (ImGui.Checkbox($"##{guid}_ignore", ref ignored))
					_captureManager.SetIgnored(guid, ignored);
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip("Ignores this capture. Chronofoil will not notify you about non-uploaded, ignored captures, nor will it let you upload ignored captures.");
				ImGui.TableNextColumn();
				ImGui.BeginDisabled(ignored || capturing || uploaded || !_config.EnableUpload || !canUpload);
				if (ImGui.Button($"Upload##{guid}_upload"))
				{
					_uploadModal.Begin(guid);
				}
				ImGui.EndDisabled();
				
				var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
				if (hovered)
				{
					if (ignored)
						ImGui.SetTooltip("To upload, turn off ignore for this capture.");
					else if (capturing)
						ImGui.SetTooltip("Captures in progress must be finished before they can be uploaded.");
					else if (!_config.EnableUpload)
						ImGui.SetTooltip("To upload, turn on uploading in the settings tab.");
					else if (!canUpload)
						ImGui.SetTooltip("To upload, please Log In to the Chronofoil Service.");
				}

				ImGui.TableNextColumn();
				// if (ImGui.Button($"Open Folder##{guid}_folder"))
				// {
				// 	Dalamud.Utility.Util.OpenLink(Path.Combine(_config.StorageDirectory, guid.ToString()));
				// }
				// ImGui.TableNextColumn();

				var keysDown = ImGui.IsKeyDown(ImGuiKey.LeftShift) && ImGui.IsKeyDown(ImGuiKey.LeftCtrl);
				ImGui.BeginDisabled(!keysDown);
				if (ImGui.Button($"Delete##{guid}_delete"))
				{
					// TODO: make a delete modal? maybe?
					_captureManager.DeleteCapture(guid);
				}
				ImGui.EndDisabled();
				var localDisabledHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
				if (!keysDown && localDisabledHovered)
				{
					ImGui.SetTooltip("Hold Left Shift and Left Control at the same time to delete this capture. This is not reversible.");
				}
				
				ImGui.TableNextColumn();

				var taskInProgress = _remoteDeletions.Contains(guid);
				ImGui.BeginDisabled(!keysDown || taskInProgress || !uploaded);
				if (ImGui.Button($"Delete from Server##{guid}_remote_delete"))
				{
					var task = Task.Run(() => _client.TryDeleteCapture(guid));
					_remoteDeletions.Add(guid);
					task.ContinueWith(t =>
					{
						if (t is { IsCompletedSuccessfully: true, Result: true })
						{
							_captureManager.SetUploaded(guid, false);
							_remoteDeletions.Remove(guid);	
						}
						else
						{
							if (t.IsFaulted)
								_log.Error(task.Exception, "Failed to delete remote capture!");
							else
								_log.Error("Failed to delete remote capture!");
							_remoteDeletions.Remove(guid);
						}
					});
				}
				ImGui.EndDisabled();
				var remoteDisabledHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
				if (remoteDisabledHovered)
				{
					if (taskInProgress)
					{
						ImGui.SetTooltip("Please wait...");
					}
					else if (!keysDown && uploaded)
					{
						ImGui.SetTooltip("Hold Left Shift and Left Control at the same time to delete this capture from the server. This is not reversible.");
					}
				}
			}
			
			ImGui.EndTable();
		}
    }
}