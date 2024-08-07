using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Chronofoil.Capture;
using Chronofoil.Common.Capture;
using Chronofoil.Utility;
using Chronofoil.Web;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using ImGuiNET;

namespace Chronofoil.UI.Components;

public class UploadsTab
{
    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private readonly CaptureManager _captureManager;
    private readonly ChronofoilClient _client;

    private readonly HashSet<Guid> _remoteDeletions;
    private bool _isWaiting;
    private bool _drewLastFrame;
    
    private List<CaptureListElement>? _uploadedCaptures;

    public UploadsTab(
	    Configuration config,
	    IPluginLog log,
	    CaptureManager captureManager,
	    ChronofoilClient chronofoilClient)
    {
        _config = config;
        _log = log;
        _captureManager = captureManager;
        _client = chronofoilClient;
        
        _remoteDeletions = new HashSet<Guid>();
    }
    
    public void Update(bool isDrawing)
    {
	    if (isDrawing && !_drewLastFrame)
	    {
		    TabFocused();
	    }
	    else if (!isDrawing && _drewLastFrame)
	    {
		    TabUnfocused();
	    }
	    _drewLastFrame = isDrawing;
    }

    private void TabFocused()
    {
	    UpdateUploadList();
    }

    private void UpdateUploadList()
    {
	    _isWaiting = true;
	    Task.Run(() => _client.GetCaptureList())
		    .ContinueWith(task =>
		    {
			    _isWaiting = false;
			    _uploadedCaptures = task.Result?.Captures;
			    _uploadedCaptures?.Sort((c1, c2) => c2.StartTime.CompareTo(c1.StartTime));

			    if (task.Exception != null)
				    _log.Error(task.Exception, "Failed to get capture list!");
			    else
			    {
				    // TODO: move this to somewhere more appropriate
				    if (_uploadedCaptures == null) return;
				    var localGuids = _captureManager.CapturesByTime;
				    
				    var localGuidsUploaded = localGuids.Where(guid => _captureManager.GetUploaded(guid)!.Value).ToHashSet();
				    var uploadedGuids = _uploadedCaptures.Select(e => e.CaptureId).ToHashSet();
				    
				    var uploadedNotMarked = uploadedGuids.Except(localGuidsUploaded);
				    var markedNotUploaded = localGuidsUploaded.Except(uploadedGuids);

				    foreach (var guid in uploadedNotMarked)
					    _captureManager.SetUploaded(guid, true);
				    foreach (var guid in markedNotUploaded)
					    _captureManager.SetUploaded(guid, false);
			    }
		    });
    }
    
    private void TabUnfocused()
    {
	    
    }

