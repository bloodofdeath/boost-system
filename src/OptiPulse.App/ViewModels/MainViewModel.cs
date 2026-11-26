using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.App.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly BackupService _backup;
    private readonly AppLogger _logger;
    private readonly TweakRegistry _registry;
    private readonly PcAnalyzerService _analyzer;

    private int _selectedMainTab;      // 0=Optimizations, 1=Analyzer, 2=Backups, 3=Logs
    private int _selectedReportSubTab; // 0=Recommendations, 1=Startup, 2=Processes, 3=AlreadyOptimized
    private string _selectedCategoryFilter = "ALL";
    private string _selectedReportCategoryFilter = "ALL";
    private string _searchQuery = string.Empty;

    private bool _isScanningStatuses;
    private bool _isAnalyzing;
    private string _analysisProgressText = string.Empty;
    private PcAnalysisReport? _currentReport;
    private int _liveReportScore;
    private string _liveReportVerdict = string.Empty;

    private string _toastMessage = string.Empty;
    private bool _isToastVisible;
    private bool _isToastError;
    private CancellationTokenSource? _toastCts;

    private bool _isRebootBannerVisible;
    private bool _isBusyGlobal;

    public bool IsAdministrator { get; }
    public string AdminBadgeText => IsAdministrator ? "Администратор" : "Права админа (UAC)";
    public string BackupDirectoryPath => _backup.BackupDirectory;
    public string LogDirectoryPath => _logger.LogDirectory;

    public ObservableCollection<TweakCardViewModel> AllTweaks { get; } = new();
    public ObservableCollection<TweakCardViewModel> FilteredTweaks { get; } = new();
    public ObservableCollection<RecommendationItemViewModel> AllReportRecommendations { get; } = new();
    public ObservableCollection<RecommendationItemViewModel> FilteredReportRecommendations { get; } = new();
    public ObservableCollection<StartupItemViewModel> ReportStartupApps { get; } = new();
    public ObservableCollection<ProcessItemViewModel> ReportTopProcesses { get; } = new();
    public ObservableCollection<string> ReportAlreadyOptimized { get; } = new();
    public ObservableCollection<BackupDisplayItem> BackupItems { get; } = new();
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public Func<TweakCardViewModel, Task>? ShowNotRecommendedModal { get; set; }
    public Func<string, string, bool>? RequestAdminConfirmationModal { get; set; }
    public Action? ShutdownApplication { get; set; }

    public int SelectedMainTab
    {
        get => _selectedMainTab;
        set
        {
            if (SetProperty(ref _selectedMainTab, value) && value == 2)
            {
                RefreshBackupList();
            }
        }
    }

    public int SelectedReportSubTab
    {
        get => _selectedReportSubTab;
        set => SetProperty(ref _selectedReportSubTab, value);
    }

    public string SelectedCategoryFilter
    {
        get => _selectedCategoryFilter;
        set
        {
            if (SetProperty(ref _selectedCategoryFilter, value))
            {
                ApplyTweakFilter();
            }
        }
    }

    public string SelectedReportCategoryFilter
    {
        get => _selectedReportCategoryFilter;
        set
        {
            if (SetProperty(ref _selectedReportCategoryFilter, value))
            {
                ApplyReportRecommendationFilter();
            }
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplyTweakFilter();
            }
        }
    }

    public bool IsScanningStatuses
    {
        get => _isScanningStatuses;
        set
        {
            if (SetProperty(ref _isScanningStatuses, value))
            {
                RaisePropertyChanged(nameof(ActiveCounterText));
            }
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        set
        {
            if (SetProperty(ref _isAnalyzing, value))
            {
                RaisePropertyChanged(nameof(HasReport));
                RaisePropertyChanged(nameof(ShowAnalyzerEmptyState));
            }
        }
    }

    public string AnalysisProgressText
    {
        get => _analysisProgressText;
        set => SetProperty(ref _analysisProgressText, value);
    }

    public PcAnalysisReport? CurrentReport
    {
        get => _currentReport;
        set
        {
            if (SetProperty(ref _currentReport, value))
            {
                LiveReportScore = value?.OptimizationScore ?? 0;
                LiveReportVerdict = value?.ScoreVerdict ?? string.Empty;
                RaisePropertyChanged(nameof(HasReport));
                RaisePropertyChanged(nameof(ShowAnalyzerEmptyState));
                RaisePropertyChanged(nameof(HighImpactCount));
                RaisePropertyChanged(nameof(MediumImpactCount));
                RaisePropertyChanged(nameof(LowImpactCount));
                RaisePropertyChanged(nameof(ReportDisksSummary));
            }
        }
    }

    public int LiveReportScore
    {
        get => _liveReportScore;
        set => SetProperty(ref _liveReportScore, value);
    }

    public string LiveReportVerdict
    {
        get => _liveReportVerdict;
        set => SetProperty(ref _liveReportVerdict, value);
    }

    public bool HasReport => CurrentReport is not null && !IsAnalyzing;
    public bool ShowAnalyzerEmptyState => CurrentReport is null && !IsAnalyzing;

    public int ActiveTweaksCount => AllTweaks.Count(t => t.IsEnabled);
    public int TotalTweaksCount => AllTweaks.Count;
    public string AllFilterLabel => $"Все ({TotalTweaksCount})";
    public string ActiveCounterText => IsScanningStatuses
        ? "Проверка системы..."
        : $"Включено: {ActiveTweaksCount} / {TotalTweaksCount}";

    public int HighImpactCount => AllReportRecommendations.Count(r => r.Impact == RecommendationImpact.High && !r.IsResolved);
    public int MediumImpactCount => AllReportRecommendations.Count(r => r.Impact == RecommendationImpact.Medium && !r.IsResolved);
    public int LowImpactCount => AllReportRecommendations.Count(r => r.Impact == RecommendationImpact.Low && !r.IsResolved);

    public string ReportDisksSummary
    {
        get
        {
            if (CurrentReport is null || CurrentReport.Hardware.Disks.Count == 0) return "—";
            return string.Join("  •  ", CurrentReport.Hardware.Disks.Select(d => $"{d.DriveLetter} {d.FreeGb:0.#}/{d.TotalGb:0.#} ГБ ({d.MediaType})"));
        }
    }

    public string ToastMessage
    {
        get => _toastMessage;
        set => SetProperty(ref _toastMessage, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        set => SetProperty(ref _isToastVisible, value);
    }

    public bool IsToastError
    {
        get => _isToastError;
        set => SetProperty(ref _isToastError, value);
    }

    public bool IsRebootBannerVisible
    {
        get => _isRebootBannerVisible;
        set => SetProperty(ref _isRebootBannerVisible, value);
    }

    public bool IsBusyGlobal
    {
        get => _isBusyGlobal;
        set => SetProperty(ref _isBusyGlobal, value);
    }

    public bool HasBackups => BackupItems.Count > 0;

    // Commands
    public ICommand SelectTabCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand SelectReportCategoryCommand { get; }
    public ICommand RequestAdminCommand { get; }
    public ICommand RunPcAnalysisCommand { get; }
    public ICommand RefreshStatusesCommand { get; }
    public ICommand ApplyGamingPresetCommand { get; }
    public ICommand RevertAllCommand { get; }
    public ICommand CreateRestorePointCommand { get; }
    public ICommand ExportReportCommand { get; }
    public ICommand CopyMarkdownReportCommand { get; }
    public ICommand ApplySafeRecommendationsCommand { get; }
    public ICommand OpenBackupFolderCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand DismissRebootBannerCommand { get; }

    public MainViewModel()
    {
        IsAdministrator = AdminHelper.IsAdministrator();
        _logger = new AppLogger();
        _backup = new BackupService();
        _registry = new TweakRegistry(_backup, _logger);
        _analyzer = new PcAnalyzerService(_registry, _backup, _logger);

        _logger.EntryAdded += entry =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                LogEntries.Insert(0, entry);
                if (LogEntries.Count > 500) LogEntries.RemoveAt(LogEntries.Count - 1);
            });
        };

        var cardByTweakId = new Dictionary<string, TweakCardViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var tweak in _registry.All)
        {
            var vm = new TweakCardViewModel(tweak, card => ShowNotRecommendedModal?.Invoke(card));
            vm.ToggleRequested += OnCardToggleRequested;
            AllTweaks.Add(vm);
            cardByTweakId[vm.Id] = vm;
        }

        SelectTabCommand = new RelayCommand(p =>
        {
            if (p is not null && int.TryParse(p.ToString(), out var idx)) SelectedMainTab = idx;
        });

        SelectCategoryCommand = new RelayCommand(p =>
        {
            if (p is string cat) SelectedCategoryFilter = cat;
        });

        SelectReportCategoryCommand = new RelayCommand(p =>
        {
            if (p is string cat) SelectedReportCategoryFilter = cat;
        });

        RequestAdminCommand = new RelayCommand(() =>
        {
            if (IsAdministrator)
            {
                ShowToast("Приложение уже запущено с правами администратора.");
                return;
            }
            ElevateWithConfirmation(
                "Повышение прав до администратора",
                "Права администратора позволяют применять системные оптимизации (реестр HKLM, службы Windows, схемы питания powercfg, NTFS и создание точек восстановления).",
                $"--tab {SelectedMainTab}");
        });

        RunPcAnalysisCommand = new AsyncRelayCommand(RunPcAnalysisAsync, onError: HandleCommandError);
        RefreshStatusesCommand = new AsyncRelayCommand(() => RefreshAllStatusesAsync(isInitialStartup: false), onError: HandleCommandError);
        ApplyGamingPresetCommand = new AsyncRelayCommand(ApplyGamingPresetAsync, onError: HandleCommandError);
        RevertAllCommand = new AsyncRelayCommand(RevertAllAsync, onError: HandleCommandError);
        CreateRestorePointCommand = new AsyncRelayCommand(CreateRestorePointAsync, onError: HandleCommandError);
        ExportReportCommand = new RelayCommand(ExportReport, () => HasReport);
        CopyMarkdownReportCommand = new RelayCommand(CopyMarkdownReport, () => HasReport);
        ApplySafeRecommendationsCommand = new AsyncRelayCommand(ApplySafeRecommendationsAsync, () => HasReport, HandleCommandError);
        OpenBackupFolderCommand = new RelayCommand(() => OpenFolder(_backup.BackupDirectory));
        OpenLogsFolderCommand = new RelayCommand(() => OpenFolder(_logger.LogDirectory));
        ClearLogsCommand = new RelayCommand(() => { _logger.ClearInMemory(); LogEntries.Clear(); });
        DismissRebootBannerCommand = new RelayCommand(() => IsRebootBannerVisible = false);

        _logger.Info("App", $"OptiPulse запущен. Режим: {(IsAdministrator ? "Администратор" : "Обычный пользователь")}.");
        ApplyTweakFilter();
    }

    private void HandleCommandError(Exception ex)
    {
        _logger.Error("UI", $"Необработанная ошибка команды: {ex.Message}");
        ShowToast($"Ошибка: {ex.Message}", isError: true);
    }

    public async Task InitializeAsync(string[] args)
    {
        var cli = CliOptions.Parse(args);
        if (cli.InitialTab.HasValue)
        {
            SelectedMainTab = cli.InitialTab.Value;
        }

        // Verify all 14 real Windows states in parallel on startup
        await RefreshAllStatusesAsync(isInitialStartup: true);
        RefreshBackupList();

        if (!string.IsNullOrWhiteSpace(cli.DisableServiceName) && IsAdministrator)
        {
            var (ok, msg, _) = await Task.Run(() => _analyzer.DisableOptionalService(cli.DisableServiceName));
            ShowToast(msg, isError: !ok);
            RefreshBackupList();
        }

        if (!string.IsNullOrWhiteSpace(cli.PendingTweakId))
        {
            var card = AllTweaks.FirstOrDefault(t => t.Id.Equals(cli.PendingTweakId, StringComparison.OrdinalIgnoreCase));
            if (card is not null)
            {
                await ExecuteCardToggleAsync(card, cli.PendingTweakEnable);
            }
        }
        else if (cli.ApplyGamingPreset)
        {
            await ApplyGamingPresetAsync();
        }
        else if (cli.RevertAll)
        {
            await RevertAllAsync();
        }
        else if (cli.CreateRestorePoint)
        {
            await CreateRestorePointAsync();
        }

        if (cli.RunAnalysis)
        {
            await RunPcAnalysisAsync();
            if (cli.ApplySafeRecommendations && HasReport)
            {
                await ApplySafeRecommendationsAsync();
            }
        }
    }

    public async Task RefreshAllStatusesAsync(bool isInitialStartup = false)
    {
        IsScanningStatuses = true;
        try
        {
            var states = await Task.Run(() => _registry.QueryAllStatusesParallel());
            var byId = states.ToDictionary(x => x.Tweak.Descriptor.Id, x => x.Status, StringComparer.OrdinalIgnoreCase);

            var enabledNames = new List<string>();
            foreach (var vm in AllTweaks)
            {
                if (byId.TryGetValue(vm.Id, out var status))
                {
                    vm.SetStateSilently(status.IsEnabled, status.StatusNote, status.IsSupported);
                    if (status.IsEnabled) enabledNames.Add(vm.Title);
                }
            }

            RaisePropertyChanged(nameof(ActiveTweaksCount));
            RaisePropertyChanged(nameof(ActiveCounterText));
            ApplyTweakFilter();

            var summary = enabledNames.Count > 0
                ? $"Проверка системы: активно {enabledNames.Count} из {AllTweaks.Count} ({string.Join(", ", enabledNames)})."
                : $"Проверка системы: активно 0 из {AllTweaks.Count} оптимизаций.";

            _logger.Info("StartupCheck", summary);
            if (!isInitialStartup)
            {
                ShowToast($"Статусы обновлены: включено {enabledNames.Count} из {AllTweaks.Count}.");
            }
        }
        finally
        {
            IsScanningStatuses = false;
        }
    }

    private async void OnCardToggleRequested(TweakCardViewModel card, bool targetState)
    {
        await ExecuteCardToggleAsync(card, targetState);
    }

    private async Task ExecuteCardToggleAsync(TweakCardViewModel card, bool targetState)
    {
        if (card.IsBusy) return;

        if (card.RequiresAdmin && !IsAdministrator)
        {
            card.SetStateSilently(!targetState, card.StatusNote, card.IsSupported);
            var actionArg = targetState
                ? $"--apply-tweak {CliOptions.QuoteArg(card.Id)}"
                : $"--revert-tweak {CliOptions.QuoteArg(card.Id)}";
            ElevateWithConfirmation(
                $"Требуются права администратора — {card.Title}",
                $"Для {(targetState ? "включения" : "отключения")} функции «{card.Title}» необходимо изменить системные параметры Windows.\n\nПосле подтверждения UAC приложение перезапустится от имени администратора и автоматически применит выбранную настройку.",
                $"--tab {SelectedMainTab} {actionArg}");
            return;
        }

        card.IsBusy = true;
        try
        {
            var result = await Task.Run(() => targetState ? card.Tweak.Apply() : card.Tweak.Revert());
            var updatedStatus = await Task.Run(() => card.Tweak.GetStatus());
            card.SetStateSilently(result.Success ? result.NewState : updatedStatus.IsEnabled, updatedStatus.StatusNote, updatedStatus.IsSupported);

            if (result.RequiresAdmin)
            {
                var actionArg = targetState
                    ? $"--apply-tweak {CliOptions.QuoteArg(card.Id)}"
                    : $"--revert-tweak {CliOptions.QuoteArg(card.Id)}";
                ElevateWithConfirmation($"Требуются права администратора — {card.Title}", result.Message, $"--tab {SelectedMainTab} {actionArg}");
            }
            else
            {
                ShowToast(result.Message, isError: !result.Success);
                if (result.Success && result.RebootRecommended)
                {
                    IsRebootBannerVisible = true;
                }
            }
        }
        finally
        {
            card.IsBusy = false;
            RaisePropertyChanged(nameof(ActiveTweaksCount));
            RaisePropertyChanged(nameof(ActiveCounterText));
            ApplyTweakFilter();
            RefreshBackupList();
        }
    }

    private async Task RunPcAnalysisAsync()
    {
        SelectedMainTab = 1;
        IsAnalyzing = true;
        AnalysisProgressText = "Инициализация диагностики ПК...";

        try
        {
            var progress = new Progress<string>(msg => AnalysisProgressText = msg);
            var report = await _analyzer.AnalyzeAsync(progress);
            CurrentReport = report;

            // Keep main 14 cards in sync with the fresh scan
            await RefreshAllStatusesAsync(isInitialStartup: true);

            AllReportRecommendations.Clear();
            foreach (var rec in report.Recommendations)
            {
                AllReportRecommendations.Add(new RecommendationItemViewModel(rec, ApplyRecommendationAsync));
            }
            SelectedReportCategoryFilter = "ALL";
            ApplyReportRecommendationFilter();

            ReportStartupApps.Clear();
            foreach (var st in report.StartupApps)
            {
                ReportStartupApps.Add(new StartupItemViewModel(st, DisableStartupAppAsync));
            }

            ReportTopProcesses.Clear();
            foreach (var proc in report.TopProcesses)
            {
                ReportTopProcesses.Add(new ProcessItemViewModel(proc, CloseBackgroundProcessAsync));
            }

            ReportAlreadyOptimized.Clear();
            foreach (var ok in report.AlreadyOptimizedItems)
            {
                ReportAlreadyOptimized.Add(ok);
            }

            SelectedReportSubTab = 0;
            RaisePropertyChanged(nameof(HighImpactCount));
            RaisePropertyChanged(nameof(MediumImpactCount));
            RaisePropertyChanged(nameof(LowImpactCount));
            ShowToast($"Анализ завершён! Оценка ПК: {report.OptimizationScore}/100 • Рекомендаций: {report.Recommendations.Count}");
        }
        catch (Exception ex)
        {
            _logger.Error("Analyzer", $"Ошибка анализа ПК: {ex.Message}");
            ShowToast($"Ошибка анализа ПК: {ex.Message}", isError: true);
        }
        finally
        {
            IsAnalyzing = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ApplyRecommendationAsync(RecommendationItemViewModel rec)
    {
        if (rec.RequiresAdmin && !IsAdministrator)
        {
            var postUacArgs = rec.ActionType switch
            {
                RecommendationActionType.EnableTweak => $"--tab 1 --apply-tweak {CliOptions.QuoteArg(rec.ActionTarget)} --analyze",
                RecommendationActionType.DisableService => $"--tab 1 --disable-service {CliOptions.QuoteArg(rec.ActionTarget)} --analyze",
                _ => "--tab 1 --analyze"
            };

            ElevateWithConfirmation(
                "Требуются права администратора",
                $"Для выполнения рекомендации «{rec.Title}» необходимы права администратора Windows.",
                postUacArgs);
            return;
        }

        rec.IsBusy = true;
        try
        {
            bool ok = false;
            string msg = string.Empty;
            bool needsAdmin = false;

            switch (rec.ActionType)
            {
                case RecommendationActionType.EnableTweak:
                {
                    var card = AllTweaks.FirstOrDefault(t => t.Id.Equals(rec.ActionTarget, StringComparison.OrdinalIgnoreCase));
                    if (card is not null)
                    {
                        var res = await Task.Run(() => card.Tweak.Apply());
                        var st = await Task.Run(() => card.Tweak.GetStatus());
                        card.SetStateSilently(res.Success ? res.NewState : st.IsEnabled, st.StatusNote, st.IsSupported);
                        ok = res.Success;
                        msg = res.Message;
                        needsAdmin = res.RequiresAdmin;
                        if (res.Success && res.RebootRecommended) IsRebootBannerVisible = true;
                    }
                    break;
                }
                case RecommendationActionType.DisableService:
                {
                    (ok, msg, needsAdmin) = await Task.Run(() => _analyzer.DisableOptionalService(rec.ActionTarget));
                    break;
                }
                case RecommendationActionType.DisableStartupItem:
                {
                    var startupVm = ReportStartupApps.FirstOrDefault(s => s.Model.Id.Equals(rec.ActionTarget, StringComparison.OrdinalIgnoreCase));
                    if (startupVm is not null)
                    {
                        (ok, msg, needsAdmin) = await Task.Run(() => _analyzer.DisableStartupItem(startupVm.Model));
                        if (ok) startupVm.IsDisabled = true;
                    }
                    break;
                }
                case RecommendationActionType.ApplyQuickSetting:
                {
                    (ok, msg, needsAdmin) = await Task.Run(() => _analyzer.ApplyQuickSetting(rec.ActionTarget));
                    break;
                }
            }

            if (needsAdmin)
            {
                ElevateWithConfirmation("Требуются права администратора", msg, "--tab 1 --analyze");
                return;
            }

            if (ok)
            {
                rec.IsResolved = true;
                UpdateLiveReportScoreAfterAction(rec.Title);
                RaisePropertyChanged(nameof(ActiveTweaksCount));
                RaisePropertyChanged(nameof(ActiveCounterText));
                ApplyTweakFilter();
                RefreshBackupList();
            }

            ShowToast(msg, isError: !ok);
        }
        finally
        {
            rec.IsBusy = false;
        }
    }

    private void UpdateLiveReportScoreAfterAction(string resolvedItemTitle)
    {
        if (CurrentReport is null) return;

        PcAnalyzerService.RecalculateScore(CurrentReport);
        LiveReportScore = CurrentReport.OptimizationScore;
        LiveReportVerdict = CurrentReport.ScoreVerdict;

        if (!ReportAlreadyOptimized.Contains(resolvedItemTitle))
        {
            ReportAlreadyOptimized.Insert(0, resolvedItemTitle);
            CurrentReport.AlreadyOptimizedItems.Insert(0, resolvedItemTitle);
        }

        RaisePropertyChanged(nameof(HighImpactCount));
        RaisePropertyChanged(nameof(MediumImpactCount));
        RaisePropertyChanged(nameof(LowImpactCount));
    }

    private async Task DisableStartupAppAsync(StartupItemViewModel item)
    {
        if (item.RequiresAdmin && !IsAdministrator)
        {
            ElevateWithConfirmation(
                "Требуются права администратора",
                $"Элемент автозагрузки «{item.Name}» находится в общесистемном разделе. Для его отключения нужны права администратора.",
                "--tab 1 --analyze");
            return;
        }

        var (ok, msg, needsAdmin) = await Task.Run(() => _analyzer.DisableStartupItem(item.Model));
        if (needsAdmin)
        {
            ElevateWithConfirmation("Требуются права администратора", msg, "--tab 1 --analyze");
            return;
        }

        if (ok)
        {
            item.IsDisabled = true;
            var matchingRec = AllReportRecommendations.FirstOrDefault(r => r.ActionTarget == item.Model.Id);
            if (matchingRec is not null)
            {
                matchingRec.IsResolved = true;
                UpdateLiveReportScoreAfterAction(matchingRec.Title);
            }
            RefreshBackupList();
        }
        ShowToast(msg, isError: !ok);
    }

    private async Task CloseBackgroundProcessAsync(ProcessItemViewModel proc)
    {
        var (ok, msg) = await Task.Run(() => _analyzer.CloseBackgroundProcess(proc.Name));
        if (ok)
        {
            proc.IsClosed = true;
        }
        ShowToast(msg, isError: !ok);
    }

    private async Task ApplySafeRecommendationsAsync()
    {
        var safeActionable = AllReportRecommendations
            .Where(r => r.CanApply && r.Safety == RecommendationSafety.Safe && !r.IsOptionalPreference)
            .ToList();

        if (safeActionable.Count == 0)
        {
            ShowToast("Все основные безопасные рекомендации уже применены!");
            return;
        }

        if (safeActionable.Any(r => r.RequiresAdmin) && !IsAdministrator)
        {
            ElevateWithConfirmation(
                "Требуются права администратора",
                $"Найдено {safeActionable.Count} безопасных рекомендаций, часть из которых требует прав администратора Windows.\n\nПосле подтверждения UAC они будут применены автоматически.",
                "--tab 1 --apply-safe");
            return;
        }

        IsBusyGlobal = true;
        int applied = 0;
        try
        {
            foreach (var rec in safeActionable)
            {
                await ApplyRecommendationAsync(rec);
                if (rec.IsResolved) applied++;
            }
            ShowToast($"Применено безопасных оптимизаций: {applied} из {safeActionable.Count}. Новая оценка: {LiveReportScore}/100.");
        }
        finally
        {
            IsBusyGlobal = false;
        }
    }

    private async Task ApplyGamingPresetAsync()
    {
        if (!IsAdministrator)
        {
            ElevateWithConfirmation(
                "Игровой пресет — требуются права администратора",
                "Игровой пресет безопасно активирует рекомендуемые оптимизации (игровой приоритет, минимальный приоритет фоновых процессов, отключение Game DVR, парковки ядер, задержки клавиатуры и план максимальной производительности).\n\nВсе исходные настройки будут автоматически сохранены в резервную копию.",
                "--tab 0 --gaming-preset");
            return;
        }

        var presetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "max_gaming_performance",
            "minimal_bg_priority",
            "disable_gamebar_dvr",
            "unpark_cpu_cores",
            "ultimate_performance_power_plan",
            "reduce_keyboard_delay",
            "disable_ntfs_last_access",
            "gpu_high_performance"
        };

        IsBusyGlobal = true;
        int count = 0;
        try
        {
            foreach (var card in AllTweaks.Where(c => presetIds.Contains(c.Id) && !c.IsEnabled))
            {
                await ExecuteCardToggleAsync(card, true);
                if (card.IsEnabled) count++;
            }
            ShowToast(count > 0
                ? $"Игровой пресет применён! Активировано новых оптимизаций: {count}."
                : "Все оптимизации игрового пресета уже включены.");
        }
        finally
        {
            IsBusyGlobal = false;
        }
    }

    private async Task RevertAllAsync()
    {
        if (_backup.TotalBackupEntriesCount == 0)
        {
            ShowToast("Нет сохранённых изменений для отката.");
            return;
        }

        if (!IsAdministrator)
        {
            ElevateWithConfirmation(
                "Откат изменений — требуются права администратора",
                "Для полного восстановления системных служб и параметров реестра HKLM требуются права администратора.\n\nПосле подтверждения UAC откат выполнится автоматически.",
                "--tab 2 --revert-all");
            return;
        }

        IsBusyGlobal = true;
        try
        {
            var (reverted, failed, _) = await Task.Run(() => _registry.RevertAllBackedUp(_backup, _logger));
            await RefreshAllStatusesAsync(isInitialStartup: true);
            RefreshBackupList();
            ShowToast(failed == 0
                ? $"Все изменения успешно откачены (восстановлено элементов: {reverted})."
                : $"Восстановлено: {reverted}, с ошибками: {failed}. Проверьте вкладку «Журнал».", isError: failed > 0);
        }
        finally
        {
            IsBusyGlobal = false;
        }
    }

    private async Task CreateRestorePointAsync()
    {
        if (!IsAdministrator)
        {
            ElevateWithConfirmation(
                "Создание точки восстановления Windows",
                "Для создания системной точки восстановления Windows требуются права администратора.\n\nПосле подтверждения UAC точка восстановления будет создана автоматически.",
                "--tab 2 --restore-point");
            return;
        }

        IsBusyGlobal = true;
        ShowToast("Создание точки восстановления Windows... Это может занять 10–30 секунд.");
        try
        {
            var (ok, msg) = await Task.Run(() => RestorePointService.CreateRestorePoint());
            if (ok) _logger.Success("RestorePoint", msg);
            else _logger.Warning("RestorePoint", msg);
            ShowToast(msg, isError: !ok);
        }
        finally
        {
            IsBusyGlobal = false;
        }
    }

    private void ExportReport()
    {
        if (CurrentReport is null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Сохранить отчёт диагностики ПК",
            FileName = $"OptiPulse_Report_{DateTime.Now:yyyy-MM-dd_HH-mm}",
            Filter = "HTML Отчёт (рекомендуется) (*.html)|*.html|Markdown Отчёт (*.md)|*.md|JSON Данные (*.json)|*.json",
            DefaultExt = ".html"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            ReportExporter.SaveReport(CurrentReport, dlg.FileName);
            _logger.Success("Report", $"Отчёт сохранён: {dlg.FileName}");
            ShowToast($"Отчёт успешно сохранён: {Path.GetFileName(dlg.FileName)}");

            if (dlg.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Report", $"Ошибка сохранения отчёта: {ex.Message}");
            ShowToast($"Не удалось сохранить отчёт: {ex.Message}", isError: true);
        }
    }

    private void CopyMarkdownReport()
    {
        if (CurrentReport is null) return;
        try
        {
            var md = ReportExporter.GenerateMarkdown(CurrentReport);
            Clipboard.SetText(md);
            ShowToast("Отчёт в формате Markdown скопирован в буфер обмена.");
        }
        catch (Exception ex)
        {
            ShowToast($"Не удалось скопировать отчёт: {ex.Message}", isError: true);
        }
    }

    private void RefreshBackupList()
    {
        var snapshot = _backup.GetSnapshot();
        BackupItems.Clear();

        foreach (var kv in snapshot.Tweaks.OrderByDescending(x => x.Value.SavedAtUtc))
        {
            var tweak = _registry.FindById(kv.Key);
            var isQuick = tweak is null && QuickSettingsService.IsQuickSettingBackupKey(kv.Key);
            var title = tweak?.Descriptor.Title
                ?? (isQuick ? QuickSettingsService.GetDisplayTitle(kv.Key) : kv.Key);
            var kindLabel = isQuick ? "Быстрая настройка" : "Оптимизация";
            var details = $"Ключей реестра: {kv.Value.RegistryValues.Count}, служб: {kv.Value.Services.Count}, доп. параметров: {kv.Value.Metadata.Count}";

            BackupItems.Add(new BackupDisplayItem
            {
                Kind = kindLabel,
                Key = kv.Key,
                Title = title,
                Details = details,
                SavedAtText = kv.Value.SavedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                RestoreCommand = new AsyncRelayCommand(async () =>
                {
                    if (tweak is not null)
                    {
                        var card = AllTweaks.FirstOrDefault(c => c.Id.Equals(tweak.Descriptor.Id, StringComparison.OrdinalIgnoreCase));
                        if (card is not null) await ExecuteCardToggleAsync(card, false);
                        return;
                    }

                    var (ok, msg, needsAdmin) = await Task.Run(() => QuickSettingsService.Revert(kv.Key, _backup, _logger));
                    if (needsAdmin)
                    {
                        ElevateWithConfirmation("Права администратора", msg, "--tab 2");
                        return;
                    }
                    RefreshBackupList();
                    ShowToast(msg, isError: !ok);
                }, onError: HandleCommandError)
            });
        }

        foreach (var svc in snapshot.DisabledServices.Values.OrderByDescending(x => x.SavedAtUtc))
        {
            BackupItems.Add(new BackupDisplayItem
            {
                Kind = "Служба",
                Key = svc.ServiceName,
                Title = $"Служба Windows: {svc.ServiceName}",
                Details = $"Исходный режим: {svc.OriginalStartMode} • Была запущена: {(svc.WasRunning ? "Да" : "Нет")}",
                SavedAtText = svc.SavedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                RestoreCommand = new AsyncRelayCommand(async () =>
                {
                    if (!IsAdministrator)
                    {
                        ElevateWithConfirmation("Права администратора", $"Для восстановления службы {svc.ServiceName} требуются права администратора.", "--tab 2");
                        return;
                    }
                    var (ok, msg) = await Task.Run(() => ServiceControlHelper.RestoreService(svc));
                    if (ok) _backup.RemoveDisabledService(svc.ServiceName);
                    RefreshBackupList();
                    ShowToast(msg, isError: !ok);
                }, onError: HandleCommandError)
            });
        }

        foreach (var st in snapshot.DisabledStartupItems.Values.OrderByDescending(x => x.DisabledAtUtc))
        {
            BackupItems.Add(new BackupDisplayItem
            {
                Kind = "Автозагрузка",
                Key = st.Id,
                Title = st.Name,
                Details = $"{st.Location} • {st.Command}",
                SavedAtText = st.DisabledAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                RestoreCommand = new AsyncRelayCommand(async () =>
                {
                    var (ok, msg, needsAdmin) = await Task.Run(() => StartupManagerService.RestoreStartupItem(st, _backup, _logger));
                    if (needsAdmin)
                    {
                        ElevateWithConfirmation("Права администратора", msg, "--tab 2");
                        return;
                    }
                    RefreshBackupList();
                    ShowToast(msg, isError: !ok);
                }, onError: HandleCommandError)
            });
        }

        RaisePropertyChanged(nameof(HasBackups));
    }

    private void ApplyTweakFilter()
    {
        var query = SearchQuery.Trim();
        var filtered = AllTweaks.Where(t =>
        {
            bool catOk = SelectedCategoryFilter switch
            {
                "ACTIVE" => t.IsEnabled,
                "INACTIVE" => !t.IsEnabled,
                "CPU" => t.Category == TweakCategory.CpuAndSystem,
                "GPU" => t.Category == TweakCategory.GpuAndGaming,
                "NET" => t.Category == TweakCategory.NetworkAndInput,
                "DISK" => t.Category == TweakCategory.StorageAndMemory,
                _ => true
            };

            if (!catOk) return false;
            if (string.IsNullOrEmpty(query)) return true;

            return t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   t.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        FilteredTweaks.Clear();
        foreach (var item in filtered) FilteredTweaks.Add(item);
    }

    private void ApplyReportRecommendationFilter()
    {
        var items = AllReportRecommendations.Where(r => SelectedReportCategoryFilter switch
        {
            "SYS" => r.Category is RecommendationCategory.SystemAndGaming or RecommendationCategory.StorageAndHardware,
            "SVC" => r.Category == RecommendationCategory.WindowsServices,
            "STARTUP" => r.Category == RecommendationCategory.StartupPrograms,
            "VISUAL" => r.Category is RecommendationCategory.VisualAndTelemetry or RecommendationCategory.BackgroundProcesses,
            _ => true
        }).ToList();

        FilteredReportRecommendations.Clear();
        foreach (var item in items) FilteredReportRecommendations.Add(item);
    }

    private void ElevateWithConfirmation(string title, string explanation, string launchArguments)
    {
        var approved = RequestAdminConfirmationModal?.Invoke(title, explanation) ?? true;
        if (!approved) return;

        var (started, cancelled, error) = AdminHelper.RequestElevation(launchArguments);
        if (started)
        {
            ShutdownApplication?.Invoke();
        }
        else if (cancelled)
        {
            _logger.Warning("UAC", error);
            ShowToast("Запрос прав администратора отменён пользователем.");
        }
        else
        {
            _logger.Error("UAC", error);
            ShowToast(error, isError: true);
        }
    }

    public async void ShowToast(string message, bool isError = false)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;

        ToastMessage = message;
        IsToastError = isError;
        IsToastVisible = true;

        try
        {
            await Task.Delay(isError ? 6000 : 4200, token);
            IsToastVisible = false;
        }
        catch (OperationCanceledException) { }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    public void Dispose()
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
    }
}
