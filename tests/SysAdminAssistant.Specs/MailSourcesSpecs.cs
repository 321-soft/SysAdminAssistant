using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

internal static class MailSourcesSpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var f=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"mail-source-v5.db")});
        await f.InitializeAsync();
        int one,two;
        await using(var db=f.Create()){
            var a=new Device{Name="Office",Type="QNAP NAS",MailSystem="DSM"};
            var b=new Device{Name="Datacenter",Type="Сервер"};
            db.Devices.AddRange(a,b);
            (await db.Schema.SingleAsync()).Version=5;
            await db.SaveChangesAsync();
            one=a.Id;two=b.Id;
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_KeywordProfile DROP COLUMN EventRulesJson");
        }
        await f.InitializeAsync();
        await using(var db=f.Create())
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_KeywordProfile DROP COLUMN AdditionalEventRulesJson");
        await f.InitializeAsync();
        await using(var db=f.Create()){
            var dsm=await db.Profiles.SingleAsync(p=>p.Provider=="DSM");
            dsm.SenderTerms="legacy-nas@example.invalid";dsm.SubjectTerms="DSM maintenance";
            dsm.MatchAllSourceFields=true;dsm.Scope=SearchScope.Subject;dsm.Info="completed";
            var existing=new MailReport("test","legacy","legacy-nas@example.invalid","DSM maintenance completed","Legacy event",null);
            var profiles=await db.Profiles.ToArrayAsync();
            var before=ReportAnalyzer.Analyze(existing,profiles);
            dsm.AdditionalEventRulesJson=MailSources.WriteEvents([new("Мало места на томе","","заканчивается доступная емкость;заканчивается доступная ёмкость",true,Severity.Warning,BackupResult.Event,"Проверьте том")]);
            await db.SaveChangesAsync();
            check("Independent DSM rule leaves existing classification unchanged",ReportAnalyzer.Analyze(existing,profiles)==before);
            foreach(var host in new[]{"NAS1","NAS2"}){
                var message=new MailReport("test",host,host+"@example.invalid","Storage notice","На томе 1 заканчивается доступная емкость.",null);
                check("Independent DSM warning matches body without relaxing existing profile conditions: "+host,
                    ReportAnalyzer.Analyze(message,profiles) is {Provider:"DSM",Severity:Severity.Warning,Result:BackupResult.Event,MatchedAdditionalRule:true});
            }
            var abb=new MailReport("test","abb","legacy-nas@example.invalid","Active Backup for Business completed","Backup finished.",null);
            check("Independent DSM rule leaves unrelated backup in ABB",ReportAnalyzer.Analyze(abb,profiles).Provider=="Active Backup for Business");
        }
        await f.InitializeAsync();
        await using(var db=f.Create())check("Independent DSM rules survive database reinitialization",
            (await db.Profiles.SingleAsync(p=>p.Provider=="DSM")).AdditionalEventRulesJson.Length>0);
        var dsmStore=new ReportStore(f);
        await dsmStore.IngestAsync(new("test","dsm","another-nas@example.invalid","Storage notice","На томе 1 заканчивается доступная емкость.",new(2026,11,11,8,0,0)));
        await using(var db=f.Create())check("Independent warning is stored under DSM without requiring a binding",
            (await db.Reports.SingleAsync()) is {Provider:"DSM",Severity:Severity.Warning,Result:BackupResult.Event,IsPending:false});
        var store=new ReportStore(f);
        MailReport Mail(string id,string body)=>new("test",id,"notifier@example.invalid","Fixture certificate renewal",body,new(2026,11,11,8,0,0));
        async Task<ReportEvent> Last(){await using var db=f.Create();return await db.Reports.AsNoTracking().OrderByDescending(e=>e.Id).FirstAsync();}
        await store.IngestAsync(new("test","built-in","notifier@example.invalid","Veeam backup completed successfully","Status: Success",new(2026,11,11,8,0,0)));
        check("Recognized built-in source is also independent of a device binding",(await Last()) is {Provider:"Veeam",Result:BackupResult.Success,IsPending:false,DeviceId:null});
        await store.IngestAsync(Mail("old-a","renewal rejected"));
        var old=await Last();
        await store.IngestAsync(Mail("old-b","renewal rejected"));
        var other=await Last();
        var package=new MailSourcePackage(1,"MailSource","Certificate fixture","notifier@example.invalid","Fixture certificate renewal","",true,
            [new("Renewal failed","","renewal rejected",true,Severity.Warning,BackupResult.Event,"Check the certificate issuer."),
             new("Renewal completed","","renewal completed",true,Severity.Info,BackupResult.Event,"")]);
        var roundTrip=MailSources.Parse(MailSources.Export(package));
        check("Mail source package round-trips all event rules",roundTrip.Events.SequenceEqual(package.Events)&&roundTrip.Sender==package.Sender);
        bool rejected=false;
        try{MailSources.Parse(MailSources.Export(package).Insert(1,"\"Unexpected\":true,"));}catch(System.Text.Json.JsonException){rejected=true;}
        check("Mail source package rejects unknown fields",rejected);
        var profile=new KeywordProfile{Provider=package.Name,SenderTerms=package.Sender,SubjectTerms=package.Subject,EventRulesJson=MailSources.WriteEvents(package.Events)};
        await using(var db=f.Create()){
            check("Schema 5 upgrades shared sources without losing devices",(await db.Schema.SingleAsync()).Version==6&&await db.Devices.CountAsync()==2&&await db.Profiles.AllAsync(p=>p.EventRulesJson==""));
            db.Profiles.Add(profile);
            db.Rules.AddRange(new BindingRule{Provider=package.Name,BodyPhrase="location office",TargetKind=TargetKind.Device,TargetId=one,Priority=10},new BindingRule{Provider=package.Name,BodyPhrase="location dc",TargetKind=TargetKind.Device,TargetId=two,Priority=20});
            await db.SaveChangesAsync();
        }
        check("Only selected pending letter is reprocessed",await store.ReprocessAsync(includeDmarc:false,selectedReportIds:[old.Id])==1);
        await using(var db=f.Create()){
            var selected=await db.Reports.SingleAsync(e=>e.Id==old.Id);
            check("Recognized unbound event preserves advice and is not pending",selected.Provider==package.Name&&!selected.IsPending&&selected.DeviceId==null&&selected.Description.Contains(package.Events[0].Advice));
            check("Unselected historical mail remains unchanged",await db.Pending.AnyAsync(x=>x.EventId==other.Id));
        }
        await store.IngestAsync(Mail("office","renewal rejected; location office"));var office=await Last();
        await store.IngestAsync(Mail("dc","renewal completed; location dc"));var dc=await Last();
        check("One source works for multiple independently bound devices",office.DeviceId==one&&dc.DeviceId==two&&office.Provider==package.Name&&dc.Provider==package.Name&&!office.IsPending&&!dc.IsPending);
        await using(var db=f.Create()){
            var saved=await db.Profiles.SingleAsync(p=>p.Provider==package.Name);
            saved.EventRulesJson=MailSources.WriteEvents(package.Events.Select(e=>e with {Advice="Revised dictionary advice"}).ToArray());
            var target=await db.Reports.SingleAsync(e=>e.Id==office.Id);
            target.BindingLocked=true;target.DeviceId=two;
            await db.SaveChangesAsync();
        }
        var recheck=await store.RecheckMailAsync(office.Id);
        await using(var db=f.Create()){
            var revised=await db.Reports.SingleAsync(e=>e.Id==office.Id);
            check("Recognized letter recheck uses the saved body and current dictionary",recheck is {Applied:true,IsPending:false}&&revised.Description.Contains("Revised dictionary advice"));
            check("Recheck preserves manual binding and receipt time without duplicating the incident",
                revised.BindingLocked&&revised.DeviceId==two&&revised.ReceivedAt==office.ReceivedAt&&await db.Incidents.CountAsync(x=>x.ReportId==office.Id)==1);
            check("Recheck leaves other recognized mail unchanged",(await db.Reports.SingleAsync(e=>e.Id==dc.Id)).Description==dc.Description);
            db.MailSources.Remove(await db.MailSources.SingleAsync(e=>e.EventId==dc.Id));await db.SaveChangesAsync();
        }
        check("Recheck reports when the original text is no longer available",await store.RecheckMailAsync(dc.Id) is {Applied:false,IsPending:false});
        await store.IngestAsync(Mail("ambiguous-target","renewal rejected; location office; location dc"));
        check("Shared source does not guess a device even across different binding priorities",(await Last()) is {DeviceId:null,IsPending:true});
        await store.IngestAsync(Mail("unknown-event","success; location office"));
        check("Matched source without an event stays pending despite generic success words",(await Last()) is {Result:BackupResult.Unknown,IsPending:true});
        await store.IngestAsync(Mail("ambiguous-event","renewal completed; renewal rejected"));
        check("Overlapping event rules remain pending",(await Last()) is {Result:BackupResult.Unknown,IsPending:true});
        await using(var db=f.Create()){
            db.Profiles.Add(new KeywordProfile{Provider="Overlapping source",SenderTerms=package.Sender,SubjectTerms=package.Subject,EventRulesJson=profile.EventRulesJson});
            await db.SaveChangesAsync();
        }
        await store.IngestAsync(Mail("ambiguous-source","renewal rejected; location office"));
        check("Overlapping source rules remain pending without auto-linking",(await Last()) is {Provider:"Неизвестно",Result:BackupResult.Unknown,DeviceId:null,IsPending:true});
    }
}
