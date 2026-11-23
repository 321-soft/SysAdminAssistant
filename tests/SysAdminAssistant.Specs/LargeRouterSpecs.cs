using System.Numerics;
using System.Net;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class LargeRouterSpecs
{
 private static Dictionary<string,string> R(params string[] fields)=>fields.ToDictionary(s=>s[..s.IndexOf('=')],s=>s[(s.IndexOf('=')+1)..]);
 public static void Run(Action<string,bool> check){var accumulator=new RouterAuditAddresses("geo",32);var start=new BigInteger(IPAddress.Parse("198.18.0.0").GetAddressBytes(),true,true);for(int i=0;i<50000;i++)accumulator.Add(R("list=geo","address="+AuditAddresses.Address(start+i,32)));var compact=accumulator.Finish();check("50000 adjacent MikroTik addresses normalize without loss",accumulator.Count==50000&&compact.Count==1&&AuditAddresses.Try(compact[0]["address"],32,out var merged)&&merged.Spans[0].Hi-merged.Spans[0].Lo+1==50000);
 var scattered=new RouterAuditAddresses("geo",32);for(int i=0;i<50000;i++)scattered.Add(R("list=geo","address="+AuditAddresses.Address(start+2*i,32)));var ranges=scattered.Finish();check("50000 disjoint addresses stay exact rather than broadening into a CIDR",ranges.Count==50000);
 var c=new RouterAuditConfig();foreach(var key in RouterAuditReader.Commands.Keys)c.Tables[key]=[];c.Tables["interfaces"]=[R("name=uplink")];c.Tables["lists"]=[R("name=WAN")];c.Tables["members"]=[R("list=WAN","interface=uplink")];c.Tables["addresses4"]=[R("address=192.0.2.1/24","interface=uplink")];c.Tables["ipv6-settings"]=[R("disable-ipv6=yes")];c.Tables["services"]=[R("name=ssh","port=22")];c.Tables["dns"]=[R("allow-remote-requests=no")];c.Tables["filter4"]=[R(".id=*1","chain=input","action=accept","src-address-list=geo"),R(".id=*2","chain=input","action=drop"),R(".id=*3","chain=forward","action=drop")];c.Tables["address-lists4"]=ranges;var timer=System.Diagnostics.Stopwatch.StartNew();
 var result=RouterAudit.Analyze(c);
 timer.Stop();
 var ssh=result.Findings.SingleOrDefault(f=>f.Key=="service:32:uplink:ssh");
 var stopReason=result.Findings.FirstOrDefault(f=>f.Key is "limits" or "budget")?.Key??"none";
 if(ssh?.Status!="Разрешено firewall: ограниченные источники"||stopReason!="none")
     throw new Exception($"FAIL: Large referenced address list is analyzed instead of rejecting whole config; SSH status: {ssh?.Status??"missing"}; analysis limit: {stopReason}; elapsed: {timer.Elapsed.TotalMilliseconds:F0} ms");
 check("Large referenced address list is analyzed instead of rejecting whole config",true);

 var oldHash=ssh.Fingerprint;c.Tables["address-lists4"][49999]["address"]="203.0.113.99";var changed=RouterAudit.Analyze(c).Findings.Single(f=>f.Key==ssh.Key);check("Ignored finding fingerprint includes members beyond visible address preview",changed.Fingerprint!=oldHash);
 c.Tables["filter4"].Add(R(".id=*4","chain=unused","action=accept","src-address-list=!other"));check("Collector requests only distinct lists referenced by active rules",RouterAuditAddresses.Referenced(c,32).SequenceEqual(new[]{"geo","other"}));c.Tables["filter4"][3]["disabled"]="true";check("Disabled rule does not cause unrelated large list download",RouterAuditAddresses.Referenced(c,32).SequenceEqual(new[]{"geo"}));
 c.Errors["address-lists4:geo"]="permission denied";check("Failed selected list cannot prove a closed firewall",RouterAudit.Analyze(c).Findings.Single(f=>f.Key==ssh.Key).Status=="Нужна проверка");
 var dynamic=new RouterAuditAddresses("live",32);dynamic.Add(R("list=live","address=203.0.113.1","dynamic=true"));check("Dynamic memberships remain uncertain after compression",dynamic.Finish().Any(r=>r.GetValueOrDefault("dynamic")=="true"));
 RouterOsClient.RequireReadCommand(["/ip/firewall/address-list/print","=.proplist=list,address","?list=geo"]);check("Large list query is constrained to read only list selection",true);bool denied=false;try{RouterOsClient.RequireReadCommand(["/ip/firewall/address-list/print","?list=geo","/system/reboot"]);}catch(ArgumentException){denied=true;}check("Address list query cannot smuggle another API command",denied);
 var random=new Random(41);bool exact=true;for(int n=0;n<50;n++){var a=Enumerable.Range(0,30).Select(_=>random.Next(256)).ToHashSet();var b=Enumerable.Range(0,30).Select(_=>random.Next(256)).ToHashSet();var aa=new AuditAddresses(32,a.Select(x=>new AuditAddresses.Span(x,x)));var bb=new AuditAddresses(32,b.Select(x=>new AuditAddresses.Span(x,x)));var difference=aa.Except(bb);var intersection=aa.Intersect(bb);for(int x=0;x<256;x++){bool Has(AuditAddresses s)=>s.Spans.Any(p=>p.Lo<=x&&p.Hi>=x);exact&=Has(difference)==(a.Contains(x)&&!b.Contains(x))&&Has(intersection)==(a.Contains(x)&&b.Contains(x));}}check("Linear address-set operations preserve membership across fragmented sets",exact);
 }
}
