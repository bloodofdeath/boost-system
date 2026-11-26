using System.Globalization;
using System.Text.RegularExpressions;

namespace OptiPulse.Core.Services;

/// <summary>
/// Centralized helper for querying and modifying Windows power schemes via powercfg.exe.
/// Uses source-generated regular expressions for fast, zero-overhead output parsing.
/// </summary>
public static partial class PowerCfgHelper
{
    public const string UltimateTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    public const string HighPerfTemplateGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string BalancedTemplateGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.CultureInvariant)]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"0x([0-9a-fA-F]{8})", RegexOptions.CultureInvariant)]
    private static partial Regex HexIndexRegex();

    [GeneratedRegex(@"\((.+?)\)", RegexOptions.CultureInvariant)]
    private static partial Regex SchemeNameRegex();

    public static (string Guid, string Name) GetActiveScheme()
    {
        var res = CommandRunner.Run("powercfg", "/getactivescheme", 6000);
        if (!res.Success || string.IsNullOrWhiteSpace(res.StandardOutput))
        {
            return (string.Empty, "Неизвестно");
        }

        var guidMatch = GuidRegex().Match(res.StandardOutput);
        var guid = guidMatch.Success ? guidMatch.Groups[1].Value : string.Empty;
        var nameMatch = SchemeNameRegex().Match(res.StandardOutput);
        var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : guid;
        return (guid, name);
    }

    public static string? FindSchemeByNameOrGuid(string exactGuid, params string[] namePatterns)
    {
        var list = CommandRunner.Run("powercfg", "/list", 6000);
        if (!list.Success || string.IsNullOrWhiteSpace(list.StandardOutput)) return null;

        foreach (var line in list.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = GuidRegex().Match(line);
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

    public static string? DuplicateScheme(string templateGuid)
    {
        var dup = CommandRunner.Run("powercfg", $"-duplicatescheme {templateGuid}", 8000);
        if (!dup.Success || string.IsNullOrWhiteSpace(dup.StandardOutput)) return null;

        var match = GuidRegex().Match(dup.StandardOutput);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static CommandResult SetActiveScheme(string schemeGuid) =>
        CommandRunner.Run("powercfg", $"/setactive {schemeGuid}", 8000);

    public static CommandResult ApplyCurrentScheme() =>
        CommandRunner.Run("powercfg", "/setactive scheme_current", 8000);

    /// <summary>
    /// Queries the current AC power setting index (including hidden settings via /qh).
    /// Works without Administrator privileges.
    /// </summary>
    public static int? QueryAcValueIndex(string subgroupGuid, string settingGuid)
    {
        var result = CommandRunner.Run("powercfg", $"/qh scheme_current {subgroupGuid} {settingGuid}", 8000);
        return ParseAcHexIndexFromOutput(result.Success ? result.StandardOutput : null);
    }

    public static int? ParseAcHexIndexFromOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        var matches = HexIndexRegex().Matches(output);
        if (matches.Count >= 2)
        {
            // Last two 8-digit hex numbers in `powercfg /qh` output are always AC Index and DC Index
            var acHex = matches[^2].Groups[1].Value;
            if (int.TryParse(acHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    public static CommandResult SetAcValueIndex(string subgroupGuid, string settingGuid, int value) =>
        CommandRunner.Run("powercfg", $"/setacvalueindex scheme_current {subgroupGuid} {settingGuid} {value}", 8000);
}
