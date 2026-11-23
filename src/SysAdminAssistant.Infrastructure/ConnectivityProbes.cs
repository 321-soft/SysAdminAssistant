using System.Net;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record ConnectivityProbe(string Group,string Host,string Result,[property:System.Text.Json.Serialization.JsonConverter(typeof(WallTimeJsonConverter))] DateTime CheckedAt);
public static class ConnectivityProbes
{
    public static string[] Hosts(string text,bool required=false)
    {
        if(text==null||text.Length>5000)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректный список ICMP-адресов"));
        var hosts=text.Split([';',',','\r','\n'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(hosts.Length>16||required&&hosts.Length==0||hosts.Any(h=>h.Length>253||Uri.CheckHostName(h)==UriHostNameType.Unknown))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите IP или DNS-имена без порта и https://; до 16 адресов в каждой группе"));
        return hosts;
    }
    public static void Validate(AppSettings settings)
    {
        _=Hosts(settings.LanPingHosts);_=Hosts(settings.PingHost,true);
        if(settings.AvailabilityAlertMinutes is <0 or >1440)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Задержка уведомления: 0–1440 минут"));
        if(string.IsNullOrWhiteSpace(settings.DnsName)||settings.DnsName.Length>253||Uri.CheckHostName(settings.DnsName)==UriHostNameType.Unknown)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите DNS-имя для проверки"));
    }
    public static string Aggregate(IEnumerable<ConnectivityProbe> probes)
    {
        var rows=probes.ToArray();return rows.Length==0?"DISABLED":rows.All(p=>p.Result=="OK")?"OK":rows.Any(p=>p.Result=="OK")?"PARTIAL":"ERROR";
    }
    public static async Task<ConnectivityProbe[]> CheckAsync(AppSettings settings,Func<string,CancellationToken,Task<string>> ping,CancellationToken token)
    {
        var targets=Hosts(settings.LanPingHosts).Select(h=>(Group:"LAN",Host:h)).Concat(Hosts(settings.PingHost,true).Select(h=>(Group:"WAN",Host:h))).ToArray();
        var results=new ConnectivityProbe[targets.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0,targets.Length),new ParallelOptions{MaxDegreeOfParallelism=4,CancellationToken=token},async(i,ct)=>{
            var target=targets[i];results[i]=new(target.Group,target.Host,await ping(target.Host,ct),AppTime.Now);
        });return results;
    }
    public static LampState Color(string status)=>status=="OK"?LampState.Green:status=="PARTIAL"?LampState.Amber:status=="DISABLED"?LampState.Gray:LampState.Red;
    public static string Label(string status)=>status=="OK"?"Доступно":status=="PARTIAL"?"Часть адресов недоступна":status=="DISABLED"?"Не настроено":"Недоступно";
}
