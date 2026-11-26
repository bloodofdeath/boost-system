using Microsoft.Win32;
using OptiPulse.Core.Models;

namespace OptiPulse.Core.Services;

/// <summary>
/// Manages auxiliary system quick-settings surfaced by the PC Analyzer
/// (Windows Transparency, Background UWP Apps, and Telemetry Policy) with full backup &amp; revert support.
/// </summary>
public static class QuickSettingsService
{
    public const string TransparencyKey = "quick_disable_transparency";
    public const string BackgroundUwpKey = "quick_disable_bg_uwp";
    public const string MinimizeTelemetryKey = "quick_minimize_telemetry";

    public static bool IsQuickSettingBackupKey(string key) =>
        key.StartsWith("quick_", StringComparison.OrdinalIgnoreCase);

    public static string GetDisplayTitle(string key) => NormalizeKey(key) switch
    {
        TransparencyKey => "Отключение эффектов прозрачности Windows",
        BackgroundUwpKey => "Запрет фоновой работы UWP-приложений",
        MinimizeTelemetryKey => "Снижение уровня телеметрии Windows",
        _ => key
    };

    public static bool RequiresAdmin(string key, TweakBackupEntry? entry = null)
    {
        if (NormalizeKey(key).Equals(MinimizeTelemetryKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return entry?.RegistryValues.Any(r => r.Root.Equals(RegistryHelper.Hklm, StringComparison.OrdinalIgnoreCase)) == true;
    }

    public static (bool Success, string Message, bool RequiresAdmin) Apply(
        string settingId,
        BackupService backup,
        AppLogger logger)
    {
        var normalized = NormalizeKey(settingId);
        switch (normalized)
        {
            case TransparencyKey:
            {
                var entry = backup.GetOrCreateTweakBackup(TransparencyKey);
                RegistryHelper.BackupAndWrite(
                    entry,
                    RegistryHelper.Hkcu,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "EnableTransparency",
                    0,
                    RegistryValueKind.DWord);
                backup.SaveTweakBackup(entry);
                logger.Success("QuickSetting", "Эффекты прозрачности Windows отключены.");
                return (true, "Эффекты прозрачности Windows отключены.", false);
            }
            case BackgroundUwpKey:
            {
                var entry = backup.GetOrCreateTweakBackup(BackgroundUwpKey);
                RegistryHelper.BackupAndWrite(
                    entry,
                    RegistryHelper.Hkcu,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications",
                    "GlobalUserDisabled",
                    1,
                    RegistryValueKind.DWord);
                backup.SaveTweakBackup(entry);
                logger.Success("QuickSetting", "Фоновая работа UWP-приложений отключена.");
                return (true, "Фоновая работа приложений Microsoft Store (UWP) отключена.", false);
            }
            case MinimizeTelemetryKey:
            {
                if (!AdminHelper.IsAdministrator())
                {
                    return (false, "Для изменения политики телеметрии Windows требуются права администратора.", true);
                }

                var entry = backup.GetOrCreateTweakBackup(MinimizeTelemetryKey);
                RegistryHelper.BackupAndWrite(
                    entry,
                    RegistryHelper.Hklm,
                    @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                    "AllowTelemetry",
                    0,
                    RegistryValueKind.DWord);
                backup.SaveTweakBackup(entry);
                logger.Success("QuickSetting", "Политика телеметрии Windows установлена на минимум (AllowTelemetry = 0).");
                return (true, "Телеметрия Windows снижена до минимального уровня.", false);
            }
            default:
                return (false, $"Неизвестная быстрая настройка: {settingId}", false);
        }
    }

    public static (bool Success, string Message, bool RequiresAdmin) Revert(
        string backupKey,
        BackupService backup,
        AppLogger logger)
    {
        var entry = backup.GetTweakBackup(backupKey);
        if (entry is null)
        {
            return (false, $"Резервная копия для «{GetDisplayTitle(backupKey)}» не найдена.", false);
        }

        if (RequiresAdmin(backupKey, entry) && !AdminHelper.IsAdministrator())
        {
            return (false, $"Для восстановления «{GetDisplayTitle(backupKey)}» требуются права администратора.", true);
        }

        try
        {
            foreach (var reg in entry.RegistryValues)
            {
                RegistryHelper.RestoreBackup(reg);
            }

            backup.RemoveTweakBackup(backupKey);
            var title = GetDisplayTitle(backupKey);
            logger.Info("QuickSetting", $"Восстановлена настройка: {title}");
            return (true, $"Восстановлено: {title}.", false);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, $"Недостаточно прав доступа для восстановления «{GetDisplayTitle(backupKey)}».", true);
        }
        catch (Exception ex)
        {
            logger.Error("QuickSetting", $"Ошибка восстановления «{GetDisplayTitle(backupKey)}»: {ex.Message}");
            return (false, $"Ошибка восстановления: {ex.Message}", false);
        }
    }

    private static string NormalizeKey(string id) => id.ToLowerInvariant() switch
    {
        "disable_transparency" or TransparencyKey => TransparencyKey,
        "disable_bg_uwp" or BackgroundUwpKey => BackgroundUwpKey,
        "minimize_telemetry" or MinimizeTelemetryKey => MinimizeTelemetryKey,
        _ => id
    };
}
