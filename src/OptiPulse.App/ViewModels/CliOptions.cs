namespace OptiPulse.App.ViewModels;

/// <summary>
/// Parses and builds post-UAC command-line continuation flags so any privileged action
/// requested in non-admin mode automatically resumes after UAC elevation.
/// </summary>
public sealed class CliOptions
{
    public int? InitialTab { get; set; }
    public string? PendingTweakId { get; set; }
    public bool PendingTweakEnable { get; set; } = true;
    public bool RunAnalysis { get; set; }
    public bool ApplyGamingPreset { get; set; }
    public bool ApplySafeRecommendations { get; set; }
    public bool RevertAll { get; set; }
    public bool CreateRestorePoint { get; set; }
    public string? DisableServiceName { get; set; }
    public string? CaptureScreenshotPath { get; set; }

    public static CliOptions Parse(string[] args)
    {
        var opts = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--tab" when i + 1 < args.Length && int.TryParse(args[i + 1], out var tab):
                    opts.InitialTab = Math.Clamp(tab, 0, 3);
                    i++;
                    break;
                case "--apply-tweak" when i + 1 < args.Length:
                    opts.PendingTweakId = args[++i];
                    opts.PendingTweakEnable = true;
                    break;
                case "--revert-tweak" when i + 1 < args.Length:
                    opts.PendingTweakId = args[++i];
                    opts.PendingTweakEnable = false;
                    break;
                case "--disable-service" when i + 1 < args.Length:
                    opts.DisableServiceName = args[++i];
                    break;
                case "--analyze":
                    opts.RunAnalysis = true;
                    break;
                case "--gaming-preset":
                    opts.ApplyGamingPreset = true;
                    break;
                case "--apply-safe":
                    opts.ApplySafeRecommendations = true;
                    opts.RunAnalysis = true;
                    break;
                case "--revert-all":
                    opts.RevertAll = true;
                    break;
                case "--restore-point":
                    opts.CreateRestorePoint = true;
                    break;
                case "--capture" when i + 1 < args.Length:
                    opts.CaptureScreenshotPath = args[++i];
                    break;
            }
        }

        return opts;
    }

    public static string QuoteArg(string value) =>
        value.Contains(' ') ? $"\"{value.Replace("\"", "\\\"")}\"" : value;
}
