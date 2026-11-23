using Microsoft.Win32;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 10. Высокая производительность GPU [Бета]
/// </summary>
public sealed class GpuHighPerformanceTweak : TweakBase
{
    private const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string UserGpuPrefsKey = @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences";
    private const string SubPciExpress = "501a4d13-42af-4429-9fd1-a8218c268e20";
    private const string PciLinkStateSetting = "ee12f906-d277-404b-b6da-e5fa1a576df5";

    public GpuHighPerformanceTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => new()
    {
        Id = "gpu_high_performance",
        Title = "Высокая производительность GPU",
        Description = "Настраивает драйвер GPU так, чтобы он отдавал приоритет максимальной производительности, а не энергосбережению.",
        NotRecommendedWhen = "Вы играете на ноутбуке от батареи или замечаете повышенные частоты и шум вентиляторов видеокарты в простое. Для полного вступления настроек драйвера в силу рекомендуется перезагрузка.",
        TechnicalDetails = "Включает аппаратное планирование GPU (HAGS: HwSchMode = 2), оптимизации для игр в оконном режиме (SwapEffectUpgradeEnable=1), отключает энергосбережение шины PCI Express (ASPM = Off) и настраивает режим макс. производительности драйвера NVIDIA/AMD (PowerMizer / Disable ULPS).",
        IconKey = "IconGpuPerformance",
        Category = TweakCategory.GpuAndGaming,
        NoticeLevel = TweakNoticeLevel.None,
        IsBeta = true,
        RequiresAdmin = true,
        RequiresRebootForFullEffect = true
    };

    public override TweakStatusInfo GetStatus()
    {
        var hags = RegistryHelper.ReadDword(RegistryHelper.Hklm, GraphicsDriversKey, "HwSchMode");
        var globalPrefs = RegistryHelper.ReadString(RegistryHelper.Hkcu, UserGpuPrefsKey, "DirectXUserGlobalSettings") ?? string.Empty;
        var hasSwapUpgrade = globalPrefs.Contains("SwapEffectUpgradeEnable=1", StringComparison.OrdinalIgnoreCase);

        bool vendorMaxPerf = false;
        foreach (var sub in RegistryHelper.EnumerateSubKeyNames(RegistryHelper.Hklm, DisplayClassKey))
        {
            if (sub.Length != 4 || !int.TryParse(sub, out _)) continue;
            var fullKey = $@"{DisplayClassKey}\{sub}";
            if (RegistryHelper.ReadDword(RegistryHelper.Hklm, fullKey, "PowerMizerLevelAC") == 1 ||
                RegistryHelper.ReadDword(RegistryHelper.Hklm, fullKey, "EnableUlps") == 0)
            {
                vendorMaxPerf = true;
                break;
            }
        }

        var hasBackupFlag = Backup.GetTweakBackup(Descriptor.Id) is not null;
        var enabled = hags == 2 && (hasSwapUpgrade || vendorMaxPerf || hasBackupFlag);
        return new TweakStatusInfo
        {
            IsEnabled = enabled,
            StatusNote = $"HAGS={(hags == 2 ? "Вкл" : "Выкл")}, WindowedOpt={(hasSwapUpgrade ? "Вкл" : "Станд.")}{(vendorMaxPerf ? ", Драйвер=Макс." : "")}"
        };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);

        // 1. Hardware-Accelerated GPU Scheduling (HAGS)
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, GraphicsDriversKey, "HwSchMode", 2, RegistryValueKind.DWord);

        // 2. DirectX Optimizations for Windowed Games
        var currentGlobal = RegistryHelper.ReadString(RegistryHelper.Hkcu, UserGpuPrefsKey, "DirectXUserGlobalSettings") ?? string.Empty;
        var updatedGlobal = UpsertDirectXSetting(currentGlobal, "SwapEffectUpgradeEnable", "1");
        updatedGlobal = UpsertDirectXSetting(updatedGlobal, "VRROptimizeEnable", "1");
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, UserGpuPrefsKey, "DirectXUserGlobalSettings", updatedGlobal, RegistryValueKind.String);

        // 3. Disable PCIe Link State Power Management (AC)
        CommandRunner.Run("powercfg", $"/setacvalueindex scheme_current {SubPciExpress} {PciLinkStateSetting} 0", 8000);
        CommandRunner.Run("powercfg", "/setactive scheme_current", 8000);

        // 4. Vendor-specific GPU driver power preferences
        int vendorTweakCount = 0;
        foreach (var sub in RegistryHelper.EnumerateSubKeyNames(RegistryHelper.Hklm, DisplayClassKey))
        {
            if (sub.Length != 4 || !int.TryParse(sub, out _)) continue;
            var fullKey = $@"{DisplayClassKey}\{sub}";
            var provider = RegistryHelper.ReadString(RegistryHelper.Hklm, fullKey, "ProviderName") ?? string.Empty;
            var desc = RegistryHelper.ReadString(RegistryHelper.Hklm, fullKey, "DriverDesc") ?? string.Empty;
            var combined = $"{provider} {desc}";

            if (combined.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, fullKey, "PowerMizerEnable", 1, RegistryValueKind.DWord);
                RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, fullKey, "PowerMizerLevel", 1, RegistryValueKind.DWord);
                RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, fullKey, "PowerMizerLevelAC", 1, RegistryValueKind.DWord);
                vendorTweakCount++;
            }
            else if (combined.Contains("AMD", StringComparison.OrdinalIgnoreCase) || combined.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            {
                if (RegistryHelper.ReadValue(RegistryHelper.Hklm, fullKey, "EnableUlps") is not null)
                {
                    RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, fullKey, "EnableUlps", 0, RegistryValueKind.DWord);
                    vendorTweakCount++;
                }
            }
        }

        Backup.SaveTweakBackup(entry);
        return TweakOperationResult.Ok(
            true,
            $"Режим максимальной производительности GPU включён (HAGS, PCIe ASPM Off, оптимизировано драйверов: {vendorTweakCount}). Рекомендуется перезагрузка.",
            rebootRecommended: true);
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
            RegistryHelper.WriteValue(RegistryHelper.Hklm, GraphicsDriversKey, "HwSchMode", 1, RegistryValueKind.DWord);
        }

        // Restore PCIe Link State Power Management to Moderate power savings (1)
        CommandRunner.Run("powercfg", $"/setacvalueindex scheme_current {SubPciExpress} {PciLinkStateSetting} 1", 8000);
        CommandRunner.Run("powercfg", "/setactive scheme_current", 8000);

        return TweakOperationResult.Ok(false, "Настройки производительности GPU восстановлены. Рекомендуется перезагрузка.", rebootRecommended: true);
    }

    private static string UpsertDirectXSetting(string existing, string key, string value)
    {
        var pairs = existing.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

        pairs[key] = value;
        return string.Join(";", pairs.Select(kv => $"{kv.Key}={kv.Value}")) + ";";
    }
}
