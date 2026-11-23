using System.Text.Json;
using System.Text.RegularExpressions;

namespace SysAdminAssistant.Core;

public sealed class DiagnosticEntry
{
    public bool IsCustom { get; init; }
    public string Id { get; init; } = "";
    public string[] Providers { get; init; } = [];
    public string Title { get; init; } = "";
    public string[] Patterns { get; init; } = [];
    public string Meaning { get; init; } = "";
    public string Impact { get; init; } = "";
    public string[] Steps { get; init; } = [];
    public string Source { get; init; } = "";
    public string Scope { get; init; } = "";
    public string Confidence { get; init; } = "";
    public string Reviewed { get; init; } = "";
    public string Format()
    {
        string Text(string value)=>IsCustom?value:ProductText.Get(value);
        return $"{Text(Title)}\n{ProductText.Get("Что означает:")} {Text(Meaning)}\n{ProductText.Get("Последствия:")} {Text(Impact)}\n{ProductText.Get("Что проверить:")}\n"+
            string.Join("\n",Steps.Select((step,index)=>$"{index+1}. {Text(step)}"))+
            $"\n{ProductText.Get("Точность:")} {Text(Confidence)}\n{ProductText.Get("Применимость:")} {Text(Scope)}\n{ProductText.Get(IsCustom?"Источник пользователя:":"Официальная статья:")} {Source}\n{ProductText.Get("Проверено:")} {Reviewed}";
    }
}
public sealed record DiagnosticFinding(DiagnosticEntry Entry, string[] Evidence);
public sealed record DiagnosticReport(DiagnosticFinding[] Findings, bool Limited)
{
    public string Format(bool needsReview)
    {
        var text = string.Join("\n\n", Findings.Select(x => x.Entry.Format() + "\nИсходные строки:\n" + string.Join("\n", x.Evidence)));
        if (Findings.Length > 0) text += "\n\nСовпадения объясняют указанные строки, а не все причины и не итог задания. Статус и привязка определяются отдельно.";
        else if (needsReview) text = "Известная причина не распознана. Проверьте подробный журнал этой сессии в исходной программе; сохраните код и текст ошибки. Общий статус Warning/Error не позволяет установить причину. Не считайте такую копию проверенной без проверки результата и восстановления.";
        if (Limited) text += "\nАнализ справочником ограничен размером сообщения или временем поиска; проверьте полный журнал.";
        return text;
    }
}

/// <summary>Offline explanations only. Never changes the status or the binding of a report.</summary>
public static class DiagnosticGuide
{
    private const int MaxBody = 500_000;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private static readonly Lazy<DiagnosticEntry[]> Catalogue = new(Load);
    public static IReadOnlyList<DiagnosticEntry> Entries => Array.AsReadOnly(Catalogue.Value);
    private static DiagnosticEntry[] Load()
    {
        using var stream = typeof(DiagnosticGuide).Assembly.GetManifestResourceStream("SysAdminAssistant.Core.Diagnostics.catalog.json")
            ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Встроенный справочник отсутствует"));
        var entries = JsonSerializer.Deserialize<DiagnosticEntry[]>(stream) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Справочник пуст"));
        if (entries.Length == 0 || entries.Select(x => x.Id).Distinct().Count() != entries.Length)
            throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Повторяющиеся коды справочника"));
        foreach (var e in entries)
        {
            if (e.Providers.Length == 0 || e.Patterns.Length == 0 || e.Steps.Length == 0 || e.Title.Length == 0 || !OfficialSource(e.Source))
                throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Некорректная статья: ") + e.Id);
            foreach (var p in e.Patterns) _ = new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
        }
        return entries;
    }
    public static bool OfficialSource(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 &&
        uri.Host is "www.veeam.com" or "helpcenter.veeam.com" or "sqlbackupandftp.com" or "kb.synology.com" or "www.qnap.com" or "docs.qnap.com" or "learn.microsoft.com" or "www.rfc-editor.org";
    public static DiagnosticReport Analyze(string provider, string body, IEnumerable<CustomDiagnostic>? custom = null)
    {
        // Subject, sender and device/job names cannot serve as diagnostic evidence.
        bool limited = body.Length > MaxBody;
        var lines = body[..Math.Min(MaxBody, body.Length)].Replace("\r", "").Split('\n');
        if (lines.Length > 4000) limited = true;
        if (lines.Any(x => x.Length > 4000)) limited = true;
        lines = lines.Take(4000).Where(x => x.Length <= 4000)
            .Select(x => Regex.Replace(x, @"\s+", " ", RegexOptions.CultureInvariant, Timeout).Trim())
            .Where(x => x.Length > 0 && !Metadata(x) && !x.StartsWith('>')).ToArray();
        var findings = new List<DiagnosticFinding>();
        foreach (var entry in Catalogue.Value.Concat((custom ?? []).Where(x => x.Enabled && x.Validate() == null).Select(x => x.ToEntry())).Where(x => x.Providers.Contains(provider, StringComparer.OrdinalIgnoreCase) || x.Providers.Contains("Любой")))
        {
            var evidence = new List<string>();
            try
            {
                foreach (var line in lines)
                {
                    if (line.Length > 4000) { limited = true; continue; }
                    foreach (var pattern in entry.Patterns)
                    {
                        var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
                        if (!match.Success || HistoricalOrNegated(line, match.Index)) continue;
                        if (!evidence.Contains(line, StringComparer.OrdinalIgnoreCase)) evidence.Add(line);
                        break;
                    }
                    if (evidence.Count == 3) break;
                }
            }
            catch (RegexMatchTimeoutException) { limited = true; }
            if (evidence.Count > 0) findings.Add(new(entry, evidence.ToArray()));
        }
        return new(findings.ToArray(), limited);
    }
    public static string IssueEvidence(string body)
    {
        // Keep a bounded excerpt for an unknown warning after the raw email is removed.
        var lines = body[..Math.Min(MaxBody, body.Length)].Replace("\r", "").Split('\n');
        var kept = new List<string>(); var size = 0;
        foreach (var raw in lines.Take(4000))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.Length > 4000 || Metadata(line) || line.StartsWith('>')) continue;
            var issue = Regex.Match(line, @"\b(?:warning|error|failed|failure|critical|exception|0x[0-9a-f]{8})\b|предупрежден|ошибк|сбой|критическ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
            if (!issue.Success || HistoricalOrNegated(line, issue.Index) || kept.Contains(line, StringComparer.OrdinalIgnoreCase)) continue;
            var remaining = 4000 - size; if (remaining <= 0) break;
            var excerpt = line[..Math.Min(remaining, line.Length)]; kept.Add(excerpt); size += excerpt.Length + 1;
            if (kept.Count == 12) break;
        }
        return kept.Count == 0 ? "" : "Исходные сообщения (выдержка):\n" + string.Join("\n", kept);
    }
    private static bool Metadata(string line) => Regex.IsMatch(line,
        @"^(?:job(?: name)?|backup job|task(?: name)?|server|host|computer|nas|задание|имя задания|сервер|устройство|duration|size|items|errors|warnings|start time|end time)\s*[:=]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static bool HistoricalOrNegated(string line, int index)
    {
        var prefix = line[..index].TrimEnd();
        return Regex.IsMatch(prefix, @"(?:previous|historical|example|resolved|fixed|was resolved|was fixed|устранено|пример|ранее)\s*(?:message|error|warning|сообщение|ошибка)?\s*[:=-]?\s*$|(?:no|without|not|нет|без)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    }
}
