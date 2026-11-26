using System.Net.NetworkInformation;
using Microsoft.Win32;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 8. Отключить энергосбережение сети
/// </summary>
public sealed class NetworkPowerSavingTweak : TweakBase
{
    private const string NetAdapterClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private const string WirelessSubgroup = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1";
    private const string WirelessPowerSetting = "12bbebe6-58d6-4636-95bb-3217ef867c1a";

    private static readonly TweakDescriptor Info = new()
    {
        Id = "disable_network_power_saving",
        Title = "Отключить энергосбережение сети",
        Description = "Отключает функции энергосбережения сети для улучшения стабильности подключения.",
        NotRecommendedWhen = "Вы играете на ноутбуке от аккумулятора — отключение энергосбережения сетевого адаптера увеличивает расход батареи. В момент включения возможно кратковременное переподключение сети (1–3 сек).",
        TechnicalDetails = "Устанавливает PnPCapabilities = 24 на физических сетевых адаптерах (запрет отключения питания Windows), отключает Energy-Efficient Ethernet (EEE) / Green Ethernet / Power Saving Mode и переводит Wi-Fi в режим максимальной производительности.",
        IconKey = "IconNetworkPower",
        Category = TweakCategory.NetworkAndInput,
        NoticeLevel = TweakNoticeLevel.Warning,
        NoticeTooltip = "При применении сетевой адаптер может кратковременно переподключиться (1–3 секунды).",
        RequiresAdmin = true
    };

    public NetworkPowerSavingTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => Info;

    public override TweakStatusInfo GetStatus()
    {
        var activeSubKeys = FindPhysicalAdapterSubKeys(preferActiveOnly: true);
        if (activeSubKeys.Count == 0)
        {
            return new TweakStatusInfo { IsEnabled = false, StatusNote = "Физические сетевые адаптеры не найдены." };
        }

        int disabledCount = 0;
        foreach (var subKey in activeSubKeys)
        {
            var pnp = RegistryHelper.ReadDword(RegistryHelper.Hklm, subKey, "PnPCapabilities");
            if (pnp.HasValue && (pnp.Value & 24) == 24)
            {
                disabledCount++;
            }
        }

        var enabled = disabledCount > 0 && disabledCount == activeSubKeys.Count;
        return new TweakStatusInfo
        {
            IsEnabled = enabled,
            StatusNote = enabled
                ? $"Энергосбережение отключено ({disabledCount}/{activeSubKeys.Count} активн. адаптеров)"
                : $"Энергосбережение активно ({disabledCount}/{activeSubKeys.Count} оптимизировано)"
        };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);
        if (!entry.Metadata.ContainsKey("OriginalWirelessAc"))
        {
            var origWireless = PowerCfgHelper.QueryAcValueIndex(WirelessSubgroup, WirelessPowerSetting);
            if (origWireless.HasValue)
            {
                entry.Metadata["OriginalWirelessAc"] = origWireless.Value.ToString();
            }
        }

        var physicalSubKeys = FindPhysicalAdapterSubKeys(preferActiveOnly: false);

