using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class ProviderBindingSpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var f=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"provider-binding.db")});await f.InitializeAsync();
        int deviceId,jobId;
        await using(var db=f.Create()) {
            var nas=new Device{Name="Storage",Type="QNAP NAS"};db.Devices.Add(nas);await db.SaveChangesAsync();deviceId=nas.Id;
            var job=new SqlBackupJob{Title="Backup",Host="Storage",DeviceId=deviceId,BackupSystem="Fixture backup",DeadlineMinutes=540,MonitoringSince=new(2026,10,1)};
            db.Profiles.Add(new KeywordProfile{Provider="Fixture backup",SuccessResults="all items copied fixture",ResultScope=SearchScope.Both});
            var qnap=await db.Profiles.SingleAsync(p=>p.Provider=="QNAP");qnap.Critical+=";danger fixture";
            db.Jobs.Add(job);await db.SaveChangesAsync();jobId=job.Id;
        }
        var store=new ReportStore(f);
        MailReport Mail(string key,string body)=>new("fixture",key,"sender","Generic notification",body,new(2026,10,19,9,0,0),"",new(2026,10,19,9,2,0));
        async Task<ReportEvent> Last(){await using var db=f.Create();return await db.Reports.AsNoTracking().OrderByDescending(e=>e.Id).FirstAsync();}
        await store.IngestAsync(Mail("backup","all items copied fixture"));var before=await Last();
        check("Unbound unique result initially remains unknown",before.IsPending&&before.Result==BackupResult.Unknown);
        await MailBinding.ApplyAsync(f,new(before.Id,TargetKind.SqlJob,jobId,"",true,false,false,false,540,RemoteData.EntityStamp(before)));
        var analyzed=await Last();
        check("Binding reruns job-specific dictionary",analyzed.Result==BackupResult.Success&&analyzed.Provider=="Fixture backup"&&!analyzed.IsPending);
        check("Binding reanalysis preserves receipt and manual slot",analyzed.ReceivedAt==before.ReceivedAt&&analyzed.ScheduledSlotMinutes==540&&analyzed.BindingLocked);
        await using(var db=f.Create())check("Successful reanalysis resolves pending and creates one incident",!await db.Pending.AnyAsync(p=>p.EventId==before.Id)&&await db.Incidents.CountAsync(i=>i.ReportId==before.Id)==1);
        var result=await MailBinding.ReanalyzeJobAsync(f,jobId);
        await using(var db=f.Create())check("Job reanalysis is repeatable without duplicate incidents",result.Bound==1&&result.NeedsReview==0&&await db.Incidents.CountAsync(i=>i.ReportId==before.Id)==1);
        await using(var db=f.Create()){db.Rules.Add(new BindingRule{BodyPhrase="all items copied fixture",TargetKind=TargetKind.SqlJob,TargetId=jobId});await db.SaveChangesAsync();}
        await store.IngestAsync(Mail("future","all items copied fixture"));var future=await Last();
        check("Future rule-bound report uses job dictionary",future.JobId==jobId&&future.Provider=="Fixture backup"&&future.Result==BackupResult.Success&&!future.IsPending);
        await store.IngestAsync(Mail("nas-critical","danger fixture"));var critical=await Last();
        await MailBinding.ApplyAsync(f,new(critical.Id,TargetKind.Device,deviceId,"",true,false,false,false,null,RemoteData.EntityStamp(critical)));
        critical=await Last();
        check("Device type does not select a dictionary for generic mail",critical.Provider=="Неизвестно"&&critical.DeviceId==deviceId&&critical.IsPending);
        await using(var db=f.Create())check("Device-only critical mail creates no extra backup rows",(await BackupQueries.LampsAsync(db,new(2026,10,19,10,0,0))).All(l=>l.JobId==jobId));
        await using(var db=f.Create()) {
            var nas=await db.Devices.SingleAsync();nas.MailSystem="Fixture backup";
            var job=await db.Jobs.SingleAsync();job.BackupSystem="QNAP";await db.SaveChangesAsync();
        }
        await MailBinding.ReanalyzeDeviceAsync(f,deviceId);
        await using(var db=f.Create()) {
            var e=await db.Reports.SingleAsync(e=>e.Id==before.Id);
            check("Job dictionary takes priority over device dictionary",e.Provider=="QNAP"&&e.IsPending);
            var qnap=await db.Profiles.SingleAsync(p=>p.Provider=="QNAP");qnap.SuccessResults+=";all items copied fixture";qnap.ResultScope=SearchScope.Both;await db.SaveChangesAsync();
        }
        await MailBinding.ReanalyzeJobAsync(f,jobId);
        await using(var db=f.Create())check("Job reanalysis applies edited current dictionary",(await db.Reports.SingleAsync(e=>e.Id==before.Id)).Result==BackupResult.Success);
        var unresolved=BackupSchedule.Evaluate(new SqlBackupJob{Id=99,Title="Unresolved",DeadlineMinutes=540,MonitoringSince=new(2026,10,1)},new[]{new ReportEvent{JobId=99,ReceivedAt=new(2026,10,19,9,1,0),IsPending=true}},new(2026,10,19,10,1,0));
        check("Unrecognized received job report has explicit status",unresolved.Status.Contains("Результат не распознан")&&unresolved.State==LampState.Red);
        var path=Path.Combine(folder,"provider-export.json");await new DatabaseTransfer(f).ExportAsync(path);
        var copy=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"provider-copy.db")});await copy.InitializeAsync();await new DatabaseTransfer(copy).ImportAsync(path);
        await using(var db=copy.Create())check("Portable archive preserves job and device dictionaries",(await db.Jobs.SingleAsync()).BackupSystem=="QNAP"&&(await db.Devices.SingleAsync()).MailSystem=="Fixture backup");
        await using(var db=f.Create()) {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_SqlBackupJob DROP COLUMN BackupSystem");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_Device DROP COLUMN MailSystem");
            await BindingSchema.EnsureAsync(db);await BindingSchema.EnsureAsync(db);
            check("Dictionary columns migrate idempotently preserving reports",(await db.Jobs.SingleAsync()).BackupSystem==""&&(await db.Devices.SingleAsync()).MailSystem==""&&await db.Reports.CountAsync()==3);
        }
        var user=new UserView("limited","limited",false,true,1,new(){["jobs"]=2,["devices"]=2,["reports"]=1});
        bool denied=false;try{AccessRules.Command(user,"device-reanalyze");}catch(UnauthorizedAccessException){denied=true;}
        check("Device reanalysis requires report write access",denied);
        denied=false;try{AccessRules.Command(user,"job-reanalyze");}catch(UnauthorizedAccessException){denied=true;}
        check("Job reanalysis requires report write access",denied);
    }
}
