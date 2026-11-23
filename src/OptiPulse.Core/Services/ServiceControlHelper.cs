using System.ServiceProcess;
using Microsoft.Win32;
using OptiPulse.Core.Models;

namespace OptiPulse.Core.Services;

public sealed record ServiceStateInfo(
    string ServiceName,
    bool Exists,
    bool IsRunning,
    int StartMode, // 2=Auto, 3=Manual, 4=Disabled
    bool IsDelayedAutoStart)
{
    public bool IsDisabled => Exists && StartMode == 4;

    public string StartModeRussian => StartMode switch
    {
        2 when IsDelayedAutoStart => "Авто (отложенный)",
        2 => "Автоматически",
        3 => "Вручную",
        4 => "Отключена",
        _ => $"Режим ({StartMode})"
    };
}

public static class ServiceControlHelper
{
    public static ServiceStateInfo GetServiceState(string serviceName)
    {
        var regPath = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
        var startMode = RegistryHelper.ReadDword(RegistryHelper.Hklm, regPath, "Start");
        if (startMode is null)
        {
            return new ServiceStateInfo(serviceName, Exists: false, IsRunning: false, StartMode: -1, IsDelayedAutoStart: false);
        }

        var delayed = (RegistryHelper.ReadDword(RegistryHelper.Hklm, regPath, "DelayedAutostart") ?? 0) == 1;
        var isRunning = false;
        try
        {
            using var sc = new ServiceController(serviceName);
            isRunning = sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
        }
        catch
        {
            // Ignore if access restricted
        }

        return new ServiceStateInfo(serviceName, Exists: true, IsRunning: isRunning, StartMode: startMode.Value, IsDelayedAutoStart: delayed);
    }

    public static ServiceBackup CaptureServiceBackup(string serviceName)
    {
        var state = GetServiceState(serviceName);
        return new ServiceBackup
        {
            ServiceName = serviceName,
            OriginalStartMode = state.Exists ? state.StartMode : 3,
            WasDelayedAutoStart = state.IsDelayedAutoStart,
            WasRunning = state.IsRunning
        };
    }

    public static (bool Success, string Message) DisableAndStopService(string serviceName)
    {
        var state = GetServiceState(serviceName);
        if (!state.Exists)
        {
            return (false, $"Служба {serviceName} не найдена в системе.");
        }

        try
        {
            var regPath = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
            RegistryHelper.WriteValue(RegistryHelper.Hklm, regPath, "Start", 4, RegistryValueKind.DWord);
            CommandRunner.Run("sc.exe", $"config \"{serviceName}\" start= disabled", 8000);

            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                {
                    if (sc.CanStop)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                    else
                    {
                        CommandRunner.Run("sc.exe", $"stop \"{serviceName}\"", 8000);
                    }
                }
            }
            catch
            {
                // Some protected services stop only after reboot; Start=4 already guarantees disabled on next boot
                CommandRunner.Run("sc.exe", $"stop \"{serviceName}\"", 8000);
            }

            return (true, $"Служба {serviceName} отключена.");
        }
        catch (Exception ex)
        {
            return (false, $"Не удалось отключить службу {serviceName}: {ex.Message}");
        }
    }

    public static (bool Success, string Message) RestoreService(ServiceBackup backup)
    {
        var state = GetServiceState(backup.ServiceName);
        if (!state.Exists)
        {
            return (false, $"Служба {backup.ServiceName} не найдена в системе.");
        }

        try
        {
            var startMode = backup.OriginalStartMode is >= 0 and <= 4 ? backup.OriginalStartMode : 3;
            var regPath = $@"SYSTEM\CurrentControlSet\Services\{backup.ServiceName}";
            RegistryHelper.WriteValue(RegistryHelper.Hklm, regPath, "Start", startMode, RegistryValueKind.DWord);

            var scStart = startMode switch
            {
                2 when backup.WasDelayedAutoStart => "delayed-auto",
                2 => "auto",
                3 => "demand",
                4 => "disabled",
                _ => "demand"
            };
            CommandRunner.Run("sc.exe", $"config \"{backup.ServiceName}\" start= {scStart}", 8000);

            if (backup.WasRunning && startMode != 4)
            {
                try
                {
                    using var sc = new ServiceController(backup.ServiceName);
                    if (sc.Status == ServiceControllerStatus.Stopped)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    }
                }
                catch
                {
                    CommandRunner.Run("sc.exe", $"start \"{backup.ServiceName}\"", 8000);
                }
            }

            return (true, $"Служба {backup.ServiceName} восстановлена (режим: {scStart}).");
        }
        catch (Exception ex)
        {
            return (false, $"Не удалось восстановить службу {backup.ServiceName}: {ex.Message}");
        }
    }
}
