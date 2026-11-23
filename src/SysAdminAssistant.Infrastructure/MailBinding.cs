using System.Text;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record MailBindRequest(int EventId,TargetKind TargetKind,int TargetId,string BodyLines,bool AllLines,bool CreateRule,bool ApplyExisting,bool ReplaceExisting,int? ScheduledSlotMinutes,string ExpectedHash,string PreviewToken="",string SubjectLines="",bool SubjectMatchAll=true,bool MatchFieldsAll=true,string MailCategory="Report",string UpdateProduct="");
public sealed record MailBindSample(int Id,string Sender,string Subject,DateTime ReceivedAt,string Target);
public sealed record MailBindPreview(int Matched,int Applicable,int OtherBindings,int MissingText,int[] ConflictingRules,MailBindSample[] Samples,string Token);
public sealed record MailBindResult(int Bound,int? RuleId,int NeedsReview=0);
public static class MailBinding
{
    public static async Task<MailBindResult> ReanalyzeJobAsync(DatabaseFactory factory,int jobId,CancellationToken token=default,IReadOnlyList<CustomDiagnostic>? custom=null)
        => await ReanalyzeTargetAsync(factory,jobId,null,token,custom);
    public static async Task<MailBindResult> ReanalyzeDeviceAsync(DatabaseFactory factory,int deviceId,CancellationToken token=default,IReadOnlyList<CustomDiagnostic>? custom=null)
        => await ReanalyzeTargetAsync(factory,null,deviceId,token,custom);
    private static async Task<MailBindResult> ReanalyzeTargetAsync(DatabaseFactory factory,int? jobId,int? deviceId,CancellationToken token,IReadOnlyList<CustomDiagnostic>? custom)
    {
        await using var db=factory.Create();await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        if(jobId!=null && !await db.Jobs.AnyAsync(j=>j.Id==jobId,token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Задание не найдено"));
        if(deviceId!=null && !await db.Devices.AnyAsync(d=>d.Id==deviceId,token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Устройство не найдено"));
        var reports=await db.Reports.Where(e=>(jobId!=null?e.JobId==jobId:e.DeviceId==deviceId)&&e.Provider!="DMARC").ToArrayAsync(token);
        var store=new ReportStore(factory,custom);int needsReview=0;
        foreach(var e in reports) {
            await store.ReanalyzeBoundAsync(db,e,token);
            if(e.IsPending)needsReview++;
            else {var pending=await db.Pending.SingleOrDefaultAsync(p=>p.EventId==e.Id,token);if(pending!=null)db.Pending.Remove(pending);}
            var incidents=await db.Incidents.Where(i=>i.ReportId==e.Id).ToArrayAsync(token);
            foreach(var i in incidents){if(e.MailCategory=="Update")i.Closed=true;i.Source=e.Provider+" / "+e.TargetName;i.Severity=e.Severity;i.Title=e.Task;i.Details=e.Description;}
            if(e.MailCategory!="Update"&&(!e.IsPending || e.DeviceId!=null && e.Severity is Severity.Warning or Severity.Critical)&&incidents.Length==0)db.Incidents.Add(new Incident{ReportId=e.Id,CreatedAt=e.ReceivedAt,Severity=e.Severity,Source=e.Provider+" / "+e.TargetName,Title=e.Task,Details=e.Description});
        }
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return new(reports.Length,null,needsReview);
    }
    public static void RequireAccess(UserView actor,MailBindRequest request)
    {
        AccessRules.Require(actor,"reports",2);
        if(request.TargetKind==TargetKind.SqlJob)AccessRules.Require(actor,"jobs");
        else if(AccessRules.Level(actor,"devices")<1&&AccessRules.Level(actor,"routers")<1)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Нет доступа к целевому устройству"));
    }
    private sealed record Scan(MailBindPreview Preview,ReportEvent Seed,BindingRule Rule,List<int> Applicable,string TargetName,int? DeviceId);
    private static bool SameTarget(ReportEvent e,MailBindRequest r) => r.TargetKind==TargetKind.SqlJob?e.JobId==r.TargetId:e.JobId==null&&e.DeviceId==r.TargetId&&e.MailCategory==r.MailCategory;
    private static async Task<Scan> ScanAsync(AssistantDb db,MailBindRequest request,CancellationToken token)
    {
        if(request.MailCategory is not("Report" or "Update")||request.UpdateProduct==null||request.UpdateProduct.Length>100||request.UpdateProduct.Any(char.IsControl)||request.MailCategory=="Update"&&(request.TargetKind!=TargetKind.Device||string.IsNullOrWhiteSpace(request.UpdateProduct)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Обновление привязывается только к устройству; укажите продукт"));
        if(request.SubjectLines==null||request.SubjectLines.Length>10000||RuleMatcher.BodyLines(request.SubjectLines).Length>32||request.BodyLines==null||!Enum.IsDefined(request.TargetKind)||request.TargetId<1||request.BodyLines.Length>10000||RuleMatcher.BodyLines(request.BodyLines).Length>32)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверная цель или более 32 строк тела"));
        var seed=await db.Reports.AsNoTracking().SingleAsync(e=>e.Id==request.EventId,token);
        if(seed.MailCategory=="Update"&&request.MailCategory!="Update")throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Уведомление об обновлении нельзя использовать как отчёт бэкапа"));
        if(seed.Provider=="DMARC")throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("DMARC не привязывается к заданию бэкапа"));
        if(request.ExpectedHash!=RemoteData.EntityStamp(seed))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Письмо изменилось. Обновите список и повторите привязку."));
        string name;int? deviceId;string targetStamp;
        if(request.TargetKind==TargetKind.Device) {
            var target=await db.Devices.AsNoTracking().SingleAsync(d=>d.Id==request.TargetId&&d.Enabled,token);name=target.Name;deviceId=target.Id;targetStamp=RemoteData.EntityStamp(target);
            if(request.ScheduledSlotMinutes!=null)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Запуск выбирается только для задания"));
            if(seed.Provider=="SQLBackupAndFTP"&&request.MailCategory!="Update")throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Для SQLBackupAndFTP выберите задание бэкапа"));
        } else {
            var job=await db.Jobs.AsNoTracking().SingleAsync(j=>j.Id==request.TargetId,token);name=job.Title;deviceId=job.DeviceId;targetStamp=RemoteData.EntityStamp(job);
            if(request.ScheduledSlotMinutes is int slot&&!BackupSchedule.Deadlines(job).Contains(slot))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выбранного времени нет в расписании задания"));
        }
        var rule=new BindingRule {MailCategory=request.MailCategory,UpdateProduct=request.UpdateProduct,Provider="Любой",BodyPhrase=string.Join("\n",RuleMatcher.BodyLines(request.BodyLines)),BodyMatchAll=request.AllLines,SubjectPhrase=string.Join("\n",RuleMatcher.BodyLines(request.SubjectLines)),SubjectMatchAll=request.SubjectMatchAll,MatchAll=request.MatchFieldsAll,TargetKind=request.TargetKind,TargetId=request.TargetId};
        if(request.CreateRule&&rule.BodyPhrase.Length==0&&rule.SubjectPhrase.Length==0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Добавьте хотя бы одно условие по теме или телу"));
        var rules=await db.Rules.AsNoTracking().Where(r=>r.Active).ToArrayAsync(token);
        var samples=new List<MailBindSample>();var applicable=new List<int>();var conflicting=new HashSet<int>();
        int matched=0,other=0,missing=0;bool seedMatches=!request.CreateRule;
        using var hash=System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        void Stamp(string text)=>hash.AppendData(Encoding.UTF8.GetBytes(text+"\0"));
        Stamp(RemoteData.Hash(request with {PreviewToken=""}));Stamp(targetStamp);Stamp(RemoteData.Hash(rules.OrderBy(r=>r.Id).ToArray()));
        var sourceQuery=from e in db.Reports.AsNoTracking()
            join s in db.MailSources.AsNoTracking() on e.Id equals s.EventId into archive
            from s in archive.DefaultIfEmpty()
            join p in db.Pending.AsNoTracking() on e.Id equals p.EventId into pending
            from p in pending.DefaultIfEmpty()
            where e.Provider!="DMARC" && (request.CreateRule||e.Id==request.EventId)
            orderby e.Id
            select new {Report=e,Sender=s!=null?s.Sender:p!=null?p.Sender:"",Subject=s!=null?s.Subject:p!=null?p.Subject:"",
                Body=s!=null&&s.Body!=""?s.Body:p!=null?p.Body:""};
        await foreach(var item in sourceQuery.AsAsyncEnumerable().WithCancellation(token)) {
            var e=item.Report;
            var mail=new MailReport("","",item.Sender,item.Subject,item.Body,null);
            if(request.CreateRule&&((rule.Provider!="Любой"&&rule.Provider!=e.Provider)||!RuleMatcher.Matches(rule,mail))) {if(item.Body.Length==0&&rule.BodyPhrase.Length>0)missing++;continue;}
            if(e.Id==seed.Id)seedMatches=true;
            matched++;Stamp(RemoteData.EntityStamp(e));Stamp(ReportStore.Digest(item.Sender+"\0"+item.Subject+"\0"+item.Body));
            if((e.DeviceId!=null||e.JobId!=null)&&!SameTarget(e,request))other++;
            if(e.Id==seed.Id || request.ApplyExisting&&(request.ReplaceExisting||e.DeviceId==null&&e.JobId==null))applicable.Add(e.Id);
            if(samples.Count<30)samples.Add(new(e.Id,item.Sender,item.Subject,e.ReceivedAt,e.TargetName));
            if(request.CreateRule)foreach(var existing in rules.Where(r=>r.MailCategory==request.MailCategory&&(r.Provider=="Любой"||r.Provider==e.Provider)&&RuleMatcher.Matches(r,mail)))
                if(existing.TargetKind!=rule.TargetKind||existing.TargetId!=rule.TargetId||existing.ResultOverride!=null||request.MailCategory=="Update"&&!existing.UpdateProduct.Equals(rule.UpdateProduct,StringComparison.OrdinalIgnoreCase))conflicting.Add(existing.Id);
        }
        if(!seedMatches)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выбранное письмо не соответствует условиям темы и тела. Исправьте условия или выберите привязку только этого письма."));
        return new(new(matched,applicable.Count,other,missing,conflicting.OrderBy(x=>x).ToArray(),samples.ToArray(),Convert.ToHexString(hash.GetHashAndReset())),seed,rule,applicable,name,deviceId);
    }
    public static async Task<MailBindPreview> PreviewAsync(DatabaseFactory factory,MailBindRequest request,CancellationToken token=default)
    {
        await using var db=factory.Create();
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        var scan=await ScanAsync(db,request,token);await tx.CommitAsync(token);return scan.Preview;
    }
    public static async Task<MailBindResult> ApplyAsync(DatabaseFactory factory,MailBindRequest request,CancellationToken token=default,IReadOnlyList<CustomDiagnostic>? custom=null)
    {
        await using var db=factory.Create();await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        var scan=await ScanAsync(db,request,token);
        if(request.CreateRule) {
            if(scan.Preview.Token!=request.PreviewToken)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Совпадения или условия изменились. Покажите предварительный просмотр заново."));
            if(scan.Preview.ConflictingRules.Length>0)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Конфликт с правилами ")+string.Join(", ",scan.Preview.ConflictingRules)+global::SysAdminAssistant.Core.ProductText.Get(". Уточните или отключите прежние правила; автоматическая привязка не сохранена."));
            db.Rules.Add(scan.Rule);await db.SaveChangesAsync(token);
        }
        int needsReview=0;
        foreach(var id in scan.Applicable) {
            var e=await db.Reports.SingleAsync(x=>x.Id==id,token);
            e.MailCategory=request.MailCategory;e.UpdateProduct=request.UpdateProduct;
            e.DeviceId=scan.DeviceId;e.JobId=request.TargetKind==TargetKind.SqlJob?request.TargetId:null;
            e.TargetName=scan.TargetName;e.RuleId=request.CreateRule?scan.Rule.Id:null;e.BindingLocked=true;
            e.ScheduledSlotMinutes=id==request.EventId?request.ScheduledSlotMinutes:null;
            await new ReportStore(factory,custom).ReanalyzeBoundAsync(db,e,token);if(e.IsPending)needsReview++;
            if(!e.IsPending) {
                var pending=await db.Pending.SingleOrDefaultAsync(p=>p.EventId==id,token);if(pending!=null)db.Pending.Remove(pending);
            }
            // Existing incidents refer to the same report; update instead of duplicating.
            var incidents=await db.Incidents.Where(i=>i.ReportId==id).ToArrayAsync(token);
            foreach(var incident in incidents) { if(e.MailCategory=="Update")incident.Closed=true;incident.Source=e.Provider+" / "+e.TargetName; incident.Severity=e.Severity; incident.Title=e.Task; incident.Details=e.Description; }
            if(e.MailCategory!="Update"&&(!e.IsPending || e.DeviceId!=null && e.Severity is Severity.Warning or Severity.Critical)&&incidents.Length==0&&e.Severity!=Severity.Unknown)db.Incidents.Add(new Incident {ReportId=id,CreatedAt=e.ReceivedAt,Severity=e.Severity,Source=e.Provider+" / "+e.TargetName,Title=e.Task,Details=e.Description});
        }
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return new(scan.Applicable.Count,request.CreateRule?scan.Rule.Id:null,needsReview);
    }
}
