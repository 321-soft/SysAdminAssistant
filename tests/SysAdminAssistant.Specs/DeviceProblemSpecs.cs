using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class DeviceProblemSpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var now=new DateTime(2026,10,19,12,0,0);
        var f=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"device-problems.db")});await f.InitializeAsync();
        int id,otherId;
        await using(var db=f.Create()) {
            var d=new Device{Name="Generic server",Type="Сервер",Status="ONLINE",CheckedAt=now};var other=new Device{Name="Other"};db.Devices.AddRange(d,other);await db.SaveChangesAsync();id=d.Id;otherId=other.Id;
            db.Rules.Add(new BindingRule{BodyPhrase="Host: generic",TargetKind=TargetKind.Device,TargetId=id});await db.SaveChangesAsync();
        }
        var store=new ReportStore(f);
        async Task Add(string key,string severity)=>await store.IngestAsync(new("fixture",key,"sender",severity+" Veeam",$"Host: generic\n{severity}",now,"",now));
        await Add("one","Critical");await Add("two","Critical");await Add("warn","Warning");
        async Task<DeviceProblem[]> Problems(){await using var db=f.Create();return await DeviceProblems.OpenAsync(db,id);}
        async Task<DeviceHealth> Health(){await using var db=f.Create();var d=await db.Devices.AsNoTracking().SingleAsync(d=>d.Id==id);return DashboardState.WithProblems(d,DashboardState.Device(d,[],now,30,true),(await Problems()).Select(p=>p.Incident),now);}
        var open=await Problems();check("All providers expose every unresolved device event",open.Length==3&&open.Count(p=>p.Incident.Severity==Severity.Critical)==2);
        check("Multiple critical events color generic device red",(await Health()).Lamp==LampState.Red&&(await Health()).Status.Contains("2"));
        var critical=open.Where(p=>p.Incident.Severity==Severity.Critical).ToArray();
        await DeviceProblems.ResolveAsync(f,new(id,critical[0].Incident.Id));
        check("Resolving one critical event keeps remaining event red",(await Health()).Lamp==LampState.Red&&(await Problems()).Count(p=>p.Incident.Severity==Severity.Critical)==1);
        await DeviceProblems.ResolveAsync(f,new(id,critical[1].Incident.Id));
        check("Remaining warning colors device amber",(await Health()).Lamp==LampState.Amber);
        await DeviceProblems.ResolveAsync(f,new(id,(await Problems()).Single().Incident.Id));
        check("All issues resolved restores healthy device green",(await Health()).Lamp==LampState.Green&&(await Problems()).Length==0);
        await DeviceProblems.ResolveAsync(f,new(id,critical[0].Incident.Id));
        check("Repeated resolve is idempotent",(await Problems()).Length==0);
        await MailBinding.ReanalyzeDeviceAsync(f,id);
        await using(var db=f.Create())check("Reanalysis preserves closed events without duplicating them",await db.Incidents.CountAsync()==3&&await db.Incidents.AllAsync(i=>i.Closed));
        await Add("new-error","Critical");check("New critical report raises alarm again",(await Health()).Lamp==LampState.Red&&(await Problems()).Length==1);
        var current=(await Problems()).Single();
        await using(var db=f.Create()){var source=await db.MailSources.SingleAsync(s=>s.EventId==current.Report.Id);source.Body="";await db.SaveChangesAsync();}
        await MailBinding.ReanalyzeDeviceAsync(f,id);
        check("Missing archived body does not erase known critical alarm",(await Problems()).Length==1&&(await Health()).Lamp==LampState.Red);
        bool blocked=false;
        try{await DeviceProblems.ResolveAsync(f,new(otherId,current.Incident.Id));}catch(ArgumentException){blocked=true;}
        check("Resolve cannot close another device event",blocked&&(await Problems()).Length==1);
        await DeviceProblems.ResolveAsync(f,new(id,current.Incident.Id));
        await using(var db=f.Create()) {var d=await db.Devices.SingleAsync(d=>d.Id==id);d.Status="OFFLINE";await db.SaveChangesAsync();}
        check("Resolving mail does not hide an offline device",(await Health()).Lamp==LampState.Red&&(await Health()).Status=="Недоступно");
        await using(var db=f.Create())check("Device reports never create backup schedule rows",!await db.Jobs.AnyAsync()&&(await BackupQueries.LampsAsync(db,now)).Count==0);
        var user=new UserView("reader","reader",false,true,1,new(){["devices"]=1,["reports"]=1});
        bool denied=false;try{AccessRules.Command(user,"device-resolve");}catch(UnauthorizedAccessException){denied=true;}
        check("Resolving device issue requires report write permission",denied);
        AccessRules.Command(user with {Permissions=new(){["devices"]=1,["reports"]=2}},"device-resolve");check("Report writer with device read can resolve issue",true);
    }
}
