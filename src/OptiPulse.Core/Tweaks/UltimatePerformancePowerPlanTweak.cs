using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 14. Режим максимальной производительности (План питания Ultimate Performance)
/// </summary>
public sealed class UltimatePerformancePowerPlanTweak : TweakBase
{
    private static readonly TweakDescriptor Info = new()
    {
        Id = "ultimate_performance_power_plan",
        Title = "Режим максимальной производительности",
        Description = "Включить план питания \"Максимальная производительность\".",
        NotRecommendedWhen = "Вы используете ноутбук от батареи — план «Максимальная производительность» отключает микро-задержки энергосбережения, что увеличивает энергопотребление и нагрев устройства.",
        TechnicalDetails = "Создаёт (если отсутствует) и активирует скрытый план электропитания Windows «Максимальная производительность» (Ultimate Performance, GUID e9a42b02-d5df-448d-aa00-03f14749eb61) через powercfg. При поддержке Modern Standby без Ultimate использует «Высокая производительность».",
        IconKey = "IconUltimatePower",
        Category = TweakCategory.CpuAndSystem,
        NoticeLevel = TweakNoticeLevel.None,
        RequiresAdmin = true
    };

    public UltimatePerformancePowerPlanTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => Info;

    public override TweakStatusInfo GetStatus()
    {
        var (activeGuid, activeName) = PowerCfgHelper.GetActiveScheme();
        var isUltimateOrHigh =
            activeName.Contains("Максимальная производительность", StringComparison.OrdinalIgnoreCase) ||
            activeName.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase) ||
            activeGuid.Equals(PowerCfgHelper.UltimateTemplateGuid, StringComparison.OrdinalIgnoreCase) ||
            (Backup.GetTweakBackup(Descriptor.Id)?.Metadata.TryGetValue("ActivatedGuid", out var savedGuid) == true &&
             activeGuid.Equals(savedGuid, StringComparison.OrdinalIgnoreCase));

        return new TweakStatusInfo
        {
            IsEnabled = isUltimateOrHigh,
            StatusNote = string.IsNullOrWhiteSpace(activeName) ? "План питания не определён" : $"Активный план: {activeName}"
        };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);
        var (previousGuid, previousName) = PowerCfgHelper.GetActiveScheme();
        if (!entry.Metadata.ContainsKey("OriginalPowerSchemeGuid") && !string.IsNullOrWhiteSpace(previousGuid))
        {
            entry.Metadata["OriginalPowerSchemeGuid"] = previousGuid;
            entry.Metadata["OriginalPowerSchemeName"] = previousName;
        }

        // 1. Check if Ultimate Performance already exists in powercfg /list
        var targetGuid = PowerCfgHelper.FindSchemeByNameOrGuid(
            PowerCfgHelper.UltimateTemplateGuid,
            "Максимальная производительность",
            "Ultimate Performance");
        var usedFallback = false;

        // 2. Duplicate Ultimate Performance template if not present
        if (string.IsNullOrWhiteSpace(targetGuid))
        {
            targetGuid = PowerCfgHelper.DuplicateScheme(PowerCfgHelper.UltimateTemplateGuid);
        }

        // 3. Fallback to High Performance on Modern Standby (S0ix) laptops where Ultimate isn't supported
        if (string.IsNullOrWhiteSpace(targetGuid))
        {
            targetGuid = PowerCfgHelper.FindSchemeByNameOrGuid(
                PowerCfgHelper.HighPerfTemplateGuid,
                "Высокая производительность",
                "High performance")
                ?? PowerCfgHelper.DuplicateScheme(PowerCfgHelper.HighPerfTemplateGuid);
            usedFallback = true;
        }

        if (string.IsNullOrWhiteSpace(targetGuid))
        {
            return TweakOperationResult.Fail("Не удалось создать или активировать план питания максимальной производительности (возможно, ограничено прошивкой Modern Standby).", GetStatus().IsEnabled);
        }

        var activate = PowerCfgHelper.SetActiveScheme(targetGuid);
        if (!activate.Success)
        {
            return TweakOperationResult.Fail($"Не удалось активировать план питания: {activate.CombinedOutput}", GetStatus().IsEnabled);
        }

        entry.Metadata["ActivatedGuid"] = targetGuid;
        Backup.SaveTweakBackup(entry);

        var msg = usedFallback
            ? "Активирован план питания «Высокая производительность» (Ultimate Performance не поддерживается на данной системе Modern Standby)."
            : "Активирован план питания «Максимальная производительность».";

        return TweakOperationResult.Ok(true, msg);
    }

    protected override TweakOperationResult RevertCore()
    {
        var entry = Backup.GetTweakBackup(Descriptor.Id);
        var restoreGuid = PowerCfgHelper.BalancedTemplateGuid;

        if (entry is not null)
        {
            if (entry.Metadata.TryGetValue("OriginalPowerSchemeGuid", out var origGuid) && !string.IsNullOrWhiteSpace(origGuid))
            {
                var activated = entry.Metadata.TryGetValue("ActivatedGuid", out var act) ? act : string.Empty;
                if (!origGuid.Equals(activated, StringComparison.OrdinalIgnoreCase))
                {
                    restoreGuid = origGuid;
                }
            }
            Backup.RemoveTweakBackup(Descriptor.Id);
        }

        var res = PowerCfgHelper.SetActiveScheme(restoreGuid);
        if (!res.Success && !restoreGuid.Equals(PowerCfgHelper.BalancedTemplateGuid, StringComparison.OrdinalIgnoreCase))
        {
            res = PowerCfgHelper.SetActiveScheme(PowerCfgHelper.BalancedTemplateGuid);
        }

        if (!res.Success)
        {
            return TweakOperationResult.Fail($"Не удалось переключить план питания: {res.CombinedOutput}", GetStatus().IsEnabled);
        }

        var (_, activeName) = PowerCfgHelper.GetActiveScheme();
        return TweakOperationResult.Ok(false, $"План питания восстановлен: {activeName}.");
    }

    public static (string Guid, string Name) GetActivePowerScheme() => PowerCfgHelper.GetActiveScheme();
}
