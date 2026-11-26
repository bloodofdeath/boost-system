using Microsoft.Win32;
using OptiPulse.Core.Models;

namespace OptiPulse.Core.Services;

/// <summary>
/// Scans Windows startup entries (HKCU/HKLM Run keys, WOW6432Node, and Startup folders),
/// respects Task Manager StartupApproved state, and supports reversible disable/restore.
/// </summary>
public static class StartupManagerService
{
    private static readonly (string Keyword, string FriendlyNote)[] HeavyStartupHints =
    [
        ("OneDrive", "Синхронизация облака в фоне нагружает диск и сеть во время игр."),
        ("Discord", "Запускается при старте Windows и занимает память в фоне — удобнее запускать вручную."),
        ("Steam", "Игровой клиент в автозагрузке замедляет старт системы и держит фоновые обновления."),
        ("EpicGamesLauncher", "Тяжёлый лаунчер в автозагрузке — запускайте только когда нужен."),
        ("Teams", "Microsoft Teams в автозагрузке активно расходует ОЗУ и CPU в фоне."),
        ("Spotify", "Музыкальный клиент в автозагрузке расходует ОЗУ и замедляет загрузку ПК."),
        ("Skype", "Редко используемый мессенджер в автозагрузке."),
        ("Telegram", "Мессенджер в автозагрузке — можно запускать вручную для более быстрого старта ПК."),
        ("Adobe", "Фоновые агенты обновлений Adobe (Creative Cloud / ARM) постоянно работают в памяти."),
        ("CCXProcess", "Фоновый процесс Adobe Creative Cloud."),
        ("EdgeAutoLaunch", "Предзагрузка Microsoft Edge в фоне занимает ОЗУ даже без открытого браузера."),
        ("RiotClient", "Лаунчер Riot Games в автозагрузке."),
        ("Battle.net", "Лаунчер Battle.net в автозагрузке."),
        ("Overwolf", "Игровой оверлей Overwolf часто снижает FPS и вызывает микрофризы."),
        ("Wallpaper", "Живые обои в автозагрузке нагружают GPU и VRAM."),
        ("Cortana", "Устаревший голосовой помощник в фоне."),
        ("uTorrent", "Торрент-клиент в автозагрузке создаёт фоновую нагрузку на сеть и диск."),
        ("BitTorrent", "Торрент-клиент в автозагрузке создаёт фоновую нагрузку на сеть и диск.")
    ];

