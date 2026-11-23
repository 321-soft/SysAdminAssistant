using System.Globalization;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace SysAdminAssistant.Core;

public static class ReportAnalyzer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.OrdinalIgnoreCase);
    public static readonly string[] Providers = ["Любой", "SQLBackupAndFTP", "Veeam", "Active Backup for Business", "DSM", "QNAP", "QNAP HBS"];
    public static string DetectProvider(string subject, string body, string sender = "")
    {
        var text = (subject + "\n" + body + "\n" + sender).ToLowerInvariant();
        if (text.Contains("sqlbackupandftp") || text.Contains("sql backup and ftp")) return "SQLBackupAndFTP";
        if (text.Contains("veeam")) return "Veeam";
        if (text.Contains("active backup for business") || text.Contains("activebackup for business")) return "Active Backup for Business";
        if (text.Contains("hybrid backup sync") || text.Contains("hbs 3") || text.Contains("hbs3") || (text.Contains("qnap") && Pattern("hbs").IsMatch(text))) return "QNAP HBS";
        if (text.Contains("qnap") || text.Contains("[qts]")) return "QNAP";
        if (text.Contains("synology") || text.Contains("diskstation") || Pattern("dsm").IsMatch(text)) return "DSM";
        return "Неизвестно";
    }
    public static string DetectProvider(string subject, string body, string sender, IReadOnlyList<KeywordProfile> profiles)
    {
        var matches = profiles.Where(p => p.Provider != "Любой" && SourceMatches(p, subject, body, sender)).Select(p => p.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (matches.Length > 1) return "Неизвестно";
        return matches.Length == 1 ? matches[0] : DetectProvider(subject, body, sender);
    }
    public static bool SourceConflict(MailReport mail, IReadOnlyList<KeywordProfile> profiles) => profiles.Where(p => p.Provider != "Любой" && SourceMatches(p, mail.Subject, mail.Body, mail.Sender)).Select(p => p.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() > 1;
    private static bool SourceMatches(KeywordProfile p, string subject, string body, string sender)
    {
        if(MailSources.HasAdditional(p,subject,body))return true;
        var checks = new List<bool>();
        Add(p.SenderTerms, sender); Add(p.SubjectTerms, subject); Add(p.BodyTerms, body);
        return checks.Count > 0 && (p.MatchAllSourceFields ? checks.All(x => x) : checks.Any(x => x));
        void Add(string terms, string text) { var values = Terms(terms).ToArray(); if (values.Length > 0) checks.Add(values.Any(t => p.EventRulesJson.Length>0 ? text.Contains(t,StringComparison.OrdinalIgnoreCase) : Pattern(t).IsMatch(text))); }
    }
    public static Analysis Analyze(MailReport mail, IReadOnlyList<KeywordProfile> profiles, string? backupSystem = null, bool isBackupReport = false)
    {
        if(string.IsNullOrWhiteSpace(backupSystem)&&SourceConflict(mail,profiles))
            return new("Неизвестно",BackupResult.Unknown,Severity.Unknown,"Требуется разбор источника","",mail.ImapReceivedAt??mail.MessageDate,"","","Конфликт условий источников");
        var provider = string.IsNullOrWhiteSpace(backupSystem) ? DetectProvider(mail.Subject, mail.Body, mail.Sender, profiles) : backupSystem;
        var common = profiles.FirstOrDefault(x => x.Provider == "Любой") ?? new KeywordProfile();
        var specific = profiles.FirstOrDefault(x => x.Provider == provider);
        if(specific is {AdditionalEventRulesJson.Length:>0}){
            var matches=MailSources.MatchAdditional(specific,mail);
            var when=mail.ImapReceivedAt??mail.MessageDate;
            if(matches.Length>1)return new Analysis(provider,BackupResult.Unknown,Severity.Unknown,"Требуется разбор события","",when,"","","Конфликт отдельных правил: "+string.Join("; ",matches.Take(5).Select(e=>e.Title))){MatchedAdditionalRule=true};
            if(matches.Length==1){
                var e=matches[0];
                return new Analysis(provider,e.Result,e.Severity,e.Title,"",when,"","","Отдельное правило: "+e.Title+(e.NumericEvidence.Length==0?"":" · "+e.NumericEvidence),e.Advice){MatchedAdditionalRule=true};
            }
        }
        if(specific is {EventRulesJson.Length:>0}){
            var matches=MailSources.Match(specific,mail);
            var when=mail.ImapReceivedAt??mail.MessageDate;
            if(matches.Length!=1)return new(provider,BackupResult.Unknown,Severity.Unknown,"Требуется разбор события","",when,"","",
                matches.Length==0?"Источник распознан, но ни одно правило события не подошло":"Конфликт правил события: "+string.Join("; ",matches.Take(5).Select(x=>x.Title)));
            var e=matches[0];
            return new(provider,e.Result,e.Severity,e.Title,"",when,"","","Правило источника: "+e.Title+(e.NumericEvidence.Length==0?"":" · "+e.NumericEvidence),e.Advice);
        }
        var resultProfile = specific ?? common;
        var levelProfile = specific != null && !string.IsNullOrWhiteSpace(specific.Critical + specific.Warning + specific.Info) ? specific : common;
        var statuses = Labels(mail.Body, "job status", "task status", "overall status", "backup status", "overall result", "result", "status", "результат", "статус задания", "статус");
        var subject = mail.Subject.ToLowerInvariant();
        bool HasResult(string terms) => Terms(terms).Any(term => statuses.Any(status =>
            string.Equals(status, term, StringComparison.OrdinalIgnoreCase) || status.StartsWith(term + " (", StringComparison.OrdinalIgnoreCase))
            || (resultProfile.ResultScope != SearchScope.Body && Pattern(term).IsMatch(Mask(mail.Subject, resultProfile.Ignore)))
            || (resultProfile.ResultScope != SearchScope.Subject && Pattern(term).IsMatch(Mask(mail.Body, resultProfile.Ignore))));
        var result = BackupResult.Unknown;
        {
            if (HasResult(resultProfile.FailedResults) || ContainsAny(subject, "backup failed", "job failed", "backup task failed", "failed to complete") || Labels(mail.Body, "failed tasks", "failed jobs", "failed objects", "failed machines", "errors", "error count", "failures", "ошибок", "ошибки", "сбоев").Any(Positive)) result = BackupResult.Failed;
            else if (HasResult(resultProfile.WarningResults) || ContainsAny(subject, "completed with warning", "completed with errors", "partially completed") || Labels(mail.Body, "warnings", "warning tasks", "warning count", "предупреждений").Any(Positive)) result = BackupResult.Warning;
            else if (HasResult(resultProfile.SuccessResults) || ContainsAny(subject, "backup completed successfully", "backup task completed successfully") || (provider == "QNAP HBS" && subject.Contains("completed successfully"))) result = BackupResult.Success;
            if (provider is "DSM" or "QNAP" && (!isBackupReport || result==BackupResult.Unknown)) result = BackupResult.Event;
        }
        var fields = levelProfile.Scope switch
        {
            SearchScope.Subject => new[] { ("тема", mail.Subject) },
            SearchScope.Body => new[] { ("тело", mail.Body) },
            _ => new[] { ("тема", mail.Subject), ("тело", mail.Body) }
        };
        var masked = fields.Select(f => (f.Item1, Text: Mask(f.Item2, levelProfile.Ignore))).ToArray();
        var severity = Severity.Unknown;
        var evidence = "Нет совпадений";
        foreach (var (level, terms) in new[] { (Severity.Critical, levelProfile.Critical), (Severity.Warning, levelProfile.Warning), (Severity.Info, levelProfile.Info) })
        {
            var matches = masked.SelectMany(f =>
            {
                var text = level == Severity.Critical && result != BackupResult.Failed ? Regex.Replace(f.Text, @"(?im)^\s*(?:(?:\d{1,4}[./-]\d{1,2}[./-]\d{1,4}\s+)?\d{1,2}:\d{2}:\d{2}\s+)?Warning\b[^\r\n]*", "", RegexOptions.CultureInvariant, RegexTimeout) : f.Text;
                return Terms(terms).Where(t => Pattern(t).IsMatch(text)).Select(t => $"{f.Item1}: «{t}»");
            }).Distinct().Take(8).ToArray();
            if (matches.Length == 0) continue;
            severity = level; evidence = string.Join("; ", matches); break;
        }
        if (result == BackupResult.Failed && (int)severity < (int)Severity.Critical) { severity = Severity.Critical; evidence = "Явная ошибка результата"; }
        if (result == BackupResult.Warning && (int)severity < (int)Severity.Warning) { severity = Severity.Warning; evidence = "Явное предупреждение результата"; }
        if (result == BackupResult.Success && severity == Severity.Unknown) { severity = Severity.Info; evidence = "Явный успешный результат"; }
        var task = Label(mail.Body, "job name", "job", "backup job", "task name", "task", "задание", "имя задания", "задача", "backup task");
        // A subject containing a date is not a stable job identity. Unknown task names remain explicit.
        var host = Label(mail.Body, "server", "server name", "host", "hostname", "nas", "nas name", "computer", "computer name", "device name", "сервер", "устройство", "имя сервера", "имя устройства").Trim('"');
        if (provider == "Active Backup for Business")
        {
            var summary = Regex.Match(mail.Body, @"(?im)^\s*Задача резервного копирования\s+(?<task>.+?)(?:\s+на\s+(?<host>.+?))?\s+(?:не\s+)?выполнена(?:\s+частично)?[.!]?\s*$", RegexOptions.CultureInvariant, RegexTimeout);
            if (summary.Success) { if (task.Length == 0) task = summary.Groups["task"].Value.Trim(); if (host.Length == 0) host = summary.Groups["host"].Value.Trim(); }
            if (host.Length == 0) host = Regex.Match(mail.Body, @"(?im)^\s*Из\s+([^\r\n]+)\s*$", RegexOptions.CultureInvariant, RegexTimeout).Groups[1].Value.Trim();
        }
        if (provider == "Veeam" && host.Length == 0)
        {
            var configuration = Regex.Match(mail.Body, @"(?im)^\s*Configuration Backup for\s+([^\r\n]+)", RegexOptions.CultureInvariant, RegexTimeout);
            if (configuration.Success) { host = configuration.Groups[1].Value.Trim(); if (task.Length == 0) task = "Configuration Backup"; }
        }
        var finish = Label(mail.Body, "end time", "finished at", "finish time", "время окончания", "завершение");
        var date = TimeSpan.TryParse(finish, CultureInfo.InvariantCulture, out var clock) && clock >= TimeSpan.Zero && clock < TimeSpan.FromDays(1) && mail.MessageDate != null
            ? mail.MessageDate.Value.Date.Add(clock) : ParseDate(finish);
        date ??= ParseDate(Label(mail.Body, "date/time", "дата/время")) ?? (provider == "SQLBackupAndFTP" ? ParseDate(Label(mail.Body, "date")) : null);
        var duration = Label(mail.Body, "duration", "total duration", "длительность", "продолжительность");
        if (provider == "SQLBackupAndFTP")
        {
            var start = ParseDate(Label(mail.Body, "date"));
            var ended = Regex.Match(mail.Body, @"(?im)^\s*(?<clock>\d{1,2}:\d{2}:\d{2})\s*:\s*Job\s+""[^""\r\n]+""\s+finished[.]?\s*$", RegexOptions.CultureInvariant, RegexTimeout);
            if (start != null && ended.Success && TimeSpan.TryParse(ended.Groups["clock"].Value, CultureInfo.InvariantCulture, out var endClock))
            {
                var end = start.Value.Date.Add(endClock); if (end < start.Value) end = end.AddDays(1);
                // The mail receipt date disambiguates jobs running longer than one day.
                if (mail.MessageDate != null && mail.MessageDate.Value.Date > end.Date) end = mail.MessageDate.Value.Date.Add(endClock);
                if (end >= start.Value) { date = end; if (duration.Length == 0) duration = (end - start.Value).ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture); }
            }
        }
        if(date==null) {
            date=mail.ImapReceivedAt??mail.MessageDate;
            if(date!=null)evidence+="; время: "+(mail.ImapReceivedAt!=null?"поступление IMAP (в отчёте нет времени)":"заголовок письма (в отчёте нет времени)");
        }
        var description = Label(mail.Body, "details", "error message", "message", "error", "описание", "ошибка", "сообщение");
        if (provider == "SQLBackupAndFTP") description = string.Join("\n", new[] { description, Summary("Тип резервной копии", Label(mail.Body, "backup type")), Summary("Количество баз", Label(mail.Body, "database count")) }.Where(x => x.Length > 0));
        if (provider == "Active Backup for Business") description = string.Join("\n", new[] { description, Summary("Устройства в задании", Label(mail.Body, "список устройств")) }.Where(x => x.Length > 0));
        return new(provider, result, severity, task.Length == 0 ? "Без имени задания" : task, host, date,
            duration,
            Label(mail.Body, "backup size", "backup archive size", "size", "transferred", "размер", "передано", "переданный размер"), evidence,
            description);
    }
    private static string Summary(string label, string value) => value.Length == 0 ? "" : label + ": " + value;
    public static IEnumerable<string> Terms(string value) => value.Split([';', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase);
    private static Regex Pattern(string term)
    {
        if (Patterns.Count > 2048) Patterns.Clear();
        return Patterns.GetOrAdd(term, value =>
        {
            var prefix = value.EndsWith('*') && value.Length > 1;
            var normalized = Regex.Replace(prefix ? value[..^1] : value, @"\s*([:=])\s*", "$1", RegexOptions.None, RegexTimeout);
            var escaped = Regex.Escape(normalized).Replace("\\ ", @"\s+").Replace(":", @"\s*:\s*").Replace("=", @"\s*=\s*");
            return new Regex(@"(?<![\p{L}\p{N}_])" + escaped + (prefix ? @"[\p{L}\p{N}_]*" : @"(?![\p{L}\p{N}_])"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        });
    }
    private static string Mask(string text, string ignores)
    {
        // Veeam HTML tables are flattened as label | value | next label. Mask only the zero cell.
        text = Regex.Replace(text, @"(?im)(?<![\p{L}\p{N}_])(errors?|error count|failures?|failed (tasks|jobs|objects|machines)|warnings?|warning count|warning tasks|skipped|skipped tasks|skipped files|ошибок|ошибки|сбоев|предупреждений)[ \t]*(?:[:=|][ \t]*|\r?\n[ \t]*)0+(?=[ \t]*(?:\||\r?$))", m => new string(' ',m.Length), RegexOptions.CultureInvariant, RegexTimeout);
        // Remove complete zero-counter lines before phrase masking can erase the numeric value.
        text = string.Join("\n", text.Replace("\r", "").Split('\n').Where(line => !Regex.IsMatch(line,
            @"^\s*(errors?|error count|failures?|failed (tasks|jobs|objects|machines)|warnings?|warning count|warning tasks|skipped|skipped tasks|skipped files|ошибок|ошибки|сбоев|предупреждений)\s*[:=]\s*0+\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout)));
        foreach (var ignore in Terms(ignores).OrderByDescending(x => x.Length)) text = Pattern(ignore).Replace(text, m => new string(' ', m.Length));
        // Counter zero is informational; a positive or nonnumeric counter must remain visible.
        text = Regex.Replace(text, @"(?im)^\s*(errors?|error count|failures?|failed (tasks|jobs|objects|machines)|warnings?|warning count|warning tasks|skipped|skipped tasks|skipped files|ошибок|ошибки|сбоев|предупреждений)\s*[:=]\s*0+\s*$", "", RegexOptions.CultureInvariant, RegexTimeout);
        return text;
    }
    private static bool ContainsAny(string text, params string[] terms) => terms.Any(text.Contains);
    private static bool Positive(string value) => Regex.IsMatch(value, @"^\s*0*[1-9]\d*(?:\s|$)", RegexOptions.None, RegexTimeout);
    public static string Label(string body, params string[] labels) => Labels(body, labels).FirstOrDefault() ?? "";
    private static IEnumerable<string> Labels(string body, params string[] labels)
    {
        var lines = body.Replace('\r', ' ').Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (labels.Any(label => string.Equals(line.TrimEnd(':', '=').Trim(), label, StringComparison.OrdinalIgnoreCase)))
            { if (i + 1 < lines.Length) yield return lines[i + 1].Trim(); continue; }
            var separator = line.IndexOf(':'); if (separator < 0) separator = line.IndexOf('=');
            if (separator < 0) continue;
            var key = line[..separator].Trim();
            if (labels.Any(label => string.Equals(key, label, StringComparison.OrdinalIgnoreCase))) yield return line[(separator + 1)..].Trim();
        }
    }
    public static DateTime? ParseDate(string value)
    {
        if(!Regex.IsMatch(value,@"(?:Z|[+-]\d{2}:?\d{2})\s*$",RegexOptions.IgnoreCase)){
            if(DateTime.TryParse(value,CultureInfo.GetCultureInfo("ru-RU"),DateTimeStyles.AllowWhiteSpaces,out var wall)||DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces,out wall))return DateTime.SpecifyKind(wall,DateTimeKind.Unspecified);return null;
        }
        if (DateTimeOffset.TryParse(value, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.AllowWhiteSpaces, out var date)
            || DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) return AppTime.FromOffset(date);
        return null;
    }
}
public static class RuleMatcher
{
    public static string[] BodyLines(string text) => text.Replace("\r","").Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static BindingRule? Match(MailReport mail, string provider, IEnumerable<BindingRule> rules, out string issue,bool requireUniqueTarget=false)
    {
        issue = "Требуется ручная привязка";
        var candidates = rules.Where(r => r.Active && (r.Provider == "Любой" || r.Provider == provider) && Matches(r, mail)).OrderBy(r => r.Priority).ToArray();
        if (candidates.Length == 0) return null;
        var first = candidates[0];
        if (candidates.Where(r => requireUniqueTarget || r.Priority == first.Priority).Any(r => r.TargetKind != first.TargetKind || r.TargetId != first.TargetId || r.ResultOverride != first.ResultOverride || r.MailCategory!=first.MailCategory || !r.UpdateProduct.Equals(first.UpdateProduct,StringComparison.OrdinalIgnoreCase)))
        { issue = requireUniqueTarget?"Конфликт правил привязки":"Конфликт правил одинакового приоритета"; return null; }
        issue = ""; return first;
    }
    public static bool Matches(BindingRule rule, MailReport mail)
    {
        var checks = new List<bool>();
        Add(rule.SenderPhrase, mail.Sender);
        var subjects=BodyLines(rule.SubjectPhrase);
        if(subjects.Length>0)checks.Add(rule.SubjectMatchAll?subjects.All(line=>mail.Subject.Contains(line,StringComparison.OrdinalIgnoreCase)):subjects.Any(line=>mail.Subject.Contains(line,StringComparison.OrdinalIgnoreCase)));
        var lines=BodyLines(rule.BodyPhrase);
        if(lines.Length>0)checks.Add(rule.BodyMatchAll?lines.All(line=>mail.Body.Contains(line,StringComparison.OrdinalIgnoreCase)):lines.Any(line=>mail.Body.Contains(line,StringComparison.OrdinalIgnoreCase)));
        return checks.Count > 0 && (rule.MatchAll ? checks.All(x => x) : checks.Any(x => x));
        void Add(string phrase, string text) { if (!string.IsNullOrWhiteSpace(phrase)) checks.Add(text.Contains(phrase, StringComparison.OrdinalIgnoreCase)); }
    }
}
