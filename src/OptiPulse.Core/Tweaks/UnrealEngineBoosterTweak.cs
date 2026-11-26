using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// Clean, reversible Unreal Engine 4/5 Engine.ini patcher (separately unit-testable).
/// </summary>
public static class UnrealEngineIniPatcher
{
    public const string BeginMarker = "; === BEGIN OPTIPULSE UNREAL ENGINE BOOSTER ===";
    public const string EndMarker = "; === END OPTIPULSE UNREAL ENGINE BOOSTER ===";

    private static readonly string OptimizationBlock = string.Join(Environment.NewLine,
    [
        BeginMarker,
        "[SystemSettings]",
        "r.CreateShadersOnLoad=1",
        "r.Shaders.Optimize=1",
        "r.GTSyncType=1",
        "r.OneFrameThreadLag=1",
        "r.FinishCurrentFrame=0",
        "r.SceneColorFringeQuality=0",
        "r.MotionBlurQuality=0",
        "r.DepthOfFieldQuality=0",
        "r.LensFlareQuality=0",
        "r.Tonemapper.GrainQuantization=0",
        "r.Tonemapper.Quality=1",
        "r.Streaming.Boost=1",
        "r.Streaming.LimitPoolSizeToVRAM=1",
        "niagara.CreateShadersOnLoad=1",
        EndMarker
    ]);

    public static bool IsPatched(string content) =>
        content.Contains(BeginMarker, StringComparison.Ordinal) &&
        content.Contains(EndMarker, StringComparison.Ordinal);

    public static string ApplyPatch(string originalContent)
    {
        var cleaned = RemovePatch(originalContent).TrimEnd();
        if (string.IsNullOrEmpty(cleaned))
        {
            return OptimizationBlock + Environment.NewLine;
        }
        return cleaned + Environment.NewLine + Environment.NewLine + OptimizationBlock + Environment.NewLine;
    }

    public static string RemovePatch(string content)
    {
        var startIdx = content.IndexOf(BeginMarker, StringComparison.Ordinal);
        if (startIdx < 0) return content;

        var endIdx = content.IndexOf(EndMarker, startIdx, StringComparison.Ordinal);
        if (endIdx < 0) return content;

        var afterEnd = endIdx + EndMarker.Length;
        while (afterEnd < content.Length && (content[afterEnd] == '\r' || content[afterEnd] == '\n'))
        {
            afterEnd++;
        }

        var before = content[..startIdx].TrimEnd('\r', '\n');
        var after = content[afterEnd..];

        if (string.IsNullOrEmpty(before)) return after;
        if (string.IsNullOrEmpty(after)) return before + Environment.NewLine;
        return before + Environment.NewLine + Environment.NewLine + after;
    }
}

/// <summary>
/// 1. Ускоритель Unreal Engine [Бета]
/// </summary>
public sealed class UnrealEngineBoosterTweak : TweakBase
{
    private static readonly TweakDescriptor Info = new()
    {
        Id = "unreal_engine_booster",
        Title = "Ускоритель Unreal Engine",
        Description = "Повышает частоту кадров в большинстве игр на Unreal Engine 4 и 5. Перед включением запустите каждую игру хотя бы один раз.",
        NotRecommendedWhen = "Вам важны кинематографические пост-эффекты (Motion Blur, хроматическая аберрация, Lens Flare, глубина резкости) либо игра использует строгую проверку пользовательского Engine.ini. Оригинальные конфиги полностью сохраняются в резервную копию.",
        TechnicalDetails = "Находит профили игр на Unreal Engine 4 и 5 в %LOCALAPPDATA%\\*\\Saved\\Config\\Windows*\\Engine.ini и применяет безопасный оптимизированный блок [SystemSettings] (кэширование шейдеров при загрузке, оптимизация потоковой передачи текстур, отключение тяжёлых пост-эффектов).",
        IconKey = "IconUnrealEngine",
        Category = TweakCategory.GpuAndGaming,
        NoticeLevel = TweakNoticeLevel.Info,
        NoticeTooltip = "Перед включением запустите каждую UE4/UE5 игру хотя бы один раз, чтобы создался Engine.ini.",
        RequiresAdmin = false
    };

