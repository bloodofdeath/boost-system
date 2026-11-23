using System.Text.RegularExpressions;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 8. Режим максимальной производительности (План питания Ultimate Performance)
/// </summary>
public sealed class UltimatePerformancePowerPlanTweak : TweakBase
{
    private const string UltimateTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string HighPerfTemplateGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedTemplateGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    private static readonly Regex GuidRegex = new(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.Compiled);

    public UltimatePerformancePowerPlanTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => new()
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

    public override TweakStatusInfo GetStatus()
    {
        var (activeGuid, activeName) = GetActivePowerScheme();
        var isUltimateOrHigh =
            activeName.Contains("Максимальная производительность", StringComparison.OrdinalIgnoreCase) ||
            activeName.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase) ||
            activeGuid.Equals(UltimateTemplateGuid, StringComparison.OrdinalIgnoreCase) ||
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
        var (previousGuid, previousName) = GetActivePowerScheme();
        if (!entry.Metadata.ContainsKey("OriginalPowerSchemeGuid") && !string.IsNullOrWhiteSpace(previousGuid))
        {
            entry.Metadata["OriginalPowerSchemeGuid"] = previousGuid;
            entry.Metadata["OriginalPowerSchemeName"] = previousName;
        }

        // 1. Check if Ultimate Performance already exists in powercfg /list
        var targetGuid = FindSchemeByNameOrGuid(UltimateTemplateGuid, "Максимальная производительность", "Ultimate Performance");
        var usedFallback = false;

        // 2. Duplicate Ultimate Performance template if not present
        if (string.IsNullOrWhiteSpace(targetGuid))
        {
            var dup = CommandRunner.Run("powercfg", $"-duplicatescheme {UltimateTemplateGuid}", 8000);
            var match = GuidRegex.Match(dup.StandardOutput);
            if (dup.Success && match.Success)
            {
                targetGuid = match.Groups[1].Value;
            }
        }

        // 3. Fallback to High Performance on Modern Standby (S0ix) laptops where Ultimate isn't supported
        if (string.IsNullOrWhiteSpace(targetGuid))
        {
            targetGuid = FindSchemeByNameOrGuid(HighPerfTemplateGuid, "Высокая производительность", "High performance");
            if (string.IsNullOrWhiteSpace(targetGuid))
            {
                var dupHigh = CommandRunner.Run("powercfg", $"-duplicatescheme {HighPerfTemplateGuid}", 8000);
                var match = GuidRegex.Match(dupHigh.StandardOutput);
                if (dupHigh.Success && match.Success)
                {
                    targetGuid = match.Groups[1].Value;
                }
            }
            usedFallback = true;
        }

        if (string.IsNullOrWhiteSpace(targetGuid))
        {
            return TweakOperationResult.Fail("Не удалось создать или активировать план питания максимальной производительности (возможно, ограничено прошивкой Modern Standby).", GetStatus().IsEnabled);
        }

        var activate = CommandRunner.Run("powercfg", $"/setactive {targetGuid}", 8000);
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
        var restoreGuid = BalancedTemplateGuid;

        if (entry is not null && entry.Metadata.TryGetValue("OriginalPowerSchemeGuid", out var origGuid) && !string.IsNullOrWhiteSpace(origGuid))
        {
            // Only restore original if it wasn't already the activated Ultimate plan
            var activated = entry.Metadata.TryGetValue("ActivatedGuid", out var act) ? act : string.Empty;
            if (!origGuid.Equals(activated, StringComparison.OrdinalIgnoreCase))
            {
                restoreGuid = origGuid;
            }
            Backup.RemoveTweakBackup(Descriptor.Id);
        }

        var res = CommandRunner.Run("powercfg", $"/setactive {restoreGuid}", 8000);
        if (!res.Success && !restoreGuid.Equals(BalancedTemplateGuid, StringComparison.OrdinalIgnoreCase))
        {
            res = CommandRunner.Run("powercfg", $"/setactive {BalancedTemplateGuid}", 8000);
        }

        if (!res.Success)
        {
            return TweakOperationResult.Fail($"Не удалось переключить план питания: {res.CombinedOutput}", GetStatus().IsEnabled);
        }

        var (_, activeName) = GetActivePowerScheme();
        return TweakOperationResult.Ok(false, $"План питания восстановлен: {activeName}.");
    }

    public static (string Guid, string Name) GetActivePowerScheme()
    {
        var res = CommandRunner.Run("powercfg", "/getactivescheme", 6000);
        if (!res.Success || string.IsNullOrWhiteSpace(res.StandardOutput))
        {
            return (string.Empty, "Неизвестно");
        }

        var guidMatch = GuidRegex.Match(res.StandardOutput);
        var guid = guidMatch.Success ? guidMatch.Groups[1].Value : string.Empty;
        var nameMatch = Regex.Match(res.StandardOutput, @"\((.+?)\)");
        var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : guid;
        return (guid, name);
    }

    private static string? FindSchemeByNameOrGuid(string exactGuid, params string[] namePatterns)
    {
        var list = CommandRunner.Run("powercfg", "/list", 6000);
        if (!list.Success) return null;

        foreach (var line in list.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = GuidRegex.Match(line);
            if (!match.Success) continue;
            var guid = match.Groups[1].Value;
            if (guid.Equals(exactGuid, StringComparison.OrdinalIgnoreCase))
            {
                return guid;
            }

            if (namePatterns.Any(p => line.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                return guid;
            }
        }
        return null;
    }
}