    public void Draw()
    {
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX;// | ImGuiTableFlags.SizingFixedFit;
        
        if (_isWaiting)
        {
	        ImGui.TextUnformatted("Please wait...");
        }
        else if (_uploadedCaptures == null)
        {
	        ImGui.TextUnformatted("Failed to get capture list.");
        }
        else
        {
			var earliestContribution = DateTime.MaxValue;
			var latestContribution = DateTime.MinValue;
			var accumulatedLength = new TimeSpan(0, 0, 0);

			using var _ = ImRaii.Child("UploadsChild##cf_uploadstab");

			var footerHeight = ImGui.CalcTextSize("test").Y;

	        if (ImGui.BeginTable("UploadsTable##cf_uploadstab", 7, tableFlags, new Vector2(-1, -1 * footerHeight - ImGui.GetStyle().WindowPadding.Y)))
	        {
		        ImGui.TableSetupColumn("Capture ID", ImGuiTableColumnFlags.WidthFixed);
		        ImGui.TableSetupColumn("Start Time", ImGuiTableColumnFlags.WidthFixed);
		        ImGui.TableSetupColumn("End Time", ImGuiTableColumnFlags.WidthFixed);
				ImGui.TableSetupColumn("Length", ImGuiTableColumnFlags.WidthFixed);
				ImGui.TableSetupColumn("Metrics Time", ImGuiTableColumnFlags.WidthFixed);
		        ImGui.TableSetupColumn("Public Time", ImGuiTableColumnFlags.WidthFixed);
		        ImGui.TableSetupColumn("Delete from Server", ImGuiTableColumnFlags.WidthFixed);
		        ImGui.TableSetupScrollFreeze(0, 1);

		        ImGui.TableHeadersRow();

		        foreach (var element in _uploadedCaptures)
		        {
			        var guid = element.CaptureId;
			        var captureStartStr = element.StartTime.ToLocalTime().ToString(CultureInfo.CurrentCulture);
			        var captureEndStr = element.EndTime.ToLocalTime().ToString(CultureInfo.CurrentCulture);
					var length = element.EndTime.ToLocalTime() - element.StartTime.ToLocalTime();
					var lengthStr = string.Format("{0:00}:{1:00}:{2:00}", Math.Floor(length.TotalHours), length.Minutes, length.Seconds);
					var metricsTimeStr = Util.GetTimeString(element.MetricsTime, element.MetricsWhenEos);
			        var publicTimeStr = Util.GetTimeString(element.PublicTime, element.PublicWhenEos);

					if (earliestContribution > element.StartTime.ToLocalTime()) earliestContribution = element.StartTime.ToLocalTime();
					if (latestContribution < element.EndTime.ToLocalTime()) latestContribution = element.EndTime.ToLocalTime();
					accumulatedLength += length;

					ImGui.TableNextRow();
			        ImGui.TableNextColumn();
			        ImGui.TextUnformatted(guid.ToString());
			        ImGui.TableNextColumn();
			        ImGui.TextUnformatted(captureStartStr);
			        ImGui.TableNextColumn();
			        ImGui.TextUnformatted(captureEndStr);
			        ImGui.TableNextColumn();
					ImGui.TextUnformatted(lengthStr);
					ImGui.TableNextColumn();
					ImGui.TextUnformatted(metricsTimeStr);
			        ImGui.TableNextColumn();
			        ImGui.TextUnformatted(publicTimeStr);
			        ImGui.TableNextColumn();

			        var taskInProgress = _remoteDeletions.Contains(guid);
			        var keysDown = ImGui.IsKeyDown(ImGuiKey.LeftShift) && ImGui.IsKeyDown(ImGuiKey.LeftCtrl);
			        ImGui.BeginDisabled(!keysDown || taskInProgress);
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
						        UpdateUploadList();
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
				        else if (!keysDown)
				        {
					        ImGui.SetTooltip("Hold Left Shift and Left Control at the same time to delete this capture from the server. This is not reversible.");
				        }
			        }
		        }

				ImGui.EndTable();
	        }

	        var startStr = $"First contribution start: {earliestContribution}";
	        var endStr = $"Last contribution end: {latestContribution}";
	        var totalTimeStr = $"{Math.Floor(accumulatedLength.TotalDays)}:{accumulatedLength.Hours:00}:{accumulatedLength.Minutes:00}:{accumulatedLength.Seconds:00}";
	        var totalStr = $"Total time contributed: {totalTimeStr}";
	        
	        var endWidth = ImGui.CalcTextSize(endStr).X;
	        var totalWidth = ImGui.CalcTextSize(totalStr).X;
	        var availableWidth = ImGui.GetWindowContentRegionMax().X;
	        
	        var cursorY = ImGui.GetCursorPosY();
	        
	        var totalStart = availableWidth - totalWidth;
	        var endStart = (availableWidth / 2) - (endWidth / 2);
	        
	        // Draw start
	        ImGui.TextUnformatted(startStr);
	        
	        // Draw end
	        ImGui.SetCursorPosX(endStart);
	        ImGui.SetCursorPosY(cursorY);
	        ImGui.TextUnformatted(endStr);
	        
	        //Draw total
	        ImGui.SetCursorPosX(totalStart);
	        ImGui.SetCursorPosY(cursorY);
	        ImGui.TextUnformatted(totalStr);
        }
    }
}