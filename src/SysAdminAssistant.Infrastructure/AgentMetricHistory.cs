using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record AgentMetricOccurrence(long Sequence,string Key,string Signature,string Transition,string Title,Severity Severity,DateTime FirstUtc,DateTime LastUtc,int Count,string Details);
public sealed class AgentMetricHistoryState {public long Sequence {get;set;}public DateTime? LastReportUtc {get;set;}public Dictionary<string,AgentMetricOccurrence> Active {get;set;}=[];}
public sealed record AgentMetricHistoryPage(AgentMetricOccurrence[] Rows,long? Before);
public static class AgentMetricHistory
{
    public static AgentFinding[] EffectiveFindings(string directory,int id,AgentReport report,AgentPolicy policy,IEnumerable<AgentFinding> current)
    {
        var rows=current.ToList();var path=Path.Combine(directory,"agent-metric-state-"+id+".json");
        return NetworkStore.Locked(path,()=>{if(File.Exists(path)){var state=JsonSerializer.Deserialize<AgentMetricHistoryState>(File.ReadAllBytes(path),AgentWire.Json);if(state!=null)foreach(var old in state.Active.Values)if(!rows.Any(f=>f.Key==old.Key)&&!LinuxPolicy.CanRecover(old.Key,report,policy,old.LastUtc))rows.Add(new(old.Key,old.Title,old.Severity,0));}return rows.ToArray();});
    }
    public static void Capture(string directory,int id,AgentReport report,AgentPolicy policy,IEnumerable<AgentFinding> findings)
    {
        var path=Path.Combine(directory,"agent-metric-state-"+id+".json");NetworkStore.Locked(path,()=>{
            var state=File.Exists(path)?JsonSerializer.Deserialize<AgentMetricHistoryState>(File.ReadAllBytes(path),AgentWire.Json)??new():new();
            if(state.LastReportUtc>=report.CapturedUtc)return false;
            var rows=findings.Where(f=>!f.Key.StartsWith("journal:")).ToArray();var keys=rows.Select(f=>f.Key).ToHashSet();var changes=new List<AgentMetricOccurrence>();
            foreach(var pair in state.Active.Where(p=>!keys.Contains(p.Key)&&LinuxPolicy.CanRecover(p.Key,report,policy,p.Value.LastUtc)).ToArray()){var old=pair.Value;changes.Add(old with{Sequence=++state.Sequence,Transition="Recovered",LastUtc=report.CapturedUtc,Details="Условие больше не присутствует в свежем отчёте. История прежней ошибки сохранена. Для новой причины формируется отдельная запись."});state.Active.Remove(pair.Key);}
            foreach(var finding in rows){
                var signature=AgentIssueSignature.For(finding,report);
                state.Active.TryGetValue(finding.Key,out var prior);var same=prior?.Signature==signature;
                var item=new AgentMetricOccurrence(++state.Sequence,finding.Key,signature,prior==null?"Opened":!same||prior.Severity!=finding.Severity?"Changed":"Repeated",finding.Title,finding.Severity,same?prior!.FirstUtc:report.CapturedUtc,report.CapturedUtc,same?prior!.Count+1:1,AgentIssueAdvice.Metric(finding,report,policy));state.Active[finding.Key]=item;changes.Add(item);
            }
            var history=Path.Combine(directory,"agent-metric-history");Directory.CreateDirectory(history);var log=Path.Combine(history,id+"-"+DateTime.UtcNow.ToString("yyyyMMdd")+".ndjson");
            using(var file=new FileStream(log,FileMode.Append,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough)){foreach(var item in changes){var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item,AgentWire.Json)+"\n");file.Write(bytes);}file.Flush(true);}
            state.LastReportUtc=report.CapturedUtc;var temp=path+".tmp";try{using(var file=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){JsonSerializer.Serialize(file,state,AgentWire.Json);file.Flush(true);}File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}return true;
        });
    }
    public static AgentMetricHistoryPage Read(string directory,int id,long? before=null)
    {
        var folder=Path.Combine(directory,"agent-metric-history");if(!Directory.Exists(folder))return new([],null);var rows=new Dictionary<long,AgentMetricOccurrence>();
        foreach(var file in Directory.EnumerateFiles(folder,id+"-*.ndjson").OrderByDescending(x=>x)){
            foreach(var line in File.ReadLines(file)){if(line.Length>40000)continue;AgentMetricOccurrence? item;try{item=JsonSerializer.Deserialize<AgentMetricOccurrence>(line,AgentWire.Json);}catch(JsonException){continue;}if(item!=null&&(before==null||item.Sequence<before)){rows[item.Sequence]=item;if(rows.Count>200)rows.Remove(rows.Keys.Min());}}
            if(rows.Count>=200)break;
        }
        var result=rows.Values.OrderByDescending(r=>r.Sequence).Take(200).ToArray();return new(result,result.Length==200?result.Last().Sequence:null);
    }
}
