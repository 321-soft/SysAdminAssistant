using System.Xml.Linq;
namespace SysAdminAssistant.Core;
public static class AgentEventQueries
{
 public static string Build(string channel,long after,IEnumerable<AgentEventRule> rules,IEnumerable<string> failedIps){var selected=rules.ToList();AgentEvents.Validate(new AgentEventPolicy{Rules=selected});if(!AgentEvents.Channels.Contains(channel)||after<0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный курсор журнала"));var ips=failedIps.Where(x=>System.Net.IPAddress.TryParse(x,out _)).Take(16).ToArray();
 string Select(AgentEventRule rule)=>"*[System[EventRecordID > "+after+" and Provider[@Name='"+rule.Provider+"'] and ("+string.Join(" or ",rule.Ids.Select(id=>"EventID="+id))+")]"+(rule.Id=="logon-success"?" and EventData["+string.Join(" or ",ips.Select(ip=>"Data[@Name='IpAddress']='"+ip+"'"))+"]":"")+"]";
 return new XElement("QueryList",new XElement("Query",new XAttribute("Id","0"),new XAttribute("Path",channel),selected.Where(r=>r.Enabled&&r.Channel==channel&&(r.Id!="logon-success"||ips.Length>0)).Select(rule=>new XElement("Select",new XAttribute("Path",channel),Select(rule))))).ToString(SaveOptions.DisableFormatting);
 }
}
