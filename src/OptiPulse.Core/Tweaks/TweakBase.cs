using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

public abstract class TweakBase : ITweak
{
    protected readonly BackupService Backup;
    protected readonly AppLogger Logger;

    protected TweakBase(BackupService backup, AppLogger logger)
    {
        Backup = backup;
        Logger = logger;
    }

    public abstract TweakDescriptor Descriptor { get; }
    public abstract TweakStatusInfo GetStatus();

    public TweakOperationResult Apply()
    {
        if (Descriptor.RequiresAdmin && !AdminHelper.IsAdministrator())
        {
            var current = GetStatus().IsEnabled;
            Logger.Warning(Descriptor.Id, $"Для включения «{Descriptor.Title}» требуются права администратора.");
            return TweakOperationResult.Fail(
                $"Для применения «{Descriptor.Title}» требуются права администратора.",
                current,
                requiresAdmin: true);
        }

        try
        {
            var result = ApplyCore();
            if (result.Success)
            {
                Logger.Success(Descriptor.Id, $"Включено: {Descriptor.Title}. {result.Message}");
            }
            else
            {
                Logger.Error(Descriptor.Id, $"Ошибка включения «{Descriptor.Title}»: {result.Message}");
            }
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            var current = GetStatus().IsEnabled;
            Logger.Warning(Descriptor.Id, $"Отказано в доступе при включении «{Descriptor.Title}».");
            return TweakOperationResult.Fail("Недостаточно прав доступа. Запустите от имени администратора.", current, requiresAdmin: true);
        }
        catch (Exception ex)
        {
            var current = GetStatus().IsEnabled;
            Logger.Error(Descriptor.Id, $"Исключение при включении «{Descriptor.Title}»: {ex.Message}");
            return TweakOperationResult.Fail($"Ошибка: {ex.Message}", current);
        }
    }

    public TweakOperationResult Revert()
    {
        if (Descriptor.RequiresAdmin && !AdminHelper.IsAdministrator())
        {
            var current = GetStatus().IsEnabled;
            Logger.Warning(Descriptor.Id, $"Для отключения «{Descriptor.Title}» требуются права администратора.");
            return TweakOperationResult.Fail(
                $"Для отката «{Descriptor.Title}» требуются права администратора.",
                current,
                requiresAdmin: true);
        }

        try
        {
            var result = RevertCore();
            if (result.Success)
            {
                Logger.Info(Descriptor.Id, $"Отключено (восстановлено): {Descriptor.Title}. {result.Message}");
            }
            else
            {
                Logger.Error(Descriptor.Id, $"Ошибка отката «{Descriptor.Title}»: {result.Message}");
            }
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            var current = GetStatus().IsEnabled;
            Logger.Warning(Descriptor.Id, $"Отказано в доступе при откате «{Descriptor.Title}».");
            return TweakOperationResult.Fail("Недостаточно прав доступа. Запустите от имени администратора.", current, requiresAdmin: true);
        }
        catch (Exception ex)
        {
            var current = GetStatus().IsEnabled;
            Logger.Error(Descriptor.Id, $"Исключение при откате «{Descriptor.Title}»: {ex.Message}");
            return TweakOperationResult.Fail($"Ошибка: {ex.Message}", current);
        }
    }

    protected abstract TweakOperationResult ApplyCore();
    protected abstract TweakOperationResult RevertCore();

    protected void RestoreBackedUpRegistryValues(TweakBackupEntry? entry)
    {
        if (entry is null) return;
        foreach (var reg in entry.RegistryValues)
        {
            RegistryHelper.RestoreBackup(reg);
        }
    }
}
