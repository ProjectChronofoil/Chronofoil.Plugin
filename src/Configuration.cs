using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.IO;
using Chronofoil.Utility;

namespace Chronofoil;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public string StorageDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "chronofoil");

    public bool EnableContext { get; set; } = false;
    public bool EnableUpload { get; set; } = false;

    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime TokenExpiryTime { get; set; } = DateTime.UnixEpoch;
    public int MaxAcceptedTosVersion { get; set; } = 0;
    public int MaxKnownTosVersion { get; set; } = 0;

    public int MetricsTimeValue { get; set; } = 7;
    public TimeSpanIdentifier MetricsTimeSpan { get; set; } = TimeSpanIdentifier.Days;
    public bool MetricsWhenEos { get; set; }

    public int PublicTimeValue { get; set; } = 1;
    public TimeSpanIdentifier PublicTimeSpan { get; set; } = TimeSpanIdentifier.Months;
    public bool PublicWhenEos { get; set; } = true;
    
    public bool NotificationsEnabled { get; set; }
    public bool CaptureBeginNotificationsEnabled { get; set; }
    public bool CaptureEndNotificationsEnabled { get; set; }
    public bool UploadCapturesNotificationsEnabled { get; set; }

    [NonSerialized] private DalamudPluginInterface _pluginInterface;

    public void Initialize(DalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
    }

    public void Save()
    {
        _pluginInterface.SavePluginConfig(this);
    }
}