using System.Text.Json;
using SysAdminAssistant.Core;
internal static class Revision141Specs
{
 public static void Run(Action<string,bool> check){
  var now=new DateTime(2026,10,22,0,0,0,DateTimeKind.Utc);
  var standard=NetworkLogRules.Standard();var config=standard.Single(r=>r.Id=="configuration-change");
  string[] routine=["ISAKMP-SA established 198.51.100.2[500]-203.0.113.7[500] spi:123:456","ISAKMP-SA dying 198.51.100.2[500]-203.0.113.7[500] spi:789:012","respond new phase 1 (Identity Protection): 198.51.100.2[500]<=>203.0.113.7[500]"];
  var ipsec=routine.Select((m,i)=>new NetworkLog{Key="i"+i,Message=m,Topics="ipsec,info",ReceivedAt=now}).ToArray();
  var problems=new List<NetworkProblem>();NetworkLogRules.Evaluate(problems,ipsec,ipsec,standard,now);
  check("Routine IPsec lifecycle does not match configuration change",ipsec.All(l=>!NetworkLogRules.Matches(l,config))&&problems.Count==0);
  var logs=new List<NetworkLog>{new(){Key="a",Message="filter rule changed by admin",Topics="system,info",RouterTime="2026-10-22 03:00:00",ReceivedAt=now}};
  NetworkLogRules.Evaluate(problems,logs,logs,[config],now);var p=problems.Single();
  check("Configuration problem captures original message and rule",p.Evidence.Count==1&&p.Evidence[0].Log.Message==logs[0].Message&&p.Evidence[0].Rule.MessageTerms.Contains("changed by")&&p.Evidence[0].MatchedCount==1);
  var original=p.Evidence[0].Log.Message;logs[0].Message="mutated";config.MessageTerms[0]="mutated";
  check("Evidence snapshots survive source log and rule mutation",p.Evidence[0].Log.Message==original&&p.Evidence[0].Rule.MessageTerms[0]=="changed by");
  logs.Clear();config=NetworkLogRules.Standard().Single(r=>r.Id=="configuration-change");
  for(var i=1;i<=35;i++){var log=new NetworkLog{Key="b"+i,Message="rule added by admin "+i,ReceivedAt=now.AddSeconds(i)};logs.Add(log);NetworkLogRules.Evaluate(problems,logs,[log],[config],log.ReceivedAt);}
  check("Evidence bounded to first and latest nineteen snapshots",p.Evidence.Count==20&&p.Evidence[0].Log.Key=="a"&&p.Evidence[^1].Log.Key=="b35");
  var count=p.Evidence.Count;NetworkLogRules.Evaluate(problems,logs,[logs[^1]],[config],logs[^1].ReceivedAt);
  check("Same source message and rule are not duplicated in evidence",p.Evidence.Count==count);
  var saved=JsonSerializer.Deserialize<NetworkProblem>(JsonSerializer.Serialize(p))!;
  check("Persisted problem retains evidence independently of log history",saved.Evidence.Count==20&&saved.Evidence[0].Log.RouterTime=="2026-10-22 03:00:00"&&saved.Evidence[0].EvaluatedAtUtc.Kind==DateTimeKind.Utc);
  check("Legacy problems have explicit empty evidence",JsonSerializer.Deserialize<NetworkProblem>("{\"Kind\":\"Log\",\"RuleId\":\"configuration-change\"}")!.Evidence.Count==0);
  NetworkLogRules.Close(p,now.AddMinutes(5),"Resolved");
  check("Resolving a problem keeps its evidence",p.Evidence.Count==20&&!p.Open);
  config.MessageTerms=["ISAKMP-SA"];var custom=new List<NetworkProblem>();NetworkLogRules.Evaluate(custom,ipsec,ipsec,[config],now);
  check("Custom matching rule records its actual broad conditions",custom.Single().Evidence.All(e=>e.Rule.MessageTerms.SequenceEqual(new[]{"ISAKMP-SA"})));
 }
}
