using SysAdminAssistant.Core;
internal static class RepeatedScheduleSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var job=new SqlBackupJob {Id=1,Title="Twice",Days=127,CompletionTimes="09:00; 21:00",GraceMinutes=30,MonitoringSince=new(2026,10,1)};
        ReportEvent Success(int hour,int minute=0)=>new(){JobId=1,Result=BackupResult.Success,Severity=Severity.Info,OccurredAt=new(2026,10,16,hour,minute,0),IsPending=false};
        var morning=Success(9);var evening=Success(21);
        var slots=BackupSchedule.EvaluateSlots(job,[morning],new(2026,10,16,22,0,0));
        check("Twice daily morning success cannot satisfy evening",slots.Count==2&&slots[0].State==LampState.Green&&slots[1].State==LampState.Red);
        check("Twice daily aggregate retains missing run",BackupSchedule.Evaluate(job,[morning],new(2026,10,16,22,0,0)).State==LampState.Red);
        slots=BackupSchedule.EvaluateSlots(job,[morning,evening],new(2026,10,16,22,0,0));
        check("Both daily reports satisfy separate runs",slots.All(l=>l.State==LampState.Green));
        slots=BackupSchedule.EvaluateSlots(job,[morning],new(2026,10,16,12,0,0));
        check("Upcoming evening is pending without erasing morning",slots[0].State==LampState.Green&&slots[1].State==LampState.Gray);
        var boundary=Success(9,30);
        slots=BackupSchedule.EvaluateSlots(job,[boundary],new(2026,10,16,22,0,0));
        check("Grace boundary report counts exactly once",slots.Count(l=>l.State==LampState.Green)==1&&slots[0].State==LampState.Green);
        var pinned=Success(9);pinned.ScheduledSlotMinutes=1260;
        slots=BackupSchedule.EvaluateSlots(job,[pinned],new(2026,10,16,22,0,0));
        check("Manual run pin cannot satisfy another run",slots[0].State==LampState.Red&&slots[1].State==LampState.Green);
        var receipt=Success(9);receipt.ReceivedAt=receipt.OccurredAt!.Value;receipt.OccurredAt=null;
        slots=BackupSchedule.EvaluateSlots(job,[receipt],new(2026,10,16,22,0,0));
        check("Undated repeated run falls back to receipt",slots[0].State==LampState.Green&&slots[1].State==LampState.Red);
        var future=Success(21);slots=BackupSchedule.EvaluateSlots(job,[future],new(2026,10,16,20,0,0));
        check("Future repeated report cannot satisfy current time",slots.All(l=>l.State!=LampState.Green));
        var overlap=false;try{BackupSchedule.Deadlines(new(){CompletionTimes="09:00;09:30",GraceMinutes=60});}catch(ArgumentException){overlap=true;}
        check("Overlapping run windows rejected",overlap);
        check("Run times deduplicate and sort",BackupSchedule.Deadlines(new(){CompletionTimes="21:00;09:00;09:00",GraceMinutes=30}).SequenceEqual(new[]{540,1260}));
        job.CompletionTimes="09:00;23:30";job.GraceMinutes=90;
        var overnight=new ReportEvent{JobId=1,OccurredAt=new(2026,10,17,0,30,0),Result=BackupResult.Success,Severity=Severity.Info,IsPending=false};
        slots=BackupSchedule.EvaluateSlots(job,[overnight],new(2026,10,17,0,45,0));
        check("Overnight report belongs only to previous late run",slots.Count(l=>l.State==LampState.Green)==1&&slots[1].State==LampState.Green);
        var parsed=ReportAnalyzer.Analyze(new("","","","[Success] Veeam","Status: Success",new(2026,10,16,8,0,0),"",new(2026,10,16,9,0,0)),[KeywordDefaults.For("Любой"),KeywordDefaults.For("Veeam")]);
        check("Missing body timestamp uses IMAP receipt before sender date",parsed.OccurredAt==new DateTime(2026,10,16,9,0,0)&&parsed.Evidence.Contains("IMAP"));
    }
}
