using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class BindingSpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var accessRequest=new MailBindRequest(1,TargetKind.SqlJob,1,"",true,false,false,false,null,"");
        var reader=new UserView("reader","reader",false,true,1,new(){["reports"]=1,["jobs"]=1});
        bool denied=false;try{MailBinding.RequireAccess(reader,accessRequest);}catch(UnauthorizedAccessException){denied=true;}
        check("Binding denies report read-only user",denied);
        var writer=reader with {Permissions=new(){["reports"]=2}};
        denied=false;try{MailBinding.RequireAccess(writer,accessRequest);}catch(UnauthorizedAccessException){denied=true;}
        check("Binding requires access to selected target module",denied);
        writer=writer with {Permissions=new(){["reports"]=2,["jobs"]=1}};
        MailBinding.RequireAccess(writer,accessRequest);check("Binding permits report writer with job read access",true);
        var probe=new MailReport("","","backup@example.test","Same subject","Host: NAS-A\nTask GUID: tenant-A\nliteral .* value",null);
        var rule=new BindingRule {BodyPhrase="Host: NAS-A\r\nTask GUID: tenant-A",BodyMatchAll=true};
        check("Body AND requires every line",RuleMatcher.Matches(rule,probe)&&!RuleMatcher.Matches(rule,probe with {Body="Host: NAS-B\nTask GUID: tenant-A"}));
        rule.BodyMatchAll=false;
        check("Body OR accepts either line",RuleMatcher.Matches(rule,probe with {Body="Task GUID: tenant-A"}));
        check("Body OR rejects no matches",!RuleMatcher.Matches(rule,probe with {Body="Host: NAS-B"}));
        check("Body lines trim ignore blanks and duplicate case",RuleMatcher.BodyLines(" one \n\nONE\r\ntwo").SequenceEqual(new[]{"one","two"}));
        rule.BodyMatchAll=true;rule.BodyPhrase="literal .* value";
        check("Body condition is literal and not regex",RuleMatcher.Matches(rule,probe)&&!RuleMatcher.Matches(rule,probe with {Body="literal arbitrary value"}));
        var f=new DatabaseFactory(new AppSettings {SqlitePath=Path.Combine(folder,"binding.db")});await f.InitializeAsync();
        int deviceA,deviceB,jobId;
        await using(var db=f.Create()) {
            var a=new Device{Name="NAS-A"};var b=new Device{Name="NAS-B"};db.Devices.AddRange(a,b);await db.SaveChangesAsync();deviceA=a.Id;deviceB=b.Id;
            var job=new SqlBackupJob {Title="Daily",Host="NAS-A",DeviceId=a.Id,CompletionTimes="09:00;21:00",DeadlineMinutes=540,GraceMinutes=30,MonitoringSince=new(2026,10,1)};
            db.Jobs.Add(job);await db.SaveChangesAsync();jobId=job.Id;
        }
        var store=new ReportStore(f);
        MailReport Mail(string key,string host,string guid,int hour,int day=16) =>new("fixture",key,"backup@example.test","[Success] Veeam same subject",$"Job name: Daily\nHost: {host}\nTask GUID: {guid}\nStatus: Success",new(2026,10,day,hour,0,0),"",new(2026,10,day,hour,2,0));
        await store.IngestAsync(Mail("a1","NAS-A","tenant-A",9));await store.IngestAsync(Mail("a2","NAS-A","tenant-A",21));await store.IngestAsync(Mail("b","NAS-B","tenant-B",9));
        async Task<ReportEvent[]> Reports(){await using var db=f.Create();return await db.Reports.AsNoTracking().OrderBy(e=>e.Id).ToArrayAsync();}
        var events=await Reports();var a1=events[0];var a2=events[1];var b1=events[2];
        MailBindRequest Request(ReportEvent seed,bool automatic=false,string lines="Host: NAS-A\nTask GUID: tenant-A")=>new(seed.Id,TargetKind.SqlJob,jobId,lines,true,automatic,true,false,540,RemoteData.EntityStamp(seed));
        var single=await MailBinding.ApplyAsync(f,Request(a1));
        check("Direct binding changes only selected letter",single.Bound==1);
        await using(var db=f.Create())check("Direct binding creates no rule and preserves receipt",!await db.Rules.AnyAsync()&&(await db.Reports.SingleAsync(e=>e.Id==a2.Id)).JobId==null&&(await db.Reports.SingleAsync(e=>e.Id==a1.Id)).ReceivedAt==a1.ReceivedAt);
        a1=(await Reports())[0];var auto=Request(a1,true);
        var preview=await MailBinding.PreviewAsync(f,auto);
        check("Preview distinguishes identical subjects by body",preview.Matched==2&&preview.Applicable==2&&preview.Samples.All(s=>s.Id!=b1.Id));
        var applied=await MailBinding.ApplyAsync(f,auto with {PreviewToken=preview.Token});
        check("Bulk binding creates one rule",applied.Bound==2&&applied.RuleId!=null);
        await using(var db=f.Create()) {
            check("Bulk binding leaves other backup untouched",(await db.Reports.SingleAsync(e=>e.Id==b1.Id)).JobId==null);
            check("Only selected letter gets manual run",(await db.Reports.SingleAsync(e=>e.Id==a1.Id)).ScheduledSlotMinutes==540&&(await db.Reports.SingleAsync(e=>e.Id==a2.Id)).ScheduledSlotMinutes==null);
            check("Binding updates incidents without duplicates",await db.Incidents.CountAsync(e=>e.ReportId==a1.Id)==1);
        }
        await store.IngestAsync(Mail("a3","NAS-A","tenant-A",9,17));
        await using(var db=f.Create())check("New matching mail automatically binds",(await db.Reports.OrderByDescending(e=>e.Id).FirstAsync()).JobId==jobId&&!await db.Reports.OrderByDescending(e=>e.Id).Select(e=>e.IsPending).FirstAsync());
        b1=(await Reports()).Single(e=>e.Id==b1.Id);
        await MailBinding.ApplyAsync(f,new(b1.Id,TargetKind.Device,deviceB,"",true,false,false,false,null,RemoteData.EntityStamp(b1)));
        a1=(await Reports()).Single(e=>e.Id==a1.Id);
        var broad=Request(a1,true,"Host: NAS-A\nHost: NAS-B") with {AllLines=false,ScheduledSlotMinutes=null};
        var broadPreview=await MailBinding.PreviewAsync(f,broad);
        check("Preview warns about other bindings and keeps them by default",broadPreview.Matched==4&&broadPreview.OtherBindings==1&&broadPreview.Applicable==1);
        await store.IngestAsync(Mail("a4","NAS-A","tenant-A",21,17));
        bool stale=false;try{await MailBinding.ApplyAsync(f,broad with {PreviewToken=broadPreview.Token});}catch(InvalidOperationException){stale=true;}
        await using(var db=f.Create())check("Changed matches reject stale preview atomically",stale&&await db.Rules.CountAsync()==1&&(await db.Reports.SingleAsync(e=>e.Id==b1.Id)).DeviceId==deviceB);
        int conflictId;
        await using(var db=f.Create()){var conflict=new BindingRule{BodyPhrase="Host: NAS-A",TargetId=deviceB};db.Rules.Add(conflict);await db.SaveChangesAsync();conflictId=conflict.Id;}
        var conflicting=await MailBinding.PreviewAsync(f,auto with {ExpectedHash=RemoteData.EntityStamp(a1)});
        check("Preview reports conflicting future rules",conflicting.ConflictingRules.Contains(conflictId));
        bool blocked=false;try{await MailBinding.ApplyAsync(f,auto with {ExpectedHash=RemoteData.EntityStamp(a1),PreviewToken=conflicting.Token});}catch(InvalidOperationException){blocked=true;}
        await using(var db=f.Create())check("Conflicting rule never partially saves",blocked&&await db.Rules.CountAsync()==2);
        await using(var db=f.Create()){db.Rules.Remove(await db.Rules.SingleAsync(r=>r.Id==conflictId));await db.SaveChangesAsync();}
        await store.IngestAsync(new("fixture","unknown","sender","Unknown message","Unclear original text",null));
        var unknown=(await Reports()).Last();
        var unknownBound=await MailBinding.ApplyAsync(f,new(unknown.Id,TargetKind.Device,deviceA,"",true,false,false,false,null,RemoteData.EntityStamp(unknown)));
        check("Manual target does not invent backup success",unknownBound.NeedsReview==1);
        await store.ReprocessAsync();
        await using(var db=f.Create())check("Reanalysis preserves manual target for unresolved mail",(await db.Reports.SingleAsync(e=>e.Id==unknown.Id)).DeviceId==deviceA&&(await db.Reports.SingleAsync(e=>e.Id==unknown.Id)).BindingLocked);
        var path=Path.Combine(folder,"bindings.json");await new DatabaseTransfer(f).ExportAsync(path);
        var copy=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"bindings-copy.db")});await copy.InitializeAsync();await new DatabaseTransfer(copy).ImportAsync(path);
        await using(var db=copy.Create())check("Portable transfer preserves multiline rules runs and manual pin",(await db.Rules.SingleAsync()).BodyMatchAll&&(await db.Jobs.SingleAsync()).CompletionTimes=="09:00;21:00"&&await db.Reports.AnyAsync(e=>e.BindingLocked&&e.ScheduledSlotMinutes==540));
        await store.IngestAsync(new("fixture","cross-source","other@example.test","[Success] QNAP HBS","Host: NAS-A\nTask GUID: tenant-A\nStatus: Success",null));
        await using(var db=f.Create())check("Body-only rule has no hidden sender subject or provider restriction",(await db.Reports.OrderByDescending(e=>e.Id).FirstAsync()).JobId==jobId);
        await using(var db=f.Create()){
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_BindingRule DROP COLUMN BodyMatchAll");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_SqlBackupJob DROP COLUMN CompletionTimes");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_ReportEvent DROP COLUMN BindingLocked");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_ReportEvent DROP COLUMN ScheduledSlotMinutes");
            await BindingSchema.EnsureAsync(db);await BindingSchema.EnsureAsync(db);
            check("Additive binding migration preserves existing links",(await db.Rules.SingleAsync()).BodyMatchAll&&(await db.Jobs.SingleAsync()).CompletionTimes==""&&(await db.Reports.SingleAsync(e=>e.Id==a1.Id)).JobId==jobId);
        }
    }
}
