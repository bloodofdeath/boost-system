using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 13. Отключить индексацию файлов Windows (служба WSearch)
/// </summary>
public sealed class WindowsSearchIndexingTweak : TweakBase
{
    private const string ServiceName = "WSearch";

    private static readonly TweakDescriptor Info = new()
    {
        Id = "disable_windows_search",
        Title = "Отключить индексацию файлов Windows",
        Description = "Отключает индексацию файлов Windows, освобождая диск для других задач.",
        NotRecommendedWhen = "Вы часто ищете файлы по содержимому в Проводнике Windows, пользуетесь мгновенным поиском в Microsoft Outlook или активно ищете локальные документы через меню «Пуск».",
        TechnicalDetails = "Останавливает службу индексации Windows Search (WSearch) и переводит её тип запуска в «Отключена» (Start = 4), устраняя фоновую нагрузку на диск и CPU.",
        IconKey = "IconSearchIndex",
        Category = TweakCategory.StorageAndMemory,
        NoticeLevel = TweakNoticeLevel.None,
        RequiresAdmin = true
    };

    public WindowsSearchIndexingTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => Info;

    public override TweakStatusInfo GetStatus()
    {
        var state = ServiceControlHelper.GetServiceState(ServiceName);
        if (!state.Exists)
        {
            return new TweakStatusInfo { IsEnabled = false, IsSupported = false, StatusNote = "Служба WSearch не найдена." };
        }

        return new TweakStatusInfo
        {
            IsEnabled = state.IsDisabled && !state.IsRunning,
            StatusNote = $"Windows Search: {(state.IsRunning ? "Запущена" : "Остановлена")} ({state.StartModeRussian})"
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
        return TweakOperationResult.Ok(true, "Служба индексации файлов Windows Search остановлена и отключена.");
    }

    protected override TweakOperationResult RevertCore()
    {
        var entry = Backup.GetTweakBackup(Descriptor.Id);
        var svcBackup = entry?.Services.FirstOrDefault(s => s.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase))
            ?? new ServiceBackup { ServiceName = ServiceName, OriginalStartMode = 2, WasDelayedAutoStart = true, WasRunning = true };

        if (svcBackup.OriginalStartMode == 4)
        {
            svcBackup.OriginalStartMode = 2;
            svcBackup.WasDelayedAutoStart = true;
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
