using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace OptiPulse.Core.Services;

public static class AdminHelper
{
    private const int ErrorCancelled = 1223;

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Requests UAC elevation by starting a new elevated instance of the current process.
    /// Returns true if the elevated process started (caller should then shut down the current instance),
    /// or false if the user cancelled UAC or launch failed.
    /// </summary>
    public static (bool Started, bool CancelledByUser, string ErrorMessage) RequestElevation(string? arguments = null)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return (false, false, "Не удалось определить путь к исполняемому файлу приложения.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
            };

            var process = Process.Start(startInfo);
            return process is not null
                ? (true, false, string.Empty)
                : (false, false, "Не удалось запустить процесс с правами администратора.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return (false, true, "Пользователь отменил запрос прав администратора (UAC).");
        }
        catch (Exception ex)
        {
            return (false, false, $"Ошибка запроса прав администратора: {ex.Message}");
        }
    }
}
