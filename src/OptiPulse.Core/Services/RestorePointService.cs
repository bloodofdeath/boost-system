namespace OptiPulse.Core.Services;

public static class RestorePointService
{
    public static (bool Success, string Message) CreateRestorePoint(string description = "OptiPulse — Перед оптимизацией ПК")
    {
        if (!AdminHelper.IsAdministrator())
        {
            return (false, "Для создания точки восстановления Windows требуются права администратора.");
        }

        // Escape single quotes safely for PowerShell string literal
        var safeDesc = description.Replace("'", "''");
        var script = $@"
            try {{
                Enable-ComputerRestore -Drive ""$env:SystemDrive\"" -ErrorAction SilentlyContinue
                Checkpoint-Computer -Description '{safeDesc}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
                Write-Output 'RESTORE_POINT_OK'
            }} catch {{
                Write-Error $_.Exception.Message
                exit 1
            }}";

        var result = CommandRunner.RunPowerShell(script, timeoutMs: 45000);
        if (result.Success && result.StandardOutput.Contains("RESTORE_POINT_OK", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"Точка восстановления «{description}» успешно создана.");
        }

        // Windows enforces a 24-hour cooldown between restore points by default
        if (result.CombinedOutput.Contains("frequency", StringComparison.OrdinalIgnoreCase) ||
            result.CombinedOutput.Contains("1440", StringComparison.OrdinalIgnoreCase) ||
            result.CombinedOutput.Contains("24", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Windows ограничивает создание точек восстановления чаще 1 раза в 24 часа (недавняя точка уже существует).");
        }

        return (false, $"Не удалось создать точку восстановления: {(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError)}");
    }
}
