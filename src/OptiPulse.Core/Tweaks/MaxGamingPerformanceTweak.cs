using Microsoft.Win32;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 6. Максимальная производительность для игр
/// </summary>
public sealed class MaxGamingPerformanceTweak : TweakBase
{
    private const string GameBarKey = @"SOFTWARE\Microsoft\GameBar";
    private const string MmcssGamesKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
    private const string PowerThrottlingKey = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";

    private static readonly TweakDescriptor Info = new()
    {
        Id = "max_gaming_performance",
        Title = "Максимальная производительность для игр",
        Description = "Приоритизирует обработку для игры, минимизируя влияние других программ.",
        NotRecommendedWhen = "Вы одновременно с игрой запускаете тяжёлые фоновые процессы (стриминг, кодирование видео) или работаете на ноутбуке от аккумулятора.",
        TechnicalDetails = "Включает Игровой режим Windows (AutoGameModeEnabled = 1), настраивает профиль планировщика MMCSS Tasks\\Games (GPU Priority = 8, Priority = 6, Scheduling Category = High, SFIO Priority = High) и отключает PowerThrottlingOff = 1.",
        IconKey = "IconMaxGaming",
        Category = TweakCategory.GpuAndGaming,
        NoticeLevel = TweakNoticeLevel.None,
        RequiresAdmin = true
    };

    public MaxGamingPerformanceTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => Info;

    public override TweakStatusInfo GetStatus()
    {
        var gameMode = RegistryHelper.ReadDword(RegistryHelper.Hkcu, GameBarKey, "AutoGameModeEnabled");
        var priority = RegistryHelper.ReadDword(RegistryHelper.Hklm, MmcssGamesKey, "Priority");
        var sched = RegistryHelper.ReadString(RegistryHelper.Hklm, MmcssGamesKey, "Scheduling Category");
        var throttlingOff = RegistryHelper.ReadDword(RegistryHelper.Hklm, PowerThrottlingKey, "PowerThrottlingOff");

        var enabled = (gameMode is null or 1) && priority == 6 &&
                      string.Equals(sched, "High", StringComparison.OrdinalIgnoreCase) &&
                      throttlingOff == 1;

        return new TweakStatusInfo
        {
            IsEnabled = enabled,
            StatusNote = $"GameMode={(gameMode == 0 ? "Выкл" : "Вкл")}, MMCSS Priority={priority ?? 2} ({sched ?? "Medium"}), PowerThrottling={(throttlingOff == 1 ? "Выкл" : "Вкл")}"
        };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);

        // 1. Enable Windows Game Mode
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, GameBarKey, "AutoGameModeEnabled", 1, RegistryValueKind.DWord);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, GameBarKey, "AllowAutoGameMode", 1, RegistryValueKind.DWord);

        // 2. MMCSS Games Task Priority
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, MmcssGamesKey, "GPU Priority", 8, RegistryValueKind.DWord);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, MmcssGamesKey, "Priority", 6, RegistryValueKind.DWord);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, MmcssGamesKey, "Scheduling Category", "High", RegistryValueKind.String);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, MmcssGamesKey, "SFIO Priority", "High", RegistryValueKind.String);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, MmcssGamesKey, "Background Only", "False", RegistryValueKind.String);

        // 3. Disable CPU Power Throttling
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, PowerThrottlingKey, "PowerThrottlingOff", 1, RegistryValueKind.DWord);

        Backup.SaveTweakBackup(entry);
        return TweakOperationResult.Ok(true, "Игровой режим, высокий приоритет MMCSS для игр и отключение Power Throttling применены.");
    }

    protected override TweakOperationResult RevertCore()
    {
        var entry = Backup.GetTweakBackup(Descriptor.Id);
        if (entry is not null && entry.RegistryValues.Count > 0)
        {
            RestoreBackedUpRegistryValues(entry);
            Backup.RemoveTweakBackup(Descriptor.Id);
        }

        if (GetStatus().IsEnabled)
        {
            RegistryHelper.WriteValue(RegistryHelper.Hklm, MmcssGamesKey, "GPU Priority", 8, RegistryValueKind.DWord);
            RegistryHelper.WriteValue(RegistryHelper.Hklm, MmcssGamesKey, "Priority", 2, RegistryValueKind.DWord);
            RegistryHelper.WriteValue(RegistryHelper.Hklm, MmcssGamesKey, "Scheduling Category", "Medium", RegistryValueKind.String);
            RegistryHelper.WriteValue(RegistryHelper.Hklm, MmcssGamesKey, "SFIO Priority", "Normal", RegistryValueKind.String);
            RegistryHelper.DeleteValue(RegistryHelper.Hklm, PowerThrottlingKey, "PowerThrottlingOff");
        }

        return TweakOperationResult.Ok(false, "Параметры игрового приоритета и MMCSS восстановлены по умолчанию.");
    }
}
