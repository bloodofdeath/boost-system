namespace OptiPulse.Core.Models;

public enum TweakCategory
{
    CpuAndSystem,
    GpuAndGaming,
    NetworkAndInput,
    StorageAndMemory
}

public enum TweakNoticeLevel
{
    None,
    Info,     // Blue "!" badge
    Warning   // Orange "!" badge
}

public sealed record TweakDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string NotRecommendedWhen { get; init; }
    public required string TechnicalDetails { get; init; }
    public required string IconKey { get; init; }
    public required TweakCategory Category { get; init; }
    public TweakNoticeLevel NoticeLevel { get; init; } = TweakNoticeLevel.None;
    public string NoticeTooltip { get; init; } = string.Empty;
    public bool IsBeta { get; init; }
    public bool RequiresAdmin { get; init; } = true;
    public bool RequiresRebootForFullEffect { get; init; }
    public bool RecommendedForGaming { get; init; } = true;
}

public sealed record TweakStatusInfo
{
    public bool IsEnabled { get; init; }
    public string StatusNote { get; init; } = string.Empty;
    public bool IsSupported { get; init; } = true;
}

public sealed record TweakOperationResult
{
    public bool Success { get; init; }
    public bool NewState { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool RequiresAdmin { get; init; }
    public bool RebootRecommended { get; init; }

    public static TweakOperationResult Ok(bool newState, string message, bool rebootRecommended = false) =>
        new() { Success = true, NewState = newState, Message = message, RebootRecommended = rebootRecommended };

    public static TweakOperationResult Fail(string message, bool currentState, bool requiresAdmin = false) =>
        new() { Success = false, NewState = currentState, Message = message, RequiresAdmin = requiresAdmin };
}
