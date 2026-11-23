using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record ResolveDeviceProblem(int DeviceId,int IncidentId);
public sealed record DeviceProblem(Incident Incident,ReportEvent Report);
public static class DeviceProblems
{
    public static async Task<DeviceProblem[]> OpenAsync(AssistantDb db,int deviceId,CancellationToken token=default)
    {
        var rows=await (from i in db.Incidents.AsNoTracking() join r in db.Reports.AsNoTracking() on i.ReportId equals r.Id
                  where r.DeviceId==deviceId && r.Provider!="DMARC" && r.MailCategory!="Update" && !i.Closed && (i.Severity==Severity.Critical || i.Severity==Severity.Warning)
                  orderby i.Severity descending,i.CreatedAt descending,i.Id descending select new DeviceProblem(i,r)).ToArrayAsync(token);
        var jobIds=rows.Where(x=>x.Report.JobId!=null).Select(x=>x.Report.JobId!.Value).Distinct().ToArray();var jobs=await db.Jobs.AsNoTracking().Where(j=>jobIds.Contains(j.Id)).ToArrayAsync(token);
        return BackupIncidentLifecycle.Group(rows.Select(x=>x.Incident),rows.Select(x=>x.Report).DistinctBy(r=>r.Id),jobs).Select(g=>{var row=rows.First(x=>x.Incident.Id==g.Incident.Id);return new DeviceProblem(new Incident{Id=row.Incident.Id,ReportId=row.Incident.ReportId,CreatedAt=g.LastAt,Severity=row.Incident.Severity,Source=row.Incident.Source,Title=row.Incident.Title+(g.Count>1?" · повторов: "+g.Count:""),Details=g.Details,Closed=row.Incident.Closed},row.Report);}).ToArray();
    }
    public static async Task ResolveAsync(DatabaseFactory factory,ResolveDeviceProblem request,CancellationToken token=default)
    {
        await using var db=factory.Create();await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        var incident=await db.Incidents.SingleAsync(i=>i.Id==request.IncidentId,token);
        if(!await db.Reports.AnyAsync(r=>r.Id==incident.ReportId && r.DeviceId==request.DeviceId && r.Provider!="DMARC",token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Событие не относится к выбранному устройству"));
        var report=await db.Reports.SingleAsync(r=>r.Id==incident.ReportId,token);
        if(report.JobId is int jid && BackupIncidentLifecycle.ParsedBackup(report) && await db.Jobs.SingleOrDefaultAsync(j=>j.Id==jid,token) is {} job){var key=BackupIncidentLifecycle.Key(job,report);var related=await(from i in db.Incidents join r in db.Reports on i.ReportId equals r.Id where !i.Closed&&r.JobId==jid&&r.DeviceId==request.DeviceId select new{i,r}).ToArrayAsync(token);foreach(var pair in related)if(BackupIncidentLifecycle.ParsedBackup(pair.r)&&BackupIncidentLifecycle.Key(job,pair.r)==key)pair.i.Closed=true;}
        incident.Closed=true;await db.SaveChangesAsync(token);await tx.CommitAsync(token);
    }
}
