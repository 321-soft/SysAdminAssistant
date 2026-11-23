using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class NetworkDictionarySpecs
{
    public static void Run(string folder,Action<string,bool> check)
    {
        var now=DateTime.UtcNow;var problems=new List<NetworkProblem>();var rule=new NetworkRule{Id="wan",Name="Internet",Interfaces=["ether1"],DelaySeconds=30};
        NetworkSignals.Observe(problems,rule,"ether1",false,now);check("Network debounce prevents transient alarm",!problems.Single().Open);
        NetworkSignals.Observe(problems,rule,"ether1",false,now.AddSeconds(31));check("Network persistent WAN outage opens alarm",problems.Single().Open);
        NetworkSignals.Observe(problems,rule,"ether1",false,now.AddSeconds(40));check("Network repeated outage keeps one problem",problems.Count==1&&problems[0].Repeats==3);
        NetworkSignals.Observe(problems,rule,"ether1",true,now.AddSeconds(50));check("Network one good poll cannot clear alarm",problems[0].Open);
        NetworkSignals.Observe(problems,rule,"ether1",true,now.AddSeconds(60));check("Network confirmed recovery retains history",!problems[0].Open&&problems[0].RestoredAt!=null);
        rule.DelaySeconds=0;NetworkSignals.Observe(problems,rule,"ether1",false,now.AddSeconds(70));NetworkSignals.Observe(problems,rule,"ether10",false,now.AddSeconds(70));NetworkSignals.Observe(problems,rule,"ether1",true,now.AddSeconds(80));NetworkSignals.Observe(problems,rule,"ether1",true,now.AddSeconds(90));
        check("Network another unresolved WAN keeps red",NetworkSignals.Color(problems,true,true)==LampState.Red);
        var dictionary=new NetworkDictionary{Id="fixture",Name="Fixture",Rules=[rule,new(){Id="vpn",Name="User VPN",Role="UserVpn",Severity=Severity.Info,Interfaces=["ovpn-server"]}]};
        var userLog=new NetworkLog{Message="ovpn-server disconnected"};NetworkSignals.Explain(userLog,dictionary,new Dictionary<string,string[]>());check("Network user VPN disconnect stays info",userLog.Severity==Severity.Info&&userLog.Meaning.Contains("User VPN"));
        var realLog=new NetworkLog{Message="ether1 link down"};NetworkSignals.Explain(realLog,dictionary,new Dictionary<string,string[]>());check("Network important WAN log explained",realLog.Severity==Severity.Critical);
        var falseLog=new NetworkLog{Message="ether10 link down"};NetworkSignals.Explain(falseLog,dictionary,new Dictionary<string,string[]>());check("Network interface selector has token boundaries",falseLog.Severity==Severity.Info);
        var missing=new List<NetworkProblem>();NetworkSignals.Interfaces(missing,[],dictionary,new Dictionary<string,string[]>(),now);check("Network missing configured interface is down",missing.Any(p=>p.Key=="wan:ether1"&&p.Open));
        var maintained=new List<NetworkProblem>();NetworkSignals.Interfaces(maintained,[],dictionary,new Dictionary<string,string[]>(),now,true);check("Network maintenance does not create alarms",maintained.Count==0);
        var dir=Path.Combine(folder,"network-fixture");var store=new NetworkStore(dir);var json=JsonSerializer.Serialize(new NetworkDictionaryPackage{Dictionaries=[dictionary]});
        store.Import(json);store.Import(json);check("Network dictionary import upserts without duplicates",new NetworkStore(dir).Dictionaries().Count(d=>d.Id=="fixture")==1);
        check("Network dictionary import never creates connection or database",!File.Exists(Path.Combine(dir,"network-state.json"))&&Directory.GetFiles(dir,"*.db").Length==0);
        store.State(n=>{n.Connections.Add(new(){DeviceId=9,User="private-user",ProtectedPassword="private-secret"});return true;},true);
        var exported=store.ExportPackage();
        check("Network export contains custom dictionary only",NetworkDictionaries.Parse(exported).Dictionaries is [{Id:"fixture"}]);
        check("Network export omits credentials and device connections",!exported.Contains("private-secret")&&!exported.Contains("private-user")&&!exported.Contains("DeviceId"));
        foreach(var invalid in new[]{"{\"Format\":1,\"Dictionaries\":null}","{\"Devices\":[],\"Dictionaries\":[]}","{\"Connections\":[],\"Dictionaries\":[]}","{\"Dictionaries\":[null]}","{\"Dictionaries\":[{\"Id\":\"x\",\"Name\":\"x\",\"Rules\":[null]}]}"}){
            bool rejected=false;try{NetworkDictionaries.Parse(invalid);}catch(Exception e)when(e is ArgumentException or JsonException){rejected=true;}check("Network invalid dictionary rejected "+invalid,rejected);
        }
        var profile=new KeywordProfile{Provider="Veeam",Critical="error;failed",Warning="warning",Info="success",SuccessResults="success",FailedResults="failed",WarningResults="warning"};
        Analysis Analyze(string body)=>ReportAnalyzer.Analyze(new("fixture","1","","Veeam success",body,null),[profile],"Veeam",true);
        var zero=Analyze("Warning | 0 | End time | 2:27:39 | Data read | 69,2 GB\nError | 0 | Duration | 1:27:28 | Transferred | 10,7 GB");
        check("Veeam flattened zero counters are green",zero.Severity==Severity.Info&&zero.Result==BackupResult.Success);
        check("Veeam positive error counter remains critical",Analyze("Error | 1 | Duration | 1:27:28").Severity==Severity.Critical);
        check("Veeam decimal error is not masked",Analyze("Error | 0.5 | Duration | 1:27:28").Severity==Severity.Critical);
        check("Veeam zero preserves real error in later cell",Analyze("Error | 0 | Detail | failed to write disk").Severity==Severity.Critical);
        check("Veeam adjacent zero cells are informational",Analyze("Warning\n0\nError\n0").Severity==Severity.Info);
    }
}
