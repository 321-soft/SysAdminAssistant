using System.Net;
using System.Net.Sockets;
using SysAdminAssistant.Core;
static class NetworkNameSpecs
{
 public static void Run(Action<string,bool> check){
 const string name="vEthernet (Realtek(R) PCI(e) Ethernet Controller - Virtual Switch)";
 var r=new AgentReport{MachineName="fixture",CpuCount=1,Networks=[new(name,"up",1,2,1000000000)]};
 bool rejected=false;try{AgentWire.Validate(r);}catch(ArgumentException){rejected=true;}check("Original 66-character Hyper-V name reproduces whole-report rejection",name.Length==66&&rejected);
 var normalized=AgentNetworkNames.WireName(name);r.Networks=[new(normalized,"up",1,2,1000000000)];AgentWire.Validate(r);check("Hyper-V adapter fits existing wire format and report validates",normalized.Length==64&&normalized.StartsWith("vEthernet (Realtek"));
 check("Long network name stable across restarts",normalized==AgentNetworkNames.WireName(name));check("Long names with identical prefix stay distinct",normalized!=AgentNetworkNames.WireName(name+" 2"));
 check("Short adapter names remain compatible with policies",AgentNetworkNames.WireName("Ethernet")=="Ethernet");check("64 character name unchanged",AgentNetworkNames.WireName(new string('a',64))==new string('a',64));
 check("Control characters cannot poison report",AgentWire.Text(AgentNetworkNames.WireName("Ethernet\nfoo"),64));check("Empty adapter label gets stable bounded identifier",AgentWire.Text(AgentNetworkNames.WireName(""),64)&&AgentNetworkNames.WireName("").Length>0);
 foreach(var length in new[]{65,66,255,1024})check("Bounded readable name at length "+length,AgentWire.Text(AgentNetworkNames.WireName(new string('x',length)),64));
 r.Os="Windows";
 r.Networks=[new("Ethernet0-WFP Native MAC Layer LightWeight Filter-0000","up",0,0,1000000000),
   new("Ethernet0-QoS Packet Scheduler-0000","up",0,0,1000000000),
   new("Ethernet0-WFP 802.3 MAC Layer LightWeight Filter-0000","up",0,0,1000000000),
   new("Ethernet0","up",1,2,1000000000)];
 check("Previously stored Windows report shows one card for Ethernet0",
    AgentGaugeModel.Build(r,new()).Where(x=>x.Key.StartsWith("network:")).Select(x=>x.Key).SequenceEqual(["network:Ethernet0"]));
 r.Networks=[new("A-backup","up",0,0,1000000000),new("vEthernet (External)","up",0,0,1000000000,true)];
 check("Gateway wins over a second live Windows adapter; Hyper-V interface remains usable",
    AgentNetworkNames.Primary(r)?.Name=="vEthernet (External)"&&AgentNetworkNames.VisibleInterfaces(r).Length==2);
 check("Older reports without gateway hints choose a stable active adapter",
    AgentNetworkNames.Primary(withNetworks())?.Name=="A-backup");
 AgentReport withNetworks()=>new(){Os="Windows",Networks=r.Networks.Select(n=>n with{DefaultGateway=false}).Reverse().ToArray()};
 const string secret="TOP-SECRET-TOKEN";var message=AgentFailure.Describe("Отправка отчёта",new HttpRequestException(secret,new SocketException((int)SocketError.ConnectionRefused),HttpStatusCode.ServiceUnavailable));check("Diagnostic contains HTTP code and socket reason",message.Contains("HTTP=503")&&message.Contains("ConnectionRefused"));check("Diagnostic never exposes exception secret",!message.Contains(secret));check("Diagnostic exposes validation stage",AgentFailure.Describe("Проверка отчёта",new ArgumentException(secret)).Contains("Проверка отчёта"));
 }
}
