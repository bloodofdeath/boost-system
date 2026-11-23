using OptiPulse.Core.Services;
using Xunit;

namespace OptiPulse.Tests;

public sealed class TweakCatalogTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BackupService _backup;
    private readonly AppLogger _logger;
    private readonly TweakRegistry _registry;

    public TweakCatalogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OptiPulse_Tests_" + Guid.NewGuid().ToString("N"));
        _backup = new BackupService(Path.Combine(_tempDir, "backups"));
        _logger = new AppLogger(Path.Combine(_tempDir, "logs"));
        _registry = new TweakRegistry(_backup, _logger);
    }

    [Fact]
    public void Registry_ContainsAll14TweaksWithUniqueIds()
    {
        Assert.Equal(14, _registry.All.Count);

        var ids = _registry.All.Select(t => t.Descriptor.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var expectedIds = new[]
        {
            "unreal_engine_booster",
            "gpu_high_performance",
            "reduce_input_lag",
            "reduce_keyboard_delay",
            "disable_perf_counters",
            "max_gaming_performance",
            "minimal_bg_priority",
            "disable_network_power_saving",
            "disable_gamebar_dvr",
            "unpark_cpu_cores",
            "disable_ntfs_last_access",
            "disable_superfetch",
            "disable_windows_search",
            "ultimate_performance_power_plan"
        };

        foreach (var expected in expectedIds)
        {
            Assert.NotNull(_registry.FindById(expected));
        }
    }

    [Fact]
    public void AllTweaks_HaveCompleteMetadataAndReadableLiveStatus()
    {
        foreach (var tweak in _registry.All)
        {
            var d = tweak.Descriptor;
            Assert.False(string.IsNullOrWhiteSpace(d.Title), $"Missing title on {d.Id}");
            Assert.False(string.IsNullOrWhiteSpace(d.Description), $"Missing description on {d.Id}");
            Assert.False(string.IsNullOrWhiteSpace(d.NotRecommendedWhen), $"Missing NotRecommendedWhen on {d.Id}");
            Assert.False(string.IsNullOrWhiteSpace(d.TechnicalDetails), $"Missing TechnicalDetails on {d.Id}");
            Assert.False(string.IsNullOrWhiteSpace(d.IconKey), $"Missing IconKey on {d.Id}");

            // Read-only live status check must never throw even without admin rights
            var status = tweak.GetStatus();
            Assert.NotNull(status);
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
