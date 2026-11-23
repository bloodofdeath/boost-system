using System.Net;
using System.Text;
using System.Text.Json;
using OptiPulse.Core.Models;

namespace OptiPulse.Core.Services;

public static class ReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string GenerateJson(PcAnalysisReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    public static string GenerateMarkdown(PcAnalysisReport report)
    {
        var sb = new StringBuilder();
        var hw = report.Hardware;

        sb.AppendLine("# Отчёт диагностики и ускорения ПК — OptiPulse");
        sb.AppendLine();
        sb.AppendLine($"- **Дата анализа:** {report.GeneratedAtLocal:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine($"- **Компьютер:** `{report.MachineName}`");
        sb.AppendLine($"- **Режим запуска:** {(report.WasRunAsAdmin ? "Администратор" : "Обычный пользователь")}");
        sb.AppendLine($"- **Оценка оптимизации:** **{report.OptimizationScore} / 100** — {report.ScoreVerdict}");
        sb.AppendLine($"- **Активно твиков OptiPulse:** {report.ActiveTweaksCount} из {report.TotalTweaksCount}");
        sb.AppendLine();

        sb.AppendLine("## Характеристики системы");
        sb.AppendLine();
        sb.AppendLine("| Компонент | Значение |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| ОС | {hw.OsCaption} (Build {hw.OsBuild}) |");
        sb.AppendLine($"| Тип устройства | {(hw.IsLaptop ? "Ноутбук (обнаружена батарея)" : "Настольный ПК")} |");
        sb.AppendLine($"| Процессор | {hw.CpuName} ({hw.CpuCores} ядер / {hw.CpuThreads} потоков, нагрузка {hw.CpuLoadPercent:0.#}%) |");
        sb.AppendLine($"| Видеокарта | {hw.PrimaryGpuName} {(string.IsNullOrWhiteSpace(hw.GpuDriverVersion) ? "" : $"(драйвер {hw.GpuDriverVersion})")} |");
        sb.AppendLine($"| ОЗУ | {hw.UsedRamGb:0.0} / {hw.TotalRamGb:0.0} ГБ ({hw.RamUsagePercent:0.#}% занято, свободно {hw.FreeRamGb:0.0} ГБ) |");
        sb.AppendLine($"| План электропитания | {hw.ActivePowerPlanName} |");
        sb.AppendLine($"| Сеть | {hw.ActiveNetworkAdapter} {(string.IsNullOrWhiteSpace(hw.NetworkLinkSpeed) ? "" : $"({hw.NetworkLinkSpeed})")} |");
        sb.AppendLine($"| Аптайм системы | {hw.SystemUptime.Days} дн. {hw.SystemUptime.Hours} ч. {hw.SystemUptime.Minutes} мин. |");
        sb.AppendLine();

        if (hw.Disks.Count > 0)
        {
            sb.AppendLine("### Диски");
            sb.AppendLine();
            sb.AppendLine("| Диск | Тип | Свободно | Всего |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var d in hw.Disks)
            {
                sb.AppendLine($"| `{d.DriveLetter}` | {d.MediaType} | {d.FreeGb:0.0} ГБ ({d.FreePercent:0.#}%) | {d.TotalGb:0.0} ГБ |");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"## Что можно выключить / оптимизировать ({report.Recommendations.Count})");
        sb.AppendLine();
        if (report.Recommendations.Count == 0)
        {
            sb.AppendLine("Отлично! Проблемных фоновых служб и неоптимизированных параметров не обнаружено.");
        }
        else
        {
            foreach (var rec in report.Recommendations)
            {
                var impactLabel = rec.Impact switch
                {
                    RecommendationImpact.High => "Высокий эффект",
                    RecommendationImpact.Medium => "Средний эффект",
                    _ => "Низкий эффект"
                };
                var safetyLabel = rec.Safety switch
                {
                    RecommendationSafety.Safe => "Безопасно",
                    RecommendationSafety.Moderate => "Умеренно",
                    _ => "Осторожно"
                };
                sb.AppendLine($"### [{impactLabel} | {safetyLabel}] {rec.Title}");
                sb.AppendLine($"- **Описание:** {rec.Description}");
                sb.AppendLine($"- **Текущее состояние:** `{rec.CurrentState}`");
                sb.AppendLine($"- **Ожидаемая польза:** {rec.ExpectedBenefit}");
                sb.AppendLine();
            }
        }

        if (report.StartupApps.Count > 0)
        {
            sb.AppendLine($"## Программы в автозагрузке ({report.StartupApps.Count})");
            sb.AppendLine();
            sb.AppendLine("| Программа | Источник | Рекомендация |");
            sb.AppendLine("|---|---|---|");
            foreach (var s in report.StartupApps)
            {
                var flag = s.IsDisabledInWindows
                    ? "Отключено в Диспетчере задач Windows"
                    : s.IsRecommendedToDisable
                        ? $"Можно отключить: {s.RecommendationReason}"
                        : "Оставить / по желанию";
                sb.AppendLine($"| {s.Name} | {s.Location} | {flag} |");
            }
            sb.AppendLine();
        }

        if (report.TopProcesses.Count > 0)
        {
            sb.AppendLine("## Топ фоновых процессов по потреблению ОЗУ");
            sb.AppendLine();
            sb.AppendLine("| Процесс | Экземпляров | ОЗУ (МБ) | Комментарий |");
            sb.AppendLine("|---|---:|---:|---|");
            foreach (var p in report.TopProcesses)
            {
                sb.AppendLine($"| `{p.Name}` | {p.InstanceCount} | {p.MemoryMb:0.#} | {p.Note} |");
            }
            sb.AppendLine();
        }

        if (report.AlreadyOptimizedItems.Count > 0)
        {
            sb.AppendLine($"## Уже оптимизировано ({report.AlreadyOptimizedItems.Count})");
            sb.AppendLine();
            foreach (var item in report.AlreadyOptimizedItems)
            {
                sb.AppendLine($"- {item}");
            }
        }

        return sb.ToString();
    }

    public static string GenerateHtml(PcAnalysisReport report)
    {
        var hw = report.Hardware;
        var scoreColor = report.OptimizationScore >= 80 ? "#34E59E" : report.OptimizationScore >= 55 ? "#F59E0B" : "#EF4444";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"ru\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"<title>OptiPulse — Отчёт диагностики ПК ({H(report.MachineName)})</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(@"
            :root { --bg: #0D0F13; --card: #17191F; --border: #252A34; --text: #F3F5F8; --muted: #8E95A5; --mint: #34E59E; --amber: #F59E0B; --blue: #4F7CFF; --red: #EF4444; }
            * { box-sizing: border-box; }
            body { margin: 0; padding: 32px; background: var(--bg); color: var(--text); font-family: 'Segoe UI', system-ui, -apple-system, sans-serif; line-height: 1.5; }
            .wrap { max-width: 1120px; margin: 0 auto; }
            .hero { display: flex; justify-content: space-between; align-items: center; gap: 24px; background: var(--card); border: 1px solid var(--border); border-radius: 14px; padding: 24px 28px; margin-bottom: 24px; }
            .brand { font-size: 13px; letter-spacing: 1px; text-transform: uppercase; color: var(--mint); font-weight: 700; }
            h1 { margin: 6px 0 4px; font-size: 26px; }
            .meta { color: var(--muted); font-size: 13px; }
            .score-box { text-align: center; min-width: 150px; padding: 14px 20px; background: #101218; border-radius: 12px; border: 1px solid var(--border); }
            .score-num { font-size: 42px; font-weight: 800; line-height: 1.1; }
            .score-label { font-size: 12px; color: var(--muted); margin-top: 4px; }
            h2 { margin: 28px 0 14px; font-size: 19px; color: var(--text); }
            .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 14px; }
            .card { background: var(--card); border: 1px solid var(--border); border-radius: 12px; padding: 16px 18px; }
            .card-label { font-size: 12px; color: var(--muted); text-transform: uppercase; letter-spacing: 0.5px; }
            .card-val { font-size: 15px; font-weight: 600; margin-top: 6px; }
            .card-sub { font-size: 12.5px; color: var(--muted); margin-top: 4px; }
            .rec { background: var(--card); border: 1px solid var(--border); border-radius: 12px; padding: 16px 20px; margin-bottom: 12px; }
            .rec-head { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
            .rec-title { font-weight: 700; font-size: 15.5px; }
            .pills { display: flex; gap: 8px; }
            .pill { font-size: 11.5px; font-weight: 600; padding: 3px 10px; border-radius: 999px; }
            .p-high { background: rgba(239,68,68,.16); color: #FCA5A5; }
            .p-med { background: rgba(245,158,11,.16); color: #FCD34D; }
            .p-low { background: rgba(79,124,255,.16); color: #93C5FD; }
            .p-safe { background: rgba(52,229,158,.15); color: #34E59E; }
            .p-mod { background: rgba(245,158,11,.15); color: #FBBF24; }
            .p-caut { background: rgba(239,68,68,.15); color: #F87171; }
            .rec-desc { color: #CBD2DF; font-size: 13.5px; margin: 8px 0; }
            .rec-foot { color: var(--muted); font-size: 12.5px; }
            table { width: 100%; border-collapse: collapse; background: var(--card); border: 1px solid var(--border); border-radius: 12px; overflow: hidden; }
            th, td { text-align: left; padding: 10px 14px; border-bottom: 1px solid var(--border); font-size: 13px; }
            th { color: var(--muted); font-weight: 600; background: #12141A; }
            tr:last-child td { border-bottom: none; }
            .ok-list { display: flex; flex-wrap: wrap; gap: 8px; }
            .ok-chip { background: rgba(52,229,158,.12); border: 1px solid rgba(52,229,158,.3); color: #A7F3D0; border-radius: 8px; padding: 6px 12px; font-size: 12.5px; }
            footer { margin-top: 36px; text-align: center; color: var(--muted); font-size: 12px; }
        ");
        sb.AppendLine("</style></head><body><div class=\"wrap\">");

        sb.AppendLine("<div class=\"hero\">");
        sb.AppendLine($"  <div><div class=\"brand\">OptiPulse Diagnostics Report</div><h1>Анализ производительности ПК: {H(report.MachineName)}</h1><div class=\"meta\">Сформировано: {report.GeneratedAtLocal:dd.MM.yyyy HH:mm:ss} • {H(report.ScoreVerdict)} • Активно оптимизаций: {report.ActiveTweaksCount}/{report.TotalTweaksCount}</div></div>");
        sb.AppendLine($"  <div class=\"score-box\"><div class=\"score-num\" style=\"color:{scoreColor}\">{report.OptimizationScore}</div><div class=\"score-label\">из 100 баллов</div></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<h2>Конфигурация оборудования</h2><div class=\"grid\">");
        sb.AppendLine($"  <div class=\"card\"><div class=\"card-label\">Процессор</div><div class=\"card-val\">{H(hw.CpuName)}</div><div class=\"card-sub\">{hw.CpuCores} ядер / {hw.CpuThreads} потоков • Нагрузка: {hw.CpuLoadPercent:0.#}%</div></div>");
        sb.AppendLine($"  <div class=\"card\"><div class=\"card-label\">Видеокарта</div><div class=\"card-val\">{H(hw.PrimaryGpuName)}</div><div class=\"card-sub\">Драйвер: {H(string.IsNullOrWhiteSpace(hw.GpuDriverVersion) ? "—" : hw.GpuDriverVersion)}</div></div>");
        sb.AppendLine($"  <div class=\"card\"><div class=\"card-label\">Оперативная память</div><div class=\"card-val\">{hw.UsedRamGb:0.0} / {hw.TotalRamGb:0.0} ГБ ({hw.RamUsagePercent:0.#}%)</div><div class=\"card-sub\">Свободно: {hw.FreeRamGb:0.0} ГБ</div></div>");
        sb.AppendLine($"  <div class=\"card\"><div class=\"card-label\">Система и Питание</div><div class=\"card-val\">{H(hw.ActivePowerPlanName)}</div><div class=\"card-sub\">{H(hw.OsCaption)} • {(hw.IsLaptop ? "Ноутбук" : "ПК")}</div></div>");
        sb.AppendLine("</div>");

        sb.AppendLine($"<h2>Рекомендации: что можно выключить и оптимизировать ({report.Recommendations.Count})</h2>");
        if (report.Recommendations.Count == 0)
        {
            sb.AppendLine("<div class=\"card\">Отлично! Ваша система уже отлично оптимизирована.</div>");
        }
        else
        {
            foreach (var rec in report.Recommendations)
            {
                var (impCls, impTxt) = rec.Impact switch
                {
                    RecommendationImpact.High => ("p-high", "Высокий эффект"),
                    RecommendationImpact.Medium => ("p-med", "Средний эффект"),
                    _ => ("p-low", "Низкий эффект")
                };
                var (safCls, safTxt) = rec.Safety switch
                {
                    RecommendationSafety.Safe => ("p-safe", "Безопасно"),
                    RecommendationSafety.Moderate => ("p-mod", "Умеренно"),
                    _ => ("p-caut", "Осторожно")
                };

                sb.AppendLine("<div class=\"rec\">");
                sb.AppendLine($"  <div class=\"rec-head\"><div class=\"rec-title\">{H(rec.Title)}</div><div class=\"pills\"><span class=\"pill {impCls}\">{impTxt}</span><span class=\"pill {safCls}\">{safTxt}</span></div></div>");
                sb.AppendLine($"  <div class=\"rec-desc\">{H(rec.Description)}</div>");
                sb.AppendLine($"  <div class=\"rec-foot\"><strong>Текущее состояние:</strong> {H(rec.CurrentState)} &nbsp;|&nbsp; <strong>Польза:</strong> {H(rec.ExpectedBenefit)}</div>");
                sb.AppendLine("</div>");
            }
        }

        if (report.StartupApps.Count > 0)
        {
            sb.AppendLine($"<h2>Автозагрузка Windows ({report.StartupApps.Count})</h2>");
            sb.AppendLine("<table><thead><tr><th>Программа</th><th>Источник</th><th>Статус</th></tr></thead><tbody>");
            foreach (var s in report.StartupApps)
            {
                var statusText = s.IsDisabledInWindows
                    ? "Отключено в Диспетчере задач Windows"
                    : s.IsRecommendedToDisable
                        ? $"Можно отключить: {s.RecommendationReason}"
                        : "Штатный запуск";
                sb.AppendLine($"<tr><td><strong>{H(s.Name)}</strong></td><td>{H(s.Location)}</td><td>{H(statusText)}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        if (report.TopProcesses.Count > 0)
        {
            sb.AppendLine("<h2>Крупнейшие фоновые процессы</h2>");
            sb.AppendLine("<table><thead><tr><th>Процесс</th><th>Экземпляров</th><th>ОЗУ (МБ)</th><th>Рекомендация</th></tr></thead><tbody>");
            foreach (var p in report.TopProcesses)
            {
                sb.AppendLine($"<tr><td><strong>{H(p.Name)}</strong></td><td>{p.InstanceCount}</td><td>{p.MemoryMb:0.#} МБ</td><td>{H(p.Note)}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        if (report.AlreadyOptimizedItems.Count > 0)
        {
            sb.AppendLine($"<h2>Уже оптимизировано ({report.AlreadyOptimizedItems.Count})</h2><div class=\"ok-list\">");
            foreach (var ok in report.AlreadyOptimizedItems)
            {
                sb.AppendLine($"<span class=\"ok-chip\">✓ {H(ok)}</span>");
            }
            sb.AppendLine("</div>");
        }

        sb.AppendLine("<footer>Создано в OptiPulse — Open-Source Windows Gaming &amp; PC Optimizer</footer>");
        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    public static void SaveReport(PcAnalysisReport report, string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var content = ext switch
        {
            ".html" or ".htm" => GenerateHtml(report),
            ".json" => GenerateJson(report),
            _ => GenerateMarkdown(report)
        };
        File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
