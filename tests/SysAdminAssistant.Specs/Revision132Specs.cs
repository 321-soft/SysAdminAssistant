using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class Revision132Specs {
 static Dictionary<string,string> R(params string[] fields)=>fields.ToDictionary(s=>s[..s.IndexOf('=')],s=>s[(s.IndexOf('=')+1)..]);
 public static void Run(Action<string,bool> check){
  var c=new RouterAuditConfig();foreach(var key in RouterAuditReader.Commands.Keys)c.Tables[key]=[];c.Tables["interfaces"]=[R("name=uplink")];c.Tables["members"]=[R("list=WAN","interface=uplink")];c.Tables["addresses4"]=[R("address=198.51.100.2/24","interface=uplink")];c.Tables["dns"]=[R("allow-remote-requests=true")];c.Tables["ipv6-settings"]=[R("disable-ipv6=yes")];
  RouterAuditFinding Dns()=>RouterAudit.Analyze(c).Findings.Single(f=>f.Key.StartsWith("dns:"));
  check("DNS enabled with empty input reports public exposure",Dns().Severity==Severity.Warning&&Dns().Status=="Разрешено firewall: любые источники");
  c.Tables["filter4"]=[R(".id=*D","chain=input","action=drop","protocol=udp","dst-port=53"),R(".id=*T","chain=input","action=drop","protocol=tcp","dst-port=53")];check("Explicit TCP UDP DNS blocks give visible closed proof",Dns().Status=="Закрыто"&&Dns().Evidence.Contains(".id=*D")&&Dns().Evidence.Contains(".id=*T"));
  c.Tables["filter4"][0]["disabled"]="true";check("Disabling UDP DNS drop restores warning while TCP remains blocked",Dns().Severity==Severity.Warning&&Dns().Status=="Разрешено firewall: любые источники"&&Dns().Evidence.Contains("Результат пути: Закрыто"));
  c.Tables["filter4"][1]["disabled"]="yes";check("Disabling both DNS drops cannot stay green",Dns().Severity==Severity.Warning&&Dns().Evidence.Contains("udp/53")&&Dns().Evidence.Contains("tcp/53"));
  c.Tables["filter4"].Add(R(".id=*FINAL","chain=input","action=drop","in-interface=uplink"));check("Final matching WAN drop really closes DNS after specific drops disabled",Dns().Status=="Закрыто"&&Dns().Evidence.Contains(".id=*FINAL"));
  c.Tables["filter4"]=[];c.Tables["dns"]=[R("allow-remote-requests=false")];check("Disabled resolver remains a visible diagnostic with firewall result",Dns().Status=="DNS-сервис отключён"&&Dns().Severity==Severity.Info&&Dns().Evidence.Contains("Результат пути: Разрешено firewall: любые источники"));
  c.Tables["dns"]=[];check("Unknown DNS setting is not silently considered safe",Dns().Severity==Severity.Warning&&Dns().Status=="Нужна проверка");
 }
}
