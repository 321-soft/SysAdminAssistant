using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Infrastructure;

public sealed class LegacyImporter(DatabaseFactory factory)
{
    public async Task<string> ImportAsync(string path, CancellationToken token = default)
    {
        // Hash the offline source copy before SQLite opens its native file handle.
        string digest;
        await using (var hashStream = File.OpenRead(path))
            digest = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, token));
        // A one-shot import must release the source even on cancellation or failure.
        // Source is read-only. Schema and content are never rewritten.
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await source.OpenAsync(token);
        using var readTransaction = source.BeginTransaction();
        await using var db = factory.Create();
        if (await db.LegacyImports.AnyAsync(x => x.Digest == digest, token)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Эта копия базы уже импортирована"));
        if (await db.Assets.AnyAsync(token) || await db.Employees.AnyAsync(token) || await db.Acts.AnyAsync(token) || await db.Devices.AnyAsync(token) || await db.Reports.AnyAsync(token) || await db.Jobs.AnyAsync(token) || await db.Payments.AnyAsync(token) || await db.Services.AnyAsync(token) || await db.Incidents.AnyAsync(token) || await db.Credentials.AnyAsync(token) || await db.Rules.AnyAsync(token))
            throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Импорт HiAsm выполняется в пустую базу, чтобы избежать смешивания и дублей. Выберите новую базу в настройках."));
        var devices = new Dictionary<int, int>(); var jobs = new Dictionary<int, int>(); var reports = new Dictionary<int, int>();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        foreach (var r in await RowsAsync("devices"))
        {
            var d = new Device { Name = S(r, "name"), Host = S(r, "host"), Site = S(r, "site"), Type = S(r, "type"), Role = S(r, "role"), Status = S(r, "status"), Enabled = I(r, "enabled") != 0, LastSeen = D(r, "last_seen"), LastError = S(r, "last_error") };
            db.Devices.Add(d); await db.SaveChangesAsync(token); devices[I(r, "id")] = d.Id;
        }
        foreach (var r in await RowsAsync("sa_services")) db.Services.Add(new ServiceEntry { Name = S(r, "name"), Type = S(r, "type"), Address = S(r, "url"), Status = S(r, "status"), Notes = S(r, "notes") });
        foreach (var r in await RowsAsync("payments")) db.Payments.Add(new Payment { Title = S(r, "title"), DueDate = D(r, "due_date") ?? AppTime.Today, Paid = S(r, "status") is "PAID" or "Оплачен", Notes = S(r, "notes"), AnchorDay = (D(r, "repeat_anchor") ?? D(r, "due_date"))?.Day ?? 0,
            Repeat = S(r, "repeat_period") switch { "Еженедельно" => RepeatPeriod.Weekly, "Ежемесячно" => RepeatPeriod.Monthly, "Ежегодно" => RepeatPeriod.Yearly, _ => RepeatPeriod.None } });
        foreach (var r in await RowsAsync("sa_sql_jobs"))
        {
            var job = new SqlBackupJob { Title = S(r, "title"), Host = S(r, "host"), Instance = S(r, "instance"), DeviceId = Map(devices, I(r, "device_id")), Days = I(r, "days"), Active = I(r, "active") != 0, GraceMinutes = I(r, "grace"), DeadlineMinutes = TimeSpan.TryParse(S(r, "deadline"), out var t) ? (int)t.TotalMinutes : 180, MonitoringSince = D(r, "created_at") ?? AppTime.Now };
            db.Jobs.Add(job); await db.SaveChangesAsync(token); jobs[I(r, "id")] = job.Id;
        }
        var levels = (await RowsAsync("sa_report_event_level")).ToDictionary(x => I(x, "event_id"));
        var pending = (await RowsAsync("sa_report_pending")).ToDictionary(x => I(x, "event_id"));
        foreach (var r in await RowsAsync("sa_report_events"))
        {
            var oldId = I(r, "id"); levels.TryGetValue(oldId, out var l);
            var e = new ReportEvent { DeviceId = Map(devices, I(r, "device_id")), JobId = Map(jobs, I(r, "job_id")), Provider = S(r, "provider"), Result = Result(S(r, "status")), Severity = Level(l == null ? "" : S(l, "severity")), Evidence = l == null ? "" : S(l, "evidence"), Task = S(r, "task"), Host = S(r, "host"), OccurredAt = D(r, "occurred_at"), ReceivedAt = D(r, "received_at") ?? AppTime.Now, Duration = S(r, "duration"), SizeText = S(r, "size_text"), Description = S(r, "description"), Issue = S(r, "issue"), IsPending = pending.ContainsKey(oldId) };
            db.Reports.Add(e); await db.SaveChangesAsync(token); reports[oldId] = e.Id;
            if (pending.TryGetValue(oldId, out var p)) db.Pending.Add(new PendingReport { EventId = e.Id, Sender = S(p, "sender"), Subject = S(p, "subject"), Body = S(p, "diagnostic"), MessageDate = D(p, "message_date") });
        }
        foreach (var r in await RowsAsync("sa_report_intake"))
        {
            var original = S(r, "message_key"); var eventId = Map(reports, I(r, "event_id"));
            if (eventId == null || original.Contains('/') || original.Length == 0) continue;
            db.LegacyMessageKeys.Add(new LegacyMessageKey { Digest = ReportStore.Digest(S(r, "account") + "\0" + original.Trim().Trim('<', '>')), EventId = eventId.Value });
        }
        foreach (var r in await RowsAsync("sa_monitor_rules"))
        {
            bool sql = S(r, "target_kind") == "SQL-задание"; var target = Map(sql ? jobs : devices, I(r, "target_id"));
            db.Rules.Add(new BindingRule { Provider = S(r, "vendor"), SenderPhrase = S(r, "sender_match"), SubjectPhrase = S(r, "subject_match"), BodyPhrase = S(r, "body_match"), MatchAll = S(r, "match_mode") != "Любое", TargetKind = sql ? TargetKind.SqlJob : TargetKind.Device, TargetId = target ?? 0, ResultOverride = S(r, "result") is "SUCCESS" or "WARNING" or "FAILED" or "INFO" ? Result(S(r, "result")) == BackupResult.Unknown ? BackupResult.Event : Result(S(r, "result")) : null, Active = target != null && I(r, "active") != 0, Priority = I(r, "priority") });
        }
        foreach (var r in await RowsAsync("events")) db.Incidents.Add(new Incident { CreatedAt = D(r, "created_at") ?? AppTime.Now, Severity = Level(S(r, "severity")), Source = S(r, "source"), Title = S(r, "title"), Details = S(r, "details"), Closed = S(r, "status") != "OPEN" });
        var profiles = await db.Profiles.ToArrayAsync(token);
        foreach (var r in await RowsAsync("sa_report_level_terms"))
        {
            var profile = profiles.FirstOrDefault(x => x.Provider == S(r, "provider")); if (profile == null) continue;
            profile.Critical = S(r, "critical"); profile.Warning = S(r, "warning"); profile.Info = S(r, "info"); profile.Ignore = S(r, "ignore"); profile.Scope = S(r, "scope") switch { "Тема" => SearchScope.Subject, "Тело" => SearchScope.Body, _ => SearchScope.Both };
        }
        foreach (var r in await RowsAsync("sa_report_terms"))
        {
            var profile = profiles.FirstOrDefault(x => x.Provider == S(r, "provider")); if (profile == null) continue;
            profile.SuccessResults = S(r, "success"); profile.WarningResults = S(r, "warning"); profile.FailedResults = S(r, "error");
        }
        db.LegacyImports.Add(new LegacyImport { Digest = digest }); await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        var vaultCount = (await RowsAsync("sa_vault_entries")).Count;
        return $"Импортировано устройств: {devices.Count}, SQL-заданий: {jobs.Count}, отчётов: {reports.Count}. Настройки подключений введите заново. Записей старого Vault: {vaultCount}; они остаются в HiAsm-базе (DPAPI), автоматический перенос секретов не выполнялся.";

        async Task<List<Dictionary<string, string>>> RowsAsync(string table)
        {
            await using var exists = source.CreateCommand(); exists.Transaction = readTransaction; exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$name"; exists.Parameters.AddWithValue("$name", table);
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(token)) == 0) return [];
            await using var cmd = source.CreateCommand(); cmd.Transaction = readTransaction; cmd.CommandText = "SELECT * FROM \"" + table + "\"";
            await using var reader = await cmd.ExecuteReaderAsync(token); var rows = new List<Dictionary<string, string>>();
            while (await reader.ReadAsync(token)) { var row = new Dictionary<string, string>(); for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i)) ?? ""; rows.Add(row); }
            return rows;
        }
    }
    private static string S(Dictionary<string, string> r, string field) => r.GetValueOrDefault(field, "");
    private static int I(Dictionary<string, string> r, string field) => int.TryParse(S(r, field), out var value) ? value : 0;
    private static DateTime? D(Dictionary<string, string> r, string field) => ReportAnalyzer.ParseDate(S(r, field));
    private static int? Map(Dictionary<int, int> map, int id) => map.TryGetValue(id, out var value) ? value : null;
    private static BackupResult Result(string text) => text switch { "SUCCESS" => BackupResult.Success, "FAILED" => BackupResult.Failed, "WARNING" => BackupResult.Warning, "EVENT" or "INFO" => BackupResult.Event, _ => BackupResult.Unknown };
    private static Severity Level(string text) => text switch { "CRITICAL" => Severity.Critical, "WARNING" => Severity.Warning, "INFO" => Severity.Info, _ => Severity.Unknown };
}
