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

    /// <summary>
    /// Returns a working copy of an existing backup entry (if previously persisted) or a fresh entry.
    /// Does not mutate the persisted store until <see cref="SaveTweakBackup"/> is called.
    /// </summary>
    public TweakBackupEntry GetOrCreateTweakBackup(string tweakId)
    {
        lock (_sync)
        {
            return _store.Tweaks.TryGetValue(tweakId, out var existing)
                ? existing.Clone()
                : new TweakBackupEntry { TweakId = tweakId, SavedAtUtc = DateTime.UtcNow };
        }
    }

    public TweakBackupEntry? GetTweakBackup(string tweakId)
    {
        lock (_sync)
        {
            return _store.Tweaks.TryGetValue(tweakId, out var entry) ? entry.Clone() : null;
        }
    }

    public void SaveTweakBackup(TweakBackupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_sync)
        {
            var copy = entry.Clone();
            copy.SavedAtUtc = DateTime.UtcNow;
            _store.Tweaks[copy.TweakId] = copy;
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
        ArgumentNullException.ThrowIfNull(backup);
        lock (_sync)
        {
            if (!_store.DisabledServices.ContainsKey(backup.ServiceName))
            {
                var copy = backup.Clone();
                copy.SavedAtUtc = DateTime.UtcNow;
                _store.DisabledServices[copy.ServiceName] = copy;
                PersistInternal();
            }
        }
    }

    public ServiceBackup? GetDisabledService(string serviceName)
    {
        lock (_sync)
        {
            return _store.DisabledServices.TryGetValue(serviceName, out var b) ? b.Clone() : null;
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
        ArgumentNullException.ThrowIfNull(backup);
        lock (_sync)
        {
            _store.DisabledStartupItems[backup.Id] = backup.Clone();
            PersistInternal();
        }
    }

    public StartupItemBackup? GetDisabledStartupItem(string id)
    {
        lock (_sync)
        {
            return _store.DisabledStartupItems.TryGetValue(id, out var b) ? b.Clone() : null;
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
            var snap = new BackupStore
            {
                SchemaVersion = _store.SchemaVersion,
                LastUpdatedUtc = _store.LastUpdatedUtc
            };
            foreach (var kv in _store.Tweaks) snap.Tweaks[kv.Key] = kv.Value.Clone();
            foreach (var kv in _store.DisabledServices) snap.DisabledServices[kv.Key] = kv.Value.Clone();
            foreach (var kv in _store.DisabledStartupItems) snap.DisabledStartupItems[kv.Key] = kv.Value.Clone();
            return snap;
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
            try
            {
                var corruptBackup = _storePath + $".corrupt_{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                if (File.Exists(_storePath)) File.Copy(_storePath, corruptBackup, overwrite: true);
            }
            catch
            {
                // Ignore secondary failure
            }

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
