using SysAdminAssistant.Core;
internal static class SymmetricWindowSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var now=new DateTime(2026,10,19,23,0,0);var job=new SqlBackupJob{Id=1,Title="Symmetric",DeadlineMinutes=1260,EarlyMinutes=120,GraceMinutes=120,MonitoringSince=now.AddDays(-10)};
        ReportEvent At(int hour,int minute=0)=>new(){JobId=1,OccurredAt=now.Date.AddHours(hour).AddMinutes(minute),ReceivedAt=now.Date.AddHours(hour).AddMinutes(minute),Result=BackupResult.Success,Severity=Severity.Info,IsPending=false};
        check("Symmetric allowance accepts exact early boundary",BackupSchedule.Evaluate(job,[At(19)],now).State==LampState.Green);
        check("Symmetric allowance rejects report before early window",BackupSchedule.Evaluate(job,[At(18,59)],now).State!=LampState.Green);
        check("Symmetric allowance accepts exact late boundary",BackupSchedule.Evaluate(job,[At(23)],now).State==LampState.Green);
        check("Symmetric allowance rejects report after late window",BackupSchedule.Evaluate(job,[At(23,1)],now.AddMinutes(2)).State!=LampState.Green);
        job.CompletionTimes="09:00;21:00";job.EarlyMinutes=360;job.GraceMinutes=360;
        var slots=BackupSchedule.EvaluateSlots(job,[At(15)],now);check("Touching symmetric windows count boundary only once",slots.Count(l=>l.State==LampState.Green)==1);
        bool invalid=false;try{BackupSchedule.Deadlines(new(){CompletionTimes="09:00;12:00",EarlyMinutes=120,GraceMinutes=120});}catch(ArgumentException){invalid=true;}check("Overlapping symmetric run windows rejected",invalid);
        job.CompletionTimes="";job.DeadlineMinutes=30;job.EarlyMinutes=120;job.GraceMinutes=120;
        var previous=new ReportEvent{JobId=1,OccurredAt=now.Date.AddDays(-1).AddHours(23),Result=BackupResult.Success,Severity=Severity.Info,IsPending=false};
        check("Symmetric window accepts previous-day early completion",BackupSchedule.Evaluate(job,[previous],now.Date.AddHours(1)).State==LampState.Green);
        var tonight=At(23);check("Early completion before midnight satisfies next-day run",BackupSchedule.Evaluate(job,[tonight],now).State==LampState.Green);
    }
}
