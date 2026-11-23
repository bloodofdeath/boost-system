using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OptiPulse.Core.Models;
using OptiPulse.Core.Tweaks;

namespace OptiPulse.Core.Services;

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

    private static readonly (string Keyword, string FriendlyNote)[] HeavyStartupHints =
    [
        ("OneDrive", "Синхронизация облака в фоне нагружает диск и сеть во время игр."),
        ("Discord", "Запускается при старте Windows и занимает память в фоне — удобнее запускать вручную."),
        ("Steam", "Игровой клиент в автозагрузке замедляет старт системы и держит фоновые обновления."),
        ("EpicGamesLauncher", "Тяжёлый лаунчер в автозагрузке — запускайте только когда нужен."),
        ("Teams", "Microsoft Teams в автозагрузке активно расходует ОЗУ и CPU в фоне."),
        ("Spotify", "Музыкальный клиент в автозагрузке расходует ОЗУ и замедляет загрузку ПК."),
        ("Skype", "Редко используемый мессенджер в автозагрузке."),
        ("Telegram", "Мессенджер в автозагрузке — можно запускать вручную для более быстрого старта ПК."),
        ("Adobe", "Фоновые агенты обновлений Adobe (Creative Cloud / ARM) постоянно работают в памяти."),
        ("CCXProcess", "Фоновый процесс Adobe Creative Cloud."),
        ("EdgeAutoLaunch", "Предзагрузка Microsoft Edge в фоне занимает ОЗУ даже без открытого браузера."),
        ("RiotClient", "Лаунчер Riot Games в автозагрузке."),
        ("Battle.net", "Лаунчер Battle.net в автозагрузке."),
        ("Overwolf", "Игровой оверлей Overwolf часто снижает FPS и вызывает микрофризы."),
        ("Wallpaper", "Живые обои в автозагрузке нагружают GPU и VRAM."),
        ("Cortana", "Устаревший голосовой помощник в фоне."),
        ("uTorrent", "Торрент-клиент в автозагрузке создаёт фоновую нагрузку на сеть и диск."),
        ("BitTorrent", "Торрент-клиент в автозагрузке создаёт фоновую нагрузку на сеть и диск.")
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
        return await Task.Run(() => AnalyzeCore(progress, ct), ct);
    }

    private PcAnalysisReport AnalyzeCore(IProgress<string>? progress, CancellationToken ct)
    {
        _logger.Info("Analyzer", "Запущен комплексный анализ ПК...");
        var report = new PcAnalysisReport
        {
            GeneratedAtLocal = DateTime.Now,
            MachineName = Environment.MachineName,
            WasRunAsAdmin = AdminHelper.IsAdministrator(),
            TotalTweaksCount = _tweaks.All.Count
        };

        progress?.Report("Сбор информации о процессоре, видеокарте, памяти и дисках...");
        report.Hardware = CollectHardwareProfile();
        ct.ThrowIfCancellationRequested();

        progress?.Report("Проверка 14 системных и игровых оптимизаций...");
        EvaluateTweaks(report);
        ct.ThrowIfCancellationRequested();

        progress?.Report("Анализ фоновых служб Windows...");
        EvaluateOptionalServices(report);
        ct.ThrowIfCancellationRequested();

        progress?.Report("Сканирование автозагрузки (с учётом Диспетчера задач)...");
        report.StartupApps = ScanStartupApps();
        EvaluateStartupApps(report);
        ct.ThrowIfCancellationRequested();

        progress?.Report("Анализ фоновых процессов и потребления ОЗУ...");
        report.TopProcesses = ScanTopBackgroundProcesses();
        EvaluateHeavyProcesses(report);
        ct.ThrowIfCancellationRequested();

        progress?.Report("Проверка визуальных эффектов, телеметрии и состояния дисков...");
        EvaluateExtraSystemSettings(report);

        progress?.Report("Расчёт взвешенной оценки и формирование отчёта...");
        RecalculateScore(report);

        // Sort recommendations: actionable core items first by impact & safety, optional preferences at bottom
        report.Recommendations = report.Recommendations
            .OrderBy(r => r.IsOptionalPreference ? 1 : 0)
            .ThenBy(r => r.Impact)
            .ThenBy(r => r.Safety)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.Success("Analyzer", $"Анализ завершён. Оценка: {report.OptimizationScore}/100, найдено рекомендаций: {report.Recommendations.Count}, уже оптимизировано: {report.AlreadyOptimizedItems.Count}.");
        return report;
    }

    private HardwareProfile CollectHardwareProfile()
    {
        var hw = new HardwareProfile
        {
            SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            CpuThreads = Environment.ProcessorCount
        };

        // 1. OS Info
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                hw.OsCaption = obj["Caption"]?.ToString()?.Trim() ?? "Windows";
                hw.OsVersion = obj["Version"]?.ToString()?.Trim() ?? string.Empty;
                hw.OsBuild = obj["BuildNumber"]?.ToString()?.Trim() ?? string.Empty;
                break;
            }
        }
        catch { }

        // 2. Laptop detection (Battery presence)
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT BatteryStatus FROM Win32_Battery");
            hw.IsLaptop = searcher.Get().Count > 0;
        }
        catch { }

        // 3. CPU Info
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                hw.CpuName = obj["Name"]?.ToString()?.Trim() ?? hw.CpuName;
                hw.CpuCores = Convert.ToInt32(obj["NumberOfCores"] ?? Environment.ProcessorCount);
                hw.CpuThreads = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? Environment.ProcessorCount);
                hw.CpuMaxClockMhz = Convert.ToInt32(obj["MaxClockSpeed"] ?? 0);
                break;
            }
        }
        catch { }

        hw.CpuLoadPercent = MeasureCpuUsagePercent();

        // 4. GPU Info (prefer discrete GPU first over Microsoft Basic Display Adapter)
        try
        {
            var gpus = new List<(string Name, string Driver)>();
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim();
                var drv = obj["DriverVersion"]?.ToString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    gpus.Add((name, drv));
                }
            }

            var orderedGpus = gpus
                .OrderByDescending(g =>
                    g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                    g.Name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
                    g.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                .ThenBy(g => g.Name.Contains("Basic", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ToList();

            foreach (var g in orderedGpus)
            {
                hw.GpuNames.Add(g.Name);
                if (string.IsNullOrWhiteSpace(hw.GpuDriverVersion)) hw.GpuDriverVersion = g.Driver;
            }
        }
        catch { }

        // 5. RAM Info via Win32 GlobalMemoryStatusEx
        try
        {
            var mem = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref mem))
            {
                hw.TotalRamGb = Math.Round(mem.ullTotalPhys / 1024.0 / 1024.0 / 1024.0, 1);
                hw.FreeRamGb = Math.Round(mem.ullAvailPhys / 1024.0 / 1024.0 / 1024.0, 1);
                hw.UsedRamGb = Math.Round(Math.Max(0, hw.TotalRamGb - hw.FreeRamGb), 1);
            }
        }
        catch { }

        // 6. Exact Drive Letter -> Physical Disk mapping (NVMe SSD vs SATA SSD vs HDD)
        var driveMap = QueryLogicalDriveToPhysicalDiskMap();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var letter = drive.Name.TrimEnd('\\');
                var keyChar = letter.TrimEnd(':').ToUpperInvariant();
                var hasPhys = driveMap.TryGetValue(keyChar, out var phys);

                hw.Disks.Add(new DiskInfo
                {
                    DriveLetter = letter,
                    Model = hasPhys ? phys.Model : "Локальный диск",
                    MediaType = hasPhys ? phys.MediaType : "SSD/Диск",
                    TotalGb = Math.Round(drive.TotalSize / 1024.0 / 1024.0 / 1024.0, 1),
                    FreeGb = Math.Round(drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0, 1)
                });
            }
        }
        catch { }

        var sysLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':').ToUpperInvariant() ?? "C";
        var sysDisk = hw.Disks.FirstOrDefault(d => d.DriveLetter.StartsWith(sysLetter, StringComparison.OrdinalIgnoreCase));
        hw.HasSsdSystemDrive = sysDisk is not null &&
            (sysDisk.MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
             sysDisk.MediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase));

        // 7. Power Plan
        var (planGuid, planName) = UltimatePerformancePowerPlanTweak.GetActivePowerScheme();
        hw.ActivePowerPlanGuid = planGuid;
        hw.ActivePowerPlanName = planName;

        // 8. Active Network Adapter
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                            !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.Speed)
                .FirstOrDefault();

            if (nic is not null)
            {
                hw.ActiveNetworkAdapter = nic.Description;
                hw.NetworkLinkSpeed = nic.Speed >= 1_000_000_000
                    ? $"{nic.Speed / 1_000_000_000.0:0.#} Гбит/с"
                    : $"{nic.Speed / 1_000_000.0:0.#} Мбит/с";
            }
        }
        catch { }

        return hw;
    }

    private void EvaluateTweaks(PcAnalysisReport report)
    {
        int activeCount = 0;
        foreach (var tweak in _tweaks.All)
        {
            var status = tweak.GetStatus();
            if (status.IsEnabled)
            {
                activeCount++;
                report.AlreadyOptimizedItems.Add($"{tweak.Descriptor.Title} ({status.StatusNote})");
                continue;
            }

            var isOptional = OptionalPreferenceTweaks.Contains(tweak.Descriptor.Id);

            // Hardware-aware check for SuperFetch on HDD
            if (tweak.Descriptor.Id == "disable_superfetch" && !report.Hardware.HasSsdSystemDrive)
            {
                report.Recommendations.Add(new AnalysisRecommendation
                {
                    Id = $"tweak_{tweak.Descriptor.Id}",
                    Title = $"[Осторожно на HDD] {tweak.Descriptor.Title}",
                    Description = "Системный диск не подтверждён как SSD. На механических HDD служба SuperFetch (SysMain) обычно полезна — отключайте только если ОС на SSD.",
                    CurrentState = status.StatusNote,
                    ExpectedBenefit = "Освобождение ОЗУ (на HDD может замедлить запуск программ)",
                    Category = RecommendationCategory.StorageAndHardware,
                    Impact = RecommendationImpact.Low,
                    Safety = RecommendationSafety.Caution,
                    ActionType = RecommendationActionType.EnableTweak,
                    ActionTarget = tweak.Descriptor.Id,
                    RequiresAdmin = tweak.Descriptor.RequiresAdmin,
                    IsOptionalPreference = true
                });
                continue;
            }

            if (report.Hardware.IsLaptop && tweak.Descriptor.Id is "ultimate_performance_power_plan" or "unpark_cpu_cores" or "disable_network_power_saving")
            {
                report.Recommendations.Add(new AnalysisRecommendation
                {
                    Id = $"tweak_{tweak.Descriptor.Id}",
                    Title = $"{tweak.Descriptor.Title} (для игры от сети)",
                    Description = $"{tweak.Descriptor.Description} Обнаружен ноутбук: рекомендуется включать при питании от розетки.",
                    CurrentState = status.StatusNote,
                    ExpectedBenefit = "Стабильные частоты CPU/сети без энергосберегающих просадок",
                    Category = RecommendationCategory.SystemAndGaming,
                    Impact = RecommendationImpact.Medium,
                    Safety = RecommendationSafety.Moderate,
                    ActionType = RecommendationActionType.EnableTweak,
                    ActionTarget = tweak.Descriptor.Id,
                    RequiresAdmin = tweak.Descriptor.RequiresAdmin
                });
                continue;
            }

            var impact = tweak.Descriptor.Id switch
            {
                "disable_gamebar_dvr" or "ultimate_performance_power_plan" or "max_gaming_performance" or "gpu_high_performance" or "minimal_bg_priority" => RecommendationImpact.High,
                "unpark_cpu_cores" or "disable_superfetch" or "disable_windows_search" or "disable_network_power_saving" or "disable_ntfs_last_access" => RecommendationImpact.Medium,
                _ => RecommendationImpact.Low
            };

            var safety = tweak.Descriptor.NoticeLevel == TweakNoticeLevel.Warning
                ? RecommendationSafety.Moderate
                : RecommendationSafety.Safe;

            report.Recommendations.Add(new AnalysisRecommendation
            {
                Id = $"tweak_{tweak.Descriptor.Id}",
                Title = isOptional ? $"{tweak.Descriptor.Title} (опционально)" : tweak.Descriptor.Title,
                Description = tweak.Descriptor.Description,
                CurrentState = string.IsNullOrWhiteSpace(status.StatusNote) ? "Отключено" : status.StatusNote,
                ExpectedBenefit = tweak.Descriptor.TechnicalDetails,
                Category = RecommendationCategory.SystemAndGaming,
                Impact = impact,
                Safety = safety,
                ActionType = RecommendationActionType.EnableTweak,
                ActionTarget = tweak.Descriptor.Id,
                RequiresAdmin = tweak.Descriptor.RequiresAdmin,
                IsOptionalPreference = isOptional
            });
        }

        report.ActiveTweaksCount = activeCount;
    }

    private void EvaluateOptionalServices(PcAnalysisReport report)
    {
        foreach (var svc in OptionalServicesCatalog)
        {
            var state = ServiceControlHelper.GetServiceState(svc.ServiceName);
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

    private List<StartupAppItem> ScanStartupApps()
    {
        var items = new List<StartupAppItem>();

        var hkcuRunDisabled = ReadStartupApprovedDisabledSet(RegistryHelper.Hkcu, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
        var hklmRunDisabled = ReadStartupApprovedDisabledSet(RegistryHelper.Hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
        var hklmRun32Disabled = ReadStartupApprovedDisabledSet(RegistryHelper.Hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32");
        var hkcuFolderDisabled = ReadStartupApprovedDisabledSet(RegistryHelper.Hkcu, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder");
        var hklmFolderDisabled = ReadStartupApprovedDisabledSet(RegistryHelper.Hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder");

        ScanRegistryRunKey(items, RegistryHelper.Hkcu, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU Run (Пользователь)", requiresAdmin: false, hkcuRunDisabled);
        ScanRegistryRunKey(items, RegistryHelper.Hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM Run (Все пользователи)", requiresAdmin: true, hklmRunDisabled);
        ScanRegistryRunKey(items, RegistryHelper.Hklm, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM Run (32-bit)", requiresAdmin: true, hklmRun32Disabled);
        ScanStartupFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Папка автозагрузки (Пользователь)", requiresAdmin: false, hkcuFolderDisabled);
        ScanStartupFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Общая папка автозагрузки", requiresAdmin: true, hklmFolderDisabled);

        return items
            .OrderBy(i => i.IsDisabledInWindows ? 1 : 0)
            .ThenByDescending(i => i.IsRecommendedToDisable)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HashSet<string> ReadStartupApprovedDisabledSet(string root, string subKey)
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var baseKey = root == RegistryHelper.Hklm
                ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            if (key is null) return disabled;

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is byte[] bytes && bytes.Length > 0)
                {
                    // In Windows StartupApproved: even first byte (0x02, 0x06) = Enabled; odd (0x03, 0x07, 0x01, 0x05) = Disabled in Task Manager
                    if ((bytes[0] & 1) == 1)
                    {
                        disabled.Add(name);
                    }
                }
            }
        }
        catch { }
        return disabled;
    }

    private static void ScanRegistryRunKey(
        List<StartupAppItem> items,
        string root,
        string subKey,
        string locationLabel,
        bool requiresAdmin,
        HashSet<string> disabledInTaskManager)
    {
        try
        {
            using var baseKey = root == RegistryHelper.Hklm
                ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            if (key is null) return;

            foreach (var name in key.GetValueNames())
            {
                var cmd = key.GetValue(name)?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(cmd)) continue;

                var isDisabledWin = disabledInTaskManager.Contains(name);
                var (recommended, reason) = isDisabledWin
                    ? (false, "Уже отключено в Диспетчере задач Windows")
                    : MatchHeavyStartup(name, cmd);

                items.Add(new StartupAppItem
                {
                    Id = $"{root}:{subKey}:{name}",
                    Name = name,
                    Command = cmd,
                    Location = locationLabel,
                    Root = root,
                    SubKey = subKey,
                    IsRecommendedToDisable = recommended,
                    RecommendationReason = reason,
                    RequiresAdmin = requiresAdmin,
                    IsDisabledInWindows = isDisabledWin,
                    IsDisabled = isDisabledWin
                });
            }
        }
        catch { }
    }

    private static void ScanStartupFolder(
        List<StartupAppItem> items,
        string folderPath,
        string locationLabel,
        bool requiresAdmin,
        HashSet<string> disabledInTaskManager)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(file);
                var isDisabledWin = disabledInTaskManager.Contains(fileName);

                var (recommended, reason) = isDisabledWin
                    ? (false, "Уже отключено в Диспетчере задач Windows")
                    : MatchHeavyStartup(name, file);

                items.Add(new StartupAppItem
                {
                    Id = $"FOLDER:{file}",
                    Name = name,
                    Command = file,
                    Location = locationLabel,
                    FilePath = file,
                    IsRecommendedToDisable = recommended,
                    RecommendationReason = string.IsNullOrEmpty(reason) ? "Ярлык в папке автозагрузки." : reason,
                    RequiresAdmin = requiresAdmin,
                    IsDisabledInWindows = isDisabledWin,
                    IsDisabled = isDisabledWin
                });
            }
        }
        catch { }
    }

    private static (bool Recommended, string Reason) MatchHeavyStartup(string name, string command)
    {
        var combined = $"{name} {command}";
        foreach (var hint in HeavyStartupHints)
        {
            if (combined.Contains(hint.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                return (true, hint.FriendlyNote);
            }
        }
        return (false, string.Empty);
    }

    private void EvaluateStartupApps(PcAnalysisReport report)
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

                // Approximate Private Working Set to avoid over-counting shared DLLs across Chromium/Electron subprocesses
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
                var note = GetProcessHint(kv.Key, mb);
                return new ProcessSummaryItem
                {
                    Name = kv.Key,
                    InstanceCount = kv.Value.Count,
                    MemoryMb = mb,
                    IsSafeToCloseBeforeGaming = mb >= 120,
                    Note = note
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

    private void EvaluateHeavyProcesses(PcAnalysisReport report)
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

    private void EvaluateExtraSystemSettings(PcAnalysisReport report)
    {
        // 1. Windows Transparency Effects
        var transparency = RegistryHelper.ReadDword(RegistryHelper.Hkcu, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency");
        if (transparency is null or 1)
        {
            report.Recommendations.Add(new AnalysisRecommendation
            {
                Id = "quick_disable_transparency",
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
                Id = "quick_disable_bg_uwp",
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
                Id = "quick_minimize_telemetry",
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
    /// Honest bounded 5-bucket weighted scoring model (0..100).
    /// Can be called again dynamically whenever the user applies recommendations inside the UI.
    /// </summary>
    public static void RecalculateScore(PcAnalysisReport report)
    {
        var openCoreRecs = report.Recommendations.Where(r => !r.IsResolved && !r.IsOptionalPreference).ToList();

        // Bucket 1: System & Gaming Core Tweaks (max 40 pts)
        int sysPenalty = 0;
        foreach (var r in openCoreRecs.Where(r => r.Category == RecommendationCategory.SystemAndGaming))
        {
            sysPenalty += r.Impact switch
            {
                RecommendationImpact.High => 6,
                RecommendationImpact.Medium => 4,
                _ => 2
            };
        }
        int sysScore = Math.Max(0, 40 - Math.Min(40, sysPenalty));

        // Bucket 2: Background Services & Telemetry (max 20 pts)
        int svcPenalty = 0;
        foreach (var r in openCoreRecs.Where(r => r.Category == RecommendationCategory.WindowsServices || r.Id == "quick_minimize_telemetry"))
        {
            svcPenalty += r.Impact switch
            {
                RecommendationImpact.High => 6,
                RecommendationImpact.Medium => 3,
                _ => 1
            };
        }
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
            .Where(r => r.Category == RecommendationCategory.VisualAndTelemetry && r.Id != "quick_minimize_telemetry")
            .Sum(r => r.Impact == RecommendationImpact.Medium ? 4 : 2);
        int visualScore = Math.Max(0, 10 - Math.Min(10, visualPenalty));

        report.OptimizationScore = Math.Clamp(sysScore + svcScore + startupScore + hwScore + visualScore, 5, 100);
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

    public (bool Success, string Message, bool RequiresAdmin) DisableStartupItem(StartupAppItem item)
    {
        if (item.RequiresAdmin && !AdminHelper.IsAdministrator())
        {
            return (false, $"Для отключения элемента автозагрузки «{item.Name}» ({item.Location}) требуются права администратора.", true);
        }

        try
        {
            var backupEntry = new StartupItemBackup
            {
                Id = item.Id,
                Name = item.Name,
                Location = item.Location,
                Command = item.Command,
                Root = item.Root,
                SubKey = item.SubKey,
                OriginalFilePath = item.FilePath,
                DisabledAtUtc = DateTime.UtcNow
            };

            if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
            {
                var startupBackupDir = _backup.GetSubFolder("startup_shortcuts");
                var dest = Path.Combine(startupBackupDir, $"{Guid.NewGuid():N}_{Path.GetFileName(item.FilePath)}");
                File.Move(item.FilePath, dest, overwrite: true);
                backupEntry.BackupFilePath = dest;
            }
            else if (!string.IsNullOrWhiteSpace(item.Root) && !string.IsNullOrWhiteSpace(item.SubKey))
            {
                RegistryHelper.DeleteValue(item.Root, item.SubKey, item.Name);
            }
            else
            {
                return (false, "Неизвестный источник автозагрузки.", false);
            }

            _backup.SaveDisabledStartupItem(backupEntry);
            _logger.Success("Startup", $"Удалено из автозагрузки (с резервной копией): {item.Name}");
            return (true, $"«{item.Name}» убрано из автозагрузки (можно вернуть во вкладке «Бэкапы»).", false);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, $"Нет прав доступа для изменения «{item.Name}».", true);
        }
        catch (Exception ex)
        {
            _logger.Error("Startup", $"Ошибка отключения «{item.Name}»: {ex.Message}");
            return (false, $"Ошибка: {ex.Message}", false);
        }
    }

    public static (bool Success, string Message, bool RequiresAdmin) RestoreStartupItemStatic(StartupItemBackup backup, BackupService backupService, AppLogger logger)
    {
        bool needsAdmin = backup.Root.Equals(RegistryHelper.Hklm, StringComparison.OrdinalIgnoreCase);
        if (needsAdmin && !AdminHelper.IsAdministrator())
        {
            return (false, $"Для восстановления «{backup.Name}» требуются права администратора.", true);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(backup.BackupFilePath) && !string.IsNullOrWhiteSpace(backup.OriginalFilePath))
            {
                if (File.Exists(backup.BackupFilePath))
                {
                    File.Move(backup.BackupFilePath, backup.OriginalFilePath, overwrite: true);
                }
            }
            else if (!string.IsNullOrWhiteSpace(backup.Root) && !string.IsNullOrWhiteSpace(backup.SubKey))
            {
                RegistryHelper.WriteValue(backup.Root, backup.SubKey, backup.Name, backup.Command, RegistryValueKind.String);
            }

            backupService.RemoveDisabledStartupItem(backup.Id);
            logger.Info("Startup", $"Восстановлено в автозагрузку: {backup.Name}");
            return (true, $"«{backup.Name}» возвращено в автозагрузку.", false);
        }
        catch (Exception ex)
        {
            return (false, $"Не удалось восстановить «{backup.Name}»: {ex.Message}", false);
        }
    }

    public (bool Success, string Message, bool RequiresAdmin) ApplyQuickSetting(string settingId)
    {
        switch (settingId)
        {
            case "disable_transparency":
            {
                var entry = _backup.GetOrCreateTweakBackup("quick_disable_transparency");
                RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, RegistryValueKind.DWord);
                _backup.SaveTweakBackup(entry);
                _logger.Success("QuickSetting", "Эффекты прозрачности Windows отключены.");
                return (true, "Эффекты прозрачности Windows отключены.", false);
            }
            case "disable_bg_uwp":
            {
                var entry = _backup.GetOrCreateTweakBackup("quick_disable_bg_uwp");
                RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hkcu, @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, RegistryValueKind.DWord);
                _backup.SaveTweakBackup(entry);
                _logger.Success("QuickSetting", "Фоновая работа UWP-приложений отключена.");
                return (true, "Фоновая работа приложений Microsoft Store (UWP) отключена.", false);
            }
            case "minimize_telemetry":
            {
                if (!AdminHelper.IsAdministrator())
                {
                    return (false, "Для изменения политики телеметрии Windows требуются права администратора.", true);
                }
                var entry = _backup.GetOrCreateTweakBackup("quick_minimize_telemetry");
                RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, RegistryValueKind.DWord);
                _backup.SaveTweakBackup(entry);
                _logger.Success("QuickSetting", "Политика телеметрии Windows установлена на минимум (AllowTelemetry = 0).");
                return (true, "Телеметрия Windows снижена до минимального уровня.", false);
            }
            default:
                return (false, $"Неизвестная быстрая настройка: {settingId}", false);
        }
    }

    private static Dictionary<string, (string Model, string MediaType)> QueryLogicalDriveToPhysicalDiskMap()
    {
        var result = new Dictionary<string, (string Model, string MediaType)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            var physicalByNumber = new Dictionary<int, (string Model, string MediaType)>();
            using (var diskSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DeviceId, FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk")))
            {
                foreach (var obj in diskSearcher.Get())
                {
                    if (!int.TryParse(obj["DeviceId"]?.ToString(), out var diskNum)) continue;
                    var model = obj["FriendlyName"]?.ToString()?.Trim() ?? "Диск";
                    var mediaCode = Convert.ToInt32(obj["MediaType"] ?? 0);
                    var busCode = Convert.ToInt32(obj["BusType"] ?? 0);

                    var mediaType = busCode == 17 ? "NVMe SSD" : mediaCode switch
                    {
                        3 => "HDD",
                        4 => busCode == 11 ? "SATA SSD" : "SSD",
                        5 => "SCM",
                        _ => model.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ? "NVMe SSD" :
                             model.Contains("SSD", StringComparison.OrdinalIgnoreCase) ? "SSD" : "Диск"
                    };

                    physicalByNumber[diskNum] = (model, mediaType);
                }
            }

            using (var partSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DriveLetter, DiskNumber FROM MSFT_Partition")))
            {
                foreach (var obj in partSearcher.Get())
                {
                    var letterRaw = obj["DriveLetter"];
                    if (letterRaw is null) continue;

                    char c = letterRaw is char ch ? ch : Convert.ToChar(letterRaw);
                    if (!char.IsLetter(c)) continue;

                    var diskNum = Convert.ToInt32(obj["DiskNumber"] ?? -1);
                    if (physicalByNumber.TryGetValue(diskNum, out var phys))
                    {
                        result[c.ToString().ToUpperInvariant()] = phys;
                    }
                }
            }
        }
        catch { }
        return result;
    }

    private static double MeasureCpuUsagePercent()
    {
        try
        {
            if (!GetSystemTimes(out var idle1, out var kernel1, out var user1)) return 0;
            Thread.Sleep(220);
            if (!GetSystemTimes(out var idle2, out var kernel2, out var user2)) return 0;

            ulong idleDiff = idle2 - idle1;
            ulong kernelDiff = kernel2 - kernel1;
            ulong userDiff = user2 - user1;
            ulong total = kernelDiff + userDiff;
            if (total == 0) return 0;

            var busy = total > idleDiff ? (double)(total - idleDiff) / total * 100.0 : 0;
            return Math.Round(Math.Clamp(busy, 0, 100), 1);
        }
        catch
        {
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out ulong lpIdleTime, out ulong lpKernelTime, out ulong lpUserTime);
}
