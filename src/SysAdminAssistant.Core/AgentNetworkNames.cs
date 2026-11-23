using System.Security.Cryptography;
using System.Text;
namespace SysAdminAssistant.Core;
public static class AgentNetworkNames
{
 // Keep the existing 64-character wire format. A stable digest prevents collisions
 // between long Hyper-V adapter names sharing the same readable prefix.
 public static string WireName(string name){ArgumentNullException.ThrowIfNull(name);var clean=new string(name.Select(c=>char.IsControl(c)?' ':c).ToArray());if(clean.Length is >0 and <=64&&clean==name)return name;var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..16];var length=Math.Min(47,clean.Length);if(length>0&&char.IsHighSurrogate(clean[length-1]))length--;return (length>0?clean[..length]:"NIC")+"~"+hash;}
 private static readonly string[] technicalLayers=["-WFP "," WFP ","-QoS Packet Scheduler","QoS Packet Scheduler","LightWeight Filter",
     "Microsoft IP-HTTPS Platform Interface","Microsoft Wi-Fi Direct Virtual Adapter","6to4 Adapter","WAN Miniport","Teredo Tunneling","Loopback Pseudo-Interface"];
 public static bool TechnicalLayer(string label)=>label.StartsWith("isatap.",StringComparison.OrdinalIgnoreCase)
     ||technicalLayers.Any(layer=>label.Contains(layer,StringComparison.OrdinalIgnoreCase));
 public static AgentNetwork[] VisibleInterfaces(AgentReport r)=>(r.Networks??[])
     .Where(n=>r.Linux!=null||r.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)||!TechnicalLayer(n.Name))
     .DistinctBy(n=>n.Name,StringComparer.Ordinal).ToArray();
 public static AgentNetwork? Primary(AgentReport r)=>VisibleInterfaces(r)
     .OrderByDescending(n=>n.DefaultGateway)
     .ThenByDescending(n=>n.State=="up")
     .ThenByDescending(n=>n.LinkBitsPerSecond>0)
     .ThenBy(n=>n.Name,StringComparer.OrdinalIgnoreCase).FirstOrDefault();
}
