using Microsoft.Win32;
using OptiPulse.Core.Models;

namespace OptiPulse.Core.Services;

public static class RegistryHelper
{
    public const string Hklm = "HKLM";
    public const string Hkcu = "HKCU";

    private static RegistryKey GetBaseKey(string root) =>
        root.Equals(Hklm, StringComparison.OrdinalIgnoreCase)
            ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

    public static object? ReadValue(string root, string subKey, string valueName)
    {
        try
        {
            using var baseKey = GetBaseKey(root);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch
        {
            return null;
        }
    }

    public static int? ReadDword(string root, string subKey, string valueName)
    {
        var raw = ReadValue(root, subKey, valueName);
        if (raw is null) return null;
        return raw switch
        {
            int i => i,
            uint u => unchecked((int)u),
            long l => unchecked((int)l),
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    public static string? ReadString(string root, string subKey, string valueName)
    {
        var raw = ReadValue(root, subKey, valueName);
        return raw?.ToString();
    }

    public static RegistryValueBackup CaptureBackup(string root, string subKey, string valueName)
    {
        var backup = new RegistryValueBackup
        {
            Root = root,
            SubKey = subKey,
            ValueName = valueName,
            Existed = false
        };

        try
        {
            using var baseKey = GetBaseKey(root);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            if (key is null) return backup;

            var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null) return backup;

            var kind = key.GetValueKind(valueName);
            backup.Existed = true;
            backup.ValueKind = kind.ToString();
            backup.SerializedValue = SerializeValue(value, kind);
        }
        catch
        {
            // Keep Existed = false if unreadable
        }

        return backup;
    }

    public static void WriteValue(string root, string subKey, string valueName, object value, RegistryValueKind kind)
    {
        using var baseKey = GetBaseKey(root);
        using var key = baseKey.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException($"Не удалось открыть или создать раздел реестра {root}\\{subKey}");
        key.SetValue(valueName, value, kind);
    }

    public static void DeleteValue(string root, string subKey, string valueName)
    {
        using var baseKey = GetBaseKey(root);
        using var key = baseKey.OpenSubKey(subKey, writable: true);
        if (key is null) return;
        if (key.GetValue(valueName) is not null)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    public static void RestoreBackup(RegistryValueBackup backup)
    {
        if (!backup.Existed)
        {
            DeleteValue(backup.Root, backup.SubKey, backup.ValueName);
            return;
        }

        if (!Enum.TryParse<RegistryValueKind>(backup.ValueKind, out var kind))
        {
            kind = RegistryValueKind.String;
        }

        var value = DeserializeValue(backup.SerializedValue, kind);
        WriteValue(backup.Root, backup.SubKey, backup.ValueName, value, kind);
    }

    /// <summary>
    /// Captures a backup only if this exact key+value hasn't been backed up yet for this tweak,
    /// then writes the new registry value.
    /// </summary>
    public static void BackupAndWrite(
        TweakBackupEntry entry,
        string root,
        string subKey,
        string valueName,
        object value,
        RegistryValueKind kind)
    {
        var alreadyBackedUp = entry.RegistryValues.Any(b =>
            b.Root.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            b.SubKey.Equals(subKey, StringComparison.OrdinalIgnoreCase) &&
            b.ValueName.Equals(valueName, StringComparison.OrdinalIgnoreCase));

        if (!alreadyBackedUp)
        {
            entry.RegistryValues.Add(CaptureBackup(root, subKey, valueName));
        }

        WriteValue(root, subKey, valueName, value, kind);
    }

    public static List<string> EnumerateSubKeyNames(string root, string subKey)
    {
        var result = new List<string>();
        try
        {
            using var baseKey = GetBaseKey(root);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            if (key is not null)
            {
                result.AddRange(key.GetSubKeyNames());
            }
        }
        catch
        {
            // Ignore
        }

        return result;
    }

    private static string SerializeValue(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.Binary when value is byte[] bytes => Convert.ToBase64String(bytes),
        RegistryValueKind.MultiString when value is string[] arr => string.Join("\u001F", arr),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static object DeserializeValue(string? serialized, RegistryValueKind kind)
    {
        var s = serialized ?? string.Empty;
        return kind switch
        {
            RegistryValueKind.DWord => int.TryParse(s, out var i) ? i : 0,
            RegistryValueKind.QWord => long.TryParse(s, out var l) ? l : 0L,
            RegistryValueKind.Binary => string.IsNullOrEmpty(s) ? Array.Empty<byte>() : Convert.FromBase64String(s),
            RegistryValueKind.MultiString => string.IsNullOrEmpty(s) ? Array.Empty<string>() : s.Split('\u001F'),
            _ => s
        };
    }
}
