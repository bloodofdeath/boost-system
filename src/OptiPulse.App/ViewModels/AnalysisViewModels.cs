using System.Windows.Input;
using OptiPulse.Core.Models;

namespace OptiPulse.App.ViewModels;

public sealed class RecommendationItemViewModel : ViewModelBase
{
    private bool _isResolved;
    private bool _isBusy;

    public AnalysisRecommendation Model { get; }

    public string Id => Model.Id;
    public string Title => Model.Title;
    public string Description => Model.Description;
    public string CurrentState => Model.CurrentState;
    public string ExpectedBenefit => Model.ExpectedBenefit;
    public RecommendationCategory Category => Model.Category;
    public RecommendationImpact Impact => Model.Impact;
    public RecommendationSafety Safety => Model.Safety;
    public RecommendationActionType ActionType => Model.ActionType;
    public string ActionTarget => Model.ActionTarget;
    public bool RequiresAdmin => Model.RequiresAdmin;
    public bool IsOptionalPreference => Model.IsOptionalPreference;

    public bool HasAction => ActionType != RecommendationActionType.None;

    public string ActionButtonLabel => ActionType switch
    {
        RecommendationActionType.EnableTweak => "Включить",
        RecommendationActionType.DisableService => "Отключить службу",
        RecommendationActionType.DisableStartupItem => "Убрать из автозагрузки",
        RecommendationActionType.ApplyQuickSetting => "Применить",
        _ => string.Empty
    };

    public bool IsResolved
    {
        get => _isResolved;
        set
        {
            if (SetProperty(ref _isResolved, value))
            {
                Model.IsResolved = value;
                RaisePropertyChanged(nameof(CanApply));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(CanApply));
            }
        }
    }

    public bool CanApply => HasAction && !IsResolved && !IsBusy;

    public ICommand ApplyCommand { get; }

    public RecommendationItemViewModel(AnalysisRecommendation model, Func<RecommendationItemViewModel, Task> onApply)
    {
        Model = model;
        _isResolved = model.IsResolved;
        ApplyCommand = new AsyncRelayCommand(() => onApply(this), () => CanApply);
    }
}

public sealed class StartupItemViewModel : ViewModelBase
{
    private bool _isDisabled;

    public StartupAppItem Model { get; }
    public string Name => Model.Name;
    public string Command => Model.Command;
    public string Location => Model.Location;
    public bool IsRecommendedToDisable => Model.IsRecommendedToDisable;
    public bool IsDisabledInWindows => Model.IsDisabledInWindows;
    public string RecommendationReason => string.IsNullOrWhiteSpace(Model.RecommendationReason)
        ? "Стандартный элемент автозагрузки"
        : Model.RecommendationReason;
    public bool RequiresAdmin => Model.RequiresAdmin;

    public string DisabledStatusText => IsDisabledInWindows
        ? "✓ Отключено в Windows"
        : "✓ Отключено";

    public bool IsDisabled
    {
        get => _isDisabled;
        set
        {
            if (SetProperty(ref _isDisabled, value))
            {
                Model.IsDisabled = value;
            }
        }
    }

    public ICommand DisableCommand { get; }

    public StartupItemViewModel(StartupAppItem model, Func<StartupItemViewModel, Task> onDisable)
    {
        Model = model;
        _isDisabled = model.IsDisabled || model.IsDisabledInWindows;
        DisableCommand = new AsyncRelayCommand(() => onDisable(this), () => !IsDisabled);
    }
}

public sealed class ProcessItemViewModel : ViewModelBase
{
    private bool _isClosed;

    public ProcessSummaryItem Model { get; }
    public string Name => Model.Name;
    public int InstanceCount => Model.InstanceCount;
    public double MemoryMb => Model.MemoryMb;
    public string Note => Model.Note;

    public bool IsClosed
    {
        get => _isClosed;
        set => SetProperty(ref _isClosed, value);
    }

    public ICommand CloseProcessCommand { get; }

    public ProcessItemViewModel(ProcessSummaryItem model, Func<ProcessItemViewModel, Task> onClose)
    {
        Model = model;
        CloseProcessCommand = new AsyncRelayCommand(() => onClose(this), () => !IsClosed);
    }
}

public sealed class BackupDisplayItem
{
    public string Kind { get; init; } = string.Empty; // Tweak | Service | Startup
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string SavedAtText { get; init; } = string.Empty;
    public ICommand RestoreCommand { get; init; } = null!;
}
