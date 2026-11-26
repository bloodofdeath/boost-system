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

    public RegistryValueBackup Clone() => new()
    {
        Root = Root,
        SubKey = SubKey,
        ValueName = ValueName,
        Existed = Existed,
        ValueKind = ValueKind,
        SerializedValue = SerializedValue
    };
}

public sealed class ServiceBackup
{
    public string ServiceName { get; set; } = string.Empty;
    public int OriginalStartMode { get; set; } = 3; // 2=Auto, 3=Manual, 4=Disabled
    public bool WasDelayedAutoStart { get; set; }
    public bool WasRunning { get; set; }
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;

    public ServiceBackup Clone() => new()
    {
        ServiceName = ServiceName,
        OriginalStartMode = OriginalStartMode,
        WasDelayedAutoStart = WasDelayedAutoStart,
        WasRunning = WasRunning,
        SavedAtUtc = SavedAtUtc
    };
}

public sealed class StartupItemBackup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string ValueKind { get; set; } = "String";
    public string Root { get; set; } = string.Empty;
    public string SubKey { get; set; } = string.Empty;
    public string? OriginalFilePath { get; set; }
    public string? BackupFilePath { get; set; }
    public bool RequiresAdmin { get; set; }
    public DateTime DisabledAtUtc { get; set; } = DateTime.UtcNow;

    public StartupItemBackup Clone() => new()
    {
        Id = Id,
        Name = Name,
        Location = Location,
        Command = Command,
        ValueKind = ValueKind,
        Root = Root,
        SubKey = SubKey,
        OriginalFilePath = OriginalFilePath,
        BackupFilePath = BackupFilePath,
        RequiresAdmin = RequiresAdmin,
        DisabledAtUtc = DisabledAtUtc
    };
}

public sealed class TweakBackupEntry
{
    public string TweakId { get; set; } = string.Empty;
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
    public List<RegistryValueBackup> RegistryValues { get; set; } = new();
    public List<ServiceBackup> Services { get; set; } = new();

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public TweakBackupEntry Clone()
    {
        var copy = new TweakBackupEntry
        {
            TweakId = TweakId,
            SavedAtUtc = SavedAtUtc,
            RegistryValues = RegistryValues.Select(r => r.Clone()).ToList(),
            Services = Services.Select(s => s.Clone()).ToList()
        };
        foreach (var kv in Metadata)
        {
            copy.Metadata[kv.Key] = kv.Value;
        }
        return copy;
    }
}

public sealed class BackupStore
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, TweakBackupEntry> Tweaks { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, ServiceBackup> DisabledServices { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, StartupItemBackup> DisabledStartupItems { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
