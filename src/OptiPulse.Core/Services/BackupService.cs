using System.Text.Json;
using OptiPulse.Core.Models;

namespace OptiPulse.Core.Services;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly string _backupDir;
    private readonly string _storePath;
    private BackupStore _store;

    public BackupService(string? customBackupDirectory = null)
    {
        _backupDir = customBackupDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OptiPulse",
            "backups");

        Directory.CreateDirectory(_backupDir);
        _storePath = Path.Combine(_backupDir, "state_backup.json");
        _store = LoadStoreInternal();
    }

    public string BackupDirectory => _backupDir;
    public string StoreFilePath => _storePath;

    public string GetSubFolder(string name)
    {
        var path = Path.Combine(_backupDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public TweakBackupEntry GetOrCreateTweakBackup(string tweakId)
    {
        lock (_sync)
        {
            if (!_store.Tweaks.TryGetValue(tweakId, out var entry))
            {
                entry = new TweakBackupEntry { TweakId = tweakId, SavedAtUtc = DateTime.UtcNow };
                _store.Tweaks[tweakId] = entry;
            }
            return entry;
        }
    }

    public TweakBackupEntry? GetTweakBackup(string tweakId)
    {
        lock (_sync)
        {
            return _store.Tweaks.TryGetValue(tweakId, out var entry) ? entry : null;
        }
    }

    public void SaveTweakBackup(TweakBackupEntry entry)
    {
        lock (_sync)
        {
            entry.SavedAtUtc = DateTime.UtcNow;
            _store.Tweaks[entry.TweakId] = entry;
            PersistInternal();
        }
    }

    public void RemoveTweakBackup(string tweakId)
    {
        lock (_sync)
        {
            if (_store.Tweaks.Remove(tweakId))
            {
                PersistInternal();
            }
        }
    }

    public void SaveDisabledService(ServiceBackup backup)
    {
        lock (_sync)
        {
            if (!_store.DisabledServices.ContainsKey(backup.ServiceName))
            {
                _store.DisabledServices[backup.ServiceName] = backup;
                PersistInternal();
            }
        }
    }

    public ServiceBackup? GetDisabledService(string serviceName)
    {
        lock (_sync)
        {
            return _store.DisabledServices.TryGetValue(serviceName, out var b) ? b : null;
        }
    }

    public void RemoveDisabledService(string serviceName)
    {
        lock (_sync)
        {
            if (_store.DisabledServices.Remove(serviceName))
            {
                PersistInternal();
            }
        }
    }

    public void SaveDisabledStartupItem(StartupItemBackup backup)
    {
        lock (_sync)
        {
            _store.DisabledStartupItems[backup.Id] = backup;
            PersistInternal();
        }
    }

    public StartupItemBackup? GetDisabledStartupItem(string id)
    {
        lock (_sync)
        {
            return _store.DisabledStartupItems.TryGetValue(id, out var b) ? b : null;
        }
    }

    public void RemoveDisabledStartupItem(string id)
    {
        lock (_sync)
        {
            if (_store.DisabledStartupItems.Remove(id))
            {
                PersistInternal();
            }
        }
    }

    public BackupStore GetSnapshot()
    {
        lock (_sync)
        {
            var json = JsonSerializer.Serialize(_store, JsonOptions);
            return JsonSerializer.Deserialize<BackupStore>(json, JsonOptions) ?? new BackupStore();
        }
    }

    public int TotalBackupEntriesCount
    {
        get
        {
            lock (_sync)
            {
                return _store.Tweaks.Count + _store.DisabledServices.Count + _store.DisabledStartupItems.Count;
            }
        }
    }

    private BackupStore LoadStoreInternal()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                return new BackupStore();
            }

            var json = File.ReadAllText(_storePath);
            return JsonSerializer.Deserialize<BackupStore>(json, JsonOptions) ?? new BackupStore();
        }
        catch
        {
            return new BackupStore();
        }
    }

    private void PersistInternal()
    {
        _store.LastUpdatedUtc = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(_store, JsonOptions);
        var tempPath = _storePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _storePath, overwrite: true);
    }
}
