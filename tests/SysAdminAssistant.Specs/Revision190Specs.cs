using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class Revision190Specs
{
 static Dictionary<string,string> R(params string[] fields)=>fields.ToDictionary(s=>s[..s.IndexOf('=')],s=>s[(s.IndexOf('=')+1)..]);
 static RouterAuditConfig Config(){var c=new RouterAuditConfig();foreach(var key in RouterAuditReader.Commands.Keys)c.Tables[key]=[];
 c.Tables["interfaces"]=[R("name=outside"),R("name=inside")];c.Tables["lists"]=[R("name=WAN")];c.Tables["members"]=[R("list=WAN","interface=outside")];
 c.Tables["addresses4"]=[R("address=198.51.100.2/24","interface=outside"),R("address=10.23.0.1/24","interface=inside")];c.Tables["dns"]=[R("allow-remote-requests=no")];c.Tables["ipv6-settings"]=[R("disable-ipv6=yes")];
 c.Tables["address-lists4"]=[R("list=blocked","address=blocked.example.invalid"),R("list=blocked","address=203.0.113.9")];
 c.Tables["filter4"]=[R(".id=*1","chain=input","action=drop"),R(".id=*2","chain=forward","action=drop","src-address-list=blocked"),R(".id=*3","chain=forward","action=drop","protocol=udp","dst-port=!443,1194,1701,500,4500","dst-address=10.23.0.8"),R(".id=*4","chain=forward","action=accept","connection-state=new","connection-nat-state=dstnat"),R(".id=*5","chain=forward","action=drop","connection-state=new","connection-nat-state=!dstnat")];
 c.Tables["nat4"]=[R(".id=*6","chain=dstnat","action=dst-nat","dst-address=198.51.100.2","to-addresses=10.23.0.8")];return c;}
 static RouterAuditFinding Group(RouterAuditResult result)=>result.DisplayFindings.Single(f=>f.Members.Count>0);
 public static void Run(string folder,Action<string,bool> check){
  var c=Config();var result=RouterAudit.Analyze(c);var g=Group(result);var raw=result.Findings.Where(f=>f.Key.StartsWith("nat:")).ToArray();
  check("Whole-address DNAT has one publication instead of per-port rows",raw.Length>30&&result.DisplayFindings.Count(f=>f.Members.Count>0)==1&&g.Members.Count==raw.Length);
  check("Publication count is one warning while uncertain packet proofs stay intact",result.Warnings==1&&g.Severity==Severity.Warning&&raw.Any(f=>f.Status=="Нужна проверка"));
  var rows=RouterAuditPresentation.Ports(g);
  check("Publication shows blocked UDP DNS and uncertain TCP DNS separately",rows.Any(r=>r.Protocol=="UDP"&&r.ExternalPort==53&&r.Path.Verdict==FirewallVerdict.Blocked)&&rows.Any(r=>r.Protocol=="TCP"&&r.ExternalPort==53&&r.Path.Verdict==FirewallVerdict.Indeterminate));
  check("Unknown proof identifies actual source address list",g.Evidence.Contains("src-address-list=blocked")&&rows.Any(r=>r.Finding.Evidence.Contains("src-address-list=blocked")));
  check("Grouping does not claim exhaustive port or live service scan",g.Recommendation.Contains("не все 65535")&&g.Recommendation.Contains("службы"));
  var key=g.Key;c.Tables["nat4"][0][".id"]="*AA";c.Tables["nat4"][0]["comment"]="renamed";check("Publication key survives rule IDs comments and changed evidence",Group(RouterAudit.Analyze(c)).Key==key);
  result.Findings.Reverse();check("Publication fingerprint independent of finding order",Group(result).Fingerprint==g.Fingerprint);
  var serialized=JsonSerializer.Serialize(result);var round=JsonSerializer.Deserialize<RouterAuditResult>(serialized)!;check("Publication rebuilt from serialized original proofs",Group(round).Key==key&&Group(round).Members.Count==raw.Length);
  c.Tables["address-lists4"].RemoveAll(r=>r["address"].Contains("example"));var known=RouterAudit.Analyze(c);check("Publication retains critical TCP exposure and UDP deny",Group(known).Severity==Severity.Critical&&RouterAuditPresentation.Ports(Group(known)).Any(r=>r.Protocol=="UDP"&&r.ExternalPort==53&&r.Path.Verdict==FirewallVerdict.Blocked));
  c.Tables["filter4"].Insert(0,R(".id=*AB","chain=forward","action=drop"));var closed=RouterAudit.Analyze(c);check("Earlier terminal drop makes publication informational and all paths blocked",Group(closed).Severity==Severity.Info&&RouterAuditPresentation.Ports(Group(closed)).All(r=>r.Path.Verdict==FirewallVerdict.Blocked));
  c=Config();c.Tables["nat4"].Add(R(".id=*B","chain=dstnat","action=dst-nat","dst-port=45678","protocol=tcp","to-addresses=10.23.0.9"));check("Different internal destination is a separate publication",RouterAudit.Analyze(c).DisplayFindings.Count(f=>f.Key.StartsWith("publication:"))==2);
  var store=new NetworkStore(Path.Combine(folder,"publication-ignore"));
  void Apply(RouterAuditResult current){store.State(s=>{var h=s.Histories.SingleOrDefault(h=>h.DeviceId==1);if(h==null){h=new(){DeviceId=1};s.Histories.Add(h);}RouterAuditStore.Apply(s,h,current,"Fixture");return true;},true);}
  c=Config();Apply(RouterAudit.Analyze(c));var view=RouterAuditStore.View(store,1);g=Group(view.Result!);RouterAuditStore.Ignore(store,new(1,g.Key,g.Fingerprint,true,view.Hash),"fixture");
  check("Ignore publication suppresses all its per-port findings",RouterAuditStore.View(store,1).Result!.Findings.Where(f=>f.Key.StartsWith("nat:")).All(f=>f.Ignored));
  c.Tables["filter4"][2]["dst-port"]="!443,1194,1701,500,4500,45679";Apply(RouterAudit.Analyze(c));var restarted=new NetworkStore(store.DirectoryPath);view=RouterAuditStore.View(restarted,1);g=Group(view.Result!);
  check("Publication ignore survives new tested ports reread and store reopen",g.Key==key&&g.Ignored&&view.Result!.Warnings==0&&view.Result.Critical==0);
  RouterAuditStore.Ignore(restarted,new(1,g.Key,g.Fingerprint,false,view.Hash),"fixture");view=RouterAuditStore.View(restarted,1);check("Unignore publication restores active warning",!Group(view.Result!).Ignored&&view.Result!.Warnings==1);
  var individual=view.Result!.Findings.First(f=>f.Key.StartsWith("nat:")&&f.Severity==Severity.Warning);RouterAuditStore.Ignore(restarted,new(1,individual.Key,individual.Fingerprint,true,view.Hash),"legacy");view=RouterAuditStore.View(restarted,1);g=Group(view.Result!);
  check("Legacy single-port exception never hides whole publication",!g.Ignored&&g.Members.Count(f=>f.Ignored)==1&&view.Result!.Warnings==1);
  RouterAuditStore.Ignore(restarted,new(1,g.Key,g.Fingerprint,false,view.Hash),"fixture");check("Unignore group clears its legacy per-port exceptions",Group(RouterAuditStore.View(restarted,1).Result!).Members.All(f=>!f.Ignored));
  var one=result.Findings.First(f=>f.Key.StartsWith("nat:"));var only=new RouterAuditResult{Findings=[one]};check("Publication key remains stable when a single tested port remains",Group(only).Key==key);
 }
}
