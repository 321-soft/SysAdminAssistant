using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class MailSubjectSpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var mail=new MailReport("","","sender","Nightly SQL backup node-A","Host: node-B",null);
        var rule=new BindingRule{SubjectPhrase="nightly\nSQL",SubjectMatchAll=true};
        check("Subject AND matches literal lines case insensitively",RuleMatcher.Matches(rule,mail));
        check("Subject AND rejects one missing line",!RuleMatcher.Matches(rule,mail with{Subject="Nightly Veeam"}));
        rule.SubjectMatchAll=false;check("Subject OR matches either literal phrase",RuleMatcher.Matches(rule,mail with{Subject="SQL report"}));
        rule.BodyPhrase="Host: node-A";rule.MatchAll=true;
        check("Subject and body AND require both fields",!RuleMatcher.Matches(rule,mail));rule.MatchAll=false;
        check("Subject or body OR accepts matching subject",RuleMatcher.Matches(rule,mail));
        rule.SubjectPhrase="literal .*";rule.BodyPhrase="";
        check("Subject matching is literal not regex",!RuleMatcher.Matches(rule,mail)&&RuleMatcher.Matches(rule,mail with{Subject="literal .*"}));
        var f=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"subject-bind.db")});await f.InitializeAsync();int device;
        await using(var db=f.Create()){var d=new Device{Name="Subject target"};db.Devices.Add(d);await db.SaveChangesAsync();device=d.Id;}
        var store=new ReportStore(f);await store.IngestAsync(new("fixture","subject-only","sender","Nightly unique-subject","",null));
        await store.IngestAsync(new("fixture","other","sender","Other report","",null));ReportEvent seed;
        await using(var db=f.Create())seed=await db.Reports.AsNoTracking().OrderBy(e=>e.Id).FirstAsync();
        var request=new MailBindRequest(seed.Id,TargetKind.Device,device,"",true,true,true,false,null,RemoteData.EntityStamp(seed),SubjectLines:"Nightly\nunique-subject");
        var preview=await MailBinding.PreviewAsync(f,request);check("Subject-only preview accepts archived mail without body",preview.Matched==1&&preview.MissingText==0);
        await MailBinding.ApplyAsync(f,request with{PreviewToken=preview.Token});
        await using(var db=f.Create())check("Subject-only binding preserves unrelated mail",(await db.Reports.SingleAsync(e=>e.Id==seed.Id)).DeviceId==device&&(await db.Reports.OrderBy(e=>e.Id).LastAsync()).DeviceId==null);
        await store.IngestAsync(new("fixture","future","different-sender","Nightly unique-subject","ordinary report",null));
        await using(var db=f.Create())check("Future mail follows subject-only rule",(await db.Reports.OrderBy(e=>e.Id).LastAsync()).DeviceId==device);
        await using(var db=f.Create())seed=await db.Reports.AsNoTracking().SingleAsync(e=>e.Id==seed.Id);
        request=request with{ExpectedHash=RemoteData.EntityStamp(seed)};preview=await MailBinding.PreviewAsync(f,request);
        await using(var db=f.Create()){var source=await db.MailSources.SingleAsync(s=>s.EventId==seed.Id);source.Subject+=" changed";await db.SaveChangesAsync();}
        bool stale=false;try{await MailBinding.ApplyAsync(f,request with{PreviewToken=preview.Token});}catch(InvalidOperationException){stale=true;}
        check("Subject edit invalidates preview token atomically",stale);
        await using(var db=f.Create()){var job=new SqlBackupJob{Title="Window",Host="host",EarlyMinutes=90,GraceMinutes=90};db.Jobs.Add(job);await db.SaveChangesAsync();}
        var path=Path.Combine(folder,"subject-portable.json");await new DatabaseTransfer(f).ExportAsync(path);var copy=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"subject-copy.db")});await copy.InitializeAsync();await new DatabaseTransfer(copy).ImportAsync(path);
        await using(var db=copy.Create())check("Portable preserves subject conditions and symmetric window",(await db.Rules.SingleAsync()).SubjectMatchAll&&(await db.Rules.SingleAsync()).SubjectPhrase.Contains("unique-subject")&&(await db.Jobs.SingleAsync()).EarlyMinutes==90);
        await using(var db=f.Create()){await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_BindingRule DROP COLUMN SubjectMatchAll");await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_SqlBackupJob DROP COLUMN EarlyMinutes");await BindingSchema.EnsureAsync(db);await BindingSchema.EnsureAsync(db);check("Subject and early-window migration keeps legacy defaults",(await db.Rules.AsNoTracking().SingleAsync()).SubjectMatchAll&&(await db.Jobs.AsNoTracking().SingleAsync()).EarlyMinutes==null);}
    }
}