    // Known large non-game folders in %LOCALAPPDATA% skipped to prevent slow recursive directory scans
    private static readonly HashSet<string> IgnoredLocalAppDataFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Temp", "Microsoft", "Packages", "Google", "Mozilla", "BraveSoftware", "Yandex",
        "Opera Software", "Discord", "Steam", "NuGet", "pip", "npm-cache", "Programs",
        "ConnectedDevicesPlatform", "CrashDumps", "D3DSCache", "NVIDIA", "NVIDIA Corporation",
        "AMD", "Intel", "GitHubDesktop", "Docker", "JetBrains", "Application Data",
        "History", "Temporary Internet Files", "CefSharp", "Electron", "SquirrelTemp",
        "Comms", "Publishers", "PlaceholderTileLogoFolder", "VirtualStore"
    };

    private static readonly string[] ConfigSubVariants =
    [
        Path.Combine("Saved", "Config", "WindowsNoEditor", "Engine.ini"),
        Path.Combine("Saved", "Config", "WindowsClient", "Engine.ini"),
        Path.Combine("Saved", "Config", "Windows", "Engine.ini")
    ];

    public UnrealEngineBoosterTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => Info;

    public override TweakStatusInfo GetStatus()
    {
        var entry = Backup.GetTweakBackup(Descriptor.Id);
        var flaggedEnabled = entry?.Metadata.TryGetValue("Enabled", out var flag) == true && flag == "true";
        var configs = DiscoverEngineIniFiles();
        var patchedCount = configs.Count(f =>
        {
            try { return UnrealEngineIniPatcher.IsPatched(File.ReadAllText(f)); }
            catch { return false; }
        });

        var isEnabled = patchedCount > 0 || flaggedEnabled;
        var note = configs.Count == 0
            ? (isEnabled ? "Активно (игры UE4/UE5 пока не обнаружены)" : "Игры на UE4/UE5 в LocalAppData не обнаружены")
            : $"Оптимизировано UE-конфигов: {patchedCount} из {configs.Count}";

        return new TweakStatusInfo { IsEnabled = isEnabled, StatusNote = note };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var configs = DiscoverEngineIniFiles();
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);
        var ueBackupFolder = Backup.GetSubFolder("ue_configs");

        var backupMap = LoadBackupMap(entry);
        int patchedCount = 0;
        var patchedGames = new List<string>();

        foreach (var iniPath in configs)
        {
            try
            {
                var info = new FileInfo(iniPath);
                if (info.IsReadOnly) continue;

                var content = File.ReadAllText(iniPath, Encoding.UTF8);
                if (!backupMap.ContainsKey(iniPath))
                {
                    var normalizedKey = iniPath.ToUpperInvariant();
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedKey)))[..12];
                    var backupFile = Path.Combine(ueBackupFolder, $"Engine_{hash}.ini.bak");
                    File.WriteAllText(backupFile, content, Encoding.UTF8);
                    backupMap[iniPath] = backupFile;
                }

                var patched = UnrealEngineIniPatcher.ApplyPatch(content);
                File.WriteAllText(iniPath, patched, Encoding.UTF8);
                patchedCount++;
                patchedGames.Add(ExtractGameName(iniPath));
            }
            catch (Exception ex)
            {
                Logger.Warning(Descriptor.Id, $"Пропущен конфиг {iniPath}: {ex.Message}");
            }
        }

        entry.Metadata["Enabled"] = "true";
        entry.Metadata["BackupMapJson"] = JsonSerializer.Serialize(backupMap);
        Backup.SaveTweakBackup(entry);

        if (configs.Count == 0)
        {
            return TweakOperationResult.Ok(true, "Ускоритель включён, но профили UE4/UE5 не найдены. Запустите игру на Unreal Engine хотя бы один раз и переключите опцию снова.");
        }

        var gamesPreview = string.Join(", ", patchedGames.Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
        return TweakOperationResult.Ok(true, $"Оптимизировано конфигов UE4/UE5: {patchedCount} ({gamesPreview}).");
    }

    protected override TweakOperationResult RevertCore()
    {
        var entry = Backup.GetTweakBackup(Descriptor.Id);
        var backupMap = entry is not null ? LoadBackupMap(entry) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int restoredCount = 0;

        foreach (var (iniPath, backupFile) in backupMap)
        {
            try
            {
                if (File.Exists(backupFile) && File.Exists(iniPath))
                {
                    var info = new FileInfo(iniPath);
                    if (!info.IsReadOnly)
                    {
                        File.Copy(backupFile, iniPath, overwrite: true);
                        restoredCount++;
                    }
                }
                else if (File.Exists(iniPath))
                {
                    var cleaned = UnrealEngineIniPatcher.RemovePatch(File.ReadAllText(iniPath, Encoding.UTF8));
                    File.WriteAllText(iniPath, cleaned, Encoding.UTF8);
                    restoredCount++;
                }

                if (File.Exists(backupFile))
                {
                    File.Delete(backupFile);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(Descriptor.Id, $"Не удалось восстановить {iniPath}: {ex.Message}");
            }
        }

        // Also strip marker block from any remaining discovered configs
        foreach (var iniPath in DiscoverEngineIniFiles())
        {
            try
            {
                var content = File.ReadAllText(iniPath, Encoding.UTF8);
                if (UnrealEngineIniPatcher.IsPatched(content) && !new FileInfo(iniPath).IsReadOnly)
                {
                    File.WriteAllText(iniPath, UnrealEngineIniPatcher.RemovePatch(content), Encoding.UTF8);
                    restoredCount++;
                }
            }
            catch
            {
                // Ignore inaccessible files
            }
        }

        Backup.RemoveTweakBackup(Descriptor.Id);
        return TweakOperationResult.Ok(false, $"Восстановлено конфигов Unreal Engine: {restoredCount}.");
    }

    public static List<string> DiscoverEngineIniFiles()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games")
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            try
            {
                foreach (var gameDir in Directory.EnumerateDirectories(root))
                {
                    var dirName = Path.GetFileName(gameDir);
                    if (IgnoredLocalAppDataFolders.Contains(dirName)) continue;

                    ProbeDirectoryVariants(gameDir, found);

                    // Some UE games nest one level deeper: LocalAppData\<Studio>\<Game>\Saved\Config\...
                    try
                    {
                        foreach (var nestedDir in Directory.EnumerateDirectories(gameDir))
                        {
                            var nestedName = Path.GetFileName(nestedDir);
                            if (nestedName.Equals("Saved", StringComparison.OrdinalIgnoreCase) ||
                                IgnoredLocalAppDataFolders.Contains(nestedName))
                            {
                                continue;
                            }

                            ProbeDirectoryVariants(nestedDir, found);
                        }
                    }
                    catch
                    {
                        // Skip restricted folders
                    }
                }
            }
            catch
            {
                // Skip restricted root folders
            }
        }

        return found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ProbeDirectoryVariants(string baseDir, HashSet<string> found)
    {
        var savedDir = Path.Combine(baseDir, "Saved");
        if (!Directory.Exists(savedDir)) return;

        foreach (var variant in ConfigSubVariants)
        {
            var candidate = Path.Combine(baseDir, variant);
            if (File.Exists(candidate))
            {
                found.Add(candidate);
            }
        }
    }

    private static Dictionary<string, string> LoadBackupMap(TweakBackupEntry entry)
    {
        if (entry.Metadata.TryGetValue("BackupMapJson", out var json) && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (parsed is not null)
                {
                    return new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }
        }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string ExtractGameName(string iniPath)
    {
        var parts = iniPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var savedIdx = Array.FindIndex(parts, p => p.Equals("Saved", StringComparison.OrdinalIgnoreCase));
        return savedIdx > 0 ? parts[savedIdx - 1] : Path.GetFileName(iniPath);
    }
}
