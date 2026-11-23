using System.Text.Json;
namespace SysAdminAssistant.Core;

// Profiles add observed running services; they never start, stop or install services.
public static class LinuxServiceProfiles
{
    public static readonly AgentServiceRole[] Catalog=[
        new("linux-web","Веб-сервер","Обнаруженные работающие nginx и Apache."),
        new("linux-db","Сервер баз данных","Обнаруженные работающие PostgreSQL, MySQL и MariaDB."),
        new("linux-1c","Сервер 1С","Обнаруженные работающие службы сервера 1С."),
        new("linux-ssh","SSH","Работающая служба SSH."),
        new("linux-container","Контейнеры","Работающие службы Docker и containerd."),
        new("linux-dns","DNS-сервер","Работающие BIND и PowerDNS."),
        new("linux-file","Файловый сервер","Работающие службы Samba.")
    ];
    public static bool IsRole(string id)=>Catalog.Any(r=>r.Id==id);
    private static bool Matches(string id,string name){var n=name.ToLowerInvariant();return id switch{
        "linux-web"=>n is "nginx.service" or "apache2.service",
        "linux-db"=>n is "mysql.service" or "mariadb.service" or "postgresql.service" || n.StartsWith("postgresql@",StringComparison.Ordinal),
        "linux-1c"=>n.StartsWith("srv1cv",StringComparison.Ordinal)||n.StartsWith("1cv8",StringComparison.Ordinal),
        "linux-ssh"=>n is "ssh.service" or "sshd.service",
        "linux-container"=>n is "docker.service" or "containerd.service",
        "linux-dns"=>n is "named.service" or "bind9.service" or "pdns.service",
        "linux-file"=>n is "smbd.service" or "nmbd.service" or "samba-ad-dc.service",_=>false};}
    public static bool Sync(AgentPolicy p,IEnumerable<AgentService> inventory)
    {
        var before=JsonSerializer.Serialize(p.Services)+":"+p.ServiceProfileOverflow;
        var active=p.ServiceRoles.Where(IsRole).ToHashSet(StringComparer.Ordinal);
        var rows=new Dictionary<string,AgentServiceRule>(StringComparer.OrdinalIgnoreCase);
        foreach(var r in p.Services.Where(r=>!p.ExcludedServices.Contains(r.Name,StringComparer.OrdinalIgnoreCase))){
            if(r.Profiles is not {Length:>0}) rows[r.Name]=r;
            else {var owners=r.Profiles.Where(active.Contains).ToArray();if(owners.Length>0)rows[r.Name]=r with{Profiles=owners};}
        }
        foreach(var s in inventory.Where(s=>s.Status=="Running"))foreach(var id in active.Where(id=>Matches(id,s.Name))){
            if(p.ExcludedServices.Contains(s.Name,StringComparer.OrdinalIgnoreCase))continue;
            if(rows.TryGetValue(s.Name,out var old)){if(old.Profiles is {Length:>0}&&!old.Profiles.Contains(id))rows[s.Name]=old with{Profiles=old.Profiles.Append(id).ToArray()};}
            else rows[s.Name]=new(s.Name,true,"Running",Severity.Critical,2,[id]);
        }
        var ordered=rows.Values.OrderByDescending(r=>r.Profiles is not {Length:>0}).ThenBy(r=>r.Name,StringComparer.OrdinalIgnoreCase).ToArray();
        p.ServiceProfileOverflow=Math.Max(0,ordered.Length-64);p.Services=ordered.Take(64).ToList();
        return before!=JsonSerializer.Serialize(p.Services)+":"+p.ServiceProfileOverflow;
    }
}
