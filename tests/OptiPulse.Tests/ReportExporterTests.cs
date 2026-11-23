using OptiPulse.Core.Models;
using OptiPulse.Core.Services;
using Xunit;

namespace OptiPulse.Tests;

public sealed class ReportExporterTests : IDisposable
{
    private readonly string _tempDir;

    public ReportExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OptiPulse_ReportTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ReportExporter_GeneratesValidHtmlMarkdownAndJson()
    {
        var report = new PcAnalysisReport
        {
            MachineName = "GAMING-PC<TEST>",
            OptimizationScore = 72,
            ScoreVerdict = "Хорошее состояние — есть несколько полезных улучшений",
            ActiveTweaksCount = 6,
            TotalTweaksCount = 14,
            Hardware = new HardwareProfile
            {
                OsCaption = "Windows 11 Pro",
                OsBuild = "26300",
                CpuName = "AMD Ryzen 7 7800X3D",
                CpuCores = 8,
                CpuThreads = 16,
                CpuLoadPercent = 12.4,
                GpuNames = ["NVIDIA GeForce RTX 4080"],
                GpuDriverVersion = "566.14",
                TotalRamGb = 32.0,
                UsedRamGb = 11.2,
                FreeRamGb = 20.8,
                ActivePowerPlanName = "Сбалансированная",
                Disks = [new DiskInfo { DriveLetter = "C:", MediaType = "NVMe", TotalGb = 1000, FreeGb = 450 }]
            },
            Recommendations =
            [
                new AnalysisRecommendation
                {
                    Id = "svc_DiagTrack",
                    Title = "Отключить службу: Телеметрия Windows (DiagTrack)",
                    Description = "Собирает телеметрию в фоне.",
                    CurrentState = "Работает",
                    ExpectedBenefit = "Меньше фоновой нагрузки",
                    Impact = RecommendationImpact.High,
                    Safety = RecommendationSafety.Safe
                }
            ],
            AlreadyOptimizedItems = ["Фоновая запись Game DVR отключена"]
        };

        var html = ReportExporter.GenerateHtml(report);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("GAMING-PC&lt;TEST&gt;", html); // Proper HTML escaping
        Assert.Contains("AMD Ryzen 7 7800X3D", html);
        Assert.Contains("DiagTrack", html);

        var md = ReportExporter.GenerateMarkdown(report);
        Assert.Contains("# Отчёт диагностики и ускорения ПК", md);
        Assert.Contains("72 / 100", md);
        Assert.Contains("NVIDIA GeForce RTX 4080", md);
        Assert.DoesNotContain("321 ГБ", md);
        Assert.DoesNotContain("111 /", md);

        var json = ReportExporter.GenerateJson(report);
        Assert.Contains("\"OptimizationScore\": 72", json);

        var htmlPath = Path.Combine(_tempDir, "report.html");
        ReportExporter.SaveReport(report, htmlPath);
        Assert.True(File.Exists(htmlPath));
    }

    [Fact]
    public async Task PcAnalyzer_RunsLiveReadOnlyScanSuccessfully()
    {
        var backup = new BackupService(Path.Combine(_tempDir, "b"));
        var logger = new AppLogger(Path.Combine(_tempDir, "l"));
        var registry = new TweakRegistry(backup, logger);
        var analyzer = new PcAnalyzerService(registry, backup, logger);

        var report = await analyzer.AnalyzeAsync();

        Assert.NotNull(report);
        Assert.InRange(report.OptimizationScore, 10, 100);
        Assert.Equal(14, report.TotalTweaksCount);
        Assert.True(report.Hardware.CpuThreads > 0);
        Assert.True(report.Hardware.TotalRamGb > 0);
        Assert.NotEmpty(report.Hardware.OsCaption);
        Assert.NotEmpty(report.ScoreVerdict);
    }

    [Fact]
    public void RecalculateScore_ImprovesWhenCoreRecommendationsAreResolvedAndIgnoresOptionalPrefs()
    {
        var report = new PcAnalysisReport
        {
            Hardware = new HardwareProfile { TotalRamGb = 32, UsedRamGb = 12, FreeRamGb = 20, CpuLoadPercent = 10 },
            Recommendations =
            [
                new AnalysisRecommendation { Id = "t1", Category = RecommendationCategory.SystemAndGaming, Impact = RecommendationImpact.High },
                new AnalysisRecommendation { Id = "s1", Category = RecommendationCategory.WindowsServices, Impact = RecommendationImpact.High },
                new AnalysisRecommendation { Id = "opt1", Category = RecommendationCategory.SystemAndGaming, Impact = RecommendationImpact.Low, IsOptionalPreference = true }
            ]
        };

        PcAnalyzerService.RecalculateScore(report);
        var initialScore = report.OptimizationScore;
        Assert.True(initialScore is >= 80 and < 100);

        report.Recommendations[0].IsResolved = true;
        report.Recommendations[1].IsResolved = true;
        PcAnalyzerService.RecalculateScore(report);

        Assert.True(report.OptimizationScore > initialScore);
        Assert.Equal(100, report.OptimizationScore); // Optional preference alone never drags down a fully optimized PC
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
