using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Server;

internal static class NavigationHealth
{
    public static async Task<Dictionary<int,LampState>> ReadAsync(ServerState server,KscMonitor ksc,UserView user,CancellationToken token)
    {
        bool Can(string module)=>AccessRules.Level(user,module)>=1;
        var result=new Dictionary<int,LampState>();
        void Mark(int page,LampState lamp)
        {
            if(lamp==LampState.Red||lamp==LampState.Amber&&!result.ContainsKey(page))
                result[page]=lamp;
        }
        static LampState Level(Severity severity)=>severity switch {
            Severity.Critical=>LampState.Red, Severity.Warning=>LampState.Amber, _=>LampState.Gray
        };
        var settings=server.Settings;
        var now=AppTime.Now;
        await using var db=server.Factory.Create();
        var services=Can("services")?await db.Services.AsNoTracking().ToArrayAsync(token):[];
        var cutoff=now.AddSeconds(-Math.Max(120,settings.PollSeconds*3));
        if(settings.MonitoringEnabled)
            foreach(var service in services.Where(s=>s.CheckedAt>=cutoff&&s.CheckedAt<=now))
                if(service.Status!="ONLINE"&&service.Status!="UNKNOWN")Mark(2,LampState.Red);
        if(Can("devices")){
            var devices=await db.Devices.AsNoTracking().Where(d=>d.Enabled&&d.Type!="MikroTik"&&d.Type!="Коммутатор").ToArrayAsync(token);
            var incidents=Can("reports")
                ?await (from i in db.Incidents.AsNoTracking() join r in db.Reports.AsNoTracking() on i.ReportId equals r.Id
                         where !i.Closed&&r.Provider!="DMARC"&&r.DeviceId!=null
                         select new {r.DeviceId,Incident=i}).ToArrayAsync(token)
                :[];
            var byDevice=incidents.ToLookup(r=>r.DeviceId);
            var servicesByDevice=services.ToLookup(s=>s.DeviceId);
            foreach(var device in devices){
                var health=DashboardState.Device(device,servicesByDevice[device.Id],now,settings.PollSeconds,settings.MonitoringEnabled);
                Mark(1,DashboardState.WithProblems(device,health,byDevice[device.Id].Select(x=>x.Incident),now).Lamp);
            }
        }
        if(Can("jobs")&&Can("reports"))
            foreach(var backup in await BackupQueries.LampsAsync(db,now,token,display:true))Mark(5,backup.State);
        if(Can("routers"))
            foreach(var router in await server.Routers.ListAsync(server.Factory,token))Mark(6,router.State);
        if(Can("payments")){
            var bills=await db.Payments.AsNoTracking().Where(p=>!p.Paid).ToArrayAsync(token);
            foreach(var bill in bills)Mark(3,DashboardState.Payment(bill,now.Date,settings.Company.WeekendMask,settings.Company.PaymentSoonDays).Lamp);
        }
        if(Can("reports")||Can("dmarc")){
            var open=await (from i in db.Incidents.AsNoTracking() join r in db.Reports.AsNoTracking() on i.ReportId equals r.Id
                            where !i.Closed && r.JobId==null && (i.Severity==Severity.Warning||i.Severity==Severity.Critical)
                            select new {r.Provider,i.Severity}).Distinct().ToArrayAsync(token);
            foreach(var i in open){
                var dmarc=i.Provider=="DMARC";
                if(dmarc?Can("dmarc"):Can("reports"))Mark(dmarc?10:11,Level(i.Severity));
            }
        }
        if(user.Administrator){
            var view=ksc.View();
            if(view.Active)
                foreach(var problem in view.Problems)Mark(12,Level(problem.Severity));
        }
        return result;
    }
}
