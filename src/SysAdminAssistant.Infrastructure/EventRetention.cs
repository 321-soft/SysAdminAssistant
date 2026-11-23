using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Infrastructure;

public sealed record RetentionCounts(int Reports, int Incidents);
public static class EventRetention
{
    public static void ValidateDays(int days) { if (days is not (0 or 30 or 90)) throw new ArgumentOutOfRangeException(nameof(days)); }
    public static async Task UpgradeAsync(AssistantDb db, CancellationToken token)
    {
        var script = db.Database.GenerateCreateScript();
        var statements = Regex.Matches(script, "CREATE TABLE [\\[\"]sa2_RetiredMailKey[\\]\"]\\s*\\([\\s\\S]*?\\);|CREATE (?:UNIQUE )?INDEX[^;]*?ON [\\[\"]sa2_RetiredMailKey[\\]\"][^;]*?;").Select(x => x.Value).ToArray();
        if (statements.Count(x => x.StartsWith("CREATE TABLE")) != 1) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось подготовить схему хранения событий"));
        foreach (var statement in statements) await db.Database.ExecuteSqlRawAsync(statement, token);
    }
    public static async Task<RetentionCounts> PreviewAsync(DatabaseFactory factory, int days, DateTime now, CancellationToken token = default)
    {
        if (factory.Remote is RemoteRepository remote) { var count = await remote.CommandAsync<RetentionCounts>("retention-preview", new RemoteRetention(days, true, remote.Snapshot.SettingsStamp), token); return count; }
        ValidateDays(days); if (days == 0) return new(0, 0);
        await using var db = factory.Create(); var cutoff = now.AddDays(-days);
        var old = db.Reports.Where(x => x.ReceivedAt < cutoff).Select(x => x.Id);
        return new(await old.CountAsync(token), await db.Incidents.CountAsync(x => x.CreatedAt < cutoff || (x.ReportId != null && old.Contains(x.ReportId.Value)), token));
    }
    public static async Task<RetentionCounts> ApplyAsync(DatabaseFactory factory, int days, DateTime now, CancellationToken token = default)
    {
        if (factory.Remote is RemoteRepository remote) { var count = await remote.CommandAsync<RetentionCounts>("retention-apply", new RemoteRetention(days, true, remote.Snapshot.SettingsStamp), token); await remote.RefreshAsync(token); return count; }
        ValidateDays(days); if (days == 0) return new(0, 0);
        await using var db = factory.Create(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var cutoff = now.AddDays(-days);
        var old = db.Reports.Where(x => x.ReceivedAt < cutoff).Select(x => x.Id);
        var keys = await db.Intake.Where(x => old.Contains(x.EventId)).Select(x => x.Digest)
            .Concat(db.LegacyMessageKeys.Where(x => old.Contains(x.EventId)).Select(x => x.Digest)).Distinct().ToArrayAsync(token);
        // Hash-only receipts prevent old IMAP messages from returning after deletion.
        foreach (var chunk in keys.Chunk(500))
        {
            var existing = await db.RetiredKeys.Where(x => chunk.Contains(x.Digest)).Select(x => x.Digest).ToArrayAsync(token);
            foreach (var key in chunk.Except(existing)) db.RetiredKeys.Add(new RetiredMailKey { Digest = key });
            await db.SaveChangesAsync(token);
        }
        var incidents = await db.Incidents.Where(x => x.CreatedAt < cutoff || (x.ReportId != null && old.Contains(x.ReportId.Value))).ExecuteDeleteAsync(token);
        await db.LegacyMessageKeys.Where(x => old.Contains(x.EventId)).ExecuteDeleteAsync(token);
        await db.MailSources.Where(x=>old.Contains(x.EventId)).ExecuteDeleteAsync(token);
        var reports = await db.Reports.Where(x => x.ReceivedAt < cutoff).ExecuteDeleteAsync(token);
        await tx.CommitAsync(token); return new(reports, incidents);
    }
}