    public static List<StartupAppItem> ScanStartupApps()
    {
        var items = new List<StartupAppItem>();

        var hkcuRunDisabled = ReadStartupApprovedDisabledSet(RegistryHelper.Hkcu, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
        var hklmRunDisabled = ReadStartupApprovedDisabledSet(RegistryHelper.Hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
        var hklmRun32Disabled = ReadStartupApprovedDisabledSet(RegistryHelper.Hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32");
        var hkcuFolderDisabled = ReadStartupApprovedDisabledSet(RegistryHelper.Hkcu, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder");
        var hklmFolderDisabled = ReadStartupApprovedDisabledSet(RegistryHelper.Hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder");

        ScanRegistryRunKey(items, RegistryHelper.Hkcu, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU Run (Пользователь)", requiresAdmin: false, hkcuRunDisabled);
        ScanRegistryRunKey(items, RegistryHelper.Hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM Run (Все пользователи)", requiresAdmin: true, hklmRunDisabled);
        ScanRegistryRunKey(items, RegistryHelper.Hklm, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM Run (32-bit)", requiresAdmin: true, hklmRun32Disabled);
        ScanStartupFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Папка автозагрузки (Пользователь)", requiresAdmin: false, hkcuFolderDisabled);
        ScanStartupFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Общая папка автозагрузки", requiresAdmin: true, hklmFolderDisabled);

        return items
            .OrderBy(i => i.IsDisabledInWindows ? 1 : 0)
            .ThenByDescending(i => i.IsRecommendedToDisable)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static (bool Success, string Message, bool RequiresAdmin) DisableStartupItem(
        StartupAppItem item,
        BackupService backupService,
        AppLogger logger)
    {
        if (item.RequiresAdmin && !AdminHelper.IsAdministrator())
        {
            return (false, $"Для отключения элемента автозагрузки «{item.Name}» ({item.Location}) требуются права администратора.", true);
        }

        try
        {
            var backupEntry = new StartupItemBackup
            {
                Id = item.Id,
                Name = item.Name,
                Location = item.Location,
                Command = item.Command,
                Root = item.Root,
                SubKey = item.SubKey,
                OriginalFilePath = item.FilePath,
                RequiresAdmin = item.RequiresAdmin,
                DisabledAtUtc = DateTime.UtcNow
            };

            if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
            {
                var startupBackupDir = backupService.GetSubFolder("startup_shortcuts");
                var dest = Path.Combine(startupBackupDir, $"{Guid.NewGuid():N}_{Path.GetFileName(item.FilePath)}");
                File.Move(item.FilePath, dest, overwrite: true);
                backupEntry.BackupFilePath = dest;
            }
            else if (!string.IsNullOrWhiteSpace(item.Root) && !string.IsNullOrWhiteSpace(item.SubKey))
            {
                // Capture unexpanded string and exact RegistryValueKind (e.g. ExpandString for %SystemRoot%)
                var captured = RegistryHelper.CaptureBackup(item.Root, item.SubKey, item.Name);
                if (captured.Existed)
                {
                    backupEntry.Command = captured.SerializedValue ?? item.Command;
                    backupEntry.ValueKind = string.IsNullOrWhiteSpace(captured.ValueKind) ? "String" : captured.ValueKind;
                }

                RegistryHelper.DeleteValue(item.Root, item.SubKey, item.Name);
            }
            else
            {
                return (false, "Неизвестный источник автозагрузки.", false);
            }

            backupService.SaveDisabledStartupItem(backupEntry);
            logger.Success("Startup", $"Удалено из автозагрузки (с резервной копией): {item.Name}");
            return (true, $"«{item.Name}» убрано из автозагрузки (можно вернуть во вкладке «Бэкапы»).", false);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, $"Нет прав доступа для изменения «{item.Name}».", true);
        }
        catch (Exception ex)
        {
            logger.Error("Startup", $"Ошибка отключения «{item.Name}»: {ex.Message}");
            return (false, $"Ошибка: {ex.Message}", false);
        }
    }

    public static (bool Success, string Message, bool RequiresAdmin) RestoreStartupItem(
        StartupItemBackup backup,
        BackupService backupService,
        AppLogger logger)
    {
        var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        bool isCommonFolder = !string.IsNullOrWhiteSpace(backup.OriginalFilePath) &&
                              !string.IsNullOrWhiteSpace(commonStartup) &&
                              backup.OriginalFilePath.StartsWith(commonStartup, StringComparison.OrdinalIgnoreCase);

        bool needsAdmin = backup.RequiresAdmin ||
                          backup.Root.Equals(RegistryHelper.Hklm, StringComparison.OrdinalIgnoreCase) ||
                          isCommonFolder;

        if (needsAdmin && !AdminHelper.IsAdministrator())
        {
            return (false, $"Для восстановления «{backup.Name}» требуются права администратора.", true);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(backup.BackupFilePath) && !string.IsNullOrWhiteSpace(backup.OriginalFilePath))
            {
                if (File.Exists(backup.BackupFilePath))
                {
                    var dir = Path.GetDirectoryName(backup.OriginalFilePath);
                    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                    File.Move(backup.BackupFilePath, backup.OriginalFilePath, overwrite: true);
                }
            }
            else if (!string.IsNullOrWhiteSpace(backup.Root) && !string.IsNullOrWhiteSpace(backup.SubKey))
            {
                var kind = Enum.TryParse<RegistryValueKind>(backup.ValueKind, ignoreCase: true, out var parsedKind)
                    ? parsedKind
                    : RegistryValueKind.String;
                RegistryHelper.WriteValue(backup.Root, backup.SubKey, backup.Name, backup.Command, kind);
            }

            backupService.RemoveDisabledStartupItem(backup.Id);
            logger.Info("Startup", $"Восстановлено в автозагрузку: {backup.Name}");
            return (true, $"«{backup.Name}» возвращено в автозагрузку.", false);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, $"Недостаточно прав доступа для восстановления «{backup.Name}».", true);
        }
        catch (Exception ex)
        {
            return (false, $"Не удалось восстановить «{backup.Name}»: {ex.Message}", false);
        }
    }

    private static HashSet<string> ReadStartupApprovedDisabledSet(string root, string subKey)
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var baseKey = RegistryHelper.OpenBaseKey(root);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            if (key is null) return disabled;

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is byte[] bytes && bytes.Length > 0 && (bytes[0] & 1) == 1)
                {
                    // Odd first byte (0x01, 0x03, 0x05, 0x07) means Disabled in Windows Task Manager
                    disabled.Add(name);
                }
            }
        }
        catch { }

        return disabled;
    }

    private static void ScanRegistryRunKey(
        List<StartupAppItem> items,
        string root,
        string subKey,
        string locationLabel,
        bool requiresAdmin,
        HashSet<string> disabledInTaskManager)
    {
        try
        {
            using var baseKey = RegistryHelper.OpenBaseKey(root);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            if (key is null) return;

            foreach (var name in key.GetValueNames())
            {
                var cmd = key.GetValue(name)?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(cmd)) continue;

                var isDisabledWin = disabledInTaskManager.Contains(name);
                var (recommended, reason) = isDisabledWin
                    ? (false, "Уже отключено в Диспетчере задач Windows")
                    : MatchHeavyStartup(name, cmd);

                items.Add(new StartupAppItem
                {
                    Id = $"{root}:{subKey}:{name}",
                    Name = name,
                    Command = cmd,
                    Location = locationLabel,
                    Root = root,
                    SubKey = subKey,
                    IsRecommendedToDisable = recommended,
                    RecommendationReason = reason,
                    RequiresAdmin = requiresAdmin,
                    IsDisabledInWindows = isDisabledWin,
                    IsDisabled = isDisabledWin
                });
            }
        }
        catch { }
    }

    private static void ScanStartupFolder(
        List<StartupAppItem> items,
        string folderPath,
        string locationLabel,
        bool requiresAdmin,
        HashSet<string> disabledInTaskManager)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(file);
                var isDisabledWin = disabledInTaskManager.Contains(fileName);

                var (recommended, reason) = isDisabledWin
                    ? (false, "Уже отключено в Диспетчере задач Windows")
                    : MatchHeavyStartup(name, file);

                items.Add(new StartupAppItem
                {
                    Id = $"FOLDER:{file}",
                    Name = name,
                    Command = file,
                    Location = locationLabel,
                    FilePath = file,
                    IsRecommendedToDisable = recommended,
                    RecommendationReason = string.IsNullOrEmpty(reason) ? "Ярлык в папке автозагрузки." : reason,
                    RequiresAdmin = requiresAdmin,
                    IsDisabledInWindows = isDisabledWin,
                    IsDisabled = isDisabledWin
                });
            }
        }
        catch { }
    }

    private static (bool Recommended, string Reason) MatchHeavyStartup(string name, string command)
    {
        var combined = $"{name} {command}";
        foreach (var hint in HeavyStartupHints)
        {
            if (combined.Contains(hint.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                return (true, hint.FriendlyNote);
            }
        }
        return (false, string.Empty);
    }
}
