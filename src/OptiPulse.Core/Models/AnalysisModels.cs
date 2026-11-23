namespace OptiPulse.Core.Models;

public enum RecommendationImpact
{
    High,
    Medium,
    Low
}

public enum RecommendationSafety
{
    Safe,      // Безопасно
    Moderate,  // Умеренно
    Caution    // Осторожно
}

public enum RecommendationCategory
{
    SystemAndGaming,
    WindowsServices,
    StartupPrograms,
    BackgroundProcesses,
    VisualAndTelemetry,
    StorageAndHardware
}

public enum RecommendationActionType
{
    None,
    EnableTweak,
    DisableService,
    DisableStartupItem,
    ApplyQuickSetting
}

public sealed class DiskInfo
{
    public string DriveLetter { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string MediaType { get; set; } = "Unknown"; // SSD, HDD, NVMe, Unknown
    public double TotalGb { get; set; }
    public double FreeGb { get; set; }
    public double FreePercent => TotalGb > 0 ? Math.Round(FreeGb / TotalGb * 100.0, 1) : 0;
}

public sealed class HardwareProfile
{
    public string OsCaption { get; set; } = "Windows";
    public string OsVersion { get; set; } = string.Empty;
    public string OsBuild { get; set; } = string.Empty;
    public TimeSpan SystemUptime { get; set; }
    public bool IsLaptop { get; set; }

    public string CpuName { get; set; } = "Неизвестный процессор";
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }
    public int CpuMaxClockMhz { get; set; }
    public double CpuLoadPercent { get; set; }

    public List<string> GpuNames { get; set; } = new();
    public string PrimaryGpuName => GpuNames.Count > 0 ? GpuNames[0] : "Не определена";
    public string GpuDriverVersion { get; set; } = string.Empty;

    public double TotalRamGb { get; set; }
    public double UsedRamGb { get; set; }
    public double FreeRamGb { get; set; }
    public double RamUsagePercent => TotalRamGb > 0 ? Math.Round(UsedRamGb / TotalRamGb * 100.0, 1) : 0;

    public List<DiskInfo> Disks { get; set; } = new();
    public bool HasSsdSystemDrive { get; set; }

    public string ActivePowerPlanName { get; set; } = "Неизвестно";
    public string ActivePowerPlanGuid { get; set; } = string.Empty;
    public string ActiveNetworkAdapter { get; set; } = "Не определён";
    public string NetworkLinkSpeed { get; set; } = string.Empty;
}

public sealed class StartupAppItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Root { get; set; } = string.Empty;
    public string SubKey { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public bool IsRecommendedToDisable { get; set; }
    public string RecommendationReason { get; set; } = string.Empty;
    public bool RequiresAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsDisabledInWindows { get; set; }
}

public sealed class ProcessSummaryItem
{
    public string Name { get; set; } = string.Empty;
    public int InstanceCount { get; set; }
    public double MemoryMb { get; set; }
    public bool IsSafeToCloseBeforeGaming { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class AnalysisRecommendation
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CurrentState { get; set; } = string.Empty;
    public string ExpectedBenefit { get; set; } = string.Empty;
    public RecommendationCategory Category { get; set; }
    public RecommendationImpact Impact { get; set; }
    public RecommendationSafety Safety { get; set; }
    public RecommendationActionType ActionType { get; set; } = RecommendationActionType.None;
    public string ActionTarget { get; set; } = string.Empty;
    public bool RequiresAdmin { get; set; }
    public bool IsResolved { get; set; }
    public bool IsOptionalPreference { get; set; }
}

public sealed class PcAnalysisReport
{
    public DateTime GeneratedAtLocal { get; set; } = DateTime.Now;
    public string MachineName { get; set; } = Environment.MachineName;
    public bool WasRunAsAdmin { get; set; }
    public int OptimizationScore { get; set; }
    public string ScoreVerdict { get; set; } = string.Empty;
    public int ActiveTweaksCount { get; set; }
    public int TotalTweaksCount { get; set; }
    public HardwareProfile Hardware { get; set; } = new();
    public List<AnalysisRecommendation> Recommendations { get; set; } = new();
    public List<StartupAppItem> StartupApps { get; set; } = new();
    public List<ProcessSummaryItem> TopProcesses { get; set; } = new();
    public List<string> AlreadyOptimizedItems { get; set; } = new();
}
