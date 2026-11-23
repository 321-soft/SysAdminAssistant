using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

public static class LinuxServiceProfileSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var p=new AgentPolicy{StandardServices=false,RoleProfilesConfigured=true,ServiceRoles=["linux-web"],Services=[new("custom.service")]};
        AgentService[] services=[new("nginx.service","nginx","Running"),new("apache2.service","Apache","Stopped")];
        LinuxServiceProfiles.Sync(p,services);
        check("Linux role adds running service and keeps manual service",p.Services.Any(s=>s.Name=="nginx.service")&&p.Services.Any(s=>s.Name=="custom.service")&&!p.Services.Any(s=>s.Name=="apache2.service"));
        LinuxServiceProfiles.Sync(p,[new("nginx.service","nginx","Stopped")]);
        check("Linux role retains previously selected stopped service",p.Services.Any(s=>s.Name=="nginx.service"));
        var report=new AgentReport{Os="Linux Ubuntu",MachineName="host",CpuCount=1,CpuPercent=1,MemoryTotalBytes=100,MemoryAvailableBytes=90,UptimeSeconds=1000,Services=[new("nginx.service","nginx","Stopped"),new("custom.service","custom","Running")]};
        check("Linux role reports service failure",AgentChecks.Evaluate(report,p).Any(f=>f.Key=="service:nginx.service"));
        report.Errors=["Не удалось получить службы systemd"];report.Services=[];
        check("Linux unavailable systemd does not invent service failures",!AgentChecks.Evaluate(report,p).Any(f=>f.Key.StartsWith("service:")));
        p.ServiceRoles=[];LinuxServiceProfiles.Sync(p,[]);
        check("Removing Linux role preserves manual entry",p.Services.Count==1&&p.Services[0].Name=="custom.service");
        var json=JsonSerializer.Serialize(new AgentReport(),AgentWire.Json);
        check("Windows report omits unused Linux fields",!json.Contains("Networks")&&!json.Contains("SwapTotalBytes")&&!json.Contains("SwapFreeBytes"));
        var record=JsonSerializer.Serialize(new AgentRecord(),AgentWire.Json);
        check("Legacy Windows index omits platform",!record.Contains("Platform"));
    }
}
