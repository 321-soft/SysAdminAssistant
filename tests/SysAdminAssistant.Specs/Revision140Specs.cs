using System.Net;
using System.Numerics;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class Revision140Specs {
 record Rule(string Chain,string Action,string Source="",bool Not=false,string Jump="",string State="",int Port=0,bool Disabled=false,bool Invalid=false,bool List=false);
 static Dictionary<string,string> R(params string[] fields)=>fields.ToDictionary(s=>s[..s.IndexOf('=')],s=>s[(s.IndexOf('=')+1)..]);
 static RouterAuditConfig Fixture(){var c=new RouterAuditConfig();foreach(var key in RouterAuditReader.Commands.Keys)c.Tables[key]=[];c.Tables["interfaces"]=[R("name=uplink"),R("name=inside")];c.Tables["lists"]=[R("name=WAN")];c.Tables["members"]=[R("list=WAN","interface=uplink")];c.Tables["addresses4"]=[R("address=198.51.100.2/24","interface=uplink"),R("address=10.23.0.1/24","interface=inside")];c.Tables["dns"]=[R("allow-remote-requests=false")];c.Tables["ipv6-settings"]=[R("disable-ipv6=yes")];c.Tables["mac"]=[R("allowed-interface-list=none")];c.Tables["mac-winbox"]=[R("allowed-interface-list=none")];c.Tables["neighbors"]=[R("discover-interface-list=none")];return c;}
 static List<Dictionary<string,string>> Encode(Rule[] rules)=>rules.Select((r,i)=>{var row=R(".id=*"+(i+1).ToString("X"),"chain="+r.Chain,"action="+r.Action);if(r.Source!="")row[r.List?"src-address-list":"src-address"]=(r.Not?"!":"")+(r.List?"allowed":r.Source+"/32");if(r.Jump!="")row["jump-target"]=r.Jump;if(r.State!="")row["connection-state"]=r.State;if(r.Port>0)row["dst-port"]=r.Port.ToString();if(r.Disabled)row["disabled"]="true";if(r.Invalid)row["invalid"]="true";return row;}).ToList();
 // Independent concrete reference: returns from a child chain continue in the caller.
 // It uses typed predicates and never invokes RouterAudit or AuditAddresses.
 static bool Oracle(Rule[] rules,string chain,string source,int port){bool? Visit(string name){foreach(var r in rules.Where(r=>r.Chain==name&&!r.Disabled&&!r.Invalid)){if(r.Source!=""&&((source==r.Source)==r.Not))continue;if(r.State!=""&&r.State!="new")continue;if(r.Port>0&&r.Port!=port)continue;switch(r.Action){case "accept":case "fasttrack-connection":return true;case "drop":case "reject":return false;case "return":return null;case "jump":var child=Visit(r.Jump);if(child!=null)return child;break;}}return null;}return Visit(chain)??true;}
 static bool Contains(AuditSourceRange[] ranges,string ip){var n=new BigInteger(IPAddress.Parse(ip).GetAddressBytes(),true,true);return ranges.Any(r=>n>=new BigInteger(IPAddress.Parse(r.First).GetAddressBytes(),true,true)&&n<=new BigInteger(IPAddress.Parse(r.Last).GetAddressBytes(),true,true));}
 public static void Run(Action<string,bool> check){
  foreach(var protocol in new[]{"tcp","udp"})foreach(var port in new[]{12001,45678,60001})for(var scenario=0;scenario<14;scenario++){
   Rule[] forward=scenario switch {
    0=>[new("forward","drop"),new("forward","accept")],
    1=>[new("forward","accept"),new("forward","drop")],
    2=>[new("forward","accept","203.0.113.1"),new("forward","drop")],
    3=>[new("forward","jump",Jump:"guard"),new("forward","drop"),new("guard","accept","203.0.113.1"),new("guard","return")],
    4=>[new("forward","jump",Jump:"guard"),new("forward","accept"),new("guard","drop","203.0.113.1"),new("guard","return")],
    5=>[new("forward","fasttrack-connection",State:"established,related"),new("forward","drop")],
    6=>[new("forward","drop","203.0.113.1",Not:true),new("forward","accept")],
    7=>[new("forward","drop",Disabled:true),new("forward","accept")],
    10=>[new("input","drop"),new("forward","accept")],
    11=>[new("forward","drop",Port:port),new("forward","accept")],
    12=>[new("forward","drop",Invalid:true),new("forward","accept")],
    13=>[new("forward","accept","203.0.113.1",List:true),new("forward","drop")],
    _=>[new("forward","accept")]
   };
   Rule[] raw=scenario==8?[new("prerouting","drop")]:scenario==9?[new("prerouting","drop","203.0.113.1")]:[];
   var c=Fixture();c.Tables["filter4"]=Encode(forward);c.Tables["raw4"]=Encode(raw);c.Tables["address-lists4"]=[R("list=allowed","address=203.0.113.1")];c.Tables["nat4"]=[R(".id=*A","chain=dstnat","action=dst-nat","protocol="+protocol,"dst-port="+port,"to-addresses=10.23.0.8","to-ports="+(port+1))];
   var finding=RouterAudit.Analyze(c).Findings.Single(f=>f.Key=="nat:"+protocol+":"+port+":10.23.0.8:"+(port+1));var path=finding.Paths.Single();
   var ok=new[]{"203.0.113.1","203.0.113.2","198.51.100.19","10.0.0.7"}.All(source=>{var expected=Oracle(raw,"prerouting",source,port)&&Oracle(forward,"forward",source,port+1);return Contains(path.Confirmed,source)==expected&&Contains(path.Possible,source)==expected;});
   check("Independent packet oracle "+protocol+"/"+port+" scenario "+scenario,ok&&path.ExternalPort==port&&path.FinalPort==port+1&&path.Chain=="forward"&&path.Stages[0].StartsWith("RAW"));
  }
  var uncertain=Fixture();uncertain.Tables["services"]=[R("name=ssh","port=45678")];uncertain.Tables["filter4"]=[R(".id=*U","chain=input","action=accept","src-address=203.0.113.1","connection-mark=unknown"),R(".id=*D","chain=input","action=drop")];
  RouterAuditFinding S(RouterAuditConfig c)=>RouterAudit.Analyze(c).Findings.Single(f=>f.Key=="service:32:uplink:ssh");
  check("Unknown narrow branch is not a proven trusted allowlist",S(uncertain).Status=="Нужна проверка"&&S(uncertain).Paths.First(p=>p.FinalDestination=="198.51.100.2").Verdict==FirewallVerdict.Indeterminate&&S(uncertain).Paths.First(p=>p.FinalDestination=="198.51.100.2").UnknownReasons.Length>0);
  uncertain.Tables["filter4"][0]["action"]="drop";check("Definite later drop closes even unknown earlier drop branch",S(uncertain).Paths.First(p=>p.FinalDestination=="198.51.100.2").Verdict==FirewallVerdict.Blocked);
  var ipsec=Fixture();ipsec.Tables["filter4"]=[R(".id=*I","chain=forward","action=accept","ipsec-policy=out,ipsec"),R(".id=*D","chain=forward","action=drop")];ipsec.Tables["ipsec-policies"]=[R("src-address=not-an-address","dst-address=10.23.0.0/24","action=encrypt")];ipsec.Tables["nat4"]=[R(".id=*A","chain=dstnat","action=dst-nat","protocol=tcp","dst-port=60001","to-addresses=10.23.0.8","to-ports=60002")];
  check("Unknown IPsec source selector cannot erase possible path",RouterAudit.Analyze(ipsec).Findings.Single(f=>f.Key.StartsWith("nat:")).Paths.Single().Verdict==FirewallVerdict.Indeterminate);
  ipsec.Tables["ipsec-policies"]=[];check("Empty known IPsec selectors do not admit ordinary Internet traffic",RouterAudit.Analyze(ipsec).Findings.Single(f=>f.Key.StartsWith("nat:")).Paths.Single().Verdict==FirewallVerdict.Blocked);
  var route=Fixture();route.Tables["services"]=[R("name=ssh","port=45678")];route.Tables["filter4"]=[R(".id=*D","chain=input","action=drop")];route.Tables["mangle4"]=[R(".id=*M","chain=prerouting","action=mark-routing","new-routing-mark=other")];check("Unsupported routing cannot masquerade as a proven final chain",S(route).Paths.First(p=>p.FinalDestination=="198.51.100.2").Verdict==FirewallVerdict.Indeterminate);
  route.Tables["raw4"]=[R(".id=*R","chain=prerouting","action=drop")];check("Earlier RAW deny proves closure before unsupported routing",S(route).Paths.First(p=>p.FinalDestination=="198.51.100.2").Verdict==FirewallVerdict.Blocked);
  var subnet=Fixture();subnet.Tables["nat4"]=[R(".id=*A","chain=dstnat","action=dst-nat","protocol=tcp","dst-address=198.51.100.0/24","dst-port=60001","to-addresses=10.23.0.8","to-ports=60002")];check("Destination subnet is not approximated by one network-address probe",RouterAudit.Analyze(subnet).Findings.Any(f=>f.Key.StartsWith("nat-unknown:")&&f.Status=="Нужна проверка")&&!RouterAudit.Analyze(subnet).Findings.Any(f=>f.Key.StartsWith("nat:")));
  var v6=Fixture();v6.Tables["addresses4"]=[];v6.Tables["addresses6"]=[R("address=2001:db8:1::1/64","interface=uplink")];v6.Tables["ipv6-settings"]=[];v6.Tables["services"]=[R("name=ssh","port=45678")];v6.Tables["filter6"]=[R(".id=*A","chain=input","action=accept","src-address=2001:db8:2::1/128"),R(".id=*D","chain=input","action=drop")];var proof=RouterAudit.Analyze(v6).Findings.Single(f=>f.Key=="service:128:uplink:ssh").Paths.Single();check("IPv6 proof retains exact approved source and complement denial",proof.Verdict==FirewallVerdict.Restricted&&Contains(proof.Confirmed,"2001:db8:2::1")&&!Contains(proof.Possible,"2001:db8:2::2"));
 }
}
