using OptiPulse.Core.Services;
using Xunit;

namespace OptiPulse.Tests;

public sealed class CoreServicesTests : IDisposable
{
    private readonly string _tempDir;

    public CoreServicesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OptiPulse_CoreSvcTests_" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void PowerCfgHelper_ParsesAcHexIndexCorrectly()
    {
        const string sampleOutput = """
            Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)
              Power Setting GUID: 0cc5b647-c1df-4637-891a-dec35c318583  (Processor performance core parking min cores)
                Minimum Possible Setting: 0x00000000
                Maximum Possible Setting: 0x00000064
                Possible Settings increment: 0x00000001
                Current AC Power Setting Index: 0x00000064
                Current DC Power Setting Index: 0x00000005
            """;

        var parsed = PowerCfgHelper.ParseAcHexIndexFromOutput(sampleOutput);
        Assert.Equal(100, parsed);
    }

    [Fact]
    public void QuickSettingsService_ResolvesTitlesAndRequiresAdminCorrectly()
    {
        Assert.True(QuickSettingsService.IsQuickSettingBackupKey("quick_disable_transparency"));
        Assert.False(QuickSettingsService.IsQuickSettingBackupKey("disable_superfetch"));

        Assert.Contains("прозрачности", QuickSettingsService.GetDisplayTitle("disable_transparency"));
        Assert.Contains("UWP", QuickSettingsService.GetDisplayTitle("quick_disable_bg_uwp"));
        Assert.Contains("телеметрии", QuickSettingsService.GetDisplayTitle("minimize_telemetry"));

        Assert.False(QuickSettingsService.RequiresAdmin("quick_disable_transparency"));
        Assert.False(QuickSettingsService.RequiresAdmin("quick_disable_bg_uwp"));
        Assert.True(QuickSettingsService.RequiresAdmin("quick_minimize_telemetry"));
    }

    [Fact]
    public void TweakRegistry_QueryAllStatusesParallel_PreservesOrderAndCount()
    {
        var backup = new BackupService(Path.Combine(_tempDir, "backups"));
        var logger = new AppLogger(Path.Combine(_tempDir, "logs"));
        var registry = new TweakRegistry(backup, logger);

        var parallelResults = registry.QueryAllStatusesParallel();
        Assert.Equal(registry.All.Count, parallelResults.Count);

        for (int i = 0; i < registry.All.Count; i++)
        {
            Assert.Equal(registry.All[i].Descriptor.Id, parallelResults[i].Tweak.Descriptor.Id);
            Assert.NotNull(parallelResults[i].Status);
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
