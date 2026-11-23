using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public static class NetworkFlaps
{
    public static void Collect(NetworkHistory history,RouterSnapshot snapshot,List<NetworkRule> rules,List<NetworkLog> fresh,DateTime now)
    {
        foreach(var rule in rules.Where(r=>r.Enabled&&r.FlapThreshold>0&&r.Kind=="Interface"&&r.Role!="UserVpn").ToArray()){
            var selected=snapshot.Interfaces.Where(p=>rule.Interfaces.Contains(p.GetValueOrDefault("name",""),StringComparer.OrdinalIgnoreCase)||rule.Comments.Any(c=>p.GetValueOrDefault("comment","").Contains(c,StringComparison.OrdinalIgnoreCase))).Concat(rule.Interfaces.Where(name=>snapshot.Interfaces.All(p=>p.GetValueOrDefault("name","")!=name)).Select(name=>new Dictionary<string,string>{{"name",name},{"running","false"}}));
            foreach(var port in selected){
                var name=port.GetValueOrDefault("name","");if(port.GetValueOrDefault("disabled") is "true" or "yes")continue;
                var key=rule.Id+":"+name;var running=port.GetValueOrDefault("running") is "true" or "yes";
                var marker="SAA interface transition "+name+":";
                var id="flap-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..20];
                rules.Add(new(){Id=id,Kind="Log",Name="Нестабильный интерфейс · "+name,Severity=Severity.Warning,MessageTerms=[marker],GroupBy="All",Threshold=rule.FlapThreshold,WindowSeconds=rule.FlapWindowSeconds,AutoCloseSeconds=running?rule.FlapQuietSeconds:0,Notify=rule.Notify,NotifyRecovery=rule.NotifyRecovery});
                var native=fresh.Where(l=>!l.Baseline&&!l.Message.StartsWith("SAA interface transition ",StringComparison.Ordinal)&&Regex.IsMatch(l.Message,@"(?<![\w.-])"+Regex.Escape(name)+@"(?![\w.-])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))&&(rule.Down.Any(t=>l.Message.Contains(t,StringComparison.OrdinalIgnoreCase))||rule.Up.Any(t=>l.Message.Contains(t,StringComparison.OrdinalIgnoreCase)))).ToArray();
                foreach(var source in native){var transition=new NetworkLog{Key=source.Key+":"+id,ReceivedAt=source.ReceivedAt,EventUtc=source.EventUtc,RouterTime=source.RouterTime,Topics="interface,info",Message=marker+" log event",Meaning="Переключение интерфейса прочитано в логе"};history.Logs.Add(transition);fresh.Add(transition);}
                if(native.Length==0&&history.InterfaceStates.TryGetValue(key,out var before)&&before!=running){var log=new NetworkLog{Key=Guid.NewGuid().ToString("N"),ReceivedAt=now,Topics="interface,info",Message=marker+(running?" up":" down"),Meaning="Изменение состояния обнаружено опросом"};history.Logs.Add(log);fresh.Add(log);}
                history.InterfaceStates[key]=running;
            }
        }
    }
}
