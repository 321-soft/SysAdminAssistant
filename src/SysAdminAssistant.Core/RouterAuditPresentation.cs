using System.Security.Cryptography;
using System.Text;
namespace SysAdminAssistant.Core;

public sealed record RouterAuditPortRow(RouterAuditFinding Finding,RouterAuditPath Path)
{
 public string Protocol=>Path.Protocol.ToUpperInvariant();public int ExternalPort=>Path.ExternalPort;public int FinalPort=>Path.FinalPort;
 public string Input=>Path.Input;public string OriginalDestination=>Path.OriginalDestination;public string FinalDestination=>Path.FinalDestination;
 public string State=>(Finding.Ignored?"Игнорируется · ":"")+(Path.Verdict switch{FirewallVerdict.Blocked=>"Закрыто",FirewallVerdict.Allowed=>"Разрешён широкий доступ",FirewallVerdict.Restricted=>"Разрешены ограниченные источники",FirewallVerdict.Indeterminate=>"Не удалось определить",_=>"Не оценено"});
}

// A presentation projection: original per-packet proofs and existing per-port exceptions remain intact.
public static class RouterAuditPresentation
{
 private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
 private static string Identity(RouterAuditFinding f)=>f.Key.StartsWith("nat:",StringComparison.Ordinal)&&f.Paths.Count>0?
  string.Join("\n",f.Paths.Select(p=>$"{p.Family}|{p.Input}|{p.OriginalDestination}|{p.FinalDestination}|{p.Chain}").Distinct().Order(StringComparer.Ordinal)):"single:"+f.Key;
 public static IReadOnlyList<RouterAuditFinding> Group(IEnumerable<RouterAuditFinding> findings)
 {
  return findings.GroupBy(Identity,StringComparer.Ordinal).Select(g=>{
   var members=g.OrderBy(f=>f.Key,StringComparer.Ordinal).ToArray();if(members.Length==1&&!members[0].Key.StartsWith("nat:",StringComparison.Ordinal))return members[0];
   var paths=members.SelectMany(f=>f.Paths).ToList();var active=members.Where(f=>!f.Ignored).ToArray();var relevant=active.Length>0?active:members;
   var significant=relevant.OrderByDescending(f=>f.Severity).ThenBy(f=>f.Key,StringComparer.Ordinal).First();
   var allowed=paths.Count(p=>p.Verdict is FirewallVerdict.Allowed or FirewallVerdict.Restricted);var blocked=paths.Count(p=>p.Verdict==FirewallVerdict.Blocked);var unknown=paths.Count(p=>p.Verdict==FirewallVerdict.Indeterminate);
   var destinations=string.Join(", ",paths.Select(p=>string.IsNullOrEmpty(p.OriginalDestination)?"адрес не определён":p.OriginalDestination).Distinct().Order(StringComparer.Ordinal));
   var targets=string.Join(", ",paths.Select(p=>p.FinalDestination).Distinct().Order(StringComparer.Ordinal));
   var ignored=members.Count(f=>f.Ignored);var summary=$"Публикация {destinations} → {targets}\nПроверенные пути: разрешено {allowed}, закрыто {blocked}, не удалось определить {unknown}.\nИгнорируется отдельных проверок: {ignored} из {members.Length}.\nПроверен набор портов из конфигурации и каталога, а не все 65535 портов. Наличие слушающей службы на конечном узле не проверяется.\nОткрой вкладку «Порты и пути» и выберите строку: её доказательства доступны в «Основаниях».";
   var reasons=paths.SelectMany(p=>p.UnknownReasons).Distinct().Order(StringComparer.Ordinal).ToArray();if(reasons.Length>0)summary+="\n\nПричины неопределённости:\n"+string.Join("\n",reasons);
   var f=new RouterAuditFinding{Key="publication:"+Hash(g.Key),Title="Публикация "+destinations+" → "+targets,Severity=significant.Severity,
    Status=unknown>0?(allowed>0?"Есть разрешённые и неопределённые пути":"Нужна проверка"):allowed>0?"Есть разрешённые пути":"Закрыто",
    Fingerprint=Hash(string.Join("\n",members.Select(m=>m.Key+":"+m.Fingerprint))),Paths=paths,Members=members.ToList(),Evidence=summary,
    Recommendation=summary+"\n\n"+significant.Recommendation,Commands=string.Join("\n\n",members.Select(m=>m.Commands).Where(c=>c.Length>0).Distinct()),
    Ignored=active.Length==0,IgnoredBy=string.Join(", ",members.Where(m=>m.Ignored).Select(m=>m.IgnoredBy).Distinct()),IgnoredUtc=members.Max(m=>m.IgnoredUtc)};
   return f;
  }).ToArray();
 }
 public static RouterAuditPortRow[] Ports(RouterAuditFinding finding)=>(finding.Members.Count>0?finding.Members:[finding]).SelectMany(f=>f.Paths.Select(p=>new RouterAuditPortRow(f,p))).OrderBy(r=>r.Protocol,StringComparer.Ordinal).ThenBy(r=>r.ExternalPort).ThenBy(r=>r.Input,StringComparer.Ordinal).ToArray();
}
