using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed class RouterAuditAddresses(string name,int bits)
{
 private readonly List<AuditAddresses.Span> spans=[];private bool unknown;public int Count {get;private set;}
 public void Add(Dictionary<string,string> row){Count++;if(row.GetValueOrDefault("list")!=name){unknown=true;return;}if(Count>500000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Превышен предел адресов одного списка (500000)"));if(row.GetValueOrDefault("disabled") is "true" or "yes")return;if(row.GetValueOrDefault("dynamic") is "true" or "yes"||(row.GetValueOrDefault("timeout","") is not("" or "0s" or "none-static" or "none-dynamic"))||!AuditAddresses.Try(row.GetValueOrDefault("address",""),bits,out var set)){unknown=true;return;}spans.AddRange(set.Spans);}
 public List<Dictionary<string,string>> Finish(){var set=new AuditAddresses(bits,spans);var rows=set.Spans.Select(s=>new Dictionary<string,string>{{"list",name},{"address",AuditAddresses.Address(s.Lo,bits)+(s.Lo==s.Hi?"":"-"+AuditAddresses.Address(s.Hi,bits))}}).ToList();if(unknown)rows.Add(new(){{"list",name},{"address","<dynamic-or-unresolved>"},{"dynamic","true"}});return rows;}
 public static string[] Referenced(RouterAuditConfig config,int bits)=>new[]{bits==32?"filter4":"filter6",bits==32?"raw4":"raw6",bits==32?"mangle4":"mangle6"}.Concat(bits==32?new[]{"nat4"}:[]).SelectMany(config.Rows).Where(r=>r.GetValueOrDefault("disabled") is not("true" or "yes")).SelectMany(r=>r.Where(p=>p.Key is "src-address-list" or "dst-address-list").Select(p=>p.Value.TrimStart('!'))).Where(n=>n!="").Distinct(StringComparer.Ordinal).Order().ToArray();
}
