using System.Net;
namespace SysAdminAssistant.Core;
public sealed record ServerNetworksView(string[] Networks,string Revision,string Peer,string FirewallStatus,string FirewallCommand);
public sealed record ServerNetworksSave(string[] Networks,string Revision,bool AllowDisconnect=false);
public static class ServerNetworkRules
{
    public static string[] Validate(string[] values){if(values==null||values.Length is <1 or >128)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите от 1 до 128 частных подсетей CIDR"));if(values.Any(v=>string.IsNullOrWhiteSpace(v)||v.Length>100))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверная строка подсети CIDR"));return values.Select(v=>{var s=v.Trim();_=new LanNetwork(s);var parts=s.Split('/');return IPAddress.Parse(parts[0])+"/"+int.Parse(parts[1]);}).Distinct(StringComparer.Ordinal).ToArray();}
    public static bool Allows(string[] networks,IPAddress peer)=>IPAddress.IsLoopback(peer)||networks.Any(n=>new LanNetwork(n).Contains(peer));
    public static string Revision(string[] networks)=>AgentWire.Hash(string.Join("\n",networks));
    public static string FirewallCommand(string[] networks)=>"Get-NetFirewallRule -DisplayName 'SysAdminAssistantServer-LAN' -ErrorAction Stop | Get-NetFirewallAddressFilter | Set-NetFirewallAddressFilter -RemoteAddress "+string.Join(",",Validate(networks).Select(n=>"'"+n+"'"));
}