        foreach (var subKey in physicalSubKeys)
        {
            RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, subKey, "PnPCapabilities", 24, RegistryValueKind.DWord);
            DisableIfPresent(entry, subKey, "*EEE", "0");
            DisableIfPresent(entry, subKey, "EEELinkAdvertisement", "0");
            DisableIfPresent(entry, subKey, "EnableGreenEthernet", "0");
            DisableIfPresent(entry, subKey, "PowerSavingMode", "0");
            DisableIfPresent(entry, subKey, "GigaLite", "0");
            DisableIfPresent(entry, subKey, "AutoPowerSaveModeEnabled", "0");
            DisableIfPresent(entry, subKey, "ULPMode", "0");
            DisableIfPresent(entry, subKey, "EnablePME", "0");
        }

        // Wireless Adapter Power Saving Mode -> 0 (Maximum Performance) on AC
        PowerCfgHelper.SetAcValueIndex(WirelessSubgroup, WirelessPowerSetting, 0);
        PowerCfgHelper.ApplyCurrentScheme();

        CommandRunner.RunPowerShell(@"
            Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' } | ForEach-Object {
                Disable-NetAdapterPowerManagement -Name $_.Name -NoRestart -ErrorAction SilentlyContinue
            }", timeoutMs: 12000);

        Backup.SaveTweakBackup(entry);
        return TweakOperationResult.Ok(true, $"Энергосбережение отключено для {physicalSubKeys.Count} физ. сетевых адаптеров.");
    }

    protected override TweakOperationResult RevertCore()
    {
        var entry = Backup.GetTweakBackup(Descriptor.Id);
        int restoreWirelessAc = 0;
        bool hasSavedWireless = false;

        if (entry is not null)
        {
            if (entry.Metadata.TryGetValue("OriginalWirelessAc", out var wStr) && int.TryParse(wStr, out var parsedW))
            {
                restoreWirelessAc = parsedW;
                hasSavedWireless = true;
            }
            if (entry.RegistryValues.Count > 0)
            {
                RestoreBackedUpRegistryValues(entry);
            }
            Backup.RemoveTweakBackup(Descriptor.Id);
        }

        if (GetStatus().IsEnabled)
        {
            foreach (var subKey in FindPhysicalAdapterSubKeys(preferActiveOnly: false))
            {
                RegistryHelper.WriteValue(RegistryHelper.Hklm, subKey, "PnPCapabilities", 0, RegistryValueKind.DWord);
            }
        }

        if (hasSavedWireless)
        {
            PowerCfgHelper.SetAcValueIndex(WirelessSubgroup, WirelessPowerSetting, restoreWirelessAc);
            PowerCfgHelper.ApplyCurrentScheme();
        }

        CommandRunner.RunPowerShell(@"
            Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' } | ForEach-Object {
                Enable-NetAdapterPowerManagement -Name $_.Name -NoRestart -ErrorAction SilentlyContinue
            }", timeoutMs: 12000);

        return TweakOperationResult.Ok(false, "Параметры энергосбережения сетевых адаптеров восстановлены.");
    }

    private static void DisableIfPresent(TweakBackupEntry entry, string subKey, string valueName, string disabledValue)
    {
        var existing = RegistryHelper.ReadValue(RegistryHelper.Hklm, subKey, valueName);
        if (existing is not null)
        {
            var kind = existing is int ? RegistryValueKind.DWord : RegistryValueKind.String;
            object val = kind == RegistryValueKind.DWord ? 0 : disabledValue;
            RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, subKey, valueName, val, kind);
        }
    }

    private static List<string> FindPhysicalAdapterSubKeys(bool preferActiveOnly)
    {
        var allPhysical = new List<(string SubKey, string NetCfgId)>();
        foreach (var name in RegistryHelper.EnumerateSubKeyNames(RegistryHelper.Hklm, NetAdapterClassKey))
        {
            if (name.Length != 4 || !int.TryParse(name, out _)) continue;
            var fullSubKey = $@"{NetAdapterClassKey}\{name}";
            var instanceId = RegistryHelper.ReadString(RegistryHelper.Hklm, fullSubKey, "DeviceInstanceID") ?? string.Empty;
            var driverDesc = RegistryHelper.ReadString(RegistryHelper.Hklm, fullSubKey, "DriverDesc") ?? string.Empty;
            var netCfgId = RegistryHelper.ReadString(RegistryHelper.Hklm, fullSubKey, "NetCfgInstanceId") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(driverDesc)) continue;
            if (!(instanceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) ||
                  instanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (driverDesc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                driverDesc.Contains("WAN Miniport", StringComparison.OrdinalIgnoreCase) ||
                driverDesc.Contains("TAP-", StringComparison.OrdinalIgnoreCase) ||
                driverDesc.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                driverDesc.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            allPhysical.Add((fullSubKey, netCfgId));
        }

        if (!preferActiveOnly || allPhysical.Count == 0)
        {
            return allPhysical.Select(x => x.SubKey).ToList();
        }

        try
        {
            var activeIds = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Select(n => n.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var activeMatched = allPhysical
                .Where(x => !string.IsNullOrWhiteSpace(x.NetCfgId) && activeIds.Contains(x.NetCfgId))
                .Select(x => x.SubKey)
                .ToList();

            if (activeMatched.Count > 0)
            {
                return activeMatched;
            }
        }
        catch { }

        return allPhysical.Select(x => x.SubKey).ToList();
    }
}
