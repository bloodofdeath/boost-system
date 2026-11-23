using System.Text.RegularExpressions;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 5. Отключить временные метки последнего доступа (NTFS Last Access)
/// </summary>
public sealed class NtfsLastAccessTweak : TweakBase
{
    private const string FileSystemKey = @"SYSTEM\CurrentControlSet\Control\FileSystem";

    public NtfsLastAccessTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => new()
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

    public override TweakStatusInfo GetStatus()
    {
        var raw = RegistryHelper.ReadDword(RegistryHelper.Hklm, FileSystemKey, "NtfsDisableLastAccessUpdate");
        int mode;
        if (raw.HasValue)
        {
            // Low 2 bits encode the fsutil mode: 0=User Enabled, 1=User Disabled, 2=System Disabled, 3=System Enabled
            mode = raw.Value & 0x3;
        }
        else
        {
            mode = QueryFsutilMode() ?? 3;
        }

        // Mode 1 (User Managed, Disabled) or 2 (System Managed, Disabled) means Last Access updates are OFF
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
        var restoreMode = 3; // System Managed, Enabled (Windows default on <128GB or modern builds)
        if (entry is not null && entry.Metadata.TryGetValue("OriginalMode", out var origStr) && int.TryParse(origStr, out var parsed))
        {
            // If the original was already 2 (System Disabled), switching OFF should explicitly enable (0 or 3) so toggle reflects OFF!
            restoreMode = parsed is 0 or 3 ? parsed : 3;
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
        var match = Regex.Match(res.StandardOutput, @"=\s*([0-3])");
        return match.Success && int.TryParse(match.Groups[1].Value, out var val) ? val : null;
    }
}
