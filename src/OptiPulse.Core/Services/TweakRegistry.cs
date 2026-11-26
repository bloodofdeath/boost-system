using OptiPulse.Core.Models;
using OptiPulse.Core.Tweaks;

namespace OptiPulse.Core.Services;

public sealed class TweakRegistry
{
    private readonly List<ITweak> _tweaks;
    private readonly Dictionary<string, ITweak> _byId;

    public TweakRegistry(BackupService backup, AppLogger logger)
    {
        // Ordered logically by GPU/Gaming, Input, CPU/System, and Storage/Power categories
        _tweaks =
        [
            new UnrealEngineBoosterTweak(backup, logger),
            new GpuHighPerformanceTweak(backup, logger),
            new ReduceInputLagTweak(backup, logger),
            new KeyboardDelayTweak(backup, logger),
            new DisablePerfCountersTweak(backup, logger),
            new MaxGamingPerformanceTweak(backup, logger),
            new MinimalBackgroundPriorityTweak(backup, logger),
            new NetworkPowerSavingTweak(backup, logger),
            new GameBarDvrTweak(backup, logger),
            new KeepCoresActiveTweak(backup, logger),
            new NtfsLastAccessTweak(backup, logger),
            new SuperFetchTweak(backup, logger),
            new WindowsSearchIndexingTweak(backup, logger),
            new UltimatePerformancePowerPlanTweak(backup, logger)
        ];

        _byId = _tweaks.ToDictionary(t => t.Descriptor.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ITweak> All => _tweaks;

    public ITweak? FindById(string id) => _byId.TryGetValue(id, out var tweak) ? tweak : null;

    /// <summary>
    /// Queries live Windows states of all 14 tweaks concurrently while preserving catalog order.
    /// </summary>
    public List<(ITweak Tweak, TweakStatusInfo Status)> QueryAllStatusesParallel() =>
        _tweaks
            .AsParallel()
            .AsOrdered()
            .Select(t => (t, t.GetStatus()))
            .ToList();

    public (int Reverted, int Failed, bool AdminRequired) RevertAllBackedUp(BackupService backup, AppLogger logger)
    {
        var snapshot = backup.GetSnapshot();
        int reverted = 0;
        int failed = 0;
        bool adminRequired = false;

        foreach (var tweakId in snapshot.Tweaks.Keys.ToList())
        {
            var tweak = FindById(tweakId);
            if (tweak is not null)
            {
                var res = tweak.Revert();
                if (res.Success)
                {
                    reverted++;
                }
                else
                {
                    failed++;
                    if (res.RequiresAdmin) adminRequired = true;
                }
                continue;
            }

            // Also revert QuickSettings (e.g., quick_disable_transparency, quick_disable_bg_uwp, quick_minimize_telemetry)
            if (QuickSettingsService.IsQuickSettingBackupKey(tweakId))
            {
                var (ok, _, needsAdmin) = QuickSettingsService.Revert(tweakId, backup, logger);
                if (ok)
                {
                    reverted++;
                }
                else
                {
                    failed++;
                    if (needsAdmin) adminRequired = true;
                }
            }
        }

        foreach (var svcBackup in snapshot.DisabledServices.Values.ToList())
        {
            if (!AdminHelper.IsAdministrator())
            {
                adminRequired = true;
                failed++;
                continue;
            }

            var (ok, msg) = ServiceControlHelper.RestoreService(svcBackup);
            if (ok)
            {
                backup.RemoveDisabledService(svcBackup.ServiceName);
                logger.Info("Backup", msg);
                reverted++;
            }
            else
            {
                logger.Error("Backup", msg);
                failed++;
            }
        }

        foreach (var startup in snapshot.DisabledStartupItems.Values.ToList())
        {
            var (ok, msg, needsAdmin) = StartupManagerService.RestoreStartupItem(startup, backup, logger);
            if (ok)
            {
                reverted++;
            }
            else
            {
                failed++;
                if (needsAdmin) adminRequired = true;
                logger.Warning("Backup", msg);
            }
        }

        return (reverted, failed, adminRequired);
    }
}
