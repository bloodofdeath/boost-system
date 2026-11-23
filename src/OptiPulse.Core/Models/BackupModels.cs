using System.Text.Json.Serialization;

namespace OptiPulse.Core.Models;

public sealed class RegistryValueBackup
{
    public string Root { get; set; } = string.Empty; // HKLM or HKCU
    public string SubKey { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public bool Existed { get; set; }
    public string ValueKind { get; set; } = string.Empty;
    public string? SerializedValue { get; set; }
}

public sealed class ServiceBackup
{
    public string ServiceName { get; set; } = string.Empty;
    public int OriginalStartMode { get; set; } = 3; // 2=Auto, 3=Manual, 4=Disabled
    public bool WasDelayedAutoStart { get; set; }
    public bool WasRunning { get; set; }
}

public sealed class StartupItemBackup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Root { get; set; } = string.Empty;
    public string SubKey { get; set; } = string.Empty;
    public string? OriginalFilePath { get; set; }
    public string? BackupFilePath { get; set; }
    public DateTime DisabledAtUtc { get; set; }
}

public sealed class TweakBackupEntry
{
    public string TweakId { get; set; } = string.Empty;
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
    public List<RegistryValueBackup> RegistryValues { get; set; } = new();
    public List<ServiceBackup> Services { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public sealed class BackupStore
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, TweakBackupEntry> Tweaks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ServiceBackup> DisabledServices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, StartupItemBackup> DisabledStartupItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
