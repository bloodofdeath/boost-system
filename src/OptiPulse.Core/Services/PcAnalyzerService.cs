using System.Diagnostics;
using OptiPulse.Core.Models;
using OptiPulse.Core.Tweaks;

namespace OptiPulse.Core.Services;

/// <summary>
/// Orchestrates comprehensive read-only PC diagnostics, hardware-aware tweak evaluation,
/// optional service auditing, startup &amp; background process analysis, and weighted scoring.
/// </summary>
public sealed class PcAnalyzerService
{
    private readonly TweakRegistry _tweaks;
    private readonly BackupService _backup;
    private readonly AppLogger _logger;

    private static readonly (string ServiceName, string DisplayName, string Reason, RecommendationImpact Impact, RecommendationSafety Safety)[] OptionalServicesCatalog =
    [
        ("DiagTrack", "Телеметрия Windows (DiagTrack)", "Собирает и отправляет диагностические данные Microsoft в фоне, потребляя CPU, диск и сеть.", RecommendationImpact.High, RecommendationSafety.Safe),
        ("dmwappushservice", "Маршрутизация WAP-push (Телеметрия)", "Вспомогательная служба телеметрии и push-сообщений управления устройствами.", RecommendationImpact.Low, RecommendationSafety.Safe),
        ("WerSvc", "Служба отчётов об ошибках Windows (WER)", "Создаёт дампы памяти и отправляет отчёты о сбоях в Microsoft, нагружая диск при вылетах приложений.", RecommendationImpact.Medium, RecommendationSafety.Safe),
        ("PcaSvc", "Помощник по совместимости программ", "Отслеживает запуск программ в фоне для выявления проблем совместимости старого ПО.", RecommendationImpact.Low, RecommendationSafety.Safe),
        ("MapsBroker", "Диспетчер загруженных карт", "Фоновое обновление автономных карт Windows (не нужно, если вы не пользуетесь приложением «Карты»).", RecommendationImpact.Low, RecommendationSafety.Safe),
        ("Fax", "Служба факсов Windows", "Устаревшая служба отправки/приёма факсов, не используется на современных игровых ПК.", RecommendationImpact.Low, RecommendationSafety.Safe),
        ("RemoteRegistry", "Удалённый реестр", "Позволяет удалённо изменять реестр по сети — рекомендуется отключить для безопасности и скорости.", RecommendationImpact.Low, RecommendationSafety.Safe),
        ("RetailDemo", "Служба розничной демонстрации", "Предназначена только для витринных ПК в магазинах.", RecommendationImpact.Low, RecommendationSafety.Safe),
        ("DoSvc", "Оптимизация доставки (P2P обновления)", "Может раздавать файлы обновлений Windows другим ПК по сети, создавая скачки пинга.", RecommendationImpact.Medium, RecommendationSafety.Moderate),
        ("XblAuthManager", "Диспетчер проверки подлинности Xbox Live", "Фоновая служба Xbox Live. Отключайте только если НЕ играете в игры Microsoft Store / Game Pass.", RecommendationImpact.Low, RecommendationSafety.Moderate),
        ("XblGameSave", "Служба сохранений игр Xbox Live", "Синхронизирует сохранения игр Xbox/Game Pass. Не отключайте, если используете Game Pass.", RecommendationImpact.Low, RecommendationSafety.Moderate),
        ("XboxNetApiSvc", "Сетевая служба Xbox Live", "Нужна только для мультиплеера игр из Microsoft Store / Xbox App.", RecommendationImpact.Low, RecommendationSafety.Moderate),
        ("XboxGipSvc", "Служба управления аксессуарами Xbox", "Нужна для фирменных беспроводных аксессуаров Xbox. Безопасно отключить, если не используете.", RecommendationImpact.Low, RecommendationSafety.Moderate)
    ];

