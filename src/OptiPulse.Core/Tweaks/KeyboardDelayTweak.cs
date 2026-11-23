using System.Runtime.InteropServices;
using Microsoft.Win32;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 12. Сократить время ввода с клавиатуры
/// </summary>
public sealed class KeyboardDelayTweak : TweakBase
{
    private const string KeyboardKey = @"Control Panel\Keyboard";
    private const uint SpiSetKeyboardDelay = 0x0017;
    private const uint SpiSetKeyboardSpeed = 0x000B;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    public KeyboardDelayTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => new()
    {
        Id = "reduce_keyboard_delay",
        Title = "Сократить время ввода с клавиатуры",
        Description = "Уменьшает интервал повторения клавиш через настройки операционной системы.",
        NotRecommendedWhen = "Вы много печатаете тексты и при слегка долгом нажатии клавиши начинают появляться случайные двойные символы.",
        TechnicalDetails = "Устанавливает минимальную задержку перед повторением клавиш (KeyboardDelay = 0, ~250 мс) и максимальную скорость повторения (KeyboardSpeed = 31, ~30 символов/сек) с мгновенным применением через Win32 SystemParametersInfo.",
        IconKey = "IconKeyboard",
        Category = TweakCategory.NetworkAndInput,
        NoticeLevel = TweakNoticeLevel.None,
        RequiresAdmin = false
    };

    public override TweakStatusInfo GetStatus()
    {
        var delay = RegistryHelper.ReadString(RegistryHelper.Hkcu, KeyboardKey, "KeyboardDelay") ?? "1";
        var speed = RegistryHelper.ReadString(RegistryHelper.Hkcu, KeyboardKey, "KeyboardSpeed") ?? "31";
        var enabled = delay == "0" && speed == "31";

        return new TweakStatusInfo
        {
            IsEnabled = enabled,
            StatusNote = $"KeyboardDelay={delay}, KeyboardSpeed={speed}"
        };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, KeyboardKey, "KeyboardDelay", "0", RegistryValueKind.String);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, KeyboardKey, "KeyboardSpeed", "31", RegistryValueKind.String);
        Backup.SaveTweakBackup(entry);

        ApplyLiveKeyboardTiming(delay: 0, speed: 31);
        return TweakOperationResult.Ok(true, "Задержка повторения клавиш снижена до минимума (применено мгновенно).");
    }

    protected override TweakOperationResult RevertCore()
    {
        var entry = Backup.GetTweakBackup(Descriptor.Id);
        if (entry is not null && entry.RegistryValues.Count > 0)
        {
            RestoreBackedUpRegistryValues(entry);
            Backup.RemoveTweakBackup(Descriptor.Id);
        }
        else
        {
            RegistryHelper.WriteValue(RegistryHelper.Hkcu, KeyboardKey, "KeyboardDelay", "1", RegistryValueKind.String);
            RegistryHelper.WriteValue(RegistryHelper.Hkcu, KeyboardKey, "KeyboardSpeed", "31", RegistryValueKind.String);
        }

        uint delay = uint.TryParse(RegistryHelper.ReadString(RegistryHelper.Hkcu, KeyboardKey, "KeyboardDelay"), out var d) ? d : 1u;
        uint speed = uint.TryParse(RegistryHelper.ReadString(RegistryHelper.Hkcu, KeyboardKey, "KeyboardSpeed"), out var s) ? s : 31u;
        ApplyLiveKeyboardTiming(delay, speed);

        return TweakOperationResult.Ok(false, $"Параметры клавиатуры восстановлены (Delay={delay}, Speed={speed}).");
    }

    private static void ApplyLiveKeyboardTiming(uint delay, uint speed)
    {
        try
        {
            SystemParametersInfo(SpiSetKeyboardDelay, delay, IntPtr.Zero, SpifUpdateIniFile | SpifSendChange);
            SystemParametersInfo(SpiSetKeyboardSpeed, speed, IntPtr.Zero, SpifUpdateIniFile | SpifSendChange);
        }
        catch
        {
            // Registry already updated
        }
    }
}
