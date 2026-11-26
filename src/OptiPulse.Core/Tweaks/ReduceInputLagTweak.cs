using System.Runtime.InteropServices;
using Microsoft.Win32;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 3. Уменьшить задержку ввода.
/// </summary>
public sealed class ReduceInputLagTweak : TweakBase
{
    private const string MouseKey = @"Control Panel\Mouse";
    private const string GameConfigStoreKey = @"System\GameConfigStore";
    private const string KernelKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    private const string MouClassKey = @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters";
    private const string KbdClassKey = @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters";

    private const uint SpiSetMouse = 0x0004;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    private static readonly TweakDescriptor Info = new()
    {
        Id = "reduce_input_lag",
        Title = "Уменьшить задержку ввода.",
        Description = "Сокращает этапы, замедляющие вывод изображения на экран, повышая ощущение плавности и контроля.",
        NotRecommendedWhen = "Вы привыкли к стандартному ускорению указателя мыши («Повышенная точность установки указателя») на рабочем столе или используете очень старую USB-периферию.",
        TechnicalDetails = "Отключает программное ускорение мыши Windows (1:1 Raw Input), включает GlobalTimerResolutionRequests = 1 (точный системный таймер Windows 11), оптимизирует поведение полноэкранного вывода GameConfigStore (FSEBehaviorMode) и уменьшает буферы очередей ввода мыши/клавиатуры (DataQueueSize = 20).",
        IconKey = "IconInputLag",
        Category = TweakCategory.NetworkAndInput,
        NoticeLevel = TweakNoticeLevel.None,
        RequiresAdmin = true,
        RequiresRebootForFullEffect = true
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);

    public ReduceInputLagTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => Info;

    public override TweakStatusInfo GetStatus()
    {
        var mouseSpeed = RegistryHelper.ReadString(RegistryHelper.Hkcu, MouseKey, "MouseSpeed");
        var timerRes = RegistryHelper.ReadDword(RegistryHelper.Hklm, KernelKey, "GlobalTimerResolutionRequests");
        var mouQueue = RegistryHelper.ReadDword(RegistryHelper.Hklm, MouClassKey, "MouseDataQueueSize");

        var enabled = mouseSpeed == "0" && timerRes == 1 && mouQueue is <= 32;
        return new TweakStatusInfo
        {
            IsEnabled = enabled,
            StatusNote = $"Ускорение мыши={(mouseSpeed == "0" ? "Выкл" : "Вкл")}, GlobalTimer={(timerRes == 1 ? "Вкл" : "Станд.")}, Queue={mouQueue ?? 100}"
        };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);

        // 1. Disable Windows Mouse Acceleration (Enhance Pointer Precision)
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, MouseKey, "MouseSpeed", "0", RegistryValueKind.String);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, MouseKey, "MouseThreshold1", "0", RegistryValueKind.String);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, MouseKey, "MouseThreshold2", "0", RegistryValueKind.String);
        ApplyLiveMouseAccel(enabled: false);

        // 2. Optimize Fullscreen Presentation / DWM handoff
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, GameConfigStoreKey, "GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, GameConfigStoreKey, "GameDVR_HonorUserFSEBehaviorMode", 1, RegistryValueKind.DWord);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, GameConfigStoreKey, "GameDVR_DXGIHonorFSEWindowsCompatible", 1, RegistryValueKind.DWord);

        // 3. Global High-Resolution Timer support & smaller mouse/keyboard queue buffers
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, KernelKey, "GlobalTimerResolutionRequests", 1, RegistryValueKind.DWord);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, MouClassKey, "MouseDataQueueSize", 20, RegistryValueKind.DWord);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, KbdClassKey, "KeyboardDataQueueSize", 20, RegistryValueKind.DWord);

        Backup.SaveTweakBackup(entry);
        return TweakOperationResult.Ok(true, "Задержка ввода оптимизирована (Raw Input мыши, точный таймер ядра, уменьшенные буферы ввода). Рекомендуется перезагрузка.", rebootRecommended: true);
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
            RegistryHelper.WriteValue(RegistryHelper.Hkcu, MouseKey, "MouseSpeed", "1", RegistryValueKind.String);
            RegistryHelper.WriteValue(RegistryHelper.Hkcu, MouseKey, "MouseThreshold1", "6", RegistryValueKind.String);
            RegistryHelper.WriteValue(RegistryHelper.Hkcu, MouseKey, "MouseThreshold2", "10", RegistryValueKind.String);
            RegistryHelper.DeleteValue(RegistryHelper.Hklm, KernelKey, "GlobalTimerResolutionRequests");
            RegistryHelper.WriteValue(RegistryHelper.Hklm, MouClassKey, "MouseDataQueueSize", 100, RegistryValueKind.DWord);
            RegistryHelper.WriteValue(RegistryHelper.Hklm, KbdClassKey, "KeyboardDataQueueSize", 100, RegistryValueKind.DWord);
        }

        var restoredSpeed = RegistryHelper.ReadString(RegistryHelper.Hkcu, MouseKey, "MouseSpeed");
        ApplyLiveMouseAccel(enabled: restoredSpeed != "0");

        return TweakOperationResult.Ok(false, "Параметры задержки ввода и указателя мыши восстановлены.");
    }

    private static void ApplyLiveMouseAccel(bool enabled)
    {
        try
        {
            int[] mouseParams = enabled ? [6, 10, 1] : [0, 0, 0];
            SystemParametersInfo(SpiSetMouse, 0, mouseParams, SpifUpdateIniFile | SpifSendChange);
        }
        catch
        {
            // Registry write already applied
        }
    }
}
