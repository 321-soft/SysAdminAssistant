using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
using Microsoft.EntityFrameworkCore;
internal static class BackupIndicatorSpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var parent=new AsyncActionGate();var modal=new AsyncActionGate();bool innerRan=false;
        await parent.TryRunAsync(async()=>{check("Parent action does not block dialog action",await modal.TryRunAsync(()=>{innerRan=true;return Task.CompletedTask;}));});
        check("Dialog resolve action actually executes inside parent",innerRan);
        var started=new TaskCompletionSource();var finish=new TaskCompletionSource();
        var running=modal.TryRunAsync(async()=>{started.SetResult();await finish.Task;});await started.Task;
        check("Dialog action rejects duplicate concurrent click",!await modal.TryRunAsync(()=>Task.CompletedTask));finish.SetResult();await running;
        try{await modal.TryRunAsync(()=>throw new InvalidOperationException("fixture"));}catch(InvalidOperationException){}
        check("Dialog action can retry after failure",await modal.TryRunAsync(()=>Task.CompletedTask));
        var now=new DateTime(2026,10,19,12,0,0);
        var job=new SqlBackupJob{Id=1,Title="Backup",DeadlineMinutes=180,GraceMinutes=60,MonitoringSince=now};
        var ok=new ReportEvent{Id=1,JobId=1,Result=BackupResult.Success,Severity=Severity.Info,ReceivedAt=now.AddHours(-4),IsPending=false};
        var row=BackupQueries.RunRows(job,[ok],now).Single();
        check("Successful current report green despite activation after deadline",row.State==LampState.Green&&row.ScheduleState==LampState.Green);
        var yesterday=new ReportEvent{Id=1,JobId=1,Result=BackupResult.Success,Severity=Severity.Info,ReceivedAt=now.AddDays(-1),IsPending=false};
        row=BackupQueries.RunRows(job,[yesterday],now).Single();
        check("Latest success stays visible separately from schedule",row.State==LampState.Green&&row.ScheduleState==LampState.Gray&&row.LastReport==yesterday.ReceivedAt);
        job.Active=false;row=BackupQueries.RunRows(job,[ok],now).Single();
        check("Disabled schedule does not hide last successful result",row.State==LampState.Green&&row.ScheduleState==LampState.Gray);
        job.Active=true;job.MonitoringSince=now.AddDays(-10);
        var warning=new ReportEvent{Id=2,JobId=1,Result=BackupResult.Warning,Severity=Severity.Warning,ReceivedAt=now.AddHours(-1),IsPending=false};
        check("Backup warning has amber result lamp",BackupQueries.RunRows(job,[ok,warning],now).Single().State==LampState.Amber);
        var failed=new ReportEvent{Id=3,JobId=1,Result=BackupResult.Failed,Severity=Severity.Critical,ReceivedAt=now,IsPending=false};
        check("Backup failure has red result lamp",BackupQueries.RunRows(job,[ok,failed],now).Single().State==LampState.Red);
        var unknown=new ReportEvent{Id=4,JobId=1,Result=BackupResult.Unknown,Severity=Severity.Unknown,ReceivedAt=now,IsPending=true};
        check("New unrecognized report cannot inherit older green lamp",BackupQueries.RunRows(job,[ok,unknown],now).Single().State==LampState.Gray);
        row=BackupQueries.RunRows(job,[ok],now,false).Single();check("No report access masks both backup indicators",row.State==LampState.Gray&&row.ScheduleState==LampState.Gray&&row.LastReport==null);
        job.CompletionTimes="09:00;21:00";job.GraceMinutes=30;
        var morning=new ReportEvent{JobId=1,Result=BackupResult.Success,Severity=Severity.Info,ReceivedAt=now.Date.AddHours(9),IsPending=false};
        var rows=BackupQueries.RunRows(job,[morning],now).ToArray();
        check("Per-run result does not copy morning green to evening",rows[0].State==LampState.Green&&rows[1].State==LampState.Gray);
        var pinned=new ReportEvent{JobId=1,Result=BackupResult.Success,Severity=Severity.Info,ReceivedAt=now.Date.AddHours(9),ScheduledSlotMinutes=1260,IsPending=false};
        rows=BackupQueries.RunRows(job,[pinned],now).ToArray();check("Per-run result honors manually assigned slot",rows[0].State==LampState.Gray&&rows[1].State==LampState.Green);
        var future=new ReportEvent{JobId=1,Result=BackupResult.Success,Severity=Severity.Info,ReceivedAt=now.AddDays(1),IsPending=false};
        check("Result lamp excludes future-dated report",BackupQueries.RunRows(job,[future],now).All(r=>r.State==LampState.Gray));
        var monday=new DateTime(2026,11,2,12,0,0);var displayJob=new SqlBackupJob{Id=8000,Title="ABB",DeadlineMinutes=180,GraceMinutes=60,MonitoringSince=monday.AddDays(-10),Days=127};
        var priorSuccess=new ReportEvent{Id=8001,JobId=displayJob.Id,Result=BackupResult.Success,Severity=Severity.Info,IsPending=false,OccurredAt=monday.Date.AddDays(-1).AddHours(3),ReceivedAt=monday.Date.AddDays(-1).AddHours(3)};
        var display=BackupQueries.RunRows(displayJob,[priorSuccess],monday).Single();
        check("Missing current backup report is primary status instead of stale success",display.CurrentStatus=="Нет отчёта"&&display.DisplayState==LampState.Red&&display.Result=="Успешно");
        check("Historical backup success is explicitly labelled and dated",display.LastResultDetails.Contains("Последний отчёт:")&&display.LastResultDetails.Contains("01.11.2026 03:00")&&display.LastResultDetails.Contains("Успешно"));
        display=BackupQueries.RunRows(displayJob,[priorSuccess],monday.Date.AddHours(3).AddMinutes(15)).Single();check("Grace window does not show yesterday success as current green",display.CurrentStatus=="Ожидается"&&display.DisplayState==LampState.Gray);
        var currentSuccess=new ReportEvent{Id=8002,JobId=displayJob.Id,Result=BackupResult.Success,Severity=Severity.Info,IsPending=false,OccurredAt=monday.Date.AddHours(3).AddMinutes(20),ReceivedAt=monday.Date.AddHours(3).AddMinutes(20)};
        display=BackupQueries.RunRows(displayJob,[priorSuccess,currentSuccess],monday).Single();check("Fresh expected backup report restores primary success and green",display.CurrentStatus=="Успешно"&&display.DisplayState==LampState.Green&&display.LastResultDetails.Contains("02.11.2026 03:20"));
        var newUnknown=new ReportEvent{Id=8003,JobId=displayJob.Id,Result=BackupResult.Unknown,Severity=Severity.Unknown,IsPending=true,ReceivedAt=monday.Date.AddHours(4)};
        display=BackupQueries.RunRows(displayJob,[priorSuccess,currentSuccess,newUnknown],monday).Single();check("New ambiguous report cannot retain older primary success label",display.CurrentStatus=="Результат не распознан"&&display.DisplayState==LampState.Gray);
        var criticalUnknown=new ReportEvent{Id=8006,JobId=displayJob.Id,Result=BackupResult.Event,Severity=Severity.Critical,IsPending=true,ReceivedAt=monday.Date.AddHours(4).AddMinutes(10)};
        display=BackupQueries.RunRows(displayJob,[currentSuccess,criticalUnknown],monday).Single();check("New critical unparsed event cannot borrow older primary success",display.DisplayState==LampState.Red&&display.CurrentStatus!="Успешно");
        displayJob.Active=false;display=BackupQueries.RunRows(displayJob,[currentSuccess],monday).Single();check("Disabled backup control is visibly grey with historical success preserved",display.CurrentStatus=="Контроль выключен"&&display.DisplayState==LampState.Gray&&display.Result=="Успешно");displayJob.Active=true;
        display=BackupQueries.RunRows(displayJob,[],monday).Single();check("No backup history is explicit instead of fabricated old success",display.DisplayState==LampState.Red&&display.CurrentStatus=="Нет отчёта"&&display.LastResultDetails=="Последний отчёт ещё не получен");
        display=BackupQueries.RunRows(displayJob,[currentSuccess],monday,false).Single();check("Presentation does not expose masked backup history",display.DisplayState==LampState.Gray&&display.CurrentStatus=="Нет доступа к отчётам"&&!display.LastResultDetails.Contains("02.11.2026"));
        displayJob.CompletionTimes="09:00;21:00";displayJob.GraceMinutes=30;
        var todayMorning=new ReportEvent{Id=8004,JobId=displayJob.Id,Result=BackupResult.Success,Severity=Severity.Info,IsPending=false,ScheduledSlotMinutes=540,OccurredAt=monday.Date.AddHours(9).AddMinutes(15),ReceivedAt=monday.Date.AddHours(9).AddMinutes(15)};
        var oldEvening=new ReportEvent{Id=8005,JobId=displayJob.Id,Result=BackupResult.Success,Severity=Severity.Info,IsPending=false,ScheduledSlotMinutes=1260,OccurredAt=monday.Date.AddDays(-1).AddHours(21).AddMinutes(15),ReceivedAt=monday.Date.AddDays(-1).AddHours(21).AddMinutes(15)};
        var tonight=BackupQueries.RunRows(displayJob,[todayMorning,oldEvening],monday.Date.AddHours(22)).ToArray();check("Missing evening report cannot borrow morning success or hide prior date",tonight[0].DisplayState==LampState.Green&&tonight[1].DisplayState==LampState.Red&&tonight[1].CurrentStatus=="Нет отчёта"&&tonight[1].LastResultDetails.Contains("01.11.2026 21:15"));
        using(AppTime.Scope("Russian Standard Time")){var localRow=BackupQueries.RunRows(displayJob,[todayMorning],monday).First();check("History caption keeps report model time without second timezone conversion",localRow.LastResultDetails.Contains("02.11.2026 09:15"));}
        var f=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"nas-backup-auto.db")});await f.InitializeAsync();
        int id;
        await using(var db=f.Create()){var j=new SqlBackupJob{Title="NAS backup",Host="NAS"};db.Jobs.Add(j);await db.SaveChangesAsync();id=j.Id;db.Rules.Add(new BindingRule{BodyPhrase="Task: backup-fixture",TargetKind=TargetKind.SqlJob,TargetId=id});await db.SaveChangesAsync();}
        await new ReportStore(f).IngestAsync(new("fixture","nas-success","sender","[Success] QNAP","Task: backup-fixture\nStatus: Success",now,"",now));
        await using(var db=f.Create()){var report=await db.Reports.SingleAsync();check("Auto system QNAP bound to job parses backup success",report.JobId==id&&report.Result==BackupResult.Success&&!report.IsPending);}
    }
}
