namespace SysAdminAssistant.Core;

public static class BackupSchedule
{
    public static int[] Deadlines(SqlBackupJob job)
    {
        if(job.EarlyMinutes is <0 or >1440)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Ранний допуск: 0–1440 минут"));
        if(job.CompletionTimes.Length>400)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Список времён слишком длинный"));
        if(string.IsNullOrWhiteSpace(job.CompletionTimes))return [job.DeadlineMinutes];
        var raw=job.CompletionTimes.Split([';',',','\n','\r'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(raw.Length is <1 or >24)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("От 1 до 24 запусков в сутки"));
        var times=raw.Select(value=>TimeSpan.TryParseExact(value,@"hh\:mm",System.Globalization.CultureInfo.InvariantCulture,out var time)?(int)time.TotalMinutes:throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Время запуска: ЧЧ:ММ"))).Distinct().OrderBy(x=>x).ToArray();
        if(times.Length>1)for(var i=0;i<times.Length;i++) {
            var next=i+1<times.Length?times[i+1]:times[0]+1440;
            if(job.EarlyMinutes is int early ? next-times[i]<job.GraceMinutes+early : next-times[i]<=job.GraceMinutes)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Окна допуска соседних запусков пересекаются; уменьши допуск"));
        }
        return times;
    }
    public static int ReportSlot(SqlBackupJob job,ReportEvent report)
    {
        var times=Deadlines(job);if(report.ScheduledSlotMinutes is int pinned)return pinned;
        var stamp=report.OccurredAt??report.ReceivedAt;
        var candidates=Enumerable.Range(-1,3).SelectMany(offset=>times.Select(t=>(Due:stamp.Date.AddDays(offset).AddMinutes(t),Slot:t)))
            .Where(c=>c.Due.AddMinutes(job.GraceMinutes)>=stamp).OrderBy(c=>c.Due);
        return candidates.First().Slot;
    }
    public static BackupLamp LatestForRun(SqlBackupJob job,IEnumerable<ReportEvent> reports,int slot,DateTime now)
    {
        var last=reports.Where(e=>e.JobId==job.Id && (e.OccurredAt??e.ReceivedAt)<=now && ReportSlot(job,e)==slot)
            .OrderByDescending(e=>e.OccurredAt??e.ReceivedAt).ThenByDescending(e=>e.Id).FirstOrDefault();
        if(last!=null && last.IsPending && last.Result is BackupResult.Unknown or BackupResult.Event && last.Severity is not (Severity.Critical or Severity.Warning))
            return new(job.Title,LampState.Gray,"Результат не распознан",last.OccurredAt??last.ReceivedAt,job.DeviceId,job.Id);
        return FromLatest(job.Title,last,job.DeviceId,job.Id);
    }
    public static IReadOnlyList<BackupLamp> EvaluateSlots(SqlBackupJob job,IEnumerable<ReportEvent> source,DateTime now)
    {
        var times=Deadlines(job);var events=source.ToArray();
        return times.Select(time=>EvaluateOne(job,events,now,time,times)).ToArray();
    }
    public static BackupLamp Evaluate(SqlBackupJob job,IEnumerable<ReportEvent> source,DateTime now)
    {
        var lamps=EvaluateSlots(job,source,now);if(lamps.Count==1)return lamps[0];
        var worst=lamps.OrderBy(l=>l.State==LampState.Red?0:l.State==LampState.Amber?1:l.State==LampState.Gray?2:3).First();
        return new(job.Title,worst.State,string.Join(" · ",lamps.Select(l=>l.Name[(job.Title.Length+3)..]+": "+l.Status)),lamps.Max(l=>l.LastReport),job.DeviceId,job.Id);
    }
    private static BackupLamp EvaluateOne(SqlBackupJob job,IEnumerable<ReportEvent> source,DateTime now,int deadline,IReadOnlyList<int> times)
    {
        var title=times.Count==1?job.Title:job.Title+" · "+TimeSpan.FromMinutes(deadline).ToString(@"hh\:mm");
        var allEvents=source.ToArray();
        var events = allEvents.Where(x => x.JobId == job.Id && (x.ScheduledSlotMinutes==null || x.ScheduledSlotMinutes==deadline) && !x.IsPending && (x.Result is BackupResult.Success or BackupResult.Warning or BackupResult.Failed)).ToArray();
        var latest = events.OrderByDescending(x => x.OccurredAt ?? x.ReceivedAt).FirstOrDefault();
        var date = times.Count==1 ? latest?.OccurredAt ?? latest?.ReceivedAt : null;
        if (!job.Active || job.Days == 0) return new(title, LampState.Gray, "Контроль выключен", date, JobId: job.Id);
        if(job.EarlyMinutes is int beforeMidnight && beforeMidnight>0) {
            var nextDue=now.Date.AddDays(1).AddMinutes(deadline);
            if((job.Days&(1<<(((int)nextDue.DayOfWeek+6)%7)))!=0 && nextDue.AddMinutes(job.GraceMinutes)>=job.MonitoringSince) {
                var earlyReport=events.Where(e=>(e.OccurredAt??e.ReceivedAt)>=nextDue.AddMinutes(-beforeMidnight)&&(e.OccurredAt??e.ReceivedAt)<=now&&ReportSlot(job,e)==deadline).OrderByDescending(e=>e.OccurredAt??e.ReceivedAt).FirstOrDefault();
                if(earlyReport!=null)return FromLatest(title,earlyReport,job.DeviceId,job.Id);
            }
        }
        // Completion windows are separated by scheduled completion times, including overnight grace.
        var candidates = Enumerable.Range(0, 9).Select(n => now.Date.AddDays(-n).AddMinutes(deadline))
            .Where(d => (job.Days & (1 << (((int)d.DayOfWeek + 6) % 7))) != 0 && d.AddMinutes(job.GraceMinutes) >= job.MonitoringSince)
            .OrderByDescending(d => d).ToArray();
        var due = candidates.FirstOrDefault(d => d <= now);
        var todayPlanned = (job.Days & (1 << (((int)now.DayOfWeek + 6) % 7))) != 0;
        var todayDue = now.Date.AddMinutes(deadline);
        var previousDay = candidates.FirstOrDefault(d => d.Date < now.Date);
        var previousGraceStillOpen = previousDay != default && previousDay.AddMinutes(job.GraceMinutes) >= now;
        if (todayPlanned && todayDue.AddMinutes(job.GraceMinutes) >= job.MonitoringSince && !previousGraceStillOpen) due = todayDue;
        // Off days do not create a new expectation, but cannot clear a missed scheduled window.
        if (due == default) {
            var received=events.Where(e=>(e.OccurredAt??e.ReceivedAt)>=(job.EarlyMinutes is int early?todayDue.AddMinutes(-early):now.Date) && (e.OccurredAt??e.ReceivedAt)<=now && ReportSlot(job,e)==deadline).OrderByDescending(e=>e.OccurredAt??e.ReceivedAt).ThenByDescending(e=>e.Id).FirstOrDefault();
            if(job.EarlyMinutes!=null && received!=null && (received.OccurredAt??received.ReceivedAt)>todayDue.AddMinutes(job.GraceMinutes))received=null;
            if(received!=null)return FromLatest(title,received,job.DeviceId,job.Id);
            return todayPlanned ? new(title, LampState.Gray, "Ожидается", date, JobId: job.Id) : new(title, LampState.Gray, "Нет ожиданий по расписанию", date, JobId: job.Id);
        }
        var boundaries=times.Count==1?candidates:Enumerable.Range(0,9).SelectMany(n=>times.Select(t=>now.Date.AddDays(-n).AddMinutes(t)))
            .Where(d=>(job.Days&(1<<(((int)d.DayOfWeek+6)%7)))!=0).OrderByDescending(d=>d).ToArray();
        var previous = boundaries.FirstOrDefault(d => d < due);
        var lower = job.EarlyMinutes is int earlyWindow ? due.AddMinutes(-earlyWindow) : due.Date;
        if (previous != default && previous.AddMinutes(job.GraceMinutes) > lower) lower = previous.AddMinutes(job.GraceMinutes);
        var strictLower=times.Count>1 && previous!=default && previous.AddMinutes(job.GraceMinutes)>=lower;
        bool InWindow(ReportEvent e) {
            var stamp=e.OccurredAt??e.ReceivedAt;
            if(stamp>now)return false;
            if(job.EarlyMinutes!=null && stamp>due.AddMinutes(job.GraceMinutes))return false;
            if(times.Count>1 && e.ScheduledSlotMinutes==deadline && job.EarlyMinutes==null)
                return stamp>=due.Date && stamp<new[]{due.Date.AddDays(1),due.AddMinutes(job.GraceMinutes)}.Max();
            return stamp>=lower && (!strictLower || stamp>lower) && (times.Count==1 && job.EarlyMinutes==null || stamp<=due.AddMinutes(job.GraceMinutes));
        }
        var current = events.Where(InWindow).OrderByDescending(e => e.OccurredAt ?? e.ReceivedAt).FirstOrDefault();
        if (current != null) return FromLatest(title, current, JobId: job.Id);
        var unresolved=allEvents.Where(e=>e.JobId==job.Id && (e.ScheduledSlotMinutes==null || e.ScheduledSlotMinutes==deadline) && (e.IsPending || e.Result is BackupResult.Unknown or BackupResult.Event) && InWindow(e)).OrderByDescending(e=>e.OccurredAt??e.ReceivedAt).FirstOrDefault();
        if(unresolved!=null)return new(title,now>due.AddMinutes(job.GraceMinutes)?LampState.Red:LampState.Gray,"Результат не распознан · откройте письмо",unresolved.OccurredAt??unresolved.ReceivedAt,JobId:job.Id);
        return new(title, now > due.AddMinutes(job.GraceMinutes) ? LampState.Red : LampState.Gray,
            now > due.AddMinutes(job.GraceMinutes) ? "Нет отчёта" : "Ожидается", date, JobId: job.Id);
    }
    public static BackupLamp FromLatest(string title, ReportEvent? report, int? DeviceId = null, int? JobId = null)
    {
        var state = report == null ? LampState.Gray : report.Severity == Severity.Critical || report.Result == BackupResult.Failed ? LampState.Red
            : report.Severity == Severity.Warning || report.Result == BackupResult.Warning ? LampState.Amber
            : report.Result == BackupResult.Success ? LampState.Green : LampState.Gray;
        return new(title, state, report == null ? "Нет отчётов" : report.Result == BackupResult.Success && report.Severity == Severity.Critical ? "Успешно · критическое событие" : report.Result == BackupResult.Success && report.Severity == Severity.Warning ? "Успешно · предупреждение" : Display.Result(report.Result), report?.OccurredAt ?? report?.ReceivedAt, DeviceId, JobId);
    }
}
