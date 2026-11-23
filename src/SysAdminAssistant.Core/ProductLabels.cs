namespace SysAdminAssistant.Core;
/// <summary>Only default, unmodified names are localized. Matching IDs/phrases are never changed.</summary>
public static class ProductLabels
{
 private static readonly Lazy<Dictionary<string,string>> NetworkNames=new(()=>NetworkDictionaries.Generic().Rules.Concat(NetworkLogRules.Standard()).GroupBy(r=>r.Id).ToDictionary(g=>g.Key,g=>g.First().Name));
 private static readonly Lazy<Dictionary<string,string>> AgentNames=new(()=>AgentEvents.Standard().Concat(AgentEvents.StandardLinux()).Concat(new[]{"SQL","AD","DNS"}.SelectMany(AgentEvents.Role)).GroupBy(r=>r.Id).ToDictionary(g=>g.Key,g=>g.First().Name));
 public static string NetworkRule(NetworkRule rule)=>NetworkNames.Value.TryGetValue(rule.Id,out var original)&&original==rule.Name?ProductText.Get(rule.Name):rule.Name;
 public static string AgentRule(AgentEventRule rule)=>AgentNames.Value.TryGetValue(rule.Id,out var original)&&original==rule.Name?ProductText.Get(rule.Name):rule.Name;
}
