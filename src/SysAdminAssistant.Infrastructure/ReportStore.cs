using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Infrastructure;

public sealed class ReportStore(DatabaseFactory factory, IReadOnlyList<CustomDiagnostic>? custom = null)
{
    public async Task<bool> IngestAsync(MailReport mail, CancellationToken token = default) =>
        await IngestMessageAsync([mail], mail.Account, mail.Key, token) > 0;
    public async Task<int> IngestMessageAsync(IReadOnlyList<MailReport> payloads, string account, string key, CancellationToken token = default)
    {
        if (factory.Remote is RemoteRepository remote)
        {
            var remoteCount = await remote.CommandAsync<int>("mail", new RemoteMail(payloads, account, key), token);
            await remote.RefreshAsync(token);
            return remoteCount;
        }
        var envelope = Digest(account + "\0" + key);
        await using var db = factory.Create();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        if (await db.Intake.AnyAsync(x => x.Digest == envelope, token) || await db.RetiredKeys.AnyAsync(x => x.Digest == envelope, token)) return 0;
        if (payloads.Count == 0) return 0;
        int count = 0;
        foreach (var mail in payloads) if (await IngestWithinAsync(db, mail, token)) count++;
        if (!await db.Intake.AnyAsync(x => x.Digest == envelope, token) && !await db.RetiredKeys.AnyAsync(x => x.Digest == envelope, token))
        {
            var payloadKeys = payloads.Select(x => Digest(x.Account + "\0" + x.Key)).ToArray();
            var eventId = await db.Intake.Where(x => payloadKeys.Contains(x.Digest)).Select(x => (int?)x.EventId).FirstOrDefaultAsync(token);
            if (eventId != null) db.Intake.Add(new IntakeKey { Digest = envelope, EventId = eventId.Value });
            else db.RetiredKeys.Add(new RetiredMailKey { Digest = envelope });
            await db.SaveChangesAsync(token);
        }
        await tx.CommitAsync(token); return count;
    }
    private async Task<bool> IngestWithinAsync(AssistantDb db, MailReport mail, CancellationToken token)
    {
        var digest = Digest(mail.Account + "\0" + mail.Key);
        if (await db.Intake.AnyAsync(x => x.Digest == digest, token) || await db.RetiredKeys.AnyAsync(x => x.Digest == digest, token)) return false;
        if (mail.MessageId.Length > 0)
        {
            var legacyDigest = Digest(mail.Account + "\0" + mail.MessageId.Trim().Trim('<', '>'));
            if (await db.RetiredKeys.AnyAsync(x => x.Digest == legacyDigest, token)) return false;
            var legacy = await db.LegacyMessageKeys.SingleOrDefaultAsync(x => x.Digest == legacyDigest, token);
            if (legacy != null)
            {
                db.Intake.Add(new IntakeKey { Digest = digest, EventId = legacy.EventId }); db.LegacyMessageKeys.Remove(legacy);
                await db.SaveChangesAsync(token); return false;
            }
        }
        string? dmarcDigest = null;
        if (mail.Body.StartsWith(DmarcParser.Marker, StringComparison.Ordinal))
        {
            try { dmarcDigest = Digest(DmarcParser.Parse(mail.Body[DmarcParser.Marker.Length..]).Identity); }
            catch (Exception e) when (e is System.Xml.XmlException or InvalidDataException or OverflowException) { }
            if (dmarcDigest != null)
            {
                if (await db.RetiredKeys.AnyAsync(x => x.Digest == dmarcDigest, token)) { db.RetiredKeys.Add(new RetiredMailKey { Digest = digest }); await db.SaveChangesAsync(token); return false; }
                var duplicate = await db.Intake.SingleOrDefaultAsync(x => x.Digest == dmarcDigest, token);
                if (duplicate != null) { db.Intake.Add(new IntakeKey { Digest = digest, EventId = duplicate.EventId }); await db.SaveChangesAsync(token); return false; }
            }
        }
        var report = await BuildAsync(db, mail, token);
        if(mail.ImapReceivedAt is DateTime receipt)report.ReceivedAt=receipt;
        db.Reports.Add(report);
        await db.SaveChangesAsync(token);
        await MailUpdateStore.SyncAsync(db,report,token);
        db.MailSources.Add(new MailSource {EventId=report.Id,Account=mail.Account,MessageKey=mail.Key,Sender=mail.Sender,Subject=mail.Subject,SentAt=mail.MessageDate,ImapReceivedAt=mail.ImapReceivedAt,Body=mail.Body});
        db.Intake.Add(new IntakeKey { Digest = digest, EventId = report.Id });
        if (dmarcDigest != null && dmarcDigest != digest) db.Intake.Add(new IntakeKey { Digest = dmarcDigest, EventId = report.Id });
        if (report.IsPending) db.Pending.Add(new PendingReport { EventId = report.Id, Sender = mail.Sender, Subject = mail.Subject, Body = mail.Body, MessageDate = mail.MessageDate });
        if(!report.IsPending || report.DeviceId!=null && report.Severity is Severity.Warning or Severity.Critical)AddIncident(db,report);
        await db.SaveChangesAsync(token);
        return true;
    }
    public async Task<int> ReprocessAsync(CancellationToken token = default, bool includeDmarc=true, bool includeReports=true, int[]? selectedReportIds=null)
    {
        if (factory.Remote is RemoteRepository remote) { var result = selectedReportIds==null ? await remote.CommandAsync<int>("reprocess",new{},token) : await remote.CommandAsync<int>("mail-reprocess-selected",new MailReprocessSelection(selectedReportIds),token); await remote.RefreshAsync(token); return result; }
        await using var listDb = factory.Create();
        var pendingQuery = listDb.Pending.AsQueryable();
        if(selectedReportIds!=null)pendingQuery=pendingQuery.Where(p=>selectedReportIds.Contains(p.EventId));
        var ids = await pendingQuery.Where(x=>listDb.Reports.Any(e=>e.Id==x.EventId && (e.Provider=="DMARC"?includeDmarc:includeReports))).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(token);
        int count = 0;
        foreach (var id in ids)
        {
            token.ThrowIfCancellationRequested();
            await using var db = factory.Create();
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
            var pending = await db.Pending.SingleOrDefaultAsync(x => x.Id == id, token);
            if (pending is null) continue;
            var previous = await db.Reports.SingleAsync(x => x.Id == pending.EventId, token);
            var mail = new MailReport("", "", pending.Sender, pending.Subject, pending.Body, pending.MessageDate, "",previous.ReceivedAt);
            var updated = await BuildAsync(db, mail, token, previous.BindingLocked ? previous : null);
            updated.Id = previous.Id; updated.ReceivedAt = previous.ReceivedAt;
            if(previous.BindingLocked) {
                updated.DeviceId=previous.DeviceId;updated.JobId=updated.MailCategory=="Update"?null:previous.JobId;updated.TargetName=previous.TargetName;updated.RuleId=previous.RuleId;
                updated.BindingLocked=true;updated.ScheduledSlotMinutes=previous.ScheduledSlotMinutes;
                // BuildAsync has already analyzed with the bound target and preserved validation issues.
            }
            db.Entry(previous).CurrentValues.SetValues(updated);
            if (!updated.IsPending) { db.Pending.Remove(pending); count++; }
            if(!updated.IsPending || updated.DeviceId!=null && updated.Severity is Severity.Warning or Severity.Critical)AddIncident(db,updated);
            await MailUpdateStore.SyncAsync(db,previous,token);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        return count;
    }
    public async Task<MailRecheckResult> RecheckMailAsync(int id,CancellationToken token=default)
    {
        await using var db=factory.Create();
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        var report=await db.Reports.SingleOrDefaultAsync(r=>r.Id==id&&r.Provider!="DMARC",token)
            ?? throw new ArgumentException("Письмо не найдено.");
        var source=await db.MailSources.AsNoTracking().SingleOrDefaultAsync(s=>s.EventId==id,token);
        var pending=await db.Pending.SingleOrDefaultAsync(p=>p.EventId==id,token);
        var body=!string.IsNullOrEmpty(source?.Body)?source.Body:pending?.Body;
        if(string.IsNullOrEmpty(body))return new(false,report.IsPending,report.Provider,"Исходный текст письма не сохранён. Результат не изменён.");
        var mail=new MailReport(source?.Account??"",source?.MessageKey??"",source?.Sender??pending!.Sender,source?.Subject??pending!.Subject,
            body,source?.SentAt??pending?.MessageDate,"",report.ReceivedAt);
        var updated=await BuildAsync(db,mail,token,report.BindingLocked?report:null);
        updated.Id=report.Id;updated.ReceivedAt=report.ReceivedAt;
        if(report.BindingLocked){
            updated.BindingLocked=true;updated.DeviceId=report.DeviceId;updated.JobId=updated.MailCategory=="Update"?null:report.JobId;
            updated.TargetName=report.TargetName;updated.RuleId=report.RuleId;updated.ScheduledSlotMinutes=report.ScheduledSlotMinutes;
        }
        db.Entry(report).CurrentValues.SetValues(updated);
        await MailUpdateStore.SyncAsync(db,report,token);
        if(report.IsPending&&pending==null)db.Pending.Add(new(){EventId=id,Sender=mail.Sender,Subject=mail.Subject,Body=mail.Body,MessageDate=mail.MessageDate});
        if(!report.IsPending&&pending!=null)db.Pending.Remove(pending);
        if(report.MailCategory!="Update"&&report.Severity!=Severity.Unknown&&(!report.IsPending||report.DeviceId!=null&&report.Severity is Severity.Warning or Severity.Critical))
            AddIncident(db,report);
        else if(await db.Incidents.SingleOrDefaultAsync(i=>i.ReportId==id,token) is {} oldIncident)
            oldIncident.Closed=true;
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return new(true,report.IsPending,report.Provider,report.Issue);
    }

    internal async Task ReanalyzeBoundAsync(AssistantDb db, ReportEvent report, CancellationToken token)
    {
        var source=await db.MailSources.AsNoTracking().SingleOrDefaultAsync(s=>s.EventId==report.Id,token);
        var pending=await db.Pending.SingleOrDefaultAsync(p=>p.EventId==report.Id,token);
        var body=!string.IsNullOrEmpty(source?.Body)?source.Body:pending?.Body;
        if(string.IsNullOrEmpty(body)&&!(report.MailCategory=="Update"&&(source!=null||pending!=null))) {
            report.Issue="Повторный анализ недоступен: исходный текст письма не сохранён";
            report.Evidence=report.Issue+"; прежний результат: "+report.Evidence;
            return;
        }
        var mail=new MailReport(source?.Account??"",source?.MessageKey??"",source?.Sender??pending!.Sender,source?.Subject??pending!.Subject,body??"",source?.SentAt??pending?.MessageDate,"",report.ReceivedAt);
        var updated=await BuildAsync(db,mail,token,report);
        updated.Id=report.Id;updated.ReceivedAt=report.ReceivedAt;updated.BindingLocked=report.BindingLocked;updated.ScheduledSlotMinutes=report.ScheduledSlotMinutes;
        updated.DeviceId=report.DeviceId;updated.JobId=updated.MailCategory=="Update"?null:report.JobId;updated.TargetName=report.TargetName;updated.RuleId=report.RuleId;
        db.Entry(report).CurrentValues.SetValues(updated);
        await MailUpdateStore.SyncAsync(db,report,token);
        if(report.IsPending && pending==null)db.Pending.Add(new PendingReport {EventId=report.Id,Sender=mail.Sender,Subject=mail.Subject,Body=mail.Body,MessageDate=mail.MessageDate});
    }
    private async Task<ReportEvent> BuildAsync(AssistantDb db, MailReport mail, CancellationToken token, ReportEvent? binding = null)
    {
        if (mail.Body.StartsWith(MailPayloadReader.ErrorMarker, StringComparison.Ordinal))
            return new ReportEvent { Provider = "Почта", Task = "Письмо не принято", Result = BackupResult.Unknown, Severity = Severity.Warning,
                Description = mail.Body[MailPayloadReader.ErrorMarker.Length..], Issue = "Проверка безопасности почты не пройдена; проверьте письмо вручную", IsPending = true, OccurredAt = mail.MessageDate };
        if (mail.Body.StartsWith(DmarcParser.ErrorMarker, StringComparison.Ordinal))
            return new ReportEvent { Provider = "DMARC", Task = "Ошибка чтения вложения", Result = BackupResult.Unknown, Severity = Severity.Warning,
                Description = "Вложение не принято: " + mail.Body[DmarcParser.ErrorMarker.Length..], Issue = "Повреждённое вложение или превышение лимита; проверьте источник", IsPending = true, OccurredAt = mail.MessageDate };
        if (mail.Body.StartsWith(DmarcParser.Marker, StringComparison.Ordinal))
        {
            try
            {
                var dmarc = DmarcParser.Parse(mail.Body[DmarcParser.Marker.Length..]);
                return new ReportEvent { Provider = "DMARC", Task = dmarc.Domain, Host = dmarc.Reporter, TargetName = dmarc.Domain,
                    OccurredAt = dmarc.EndUtc.ToServerTime(), Duration = $"{dmarc.BeginUtc:dd.MM.yyyy} — {dmarc.EndUtc:dd.MM.yyyy} UTC",
                    SizeText = $"{dmarc.Total} писем", Result = dmarc.Failed > 0 ? BackupResult.Warning : BackupResult.Success,
                    Severity = dmarc.Failed > 0 ? Severity.Warning : Severity.Info, Description = dmarc.Format(), Evidence = "Статистика DMARC из XML; данные получателя", IsPending = false };
            }
            catch (Exception e) when (e is System.Xml.XmlException or InvalidDataException or OverflowException)
            {
                return new ReportEvent { Provider = "DMARC", Task = "Повреждённый отчёт", Result = BackupResult.Unknown, Severity = Severity.Warning,
                    Description = "DMARC XML не принят: " + e.Message, Issue = "Ошибка формата DMARC; проверьте источник и вложение", IsPending = true, OccurredAt = mail.MessageDate };
            }
        }
        Analysis analysis;
        var profiles = await db.Profiles.AsNoTracking().ToArrayAsync(token);
        bool sourceConflict=false;
        try { sourceConflict=ReportAnalyzer.SourceConflict(mail,profiles); analysis = ReportAnalyzer.Analyze(mail, profiles); }
        catch (RegexMatchTimeoutException)
        {
            analysis = new(ReportAnalyzer.DetectProvider(mail.Subject, "", mail.Sender), BackupResult.Unknown, Severity.Unknown, "Требуется проверка отчёта", "", mail.MessageDate, "", "", "Превышен лимит анализа; сократите сложные или слишком длинные фразы словаря");
        }
        var rules = await db.Rules.AsNoTracking().ToArrayAsync(token);
        var customSource=analysis.MatchedAdditionalRule||profiles.Any(p=>p.Provider==analysis.Provider&&p.EventRulesJson.Length>0);
        if(binding?.MailCategory=="Update"||!customSource&&!sourceConflict){var updateNotice=await BuildUpdateAsync(db,mail,binding,profiles,token);if(updateNotice!=null)return updateNotice;}
        string issue="";
        var rule = binding != null ? rules.FirstOrDefault(r=>r.Id==binding.RuleId && r.Active) : sourceConflict?null:RuleMatcher.Match(mail, analysis.Provider, rules.Where(r=>r.MailCategory=="Report"), out issue,customSource);
        if(analysis.Provider!="Неизвестно"&&issue=="Требуется ручная привязка")issue="";
        int? deviceId = binding?.DeviceId, jobId = binding?.JobId; string targetName = binding?.TargetName ?? "";
        SqlBackupJob? boundJob = jobId is int boundId ? await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j=>j.Id==boundId,token) : null;
        if (rule != null && binding == null)
        {
            if (rule.TargetKind == TargetKind.Device)
            {
                var target = await db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rule.TargetId && x.Enabled, token);
                if (target == null) issue = "Цель правила удалена или архивирована";
                else { deviceId = target.Id; targetName = target.Name; }
            }
            else
            {
                var target = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rule.TargetId, token);
                if (target == null) issue = "Задание бэкапа удалено";
                else { jobId = target.Id; deviceId = target.DeviceId; targetName = target.Title; boundJob=target; }
            }
        }
        var selectedSystem=analysis.Provider=="Неизвестно"&&!sourceConflict?boundJob?.BackupSystem:null;
        if(profiles.Any(p=>p.Provider==selectedSystem&&p.EventRulesJson.Length>0))selectedSystem=null;
        if(!customSource&&!sourceConflict&&(!string.IsNullOrWhiteSpace(selectedSystem) || jobId!=null)) {
            try { analysis=ReportAnalyzer.Analyze(mail,profiles,selectedSystem,jobId!=null); }
            catch(RegexMatchTimeoutException) { analysis=analysis with {Result=BackupResult.Unknown,Severity=Severity.Unknown,Evidence="Превышен лимит анализа словаря выбранной системы"}; }
        }
        if(rule != null) {
            if ((!customSource||analysis.Result!=BackupResult.Unknown)&&rule.ResultOverride is BackupResult result)
            {
                var severity = result == BackupResult.Failed ? Severity.Critical : result == BackupResult.Warning && (int)analysis.Severity < (int)Severity.Warning ? Severity.Warning
                    : analysis.Severity == Severity.Unknown && result is BackupResult.Success or BackupResult.Event ? Severity.Info : analysis.Severity;
                analysis = analysis with { Result = result, Severity = severity, Evidence = analysis.Evidence + "; результат задан правилом" };
            }
        }
        if (issue.Length == 0 && deviceId != null && jobId == null && analysis.Result == BackupResult.Unknown && analysis.Severity != Severity.Unknown)
            analysis = analysis with { Result = BackupResult.Event };
        if (issue.Length == 0 && analysis.Result == BackupResult.Unknown) issue = customSource?analysis.Evidence:"Неизвестный результат; уточните словарь";
        if (issue.Length == 0 && analysis.Severity == Severity.Unknown) issue = "Неизвестный уровень события; уточните ключевые слова";
        if (issue.Length == 0 && jobId != null && analysis.Result == BackupResult.Event) issue = "Отчёт не содержит результата бэкапа";
        
        if (sourceConflict) issue = "Конфликт условий источников; уточните правила распознавания";
        var advice = DiagnosticGuide.Analyze(analysis.Provider, mail.Body, custom).Format(
            analysis.Result is BackupResult.Warning or BackupResult.Failed or BackupResult.Unknown || analysis.Severity is Severity.Warning or Severity.Critical);
        return new ReportEvent
        {
            DeviceId = deviceId, JobId = jobId, RuleId = rule?.Id, Provider = analysis.Provider, Result = analysis.Result,
            Severity = analysis.Severity, TargetName = targetName, Task = analysis.Task, Host = analysis.Host, OccurredAt = analysis.OccurredAt,
            Duration = analysis.Duration, SizeText = analysis.Size, Evidence = analysis.Evidence,
            Description = $"{Display.Result(analysis.Result)}; {analysis.Evidence}; {analysis.Details[..Math.Min(4000, analysis.Details.Length)]}\n\n{DiagnosticGuide.IssueEvidence(mail.Body)}\n\n{advice}", Issue = issue, IsPending = issue.Length > 0
        };
    }
    private static async Task<ReportEvent?> BuildUpdateAsync(AssistantDb db,MailReport mail,ReportEvent? binding,KeywordProfile[] profiles,CancellationToken token)
    {
        var updateRules=await db.Rules.AsNoTracking().Where(r=>r.Active&&r.MailCategory=="Update").ToArrayAsync(token);
        var templates=profiles.Where(p=>MailUpdates.Matches(p,mail)).ToArray();
        var matching=updateRules.Where(r=>(r.Provider=="Любой"||templates.Any(p=>p.Provider==r.Provider))&&RuleMatcher.Matches(r,mail)).ToArray();
        if(binding?.MailCategory!="Update"&&templates.Length==0&&matching.Length==0)return null;
        string issue="";var rule=binding?.MailCategory=="Update"?updateRules.FirstOrDefault(r=>r.Id==binding.RuleId):RuleMatcher.Match(mail,templates.FirstOrDefault()?.Provider??"Неизвестно",matching,out issue);
        string provider=templates.FirstOrDefault()?.Provider??"Неизвестно";string product=binding?.MailCategory=="Update"?binding.UpdateProduct:rule?.UpdateProduct??templates.FirstOrDefault()?.UpdateProduct??"Обновление";
        if(binding==null&&templates.Select(p=>p.Provider).Distinct().Count()>1){issue="Конфликт словарей обновлений; уточните условия";rule=null;}
        if(string.IsNullOrWhiteSpace(product))product=provider;
        var match=MailUpdates.Extract(mail,provider,product);int? id=binding?.MailCategory=="Update"?binding.DeviceId:rule?.TargetKind==TargetKind.Device?rule.TargetId:null;
        var device=id!=null?await db.Devices.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==id&&d.Enabled,token):null;
        if(device==null)issue=issue.Length>0?issue:"Привяжите уведомление об обновлении к устройству";
        else if(binding?.MailCategory=="Update"||rule!=null)issue="";
        return new(){MailCategory="Update",UpdateProduct=match.Product,UpdateVersion=match.Version,UpdateKey=match.Key,Provider=provider,DeviceId=device?.Id,RuleId=rule?.Id,TargetName=device?.Name??"",JobId=null,Result=BackupResult.Event,Severity=Severity.Info,Task="Доступно обновление "+match.Product+(match.Version!=""?" "+match.Version:" (версия не определена)"),OccurredAt=mail.MessageDate,Description="Уведомление об обновлении из письма. Установка не подтверждена. "+match.Issue,Evidence="Категория: обновления; "+match.Issue,IsPending=issue.Length>0,Issue=issue};
    }
    private static void AddIncident(AssistantDb db, ReportEvent report)
    {
        if (report.Severity == Severity.Unknown || report.MailCategory=="Update") return;
        var existing=db.Incidents.SingleOrDefault(i=>i.ReportId==report.Id);
        if(existing!=null){existing.Severity=report.Severity;existing.Source=report.Provider+" / "+report.TargetName;existing.Title=report.Task;existing.Details=report.Description;return;}
        db.Incidents.Add(new Incident { ReportId = report.Id, CreatedAt = report.ReceivedAt, Severity = report.Severity, Source = report.Provider + " / " + report.TargetName,
            Title = report.Task, Details = $"{report.Host}; {report.Description}; {report.Duration}; {report.SizeText}" });
    }
    public static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
