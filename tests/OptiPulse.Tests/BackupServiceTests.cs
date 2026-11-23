using OptiPulse.Core.Models;
using OptiPulse.Core.Services;
using Xunit;

namespace OptiPulse.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _tempDir;

    public BackupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OptiPulse_BackupTests_" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void BackupService_PersistsAndReloadsTweakServiceAndStartupBackups()
    {
        var service1 = new BackupService(_tempDir);
        var entry = service1.GetOrCreateTweakBackup("minimal_bg_priority");
        entry.RegistryValues.Add(new RegistryValueBackup
        {
            Root = "HKLM",
            SubKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl",
            ValueName = "Win32PrioritySeparation",
            Existed = true,
            ValueKind = "DWord",
            SerializedValue = "2"
        });
        service1.SaveTweakBackup(entry);

        service1.SaveDisabledService(new ServiceBackup
        {
            ServiceName = "DiagTrack",
            OriginalStartMode = 2,
            WasRunning = true
        });

        service1.SaveDisabledStartupItem(new StartupItemBackup
        {
            Id = "HKCU:Run:Discord",
            Name = "Discord",
            Location = "HKCU Run",
            Command = "\"C:\\Discord\\Update.exe\""
        });

        // Reload in a brand new instance to verify disk persistence
        var service2 = new BackupService(_tempDir);
        Assert.Equal(3, service2.TotalBackupEntriesCount);

        var loadedTweak = service2.GetTweakBackup("minimal_bg_priority");
        Assert.NotNull(loadedTweak);
        Assert.Single(loadedTweak!.RegistryValues);
        Assert.Equal("2", loadedTweak.RegistryValues[0].SerializedValue);

        var loadedSvc = service2.GetDisabledService("DiagTrack");
        Assert.NotNull(loadedSvc);
        Assert.Equal(2, loadedSvc!.OriginalStartMode);

        var loadedStartup = service2.GetDisabledStartupItem("HKCU:Run:Discord");
        Assert.NotNull(loadedStartup);
        Assert.Equal("Discord", loadedStartup!.Name);

        service2.RemoveTweakBackup("minimal_bg_priority");
        Assert.Null(service2.GetTweakBackup("minimal_bg_priority"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