    private static readonly HashSet<string> ProtectedSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass", "svchost",
        "fontdrvhost", "winlogon", "dwm", "Memory Compression", "explorer", "sihost", "taskhostw",
        "RuntimeBroker", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "ctfmon",
        "MsMpEng", "NisSrv", "SecurityHealthService", "SecurityHealthSystray", "audiodg", "conhost",
        "WmiPrvSE", "OptiPulse", "TextInputHost", "ApplicationFrameHost", "dllhost", "spoolsv",
        "nvcontainer", "NVDisplay.Container", "RadeonSoftware", "igfxEM", "RtkAudUService64",
        "smartscreen", "MoUsoCoreWorker", "TiWorker", "TrustedInstaller", "SearchIndexer", "MsSense"
    };

    private static readonly HashSet<string> OptionalPreferenceTweaks = new(StringComparer.OrdinalIgnoreCase)
    {
        "unreal_engine_booster",
        "reduce_keyboard_delay",
        "reduce_input_lag",
        "disable_perf_counters"
    };

    public PcAnalyzerService(TweakRegistry tweaks, BackupService backup, AppLogger logger)
    {
        _tweaks = tweaks;
        _backup = backup;
        _logger = logger;
    }

    public async Task<PcAnalysisReport> AnalyzeAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.Info("Analyzer", "Запущен комплексный анализ ПК...");

        var report = new PcAnalysisReport
        {
            GeneratedAtLocal = DateTime.Now,
            MachineName = Environment.MachineName,
            WasRunAsAdmin = AdminHelper.IsAdministrator(),
            TotalTweaksCount = _tweaks.All.Count
        };

        progress?.Report("Параллельный сбор данных о системе, оборудовании, службах и автозагрузке...");

        // Run independent read-only scans concurrently for fast response
        var hardwareTask = HardwareInspector.CollectAsync(ct);
        var tweakStatesTask = Task.Run(() => _tweaks.QueryAllStatusesParallel(), ct);
        var serviceStatesTask = Task.Run(() =>
            OptionalServicesCatalog
                .AsParallel()
                .AsOrdered()
                .Select(svc => (svc, State: ServiceControlHelper.GetServiceState(svc.ServiceName)))
                .ToList(), ct);
        var startupTask = Task.Run(StartupManagerService.ScanStartupApps, ct);
        var processesTask = Task.Run(ScanTopBackgroundProcesses, ct);

        await Task.WhenAll(hardwareTask, tweakStatesTask, serviceStatesTask, startupTask, processesTask).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        report.Hardware = await hardwareTask.ConfigureAwait(false);
        report.StartupApps = await startupTask.ConfigureAwait(false);
        report.TopProcesses = await processesTask.ConfigureAwait(false);

        progress?.Report("Оценка 14 игровых твиков, служб и параметров Windows...");
        EvaluateTweaks(report, await tweakStatesTask.ConfigureAwait(false));
        EvaluateOptionalServices(report, await serviceStatesTask.ConfigureAwait(false));
        EvaluateStartupApps(report);
        EvaluateHeavyProcesses(report);
        EvaluateExtraSystemSettings(report);

        progress?.Report("Расчёт взвешенной оценки и формирование отчёта...");
        RecalculateScore(report);

        report.Recommendations = report.Recommendations
            .OrderBy(r => r.IsOptionalPreference ? 1 : 0)
            .ThenBy(r => r.Impact)
            .ThenBy(r => r.Safety)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.Success("Analyzer", $"Анализ завершён. Оценка: {report.OptimizationScore}/100, найдено рекомендаций: {report.Recommendations.Count}, уже оптимизировано: {report.AlreadyOptimizedItems.Count}.");
        return report;
    }

    private static void EvaluateTweaks(PcAnalysisReport report, IReadOnlyList<(ITweak Tweak, TweakStatusInfo Status)> tweakStates)
    {
        int activeCount = 0;
        foreach (var (tweak, status) in tweakStates)
        {
            var d = tweak.Descriptor;
            if (status.IsEnabled)
            {
                activeCount++;
                report.AlreadyOptimizedItems.Add($"{d.Title} ({status.StatusNote})");
                continue;
            }

            if (!status.IsSupported)
            {
                continue;
            }

            var isOptional = OptionalPreferenceTweaks.Contains(d.Id);

            // Hardware-aware guard: SuperFetch on HDD
            if (d.Id == "disable_superfetch" && !report.Hardware.HasSsdSystemDrive)
            {
                report.Recommendations.Add(new AnalysisRecommendation
                {
                    Id = $"tweak_{d.Id}",
                    Title = $"[Осторожно на HDD] {d.Title}",
                    Description = "Системный диск не подтверждён как SSD. На механических HDD служба SuperFetch (SysMain) обычно полезна — отключайте только если ОС на SSD.",
                    CurrentState = status.StatusNote,
                    ExpectedBenefit = "Освобождение ОЗУ (на HDD может замедлить запуск программ)",
                    Category = RecommendationCategory.StorageAndHardware,
                    Impact = RecommendationImpact.Low,
                    Safety = RecommendationSafety.Caution,
                    ActionType = RecommendationActionType.EnableTweak,
                    ActionTarget = d.Id,
                    RequiresAdmin = d.RequiresAdmin,
                    IsOptionalPreference = true
                });
                continue;
            }

            if (report.Hardware.IsLaptop && d.Id is "ultimate_performance_power_plan" or "unpark_cpu_cores" or "disable_network_power_saving")
            {
                report.Recommendations.Add(new AnalysisRecommendation
                {
                    Id = $"tweak_{d.Id}",
                    Title = $"{d.Title} (для игры от сети)",
                    Description = $"{d.Description} Обнаружен ноутбук: рекомендуется включать при питании от розетки.",
                    CurrentState = status.StatusNote,
                    ExpectedBenefit = "Стабильные частоты CPU/сети без энергосберегающих просадок",
                    Category = RecommendationCategory.SystemAndGaming,
                    Impact = RecommendationImpact.Medium,
                    Safety = RecommendationSafety.Moderate,
                    ActionType = RecommendationActionType.EnableTweak,
                    ActionTarget = d.Id,
                    RequiresAdmin = d.RequiresAdmin
                });
                continue;
            }

            var impact = d.Id switch
            {
                "disable_gamebar_dvr" or "ultimate_performance_power_plan" or "max_gaming_performance" or "gpu_high_performance" or "minimal_bg_priority" => RecommendationImpact.High,
                "unpark_cpu_cores" or "disable_superfetch" or "disable_windows_search" or "disable_network_power_saving" or "disable_ntfs_last_access" => RecommendationImpact.Medium,
                _ => RecommendationImpact.Low
            };

            var safety = d.NoticeLevel == TweakNoticeLevel.Warning
                ? RecommendationSafety.Moderate
                : RecommendationSafety.Safe;

            report.Recommendations.Add(new AnalysisRecommendation
            {
                Id = $"tweak_{d.Id}",
                Title = isOptional ? $"{d.Title} (опционально)" : d.Title,
                Description = d.Description,
                CurrentState = string.IsNullOrWhiteSpace(status.StatusNote) ? "Отключено" : status.StatusNote,
                ExpectedBenefit = d.TechnicalDetails,
                Category = RecommendationCategory.SystemAndGaming,
                Impact = impact,
                Safety = safety,
                ActionType = RecommendationActionType.EnableTweak,
                ActionTarget = d.Id,
                RequiresAdmin = d.RequiresAdmin,
                IsOptionalPreference = isOptional
            });
        }

        report.ActiveTweaksCount = activeCount;
    }

    private static void EvaluateOptionalServices(
        PcAnalysisReport report,
        IReadOnlyList<((string ServiceName, string DisplayName, string Reason, RecommendationImpact Impact, RecommendationSafety Safety) Svc, ServiceStateInfo State)> states)
    {
        foreach (var (svc, state) in states)
        {
            if (!state.Exists) continue;

            if (state.IsDisabled)
            {
                report.AlreadyOptimizedItems.Add($"Служба «{svc.DisplayName}» отключена.");
                continue;
            }

            if (state.IsRunning || state.StartMode == 2)
            {
                var effectiveImpact = state.IsRunning ? svc.Impact : RecommendationImpact.Low;
                report.Recommendations.Add(new AnalysisRecommendation
                {
                    Id = $"svc_{svc.ServiceName}",
                    Title = $"Отключить службу: {svc.DisplayName}",
                    Description = svc.Reason,
                    CurrentState = $"{(state.IsRunning ? "Работает сейчас" : "Остановлена")} • Запуск: {state.StartModeRussian}",
                    ExpectedBenefit = "Меньше фоновой активности CPU, диска и сетевых запросов",
                    Category = RecommendationCategory.WindowsServices,
                    Impact = effectiveImpact,
                    Safety = svc.Safety,
                    ActionType = RecommendationActionType.DisableService,
                    ActionTarget = svc.ServiceName,
                    RequiresAdmin = true
                });
            }
        }
    }

    private static void EvaluateStartupApps(PcAnalysisReport report)
    {
        var activeStartup = report.StartupApps.Where(s => !s.IsDisabledInWindows).ToList();
        var heavyStartup = activeStartup.Where(s => s.IsRecommendedToDisable).ToList();

        foreach (var app in heavyStartup)
        {
            report.Recommendations.Add(new AnalysisRecommendation
            {
                Id = $"startup_{app.Id}",
                Title = $"Убрать из автозагрузки: {app.Name}",
                Description = app.RecommendationReason,
                CurrentState = $"Активно в автозагрузке ({app.Location})",
                ExpectedBenefit = "Ускорение загрузки Windows и меньше фоновых программ в ОЗУ",
                Category = RecommendationCategory.StartupPrograms,
                Impact = RecommendationImpact.Medium,
                Safety = RecommendationSafety.Safe,
                ActionType = RecommendationActionType.DisableStartupItem,
                ActionTarget = app.Id,
                RequiresAdmin = app.RequiresAdmin
            });
        }

        if (activeStartup.Count <= 4 && heavyStartup.Count == 0)
        {
            report.AlreadyOptimizedItems.Add($"Чистая автозагрузка ({activeStartup.Count} активных программ).");
        }
    }

    private static List<ProcessSummaryItem> ScanTopBackgroundProcesses()
    {
        var grouped = new Dictionary<string, (int Count, long MemoryBytes)>(StringComparer.OrdinalIgnoreCase);
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var name = proc.ProcessName;
                if (ProtectedSystemProcesses.Contains(name)) continue;

                long ws = proc.WorkingSet64;
                long priv = proc.PrivateMemorySize64;
                long accurateBytes = priv > 0 ? Math.Min(ws, priv) : ws;

                if (grouped.TryGetValue(name, out var existing))
                {
                    grouped[name] = (existing.Count + 1, existing.MemoryBytes + accurateBytes);
                }
                else
                {
                    grouped[name] = (1, accurateBytes);
                }
            }
            catch { }
            finally
            {
                proc.Dispose();
            }
        }

        return grouped
            .Select(kv =>
            {
                var mb = Math.Round(kv.Value.MemoryBytes / 1024.0 / 1024.0, 1);
                return new ProcessSummaryItem
                {
                    Name = kv.Key,
                    InstanceCount = kv.Value.Count,
                    MemoryMb = mb,
                    IsSafeToCloseBeforeGaming = mb >= 120,
                    Note = GetProcessHint(kv.Key, mb)
                };
            })
            .Where(p => p.MemoryMb >= 50)
            .OrderByDescending(p => p.MemoryMb)
            .Take(12)
            .ToList();
    }

    private static string GetProcessHint(string procName, double mb)
    {
        if (procName.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
            procName.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
            procName.Contains("firefox", StringComparison.OrdinalIgnoreCase) ||
            procName.Contains("opera", StringComparison.OrdinalIgnoreCase) ||
            procName.Contains("yandex", StringComparison.OrdinalIgnoreCase) ||
            procName.Contains("brave", StringComparison.OrdinalIgnoreCase))
        {
            return $"Браузер (~{mb:0} МБ) — закрывайте лишние вкладки или браузер перед игрой.";
        }
        if (procName.Contains("Discord", StringComparison.OrdinalIgnoreCase))
        {
            return "Рекомендуется отключить аппаратное ускорение и оверлей в настройках Discord.";
        }
        if (procName.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
        {
            return "Приостановите синхронизацию OneDrive во время игр.";
        }
        return mb >= 250 ? "Крупное фоновое приложение — можно закрыть перед запуском требовательных игр." : "Фоновое приложение.";
    }

    private static void EvaluateHeavyProcesses(PcAnalysisReport report)
    {
        var heavy = report.TopProcesses.Where(p => p.MemoryMb >= 250).Take(4).ToList();
        if (heavy.Count == 0) return;

        var totalMb = Math.Round(heavy.Sum(p => p.MemoryMb));
        var listStr = string.Join(", ", heavy.Select(p => $"{p.Name} ({p.MemoryMb:0} МБ)"));

        var ramPct = report.Hardware.RamUsagePercent;
        var impact = ramPct >= 75 ? RecommendationImpact.High :
                     ramPct >= 58 ? RecommendationImpact.Medium :
                     RecommendationImpact.Low;

        report.Recommendations.Add(new AnalysisRecommendation
        {
            Id = "proc_heavy_ram",
            Title = $"Фоновые программы перед игрой (~{totalMb:0} МБ ОЗУ)",
            Description = $"Крупнейшие открытые приложения сейчас: {listStr}. Их можно закрыть перед запуском игр во вкладке «Фоновые процессы».",
            CurrentState = $"Занято ОЗУ: {report.Hardware.UsedRamGb:0.0} / {report.Hardware.TotalRamGb:0.0} ГБ ({ramPct:0}%)",
            ExpectedBenefit = $"Высвобождение до ~{totalMb:0} МБ ОЗУ и снижение фоновой нагрузки CPU",
            Category = RecommendationCategory.BackgroundProcesses,
            Impact = impact,
            Safety = RecommendationSafety.Safe,
            ActionType = RecommendationActionType.None,
            IsOptionalPreference = ramPct < 58
        });
    }

    private static void EvaluateExtraSystemSettings(PcAnalysisReport report)
    {
        // 1. Windows Transparency Effects
        var transparency = RegistryHelper.ReadDword(RegistryHelper.Hkcu, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency");
        if (transparency is null or 1)
        {
            report.Recommendations.Add(new AnalysisRecommendation
            {
                Id = QuickSettingsService.TransparencyKey,
                Title = "Отключить эффекты прозрачности Windows (Acrylic / Mica)",
                Description = "Эффекты полупрозрачности окон и панели задач нагружают DWM и видеокарту в фоне, особенно при игре в окне без рамки.",
                CurrentState = "Эффекты прозрачности включены",
                ExpectedBenefit = "Снижение нагрузки на DWM и ускорение отклика интерфейса",
                Category = RecommendationCategory.VisualAndTelemetry,
                Impact = RecommendationImpact.Low,
                Safety = RecommendationSafety.Safe,
                ActionType = RecommendationActionType.ApplyQuickSetting,
                ActionTarget = "disable_transparency",
                RequiresAdmin = false
            });
        }
        else
        {
            report.AlreadyOptimizedItems.Add("Эффекты прозрачности Windows отключены.");
        }

        // 2. Background UWP Apps
        var bgAppsDisabled = RegistryHelper.ReadDword(RegistryHelper.Hkcu, @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled");
        if (bgAppsDisabled != 1)
        {
            report.Recommendations.Add(new AnalysisRecommendation
            {
                Id = QuickSettingsService.BackgroundUwpKey,
                Title = "Запретить фоновую работу приложений Microsoft Store (UWP)",
                Description = "Запрещает встроенным приложениям Windows (Погода, Почта, Новости, Советы и др.) просыпаться в фоне и расходовать ОЗУ/сеть.",
                CurrentState = "Фоновые UWP-приложения разрешены",
                ExpectedBenefit = "Меньше скрытой фоновой активности CPU, сети и ОЗУ",
                Category = RecommendationCategory.VisualAndTelemetry,
                Impact = RecommendationImpact.Medium,
                Safety = RecommendationSafety.Safe,
                ActionType = RecommendationActionType.ApplyQuickSetting,
                ActionTarget = "disable_bg_uwp",
                RequiresAdmin = false
            });
        }
        else
        {
            report.AlreadyOptimizedItems.Add("Фоновая работа UWP-приложений отключена.");
        }

        // 3. Windows Telemetry Policy
        var telemetryLevel = RegistryHelper.ReadDword(RegistryHelper.Hklm, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
        if (telemetryLevel is null or > 0)
        {
            report.Recommendations.Add(new AnalysisRecommendation
            {
                Id = QuickSettingsService.MinimizeTelemetryKey,
                Title = "Снизить уровень телеметрии и диагностики Windows",
                Description = "Ограничивает сбор диагностических данных Windows (AllowTelemetry = 0).",
                CurrentState = $"AllowTelemetry = {(telemetryLevel.HasValue ? telemetryLevel.Value.ToString() : "По умолчанию")}",
                ExpectedBenefit = "Меньше фоновых задач отправки данных и выше приватность",
                Category = RecommendationCategory.VisualAndTelemetry,
                Impact = RecommendationImpact.Medium,
                Safety = RecommendationSafety.Safe,
                ActionType = RecommendationActionType.ApplyQuickSetting,
                ActionTarget = "minimize_telemetry",
                RequiresAdmin = true
            });
        }
        else
        {
            report.AlreadyOptimizedItems.Add("Уровень телеметрии Windows снижен до минимума.");
        }

        // 4. System Disk Free Space Check
        var sysLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
        var sysDrive = report.Hardware.Disks.FirstOrDefault(d => d.DriveLetter.Equals(sysLetter, StringComparison.OrdinalIgnoreCase));
        if (sysDrive is not null && sysDrive.FreePercent < 15.0)
        {
            report.Recommendations.Add(new AnalysisRecommendation
            {
                Id = "hw_low_disk_space",
                Title = $"Освободить место на системном диске {sysDrive.DriveLetter} (свободно {sysDrive.FreePercent:0.0}%)",
                Description = $"На диске {sysDrive.DriveLetter} осталось {sysDrive.FreeGb:0.0} ГБ из {sysDrive.TotalGb:0.0} ГБ. Когда на SSD остаётся меньше 15% свободного места, падает скорость записи и страдает файл подкачки.",
                CurrentState = $"Свободно {sysDrive.FreeGb:0.0} ГБ ({sysDrive.FreePercent:0.0}%)",
                ExpectedBenefit = "Восстановление нормальной скорости SSD и устранение подгрузок в играх",
                Category = RecommendationCategory.StorageAndHardware,
                Impact = RecommendationImpact.High,
                Safety = RecommendationSafety.Safe,
                ActionType = RecommendationActionType.None
            });
        }
        else if (sysDrive is not null)
        {
            report.AlreadyOptimizedItems.Add($"Системный диск {sysDrive.DriveLetter} ({sysDrive.MediaType}) имеет достаточный запас места ({sysDrive.FreeGb:0.0} ГБ / {sysDrive.FreePercent:0.0}%).");
        }

        // 5. High RAM Pressure Warning
        if (report.Hardware.TotalRamGb > 0 && report.Hardware.RamUsagePercent >= 78.0)
        {
            report.Recommendations.Add(new AnalysisRecommendation
            {
                Id = "hw_high_ram",
                Title = $"Высокая загрузка оперативной памяти ({report.Hardware.RamUsagePercent:0}%)",
                Description = $"Сейчас используется {report.Hardware.UsedRamGb:0.0} ГБ из {report.Hardware.TotalRamGb:0.0} ГБ ОЗУ. При нехватке памяти Windows начинает активно использовать файл подкачки, вызывая статтеры.",
                CurrentState = $"Свободно всего {report.Hardware.FreeRamGb:0.0} ГБ ОЗУ",
                ExpectedBenefit = "Устранение микрофризов за счёт освобождения ОЗУ",
                Category = RecommendationCategory.StorageAndHardware,
                Impact = RecommendationImpact.High,
                Safety = RecommendationSafety.Safe,
                ActionType = RecommendationActionType.None
            });
        }
    }

    /// <summary>
    /// Honest bounded 5-bucket weighted scoring model (10..100).
    /// Can be called dynamically whenever the user applies recommendations inside the UI.
    /// </summary>
    public static void RecalculateScore(PcAnalysisReport report)
    {
        var openCoreRecs = report.Recommendations.Where(r => !r.IsResolved && !r.IsOptionalPreference).ToList();

        // Bucket 1: System & Gaming Core Tweaks (max 40 pts)
        int sysPenalty = openCoreRecs
            .Where(r => r.Category == RecommendationCategory.SystemAndGaming)
            .Sum(r => r.Impact switch
            {
                RecommendationImpact.High => 6,
                RecommendationImpact.Medium => 4,
                _ => 2
            });
        int sysScore = Math.Max(0, 40 - Math.Min(40, sysPenalty));

        // Bucket 2: Background Services & Telemetry (max 20 pts)
        int svcPenalty = openCoreRecs
            .Where(r => r.Category == RecommendationCategory.WindowsServices || r.Id == QuickSettingsService.MinimizeTelemetryKey)
            .Sum(r => r.Impact switch
            {
                RecommendationImpact.High => 6,
                RecommendationImpact.Medium => 3,
                _ => 1
            });
        int svcScore = Math.Max(0, 20 - Math.Min(20, svcPenalty));

        // Bucket 3: Startup Cleanliness (max 15 pts)
        int startupCount = openCoreRecs.Count(r => r.Category == RecommendationCategory.StartupPrograms);
        int startupScore = Math.Max(0, 15 - Math.Min(15, startupCount * 3));

        // Bucket 4: Live Resource Health: RAM, CPU, Disk C: (max 15 pts)
        int hwScore = 15;
        if (report.Hardware.RamUsagePercent >= 85) hwScore -= 7;
        else if (report.Hardware.RamUsagePercent >= 72) hwScore -= 4;
        if (report.Hardware.CpuLoadPercent >= 75) hwScore -= 4;
        else if (report.Hardware.CpuLoadPercent >= 55) hwScore -= 2;
        if (openCoreRecs.Any(r => r.Id == "hw_low_disk_space")) hwScore -= 6;
        hwScore = Math.Max(0, hwScore);

        // Bucket 5: Visual & Background UWP Apps (max 10 pts)
        int visualPenalty = openCoreRecs
            .Where(r => r.Category == RecommendationCategory.VisualAndTelemetry && r.Id != QuickSettingsService.MinimizeTelemetryKey)
            .Sum(r => r.Impact == RecommendationImpact.Medium ? 4 : 2);
        int visualScore = Math.Max(0, 10 - Math.Min(10, visualPenalty));

        report.OptimizationScore = Math.Clamp(sysScore + svcScore + startupScore + hwScore + visualScore, 10, 100);
        report.ScoreVerdict = report.OptimizationScore switch
        {
            >= 82 => "Отличная оптимизация — система настроена для максимального FPS",
            >= 62 => "Хорошее состояние — осталось несколько полезных улучшений",
            >= 45 => "Средний уровень — можно заметно ускорить систему и снизить фоновую нагрузку",
            _ => "Высокий потенциал ускорения — обнаружено много неоптимизированных параметров"
        };
    }

    public (bool Success, string Message, bool RequiresAdmin) DisableOptionalService(string serviceName)
    {
        if (!AdminHelper.IsAdministrator())
        {
            return (false, $"Для отключения службы {serviceName} требуются права администратора.", true);
        }

        var backup = ServiceControlHelper.CaptureServiceBackup(serviceName);
        var (ok, msg) = ServiceControlHelper.DisableAndStopService(serviceName);
        if (ok)
        {
            _backup.SaveDisabledService(backup);
            _logger.Success("Analyzer", msg);
        }
        else
        {
            _logger.Error("Analyzer", msg);
        }
        return (ok, msg, false);
    }

    public (bool Success, string Message) CloseBackgroundProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName) || ProtectedSystemProcesses.Contains(processName))
        {
            return (false, $"Процесс «{processName}» защищён и не может быть завершён через OptiPulse.");
        }

        int closed = 0;
        int failed = 0;
        foreach (var proc in Process.GetProcessesByName(processName))
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: false);
                    closed++;
                }
            }
            catch
            {
                failed++;
            }
            finally
            {
                proc.Dispose();
            }
        }

        if (closed > 0)
        {
            _logger.Info("Process", $"Завершён фоновый процесс {processName} (экземпляров: {closed}).");
            return (true, $"Процесс «{processName}» завершён (закрыто процессов: {closed}).");
        }

        return (false, failed > 0
            ? $"Не удалось завершить «{processName}» (требуются права администратора или процесс защищён)."
            : $"Процесс «{processName}» уже завершён.");
    }

    public (bool Success, string Message, bool RequiresAdmin) DisableStartupItem(StartupAppItem item) =>
        StartupManagerService.DisableStartupItem(item, _backup, _logger);

    public static (bool Success, string Message, bool RequiresAdmin) RestoreStartupItemStatic(
        StartupItemBackup backup,
        BackupService backupService,
        AppLogger logger) =>
        StartupManagerService.RestoreStartupItem(backup, backupService, logger);

    public (bool Success, string Message, bool RequiresAdmin) ApplyQuickSetting(string settingId) =>
        QuickSettingsService.Apply(settingId, _backup, _logger);
}
