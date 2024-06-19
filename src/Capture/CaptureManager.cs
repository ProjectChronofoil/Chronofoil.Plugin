using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Chronofoil.Capture.Session;
using Chronofoil.CaptureFile;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Internal.Notifications;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;

namespace Chronofoil.Capture;

public class CaptureManager
{
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly CaptureSessionManager _captureSessionManager;
    
    private CaptureManagerState _state;
    private readonly HashSet<Guid> _scannedCaptures;

    private string StatePath => Path.Combine(_config.StorageDirectory, "CaptureState.json");
    
    public ICollection<Guid> CapturesByTime
    {
        get
        {
            return _scannedCaptures
                .ToImmutableSortedSet(Comparer<Guid>.Create((c1, c2) => GetStartTime(c2)!.Value.CompareTo(GetStartTime(c1)!.Value)));
        }
    }

    public CaptureManager(
        IPluginLog log,
        Configuration config,
        CaptureSessionManager captureSessionManager,
        INotificationManager notificationManager,
        ICommandManager commandManager)
    {
        _log = log;
        _config = config;
        _captureSessionManager = captureSessionManager;
        _captureSessionManager.CaptureSessionStarted += StartCapture;
        _captureSessionManager.CaptureSessionFinished += FinishCapture;
        _scannedCaptures = new HashSet<Guid>();
        
        Load();
        Scan();
        Notify(notificationManager, commandManager);
    }

    private void Notify(INotificationManager notifs, ICommandManager cmd)
    {
        if (_config is not { NotificationsEnabled: true, UploadCapturesNotificationsEnabled: true }) return;
        var shouldNotify = false;
        
        foreach (var capture in _scannedCaptures)
        {
            var ignored = GetIgnored(capture)!.Value;
            var uploaded = GetUploaded(capture)!.Value;

            if (ignored || uploaded) continue;
            shouldNotify = true;
            break;
        }

        if (!shouldNotify) return;
        
        var notification = notifs.AddNotification(new Notification
        {
            Title = "Chronofoil",
            Content = "Captures are available for upload",
            Type = NotificationType.Info,
            UserDismissable = true
        });
        notification.Click += _ => cmd.ProcessCommand("/chronofoil"); // TODO this is stupid, fix it
    }

    private void Scan()
    {
        _log.Verbose("[CaptureManager] Doing folder scan...");
        foreach (var folder in Directory.GetDirectories(_config.StorageDirectory).Where(dir => Guid.TryParse(dir, out _)))
        {
            // This will finalize any unfinished captures in the directory
            new CaptureReader(folder);
        }
        
        _log.Verbose("[CaptureManager] Doing file scan...");
        foreach (var file in Directory.GetFiles(_config.StorageDirectory, "*.cfcap"))
            ScanAndAddCapture(file);
    }

    private void ScanAndAddCapture(string capturePath)
    {
        _log.Verbose($"[CaptureManager] file: {capturePath}");
        var reader = new CaptureReader(capturePath);
        _log.Verbose($"[CaptureManager] read capture {reader.CaptureInfo.CaptureId}");

        var captureId = Guid.Parse(reader.CaptureInfo.CaptureId);
        var startTime = reader.CaptureInfo.CaptureStartTime.ToDateTime();
        var endTime = reader.CaptureInfo.CaptureEndTime.ToDateTime();

        if (_state.TryGetCapture(captureId, out var existingCapture))
        {
            existingCapture.StartTime = startTime;
            existingCapture.EndTime = endTime;
        }
        else
        {
            var capture = new KnownCapture
            {
                CaptureId = captureId,
                StartTime = startTime,
                EndTime = endTime,
                IsUploaded = false,
                IsIgnored = false
            };
            _state.AddCapture(capture);
        }
        _scannedCaptures.Add(captureId);
            
        _log.Verbose($"Adding scanned capture {captureId}");
    }

    private void Load()
    {
        if (!File.Exists(StatePath))
        {
            _state = new CaptureManagerState();
            Save();
        }
        else
        {
            var text = File.ReadAllText(StatePath);
            try
            {
                _state = JsonConvert.DeserializeObject<CaptureManagerState>(text);
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to read Capture Manager state.");
            }
        }
        _state.Init();
    }
    
    private void Save()
    {
        var text = JsonConvert.SerializeObject(_state);
        var dir = Directory.GetParent(StatePath)!.FullName;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        Dalamud.Utility.Util.WriteAllTextSafe(StatePath, text);
    }

    public void StartCapture(Guid captureId, DateTime startTime)
    {
        if (_state.TryGetCapture(captureId, out _))
        {
            _log.Error($"Tried to add a capture {captureId} but it already exists!");
            return;
        }

        // Create a known because we know this exists here
        var capture = new KnownCapture
        {
            CaptureId = captureId,
            StartTime = startTime,
            EndTime = DateTime.UnixEpoch,
            IsUploaded = false,
            IsIgnored = false
        };
        _scannedCaptures.Add(capture.CaptureId);
        _state.AddCapture(capture);
        Save();
    }

