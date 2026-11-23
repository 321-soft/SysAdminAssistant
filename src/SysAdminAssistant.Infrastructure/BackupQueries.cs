using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public static class BackupQueries
{
    public static IEnumerable<BackupRunRow> RunRows(SqlBackupJob job,IEnumerable<ReportEvent> reports,DateTime now,bool canRead=true)
    {
        var events=reports.ToArray();var slots=BackupSchedule.Deadlines(job);var schedules=BackupSchedule.EvaluateSlots(job,events,now);
        for(int n=0;n<slots.Length;n++) {
            var denied=new BackupLamp(job.Title,LampState.Gray,"Нет доступа к отчётам",null,job.DeviceId,job.Id);
            yield return new(job,slots[n],canRead?BackupSchedule.LatestForRun(job,events,slots[n],now):denied,canRead?schedules[n]:denied);
        }
    }
    public static async Task<IReadOnlyList<BackupLamp>> LampsAsync(AssistantDb db, DateTime now, CancellationToken token = default,bool display=false)
    {
        var jobs = await db.Jobs.AsNoTracking().OrderBy(x => x.Title).ToArrayAsync(token);
        var lower = now.Date.AddDays(-9);
        var recent = await db.Reports.AsNoTracking().Where(x => x.JobId != null && (x.OccurredAt ?? x.ReceivedAt) >= lower).ToListAsync(token);
        foreach (var job in jobs)
        {
            var last = await db.Reports.AsNoTracking().Where(x => x.JobId == job.Id).OrderByDescending(x => x.OccurredAt ?? x.ReceivedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(token);
            if (last != null && recent.All(x => x.Id != last.Id)) recent.Add(last);
        }
        if(display)return jobs.SelectMany(j=>RunRows(j,recent,now)).Select(r=>r.Schedule with{State=r.DisplayState}).ToList();
        var lamps = jobs.SelectMany(j => BackupSchedule.EvaluateSlots(j, recent, now)).ToList();
        return lamps;
    }
}
