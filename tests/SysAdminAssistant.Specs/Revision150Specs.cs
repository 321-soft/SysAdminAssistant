using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class Revision150Specs
{
 static Dictionary<string,string> R(params string[] fields)=>fields.ToDictionary(s=>s[..s.IndexOf('=')],s=>s[(s.IndexOf('=')+1)..]);
 static RouterAuditConfig Fixture(){var c=new RouterAuditConfig();foreach(var key in RouterAuditReader.Commands.Keys)c.Tables[key]=[];c.Tables["interfaces"]=[R("name=uplink"),R("name=inside")];c.Tables["lists"]=[R("name=WAN")];c.Tables["members"]=[R("list=WAN","interface=uplink")];c.Tables["addresses4"]=[R("address=198.51.100.2/24","interface=uplink"),R("address=10.23.0.1/24","interface=inside")];c.Tables["dns"]=[R("allow-remote-requests=true")];c.Tables["ipv6-settings"]=[R("disable-ipv6=yes")];c.Tables["mac"]=[R("allowed-interface-list=none")];c.Tables["mac-winbox"]=[R("allowed-interface-list=none")];c.Tables["neighbors"]=[R("discover-interface-list=none")];c.Tables["filter4"]=[R(".id=*D","chain=forward","action=drop","connection-state=new","connection-nat-state=!dstnat","in-interface=uplink"),R(".id=*E","chain=input","action=drop","in-interface=uplink")];return c;}
 static RouterAuditFinding Nat(RouterAuditConfig c)=>RouterAudit.Analyze(c).Findings.Single(f=>f.Key.StartsWith("nat:"));
 public static void Run(Action<string,bool> check){
  foreach(var protocol in new[]{"tcp","udp"})foreach(var port in new[]{12001,45678,60001}){
   var c=Fixture();c.Tables["filter4"][0]["protocol"]=protocol;c.Tables["filter4"][0]["dst-port"]=port.ToString();c.Tables["mangle4"]=[R(".id=*A","chain=prerouting","action=mark-routing","new-routing-mark=other")];
   var result=RouterAudit.Analyze(c);var f=result.Findings.Single(f=>f.Key=="forward:32:uplink:"+port+(protocol=="udp"?":udp":""));
   check("Routing uncertainty preserves terminal firewall deny "+protocol+"/"+port,f.Status=="Закрыто"&&f.Paths.All(p=>p.Verdict==FirewallVerdict.Blocked));
   c.Tables["filter4"].Insert(0,R(".id=*B","chain=forward","action=accept","protocol="+protocol,"dst-port="+port));f=RouterAudit.Analyze(c).Findings.Single(f=>f.Key=="forward:32:uplink:"+port+(protocol=="udp"?":udp":""));
   check("Early accept before deny cannot become green with policy routing "+protocol+"/"+port,f.Status=="Нужна проверка"&&f.Paths.Any(p=>p.Possible.Length>0));
   foreach(var mode in new[]{"lan","output","bypass","disabled","unreachable"}){
    c=Fixture();c.Tables["nat4"]=[R(".id=*1","chain=dstnat","action=dst-nat","protocol="+protocol,"dst-port="+port,"to-addresses=10.23.0.8","to-ports="+(port+1),"src-address=203.0.113.7")];
    c.Tables["filter4"]=[R(".id=*2","chain=forward","action=accept","connection-nat-state=dstnat"),R(".id=*3","chain=forward","action=drop"),R(".id=*4","chain=input","action=drop")];
    var mark=R(".id=*5","chain="+(mode=="output"?"output":mode=="unreachable"?"not-called":"prerouting"),"action=mark-routing","new-routing-mark=other");if(mode=="lan")mark["in-interface"]="inside";if(mode=="disabled")mark["disabled"]="true";
    c.Tables["mangle4"]=[mark];if(mode=="bypass")c.Tables["mangle4"].Insert(0,R(".id=*6","chain=prerouting","action=accept"));
    check("Irrelevant mangle retains NAT whitelist "+mode+" "+protocol+"/"+port,Nat(c).Status=="Разрешено firewall: ограниченные источники");
   }
   c=Fixture();c.Tables["nat4"]=[R(".id=*1","chain=dstnat","action=dst-nat","protocol="+protocol,"dst-port="+port,"to-addresses=10.23.0.8","to-ports="+(port+1),"src-address=203.0.113.7")];
   c.Tables["filter4"]=[R(".id=*2","chain=forward","action=accept"),R(".id=*3","chain=input","action=accept")];c.Tables["mangle4"]=[R(".id=*4","chain=prerouting","action=mark-routing","new-routing-mark=other","protocol="+protocol,"dst-port="+port)];
   var n=Nat(c);check("Relevant prerouting uses external port and retains NAT source constraint "+protocol+"/"+port,n.Status=="Нужна проверка"&&n.Paths.Single().Possible.Single().First=="203.0.113.7"&&n.Paths.Single().Possible.Single().Last=="203.0.113.7");
   c.Tables["mangle4"][0]["dst-port"]=(port+1).ToString();check("Mangle before DNAT does not match translated port "+protocol+"/"+port,Nat(c).Status=="Разрешено firewall: ограниченные источники");
   c.Tables["mangle4"][0]["dst-port"]=port.ToString();c.Tables["filter4"]=[R(".id=*2","chain=forward","action=drop"),R(".id=*3","chain=input","action=drop")];check("DNAT plus applicable mangle cannot reopen terminal filter drops "+protocol+"/"+port,Nat(c).Status=="Закрыто");
  }
  foreach(var protocol in new[]{"tcp","udp"})foreach(var external in new[]{12001,45678,60001}){
   var m=Fixture();m.Tables["nat4"]=[R(".id=*1","chain=dstnat","action=dst-nat","protocol="+protocol,"dst-port="+external,"to-addresses=10.23.0.8","to-ports="+(external+1))];m.Tables["filter4"]=[R(".id=*2","chain=forward","action=accept")];m.Tables["mangle4"]=[R(".id=*3","chain=prerouting","action=drop","protocol="+protocol,"dst-port="+external)];
   check("Mangle prerouting drop is enforced before DNAT "+protocol+"/"+external,Nat(m).Status=="Закрыто");
   m.Tables["mangle4"].Insert(0,R(".id=*4","chain=prerouting","action=mark-routing","new-routing-mark=other","passthrough=yes"));check("Routing mark passthrough still reaches subsequent mangle drop "+protocol+"/"+external,Nat(m).Status=="Закрыто");
   m.Tables["mangle4"][0]["passthrough"]="no";check("Terminal mark skips subsequent mangle drop but does not fake closure "+protocol+"/"+external,Nat(m).Paths.Any(p=>p.Possible.Length>0));
   m.Tables["filter4"]=[R(".id=*2","chain=forward","action=drop")];check("Mangle passthrough no cannot bypass terminal filter drop "+protocol+"/"+external,Nat(m).Status=="Закрыто");
  }
  var direct=Fixture();direct.Tables["nat4"]=[R(".id=*1","chain=dstnat","action=dst-nat","protocol=tcp","dst-port=60001","to-addresses=10.23.0.8","to-ports=60002")];direct.Tables["services"]=[R("name=ssh","port=60001")];direct.Tables["filter4"]=[R(".id=*2","chain=input","action=accept"),R(".id=*3","chain=forward","action=drop")];direct.Tables["mangle4"]=[R(".id=*4","chain=prerouting","action=mark-routing","new-routing-mark=other")];
  var directResult=RouterAudit.Analyze(direct);
  check("Mangle cannot restore direct service traffic already consumed by DNAT",directResult.Findings.Single(f=>f.Key=="service:32:uplink:ssh").Status=="Закрыто");
  check("Remote DNAT cannot bypass forward denial through unrelated input allow",directResult.Findings.Single(f=>f.Key.StartsWith("nat:")).Status=="Закрыто");
  var x=Fixture();x.Tables["mangle4"]=[R(".id=*1","chain=prerouting","action=mark-routing","new-routing-mark=other")];
  check("Common unsolicited ingress deny closes every probed transit port despite mangle",RouterAudit.Analyze(x).Findings.Where(f=>f.Key.StartsWith("forward:")).All(f=>f.Status=="Закрыто"));
  check("Policy routing feature alone is informational",RouterAudit.Analyze(x).Findings.Single(f=>f.Key=="policy:mangle4").Severity==Severity.Info);
  x.Tables["mangle4"]=[];x.Errors["mangle4"]="unreadable";check("Unread mangle cannot erase firewall drop proof",RouterAudit.Analyze(x).Findings.Where(f=>f.Key.StartsWith("forward:")).All(f=>f.Status=="Закрыто"));
  x=Fixture();x.Tables["mangle4"]=[R(".id=*1","chain=prerouting","action=jump","jump-target=policy"),R(".id=*2","chain=policy","action=mark-routing","new-routing-mark=other")];check("Reachable custom mangle chain is examined through subsequent firewall",RouterAudit.Analyze(x).Findings.Where(f=>f.Key.StartsWith("forward:")).All(f=>f.Status=="Закрыто"&&f.Evidence.Contains("Mangle не обходит firewall")));
  x=Fixture();x.Tables["filter4"]=[R(".id=*1","chain=input","action=drop","src-address=203.0.113.7","connection-mark=unknown"),R(".id=*2","chain=input","action=accept")];x.Tables["services"]=[R("name=ssh","port=45678")];var outside=RouterAudit.Analyze(x).Findings.Single(f=>f.Key=="service:32:uplink:ssh").Paths.First(p=>p.FinalDestination=="198.51.100.2");
  check("Unknown narrow matcher does not contaminate known complement sources",outside.Confirmed.Length>0&&!outside.Confirmed.Any(a=>a.First=="203.0.113.7"&&a.Last=="203.0.113.7"));
 }
}
