namespace SysAdminAssistant.Core;
public sealed record AgentGauge(string Key,string Label,double? Percent,string Value,string Detail,Severity? Severity=null);
public static class AgentGaugeModel
{
    public static bool Stale(AgentReport r,DateTime? seen,AgentPolicy p,DateTime utc)=>seen==null||utc-seen.Value>TimeSpan.FromSeconds(Math.Max(90,p.IntervalSeconds*3))||utc-r.CapturedUtc>TimeSpan.FromSeconds(Math.Max(90,p.IntervalSeconds*3));
    public static AgentGauge[] Build(AgentReport r,AgentPolicy p)
    {
        var findings=AgentChecks.Evaluate(r,p);Severity? Level(string key)=>findings.Where(f=>f.Key==key).Select(f=>(Severity?)f.Severity).Max();
        string Advice(string key)=>findings.FirstOrDefault(f=>f.Key==key) is {} f?AgentIssueAdvice.Metric(f,r,p):"Порог не превышен. Изменить условия можно в настройках агента этого устройства.";
        var rows=new List<AgentGauge>{new("cpu","Процессор",r.CpuPercent,r.CpuPercent is double c?$"{c:0.0}% · {r.CpuCount} ядер":"нет данных",r.CpuName+$"\nПорог ≥ {p.CpuPercent}%\n"+Advice("cpu"),Level("cpu")),new("ram","Оперативная память",r.MemoryTotalBytes>0?100d*(r.MemoryTotalBytes-r.MemoryAvailableBytes)/r.MemoryTotalBytes:null,$"Занято {AgentIssueAdvice.Size(r.MemoryTotalBytes-r.MemoryAvailableBytes)} из {AgentIssueAdvice.Size(r.MemoryTotalBytes)} · доступно {AgentIssueAdvice.Size(r.MemoryAvailableBytes)}",$"Порог ≥ {p.RamPercent}%\n"+Advice("ram"),Level("ram"))};
        bool linux=r.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase);
        foreach(var v in LinuxPolicy.Volumes(r)){
            var rule=LinuxPolicy.EffectiveDisk(v.Mount,p,v.TotalBytes);var free=v.TotalBytes>0?100d*v.AvailableBytes/v.TotalBytes:0;var severity=Level("disk:"+v.Mount)??Level("inode:"+v.Mount)??Level("disk-policy:"+v.Mount);
            var disabled=p.ExcludedVolumes.Contains(v.Mount,StringComparer.OrdinalIgnoreCase)||!rule.Enabled;
            var detail=$"Хост {r.MachineName}\nТочка / буква: {v.Mount}\n"+(linux?$"Источник: {v.Source}; ФС: {v.FileSystem}; тип: {v.DeviceKind}; mount root: {v.MountRoot}\nЗарезервировано ФС: {AgentIssueAdvice.Size(v.ReservedBytes)}\nInode: {v.FreeInodes?.ToString()??"нет данных"} / {v.Inodes?.ToString()??"нет данных"}\n":"")+$"Порог свободного места: ≤ {rule.FreePercent}% ИЛИ ≤ {rule.FreeMiB} МиБ. "+(disabled?"Контроль выключен.":"Контроль включён.")+"\n"+Advice("disk:"+v.Mount)+"\n"+(Level("inode:"+v.Mount)!=null?Advice("inode:"+v.Mount):"");
            rows.Add(new("disk:"+v.Mount,v.Mount,v.TotalBytes>0&&v.Error==""?100d*v.UsedBytes/v.TotalBytes:null,v.Error!=""?v.Error:$"Занято {AgentIssueAdvice.Size(v.UsedBytes)} из {AgentIssueAdvice.Size(v.TotalBytes)} · свободно {AgentIssueAdvice.Size(v.AvailableBytes)} ({free:0.0}%)",detail,severity));
        }
        if(linux&&r.SwapTotalBytes>0)rows.Add(new("swap","Swap",100d*(r.SwapTotalBytes-r.SwapFreeBytes)/r.SwapTotalBytes,$"Занято {AgentIssueAdvice.Size(r.SwapTotalBytes-r.SwapFreeBytes)} из {AgentIssueAdvice.Size(r.SwapTotalBytes)}",Advice("swap")+"\nСам по себе занятый swap не доказывает нехватку RAM.",Level("swap")));
        if(AgentNetworkNames.Primary(r) is {} n){var rule=p.NetworkRules?.FirstOrDefault(v=>v.Name==n.Name&&v.Enabled);long capacity=rule?.CapacityMbps>0?(long)rule.CapacityMbps*1000000:n.LinkBitsPerSecond;double? percent=capacity>0?100d*Math.Max(n.ReceiveBytesPerSecond,n.SendBytesPerSecond)*8/capacity:null;rows.Add(new("network:"+n.Name,n.Name+" · "+n.State,n.State=="up"?percent:null,$"↓ {AgentIssueAdvice.Size((long)n.ReceiveBytesPerSecond)}/с   ↑ {AgentIssueAdvice.Size((long)n.SendBytesPerSecond)}/с"+(capacity>0?$" · {capacity/1000000d:0.##} Мбит/с":" · скорость канала неизвестна"),"Процент вычислен по более загруженному направлению; вход и выход не складываются.\n"+(rule!=null?$"Порог ≥ {rule.WarnPercent}%, задержка {rule.DelayMinutes} мин.":"Индивидуальный контроль интерфейса не настроен.")+"\n"+Advice("network:"+n.Name),Level("network:"+n.Name)));}
        return rows.ToArray();
    }
}
