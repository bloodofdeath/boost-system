using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 6. Отключить SuperFetch (служба SysMain)
/// </summary>
public sealed class SuperFetchTweak : TweakBase
{
    private const string ServiceName = "SysMain";

    public SuperFetchTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => new()
    {
        Id = "disable_superfetch",
        Title = "Отключить SuperFetch",
        Description = "Отключает SuperFetch для освобождения большего объема оперативной памяти для игр.",
        NotRecommendedWhen = "Ваша система и игры установлены на медленном механическом жёстком диске (HDD) — SuperFetch (SysMain) кэширует часто используемые программы и ускоряет их запуск на HDD. На SSD/NVMe отключение полностью безопасно.",
        TechnicalDetails = "Останавливает системную службу Windows «SysMain» (ранее SuperFetch) и переводит её тип запуска в «Отключена» (Start = 4).",
        IconKey = "IconSuperFetch",
        Category = TweakCategory.StorageAndMemory,
        NoticeLevel = TweakNoticeLevel.None,
        RequiresAdmin = true
    };

    public override TweakStatusInfo GetStatus()
    {
        var state = ServiceControlHelper.GetServiceState(ServiceName);
        if (!state.Exists)
        {
            return new TweakStatusInfo { IsEnabled = false, IsSupported = false, StatusNote = "Служба SysMain не найдена." };
        }

        return new TweakStatusInfo
        {
            IsEnabled = state.IsDisabled && !state.IsRunning,
            StatusNote = $"SysMain: {(state.IsRunning ? "Запущена" : "Остановлена")} ({state.StartModeRussian})"
        };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);
        if (!entry.Services.Any(s => s.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase)))
        {
            entry.Services.Add(ServiceControlHelper.CaptureServiceBackup(ServiceName));
        }

        var (ok, msg) = ServiceControlHelper.DisableAndStopService(ServiceName);
        if (!ok)
        {
            return TweakOperationResult.Fail(msg, GetStatus().IsEnabled);
        }

        Backup.SaveTweakBackup(entry);
        return TweakOperationResult.Ok(true, "Служба SysMain (SuperFetch) остановлена и отключена.");
    }

    protected override TweakOperationResult RevertCore()
    {
        var entry = Backup.GetTweakBackup(Descriptor.Id);
        var svcBackup = entry?.Services.FirstOrDefault(s => s.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase))
            ?? new ServiceBackup { ServiceName = ServiceName, OriginalStartMode = 2, WasRunning = true };

        if (svcBackup.OriginalStartMode == 4)
        {
            svcBackup.OriginalStartMode = 2;
            svcBackup.WasRunning = true;
        }

        var (ok, msg) = ServiceControlHelper.RestoreService(svcBackup);
        if (!ok)
        {
            return TweakOperationResult.Fail(msg, GetStatus().IsEnabled);
        }

        Backup.RemoveTweakBackup(Descriptor.Id);
        return TweakOperationResult.Ok(false, msg);
    }
}
