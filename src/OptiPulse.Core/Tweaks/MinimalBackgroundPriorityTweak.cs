using Microsoft.Win32;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 7. Минимальный приоритет для фоновых процессов
/// </summary>
public sealed class MinimalBackgroundPriorityTweak : TweakBase
{
    private const string PriorityControlKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string MultimediaKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    private static readonly TweakDescriptor Info = new()
    {
        Id = "minimal_bg_priority",
        Title = "Минимальный приоритет для фоновых процессов",
        Description = "Снижает приоритет фоновых служб и освобождает больше ресурсов для игры.",
        NotRecommendedWhen = "Вы одновременно с игрой ведёте стрим (OBS), рендерите видео, компилируете проекты или выполняете ресурсоёмкие фоновые задачи — им будет выделяться меньше процессорного времени.",
        TechnicalDetails = "Настраивает Win32PrioritySeparation = 0x26 (приоритет активного окна, короткие переменные кванты CPU) и снижает SystemResponsiveness до 10% (по умолчанию 20%) для минимизации резерва под фоновые службы.",
        IconKey = "IconBgPriority",
        Category = TweakCategory.CpuAndSystem,
        NoticeLevel = TweakNoticeLevel.None,
        RequiresAdmin = true
    };

    public MinimalBackgroundPriorityTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => Info;

    public override TweakStatusInfo GetStatus()
    {
        var win32Priority = RegistryHelper.ReadDword(RegistryHelper.Hklm, PriorityControlKey, "Win32PrioritySeparation");
        var responsiveness = RegistryHelper.ReadDword(RegistryHelper.Hklm, MultimediaKey, "SystemResponsiveness");
        var isForegroundBoosted = win32Priority is 0x26 or 0x28 or 0x24 or 0x16;
        var enabled = isForegroundBoosted && responsiveness is <= 10;
        return new TweakStatusInfo
        {
            IsEnabled = enabled,
            StatusNote = $"Win32PrioritySeparation=0x{(win32Priority ?? 2):X}, SystemResponsiveness={responsiveness ?? 20}%"
        };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, PriorityControlKey, "Win32PrioritySeparation", 0x26, RegistryValueKind.DWord);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, MultimediaKey, "SystemResponsiveness", 10, RegistryValueKind.DWord);
        Backup.SaveTweakBackup(entry);

        return TweakOperationResult.Ok(true, "Приоритет активных приложений повышен, резерв фоновых служб снижен до 10%.");
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
            RegistryHelper.WriteValue(RegistryHelper.Hklm, PriorityControlKey, "Win32PrioritySeparation", 2, RegistryValueKind.DWord);
            RegistryHelper.WriteValue(RegistryHelper.Hklm, MultimediaKey, "SystemResponsiveness", 20, RegistryValueKind.DWord);
        }

        return TweakOperationResult.Ok(false, "Восстановлены стандартные параметры планировщика Windows.");
    }
}
