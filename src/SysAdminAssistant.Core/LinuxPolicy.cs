using System.Text.Json;
namespace SysAdminAssistant.Core;
public static class LinuxPolicy
{
    public static void Validate(AgentPolicy p)
    {
        if(p.Services.Any(s=>s?.MaxIntervalMinutes is <1 or >525600))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Ожидаемый интервал systemd: 1–525600 минут или не задан"));
        if(p.Linux is not {} l)return;
        if(l.SwapUsedPercent is <0 or >100||l.SwapDelayMinutes is <0 or >1440)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверные пороги swap"));
        if(l.Disks==null||l.Disks.Count>64||l.BootFreePercent is <0 or >95||l.EfiFreePercent is <0 or >95||l.InodeFreePercent is <0 or >95||l.BootFreeMiB is <0 or >1000000||l.EfiFreeMiB is <0 or >1000000||l.Disks.Any(d=>d==null||!AgentWire.Text(d.Mount,512)||!d.Mount.StartsWith('/')||d.FreePercent is <0 or >95||d.FreeMiB is <0 or >1000000000||d.InodeFreePercent is <0 or >95||d.InodeMinimum<0)||l.Disks.Select(d=>d.Mount).Distinct(StringComparer.Ordinal).Count()!=l.Disks.Count)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверные индивидуальные пороги Linux"));
    }
    public static void ValidateTelemetry(LinuxTelemetry t)
    {
        if(t.Volumes==null||t.Volumes.Length>64||t.Units==null||t.Units.Length>128)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный размер Linux-телеметрии"));
        foreach(var v in t.Volumes)if(v==null||!AgentWire.Text(v.Mount,512)||!AgentWire.Text(v.Source,512)||!AgentWire.Text(v.FileSystem,64)||!AgentWire.Text(v.DeviceKind,64)||!AgentWire.Text(v.DeviceId,64)||!AgentWire.Text(v.MountRoot,512)||!AgentWire.Text(v.Error,512)||v.TotalBytes<0||v.UsedBytes<0||v.AvailableBytes<0||v.ReservedBytes<0||v.UsedBytes>v.TotalBytes||v.AvailableBytes>v.TotalBytes||v.Inodes<0||v.FreeInodes<0||v.FreeInodes>v.Inodes)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректные параметры тома Linux"));
        foreach(var u in t.Units)if(u==null||!AgentWire.Text(u.Name,256)||!AgentWire.Text(u.Type,32)||!AgentWire.Text(u.Active,32)||!AgentWire.Text(u.Sub,64)||!AgentWire.Text(u.Result,128)||!AgentWire.Text(u.Trigger,256)||!AgentWire.Text(u.Error,512)||u.ExitCode<0||u.ExitStatus<0||u.LastRunUtc>DateTime.UtcNow.AddMinutes(10))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректные сведения systemd"));
        if(t.UpdateDelivery is {} d&&(!AgentWire.Text(d.Code,100)||!AgentWire.Text(d.Message,1000)||!AgentWire.Text(d.State,32)||!AgentWire.Text(d.Version,50)||!AgentWire.Text(d.PackageHash,64)||!AgentWire.Text(d.Signature,64)||d.Count<1||d.FirstUtc>d.LastUtc||d.LastUtc>DateTime.UtcNow.AddMinutes(10)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректная диагностика обновления"));
    }
    public static AgentPolicy ForAgent(AgentPolicy p,string version)
    {
        if(Version.TryParse(version,out var v)&&v>=new Version(0,32,0))return p;
        // 0.31.x rejects unknown JSON members: keep responses compatible while it downloads the new binary.
        var copy=JsonSerializer.Deserialize<AgentPolicy>(JsonSerializer.Serialize(p,AgentWire.Json),AgentWire.Json)!;copy.Linux=null;copy.Services=copy.Services.Select(s=>s with{MaxIntervalMinutes=null}).ToList();return copy;
    }
    public static LinuxDiskRule EffectiveDisk(string mount,AgentPolicy p,long total)
    {
        var options=p.Linux??new();var custom=options.Disks.FirstOrDefault(d=>d.Mount==mount);if(custom!=null)return custom;
        if(mount=="/boot"||mount=="/boot/efi"){
            var percent=mount=="/boot"?options.BootFreePercent:options.EfiFreePercent;var mib=mount=="/boot"?options.BootFreeMiB:options.EfiFreeMiB;
            // Default boot floors scale down for unusually small filesystems, never exceed their capacity.
            var floor=(int)Math.Min(mib,Math.Max(0,total/1048576d*0.1));return new(mount,percent,floor,options.InodeFreePercent);
        }
        return new(mount,p.DiskFreePercent,(int)Math.Min(1000000000L,(long)p.DiskFreeGb*1024),options.InodeFreePercent);
    }
    public static LinuxVolume[] Volumes(AgentReport r)=>r.Linux?.Volumes??r.Disks.Select(d=>new LinuxVolume(d.Name,"не передано старым агентом","не передано","не передано","","",d.TotalBytes,Math.Max(0,d.TotalBytes-d.FreeBytes),d.FreeBytes,0,null,null,d.Error)).ToArray();
    public static AgentFinding[] Disks(AgentReport r,AgentPolicy p)
    {
        var result=new List<AgentFinding>();foreach(var v in Volumes(r)){
            if(p.ExcludedVolumes.Contains(v.Mount,StringComparer.OrdinalIgnoreCase))continue;var rule=EffectiveDisk(v.Mount,p,v.TotalBytes);if(!rule.Enabled)continue;
            if(v.Error!=""){result.Add(new("disk:"+v.Mount,"Не прочитан том "+v.Mount,Severity.Warning,p.AlertMinutes));continue;}
            if(v.TotalBytes<=0)continue;var percent=100d*v.AvailableBytes/v.TotalBytes;long absolute=(long)rule.FreeMiB*1048576;
            if(absolute>=v.TotalBytes&&absolute>0)result.Add(new("disk-policy:"+v.Mount,"Абсолютный порог не соответствует размеру "+v.Mount,Severity.Warning,0));
            if(rule.FreePercent>0&&percent<=rule.FreePercent||absolute>0&&absolute<v.TotalBytes&&v.AvailableBytes<=absolute)result.Add(new("disk:"+v.Mount,$"Мало места на {v.Mount}: {AgentIssueAdvice.Size(v.AvailableBytes)} из {AgentIssueAdvice.Size(v.TotalBytes)} ({percent:0.0}% свободно)",Severity.Critical,p.AlertMinutes));
            if(v.Inodes is >0&&v.FreeInodes is long free&&(rule.InodeFreePercent>0&&100d*free/v.Inodes<=rule.InodeFreePercent||rule.InodeMinimum>0&&free<=rule.InodeMinimum))result.Add(new("inode:"+v.Mount,$"Мало свободных inode на {v.Mount}: {free} из {v.Inodes}",Severity.Critical,p.AlertMinutes));
        }return result.ToArray();
    }
    public static AgentServiceRule[] EffectiveServices(AgentPolicy p)
    {
        var rows=new Dictionary<string,AgentServiceRule>(StringComparer.OrdinalIgnoreCase){["sysadminassistant-agent-update.service"]=new("sysadminassistant-agent-update.service",Severity:Severity.Warning),["sysadminassistant-agent-update.timer"]=new("sysadminassistant-agent-update.timer",Severity:Severity.Warning,MaxIntervalMinutes:15)};
        foreach(var rule in p.Services)rows[rule.Name]=rule;return rows.Values.Where(r=>!p.ExcludedServices.Contains(r.Name,StringComparer.OrdinalIgnoreCase)).ToArray();
    }
    public static AgentFinding[] Systemd(AgentReport r,AgentPolicy p)
    {
        if(r.Linux==null||r.Errors.Contains("Не удалось получить службы systemd",StringComparer.Ordinal)||r.UptimeSeconds<p.BootGraceMinutes*60)return [];
        var list=new List<AgentFinding>();foreach(var rule in EffectiveServices(p).Where(s=>s.Enabled)){
            var u=r.Linux.Units.FirstOrDefault(u=>u.Name==rule.Name);if(u==null||u.Error!=""){list.Add(new("service:"+rule.Name,"Нет подтверждённых данных systemd: "+rule.Name,Severity.Warning,rule.DelayMinutes));continue;}
            if(rule.Expected=="Stopped"){if(u.Active is "active" or "activating")list.Add(new("service:"+u.Name,"Служба должна быть остановлена: "+u.Name,rule.Severity,rule.DelayMinutes));continue;}
            if(u.Type=="timer"){
                if(u.Active!="active")list.Add(new("service:"+u.Name,"Таймер не активен: "+u.Name+" · "+u.Active+"/"+u.Sub,rule.Severity,rule.DelayMinutes));
                var job=r.Linux.Units.FirstOrDefault(x=>x.Name==u.Trigger);if(job!=null)EvaluateJob(job,rule,u.Name,list,r,p);
                if(u.NextRunUtc is DateTime due&&r.CapturedUtc>due.AddMinutes(Math.Max(1,rule.DelayMinutes)))list.Add(new("systemd-schedule:"+u.Name,"Просрочено запланированное срабатывание таймера "+u.Name,Severity.Warning,0));
            }else if(u.Type=="oneshot")EvaluateJob(u,rule,u.Name,list,r,p);
            else if(!SocketActivated(u,r)&&(u.Active!="active"||u.Sub is "failed" or "dead"))list.Add(new("service:"+u.Name,$"Служба {u.Name}: {u.Active}/{u.Sub}; Result={u.Result}",rule.Severity,rule.DelayMinutes));
        }return list.GroupBy(f=>f.Key,StringComparer.Ordinal).Select(g=>g.OrderByDescending(f=>f.Severity).First()).ToArray();
    }
    // A missing finding during a transition is not proof of recovery.
    public static bool CanRecover(string key,AgentReport report,AgentPolicy policy,DateTime problemUtc)
    {
        if(!report.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)||!(key.StartsWith("systemd-job:")||key.StartsWith("systemd-schedule:")||key.StartsWith("service:")))return true;
        var name=key[(key.IndexOf(':')+1)..];var rules=EffectiveServices(policy);var rule=rules.FirstOrDefault(r=>r.Name==name);
        var unit=report.Linux?.Units.FirstOrDefault(u=>u.Name==name);
        // Removing/disabling a configured check explicitly ends monitoring of it.
        bool selected=rule?.Enabled==true||key.StartsWith("systemd-job:")&&rules.Any(r=>r.Enabled&&report.Linux?.Units.Any(u=>u.Name==r.Name&&u.Trigger==name)==true);
        if(!selected&&key.StartsWith("systemd-job:")&&rules.Any(r=>r.Enabled&&r.Name.EndsWith(".timer",StringComparison.Ordinal)&&(report.Linux==null||!report.Linux.Units.Any(u=>u.Name==r.Name&&u.Error==""))))return false;
        if(!selected)return true;
        if(report.CapturedUtc.Kind!=DateTimeKind.Utc||report.CapturedUtc<problemUtc)return false;
        if(report.Linux==null||report.Errors.Contains("Не удалось получить службы systemd",StringComparer.Ordinal)||unit==null||unit.Error!=""||unit.Active is "activating" or "deactivating" or "reloading")return false;
        if(rule?.Expected=="Stopped")return unit.Active=="inactive";
        if(key.StartsWith("systemd-schedule:")&&unit.Type=="timer"){
            if(unit.Active!="active")return false;
            unit=report.Linux.Units.FirstOrDefault(u=>u.Name==unit.Trigger);if(unit==null)return false;
        }
        if(key.StartsWith("systemd-job:")||key.StartsWith("systemd-schedule:")||unit.Type=="oneshot")return SuccessfulCompletion(unit,problemUtc,report.CapturedUtc);
        return SocketActivated(unit,report)||unit.Active=="active"&&(unit.Type=="timer"||unit.Sub is "running" or "exited");
    }
    // A declared socket dependency must actually be listening in this same report.
    public static bool SocketActivated(LinuxUnit unit,AgentReport report)=>unit.Name.EndsWith(".service",StringComparison.Ordinal)&&unit.Type!="oneshot"&&unit.Error==""&&unit.Active=="inactive"&&unit.Sub=="dead"&&unit.Result is "" or "success"&&unit.ExitStatus==0&&unit.ExitCode<=1&&unit.Trigger.EndsWith(".socket",StringComparison.Ordinal)&&report.Linux?.Units.Any(s=>unit.Trigger.Split(' ',StringSplitOptions.RemoveEmptyEntries).Contains(s.Name,StringComparer.Ordinal)&&s.Type=="socket"&&s.Error==""&&s.Active=="active"&&s.Sub=="listening")==true;
    public static bool SuccessfulCompletion(LinuxUnit unit,DateTime sinceUtc,DateTime capturedUtc)=>unit.Error==""&&unit.Result=="success"&&unit.ExitCode==1&&unit.ExitStatus==0&&unit.Active is "inactive" or "active"&&unit.Sub is "dead" or "exited"&&unit.LastRunUtc is DateTime completed&&completed.Kind==DateTimeKind.Utc&&completed>=sinceUtc&&completed<=capturedUtc.AddSeconds(30);
    private static void EvaluateJob(LinuxUnit u,AgentServiceRule rule,string owner,List<AgentFinding> list,AgentReport r,AgentPolicy p)
    {
        if(u.Active is "activating" or "deactivating")return;
        if(u.Active=="failed"||u.Result is not("success" or "")||u.ExitStatus!=0||u.ExitCode is >1)list.Add(new("systemd-job:"+u.Name,$"Задание {u.Name}: Result={u.Result}; код завершения {u.ExitStatus}",rule.Severity,0));
        if(rule.MaxIntervalMinutes is int expected){var late=u.LastRunUtc is DateTime at?r.CapturedUtc-at>TimeSpan.FromMinutes(expected+rule.DelayMinutes):r.UptimeSeconds>(expected+p.BootGraceMinutes)*60L;if(late)list.Add(new("systemd-schedule:"+owner,$"Нет выполнения {u.Name} в ожидаемые {expected} мин.",Severity.Warning,0));}
    }
}
