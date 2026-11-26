using System.Text.RegularExpressions;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 11. Отключить временные метки последнего доступа (NTFS Last Access)
/// </summary>
public sealed partial class NtfsLastAccessTweak : TweakBase
{
    private const string FileSystemKey = @"SYSTEM\CurrentControlSet\Control\FileSystem";

    private static readonly TweakDescriptor Info = new()
    {
        Id = "disable_ntfs_last_access",
        Title = "Отключить временные метки последнего доступа",
        Description = "Отключает запись последних файлов для повышения производительности диска.",
        NotRecommendedWhen = "Вы используете корпоративное ПО для аудита файлов или системы резервного копирования, которым критически важна точная дата последнего чтения каждого файла (NTFS Last Access Timestamp).",
        TechnicalDetails = "Выполняет команду fsutil behavior set disablelastaccess 1 (NtfsDisableLastAccessUpdate), отключая перезапись метаданных последнего доступа при каждом чтении файла на дисках NTFS.",
        IconKey = "IconDiskTimestamp",
        Category = TweakCategory.StorageAndMemory,
        NoticeLevel = TweakNoticeLevel.Info,
        NoticeTooltip = "Для полного применения к системному диску требуется перезагрузка Windows.",
        RequiresAdmin = true,
        RequiresRebootForFullEffect = true
    };

    [GeneratedRegex(@"=\s*([0-3])", RegexOptions.CultureInvariant)]
    private static partial Regex FsutilModeRegex();

    public NtfsLastAccessTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => Info;

    public override TweakStatusInfo GetStatus()
    {
        var raw = RegistryHelper.ReadDword(RegistryHelper.Hklm, FileSystemKey, "NtfsDisableLastAccessUpdate");
        int mode = raw.HasValue ? (raw.Value & 0x3) : (QueryFsutilMode() ?? 3);

        var isDisabled = mode is 1 or 2;
        var modeText = mode switch
        {
            0 => "Включено пользователем (0)",
            1 => "Отключено пользователем (1)",
            2 => "Отключено системой (2)",
            3 => "Включено системой (3)",
            _ => $"Режим ({mode})"
        };

        return new TweakStatusInfo
        {
            IsEnabled = isDisabled,
            StatusNote = $"NTFS DisableLastAccess = {modeText}"
        };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);
        if (!entry.Metadata.ContainsKey("OriginalMode"))
        {
            var raw = RegistryHelper.ReadDword(RegistryHelper.Hklm, FileSystemKey, "NtfsDisableLastAccessUpdate");
            var origMode = raw.HasValue ? (raw.Value & 0x3) : (QueryFsutilMode() ?? 3);
            entry.Metadata["OriginalMode"] = origMode.ToString();
        }

        var res = CommandRunner.Run("fsutil", "behavior set disablelastaccess 1", 8000);
        if (!res.Success)
        {
            return TweakOperationResult.Fail($"fsutil вернул ошибку: {res.CombinedOutput}", GetStatus().IsEnabled);
        }

        Backup.SaveTweakBackup(entry);
        return TweakOperationResult.Ok(true, "Запись меток последнего доступа NTFS отключена (режим 1). Рекомендуется перезагрузка.", rebootRecommended: true);
    }

    protected override TweakOperationResult RevertCore()
    {
        var entry = Backup.GetTweakBackup(Descriptor.Id);
        var restoreMode = 3;
        if (entry is not null)
        {
            if (entry.Metadata.TryGetValue("OriginalMode", out var origStr) && int.TryParse(origStr, out var parsed))
            {
                restoreMode = parsed is 0 or 3 ? parsed : 3;
            }
            Backup.RemoveTweakBackup(Descriptor.Id);
        }

        var res = CommandRunner.Run("fsutil", $"behavior set disablelastaccess {restoreMode}", 8000);
        if (!res.Success)
        {
            return TweakOperationResult.Fail($"fsutil вернул ошибку: {res.CombinedOutput}", GetStatus().IsEnabled);
        }

        return TweakOperationResult.Ok(false, $"Запись меток последнего доступа NTFS восстановлена (режим {restoreMode}).", rebootRecommended: true);
    }

    private static int? QueryFsutilMode()
    {
        var res = CommandRunner.Run("fsutil", "behavior query disablelastaccess", 6000);
        if (!res.Success) return null;
        var match = FsutilModeRegex().Match(res.StandardOutput);
        return match.Success && int.TryParse(match.Groups[1].Value, out var val) ? val : null;
    }
}