    public void FinishCapture(Guid captureId, DateTime _)
    {
        var path = GetCaptureFilePath(captureId);
        if (path != null)
            ScanAndAddCapture(path);
        Save();
    }

    public void DeleteCapture(Guid captureId)
    {
        var capture = _scannedCaptures.FirstOrDefault(x => x == captureId, Guid.Empty);
        if (capture != Guid.Empty) _scannedCaptures.Remove(capture);
        var path = GetCaptureFilePath(captureId);
        if (path != null)
        {
            var censoredPath = path.Replace(".cfcap", ".ccfcap");
            File.Delete(path);
            File.Delete(censoredPath);

            var contextDirectory = Path.Combine(_config.StorageDirectory, "{captureId}_ctx");
            if (Directory.Exists(contextDirectory))
                Directory.Delete(contextDirectory, recursive: true);
        }
        _state.RemoveCapture(captureId);
        Save();
    }

    public void SetUploaded(Guid captureId, bool state)
    {
        if (_state.TryGetCapture(captureId, out var capture))
            capture.IsUploaded = state;
        else
            _log.Error($"Tried to mark a capture that didn't exist as uploaded: {captureId}");
        Save();
    }

    public void SetIgnored(Guid captureId, bool state)
    {
        if (_state.TryGetCapture(captureId, out var capture))
            capture.IsIgnored = state;
        else
            _log.Error($"Tried to mark a capture that didn't exist as uploaded: {captureId}");
        Save();
    }

    public DateTime? GetStartTime(Guid captureId)
    {
        if (_state.TryGetCapture(captureId, out var capture))
            return capture.StartTime;
        _log.Error($"Tried to get capture start time for a capture that didn't exist: {captureId}");
        return null;
    }
    
    public DateTime? GetEndTime(Guid captureId)
    {
        if (_state.TryGetCapture(captureId, out var capture))
            return capture.EndTime;
        _log.Error($"Tried to get capture end time for a capture that didn't exist: {captureId}");
        return null;
    }
    
    public bool? GetUploaded(Guid captureId)
    {
        if (_state.TryGetCapture(captureId, out var capture))
            return capture.IsUploaded;
        _log.Error($"Tried to get uploaded for a capture that didn't exist: {captureId}");
        return null;
    }

    public bool? GetIgnored(Guid captureId)
    {
        if (_state.TryGetCapture(captureId, out var capture))
            return capture.IsIgnored;
        _log.Error($"Tried to get ignored for a capture that didn't exist: {captureId}");
        return false;
    }

    public bool? GetCapturing(Guid captureId)
    {
        if (_state.TryGetCapture(captureId, out var capture))
            return _captureSessionManager is { IsCapturing: true, Session: not null }
                   && captureId == _captureSessionManager.Session.CaptureId;
        _log.Error($"Tried to get capturing for a capture that didn't exist: {captureId}");
        return null;
    }

    public string? GetCaptureFilePath(Guid captureId)
    {
        if (_state.TryGetCapture(captureId, out var capture))
            return Path.Combine(_config.StorageDirectory, $"{captureId}.cfcap");
        _log.Error($"Tried to get capture path for a capture that didn't exist: {captureId}");
        return null;
    }
    
    public string? GetCensoredCaptureFilePath(Guid captureId)
    {
        if (_state.TryGetCapture(captureId, out var capture))
            return Path.Combine(_config.StorageDirectory, $"{captureId}.ccfcap");
        _log.Error($"Tried to get capture path for a capture that didn't exist: {captureId}");
        return null;
    }
}

// A capture that we know of whether it still exists on disk or not
internal class KnownCapture
{
    public Guid CaptureId { get; init; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsUploaded { get; set; }
    public bool IsIgnored { get; set; }
}

internal class CaptureManagerState
{
    [JsonProperty]
    private Dictionary<Guid, KnownCapture> _captures;
    
    public CaptureManagerState() { }

    public void Init()
    {
        _captures ??= new();
    }

    public HashSet<Guid> GetCaptures()
    {
        return _captures.Keys.ToHashSet();
    }

    public bool TryGetCapture(Guid captureId, out KnownCapture knownCapture)
    {
        return _captures.TryGetValue(captureId, out knownCapture);
    }

    public void TryAddCapture(KnownCapture capture)
    {
        if (_captures.ContainsKey(capture.CaptureId)) return;
        var storedCapture = new KnownCapture
        {
            CaptureId = capture.CaptureId,
            StartTime = capture.StartTime,
            EndTime = capture.EndTime,
            IsIgnored = false,
            IsUploaded = false,
        };
        AddCapture(storedCapture);
    }

    public void AddCapture(KnownCapture knownCapture)
    {
        _captures.Add(knownCapture.CaptureId, knownCapture);
    }

    public void RemoveCapture(Guid captureId)
    {
        _captures.Remove(captureId);
    }
}