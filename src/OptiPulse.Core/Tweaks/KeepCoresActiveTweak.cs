using Microsoft.Win32;
using OptiPulse.Core.Models;
using OptiPulse.Core.Services;

namespace OptiPulse.Core.Tweaks;

/// <summary>
/// 10. Держать все ядра активными (отключение парковки ядер CPU)
/// </summary>
public sealed class KeepCoresActiveTweak : TweakBase
{
    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string CpMinCores = "0cc5b647-c1df-4637-891a-dec35c318583";
    private const string CpMinCoresHetero = "0cc5b647-c1df-4637-891a-dec35c318584";
    private const string PowerSettingRegKey = @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583";

    private static readonly TweakDescriptor Info = new()
    {
        Id = "unpark_cpu_cores",
        Title = "Держать все ядра активными",
        Description = "Поддерживает активность всех ядер процессора для обеспечения максимальной производительности.",
        NotRecommendedWhen = "Вы используете ноутбук от батареи или система охлаждения процессора работает на пределе — отключение парковки ядер слегка увеличивает энергопотребление и температуру CPU в простое.",
        TechnicalDetails = "Отключает парковку ядер CPU (Core Parking) в текущей схеме питания через powercfg: устанавливает CPMINCORES = 100% (и CPMINCORES1 = 100% для гибридных P/E-процессоров).",
        IconKey = "IconCpuCores",
        Category = TweakCategory.CpuAndSystem,
        NoticeLevel = TweakNoticeLevel.Info,
        NoticeTooltip = "Отключает парковку ядер CPU в активной схеме электропитания (питание от сети).",
        RequiresAdmin = true
    };

    public KeepCoresActiveTweak(BackupService backup, AppLogger logger) : base(backup, logger) { }

    public override TweakDescriptor Descriptor => Info;

    public override TweakStatusInfo GetStatus()
    {
        var currentAc = PowerCfgHelper.QueryAcValueIndex(SubProcessor, CpMinCores);
        var valueMax = RegistryHelper.ReadDword(RegistryHelper.Hklm, PowerSettingRegKey, "ValueMax");
        var enabled = currentAc == 100 || valueMax == 0;

        var note = currentAc == 100
            ? "Все ядра активны (CPMINCORES = 100%)"
            : valueMax == 0
                ? "Парковка ядер отключена (ValueMax = 0)"
                : currentAc.HasValue
                    ? $"Минимум активных ядер (от сети): {currentAc.Value}%"
                    : "Параметр парковки ядер не определён";

        return new TweakStatusInfo
        {
            IsEnabled = enabled,
            StatusNote = note
        };
    }

    protected override TweakOperationResult ApplyCore()
    {
        var entry = Backup.GetOrCreateTweakBackup(Descriptor.Id);
        if (!entry.Metadata.ContainsKey("OriginalCpMinCoresAc"))
        {
            var origAc = PowerCfgHelper.QueryAcValueIndex(SubProcessor, CpMinCores) ?? 5;
            entry.Metadata["OriginalCpMinCoresAc"] = origAc.ToString();
        }
        if (!entry.Metadata.ContainsKey("OriginalCpMinCoresHeteroAc"))
        {
            var origHetero = PowerCfgHelper.QueryAcValueIndex(SubProcessor, CpMinCoresHetero);
            if (origHetero.HasValue)
            {
                entry.Metadata["OriginalCpMinCoresHeteroAc"] = origHetero.Value.ToString();
            }
        }

        // Unhide Core Parking setting and ensure ValueMax allows 100% before setting CPMINCORES=100
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, PowerSettingRegKey, "Attributes", 0, RegistryValueKind.DWord);
        RegistryHelper.BackupAndWrite(entry, RegistryHelper.Hklm, PowerSettingRegKey, "ValueMax", 100, RegistryValueKind.DWord);

        var r1 = PowerCfgHelper.SetAcValueIndex(SubProcessor, CpMinCores, 100);
        PowerCfgHelper.SetAcValueIndex(SubProcessor, CpMinCoresHetero, 100);
        PowerCfgHelper.ApplyCurrentScheme();

        if (!r1.Success)
        {
            return TweakOperationResult.Fail($"powercfg вернул ошибку: {r1.CombinedOutput}", GetStatus().IsEnabled);
        }

        Backup.SaveTweakBackup(entry);
        return TweakOperationResult.Ok(true, "Парковка ядер процессора отключена (100% ядер активны от сети).");
    }

    protected override TweakOperationResult RevertCore()
    {
        var entry = Backup.GetTweakBackup(Descriptor.Id);
        int restoreValue = 5;
        int? restoreHetero = null;

        if (entry is not null)
        {
            if (entry.Metadata.TryGetValue("OriginalCpMinCoresAc", out var origStr) && int.TryParse(origStr, out var parsed))
            {
                restoreValue = parsed < 100 ? parsed : 5;
            }
            if (entry.Metadata.TryGetValue("OriginalCpMinCoresHeteroAc", out var heteroStr) && int.TryParse(heteroStr, out var parsedHetero))
            {
                restoreHetero = parsedHetero < 100 ? parsedHetero : 5;
            }
            RestoreBackedUpRegistryValues(entry);
            Backup.RemoveTweakBackup(Descriptor.Id);
        }

        // Ensure ValueMax allows normal range 0..100 if previously locked to 0 externally
        if (RegistryHelper.ReadDword(RegistryHelper.Hklm, PowerSettingRegKey, "ValueMax") == 0)
        {
            RegistryHelper.WriteValue(RegistryHelper.Hklm, PowerSettingRegKey, "ValueMax", 100, RegistryValueKind.DWord);
        }

        PowerCfgHelper.SetAcValueIndex(SubProcessor, CpMinCores, restoreValue);
        if (restoreHetero.HasValue)
        {
            PowerCfgHelper.SetAcValueIndex(SubProcessor, CpMinCoresHetero, restoreHetero.Value);
        }
        PowerCfgHelper.ApplyCurrentScheme();

        return TweakOperationResult.Ok(false, $"Парковка ядер восстановлена (минимум активных ядер: {restoreValue}%).");
    }
}
