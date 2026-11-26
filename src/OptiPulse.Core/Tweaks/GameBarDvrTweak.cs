using Microsoft.Win32;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 9. Отключить фоновую запись в игровой панели
/// </summary>
public sealed class GameBarDvrTweak : TweakBase
{
    private const string GameConfigStoreKey = @"System\GameConfigStore";
    private const string UserGameDvrKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string PolicyGameDvrKey = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";

    private static readonly TweakDescriptor Info = new()
    {
        Id = "disable_gamebar_dvr",
        Title = "Отключить фоновую запись в игровой панели",
        Description = "Отключает автоматические записи и снимки экрана для освобождения ресурсов и повышения FPS.",
        NotRecommendedWhen = "Вы используете встроенную Xbox Game Bar (Win + G) для фоновой записи последних 30 секунд геймплея, записи видео или снятия скриншотов.",
        TechnicalDetails = "Отключает GameDVR_Enabled (HKCU\\System\\GameConfigStore), AppCaptureEnabled и HistoricalCaptureEnabled (GameDVR), а также политику AllowGameDVR = 0 (HKLM).",
        IconKey = "IconGameBarRecord",
        Category = TweakCategory.GpuAndGaming,
        NoticeLevel = TweakNoticeLevel.Warning,
        NoticeTooltip = "Встроенная запись клипов через Xbox Game Bar (Win+G) перестанет работать.",
        RequiresAdmin = true
    };

    public GameBarDvrTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => Info;

    public override TweakStatusInfo GetStatus()
    {
        var gameDvrEnabled = RegistryHelper.ReadDword(RegistryHelper.Hkcu, GameConfigStoreKey, "GameDVR_Enabled");
        var appCapture = RegistryHelper.ReadDword(RegistryHelper.Hkcu, UserGameDvrKey, "AppCaptureEnabled");
        var policyDvr = RegistryHelper.ReadDword(RegistryHelper.Hklm, PolicyGameDvrKey, "AllowGameDVR");

        var isDisabled = gameDvrEnabled == 0 && (appCapture == 0 || policyDvr == 0);
        return new TweakStatusInfo
        {
            IsEnabled = isDisabled,
            StatusNote = isDisabled ? "Фоновая запись Game DVR отключена" : "Фоновая запись Game DVR активна"
        };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, GameConfigStoreKey, "GameDVR_Enabled", 0, RegistryValueKind.DWord);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, UserGameDvrKey, "AppCaptureEnabled", 0, RegistryValueKind.DWord);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, UserGameDvrKey, "HistoricalCaptureEnabled", 0, RegistryValueKind.DWord);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, PolicyGameDvrKey, "AllowGameDVR", 0, RegistryValueKind.DWord);
        Backup.SaveTweakBackup(entry);

        return TweakOperationResult.Ok(true, "Фоновая запись и захват экрана Xbox Game Bar отключены.");
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
            RegistryHelper.WriteValue(RegistryHelper.Hkcu, GameConfigStoreKey, "GameDVR_Enabled", 1, RegistryValueKind.DWord);
            RegistryHelper.WriteValue(RegistryHelper.Hkcu, UserGameDvrKey, "AppCaptureEnabled", 1, RegistryValueKind.DWord);
            RegistryHelper.DeleteValue(RegistryHelper.Hklm, PolicyGameDvrKey, "AllowGameDVR");
        }

        return TweakOperationResult.Ok(false, "Параметры записи Xbox Game Bar восстановлены.");
    }
}
