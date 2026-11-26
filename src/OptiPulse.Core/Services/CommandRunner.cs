using System.Diagnostics;
using System.Text;

namespace OptiPulse.Core.Services;

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
    public string CombinedOutput => string.IsNullOrWhiteSpace(StandardError)
        ? StandardOutput
        : $"{StandardOutput}\n{StandardError}".Trim();
}

public static class CommandRunner
{
    public static CommandResult Run(string fileName, string arguments, int timeoutMs = 15000)
    {
        try
        {
            using var process = CreateProcess(fileName, arguments);
            var output = new StringBuilder();
            var error = new StringBuilder();
            using var outDone = new ManualResetEventSlim(false);
            using var errDone = new ManualResetEventSlim(false);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) outDone.Set();
                else output.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) errDone.Set();
                else error.AppendLine(e.Data);
            };

            if (!process.Start())
            {
                return new CommandResult(-1, string.Empty, $"Не удалось запустить {fileName}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new CommandResult(-2, output.ToString().Trim(), $"Превышено время ожидания ({timeoutMs} мс) для {fileName}");
            }

            // Bounded wait on stream EOF so grandchild handle inheritance can never deadlock
            outDone.Wait(500);
            errDone.Wait(500);

            return new CommandResult(process.ExitCode, output.ToString().Trim(), error.ToString().Trim());
        }
        catch (Exception ex)
        {
            return new CommandResult(-1, string.Empty, ex.Message);
        }
    }

    public static CommandResult RunPowerShell(string script, int timeoutMs = 20000)
    {
        var wrapped = $"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $ProgressPreference = 'SilentlyContinue'; {script}";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));
        return Run("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", timeoutMs);
    }

    private static Process CreateProcess(string fileName, string arguments) => new()
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        }
    };
}
