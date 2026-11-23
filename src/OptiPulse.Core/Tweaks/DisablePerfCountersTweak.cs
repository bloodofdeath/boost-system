using Microsoft.Win32;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 13. Выключить счетчики производительности Windows
/// </summary>
public sealed class DisablePerfCountersTweak : TweakBase
{
    private const string PerflibMasterKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Perflib";

    private static readonly string[] ServicePerfKeys =
    [
        @"SYSTEM\CurrentControlSet\Services\PerfOS\Performance",
        @"SYSTEM\CurrentControlSet\Services\PerfDisk\Performance",
        @"SYSTEM\CurrentControlSet\Services\PerfNet\Performance",
        @"SYSTEM\CurrentControlSet\Services\PerfProc\Performance",
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Performance"
    ];

    private const string ValueName = "Disable Performance Counters";

    public DisablePerfCountersTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => new()
    {
        Id = "disable_perf_counters",
        Title = "Выключить счетчики производительности Windows",
        Description = "Отключает мониторинг Windows для освобождения ресурсов процессора и улучшения производительности.",
        NotRecommendedWhen = "Вы пользуетесь системным монитором производительности (perfmon.exe), диагностикой серверов или сторонними виджетами мониторинга, зависящими от Windows Performance Counters (Диспетчер задач продолжит работать).",
        TechnicalDetails = "Устанавливает Disable Performance Counters = 1 в основном ключе Windows Perflib (HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Perflib), а также для провайдеров PerfOS, PerfDisk, PerfNet, PerfProc и Tcpip.",
        IconKey = "IconPerfCounters",
        Category = TweakCategory.CpuAndSystem,
        NoticeLevel = TweakNoticeLevel.Info,
        NoticeTooltip = "Отключает фоновые счётчики PerfMon; Диспетчер задач Windows и диагностика OptiPulse продолжат работать.",
        RequiresAdmin = true
    };

    public override TweakStatusInfo GetStatus()
    {
        var masterDisabled = RegistryHelper.ReadDword(RegistryHelper.Hklm, PerflibMasterKey, ValueName) == 1;
        int disabledServicesCount = ServicePerfKeys.Count(k => RegistryHelper.ReadDword(RegistryHelper.Hklm, k, ValueName) == 1);

        var enabled = masterDisabled || disabledServicesCount == ServicePerfKeys.Length;
        var note = masterDisabled
            ? $"Perflib отключён глобально (провайдеров: {disabledServicesCount}/{ServicePerfKeys.Length})"
            : $"Отключено провайдеров счётчиков: {disabledServicesCount} из {ServicePerfKeys.Length}";

        return new TweakStatusInfo
        {
            IsEnabled = enabled,
            StatusNote = note
        };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, PerflibMasterKey, ValueName, 1, RegistryValueKind.DWord);
        foreach (var key in ServicePerfKeys)
        {
            RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, key, ValueName, 1, RegistryValueKind.DWord);
        }
        Backup.SaveTweakBackup(entry);

        return TweakOperationResult.Ok(true, "Счётчики производительности Windows (Perflib + PerfOS/PerfDisk/PerfNet/PerfProc/Tcpip) отключены.");
    }

    protected override TweakOperationResult RevertCore()
    {
        var entry = Backup.GetTweakBackup(Descriptor.Id);
        if (entry is not null && entry.RegistryValues.Count > 0)
        {
            RestoreBackedUpRegistryValues(entry);
            Backup.RemoveTweakBackup(Descriptor.Id);
        }

        // Also ensure global master switch is re-enabled if it was disabled externally before OptiPulse ran
        if (RegistryHelper.ReadDword(RegistryHelper.Hklm, PerflibMasterKey, ValueName) == 1)
        {
            RegistryHelper.DeleteValue(RegistryHelper.Hklm, PerflibMasterKey, ValueName);
        }
        foreach (var key in ServicePerfKeys)
        {
            if (RegistryHelper.ReadDword(RegistryHelper.Hklm, key, ValueName) == 1)
            {
                RegistryHelper.DeleteValue(RegistryHelper.Hklm, key, ValueName);
            }
        }

        return TweakOperationResult.Ok(false, "Счётчики производительности Windows восстановлены.");
    }
}
