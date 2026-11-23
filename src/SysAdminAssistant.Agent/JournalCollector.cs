using System.Diagnostics.Eventing.Reader;
using System.Security;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Agent;
internal sealed class JournalCursor {public long Record {get;set;} public string Epoch {get;set;}=Guid.NewGuid().ToString("N");}
internal sealed class JournalState {public Dictionary<string,JournalCursor> Cursors {get;set;}=[];public List<AgentJournalEvent> Pending {get;set;}=[];public AgentEventBatch? Batch {get;set;}public Dictionary<string,DateTime> FailedIPs {get;set;}=[];}
internal sealed class JournalCollector(string directory)
{
 private string PathName=>Path.Combine(directory,"events-state.json");private JournalState? state;
 private void Save(){var p=PathName+".tmp";using(var f=new FileStream(p,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){JsonSerializer.Serialize(f,state,AgentWire.Json);f.Flush(true);}File.Move(p,PathName,true);}
 public void Ack(string id){if(state?.Batch?.Id!=id)return;var keys=state.Batch.Events.Select(e=>e.Key).ToHashSet();state.Pending.RemoveAll(e=>keys.Contains(e.Key));state.Batch=null;Save();}
 private static long? Edge(string channel,bool newest){using var reader=new EventLogReader(new EventLogQuery(channel,PathType.LogName){ReverseDirection=newest});using var e=reader.ReadEvent();return e?.RecordId;}
 public AgentEventBatch? Read(AgentEventPolicy policy){
 AgentEvents.Validate(policy);state??=File.Exists(PathName)?JsonSerializer.Deserialize<JournalState>(File.ReadAllText(PathName),AgentWire.Json)??throw new InvalidDataException():new();
 if(!policy.Enabled)return null;if(state.Batch!=null)return state.Batch;
 var health=new List<AgentJournalHealth>();
 foreach(var channel in policy.Rules.Where(r=>r.Enabled).Select(r=>r.Channel).Distinct()){
 try{
 var latest=Edge(channel,true)??0;var oldest=Edge(channel,false)??0;bool gap=false;
 if(!state.Cursors.TryGetValue(channel,out var cursor)){state.Cursors[channel]=new(){Record=latest};health.Add(new(channel,"OK","Начато наблюдение новых событий"));continue;}
 if(latest<cursor.Record||oldest>cursor.Record+1){cursor.Record=Math.Max(0,oldest-1);cursor.Epoch=Guid.NewGuid().ToString("N");gap=true;}
 var rules=policy.Rules.Where(r=>r.Enabled&&r.Channel==channel).ToArray();
 foreach(var ip in state.FailedIPs.Where(x=>x.Value<DateTime.UtcNow.AddMinutes(-10)).Select(x=>x.Key).ToArray())state.FailedIPs.Remove(ip);
 var ips=state.FailedIPs.Keys.Where(x=>System.Net.IPAddress.TryParse(x,out _)).Take(16).ToArray();
 var selected=rules.Where(rule=>rule.Id!="logon-success"||ips.Length>0).ToArray();if(selected.Length==0){health.Add(new(channel,"OK","Нет выбранных источников"));continue;}
 var xpath=AgentEventQueries.Build(channel,cursor.Record,selected,ips);
 using var reader=new EventLogReader(new EventLogQuery(channel,PathType.LogName,xpath));bool exhausted=false;int count=0;
 while(state.Pending.Count<128&&count++<64){using var e=reader.ReadEvent();if(e==null){exhausted=true;break;}string message;try{message=e.FormatDescription()??"Описание недоступно";}catch{message="Описание недоступно; см. журнал Windows по ID записи";}
 message=Regex.Replace(message,@"(?i)\b(password|pwd|token|secret)\s*[:=]\s*\S+","$1=[скрыто]",RegexOptions.NonBacktracking,TimeSpan.FromMilliseconds(100));
 string source="";try{using var xr=XmlReader.Create(new StringReader(e.ToXml()),new(){DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=200000});var xml=XDocument.Load(xr);var values=xml.Descendants().Where(x=>x.Name.LocalName=="Data").ToArray();foreach(var name in new[]{"IpAddress","ServiceName","AppName","DeviceName","param1","TargetUserName"}){source=values.FirstOrDefault(x=>(string?)x.Attribute("Name")==name)?.Value??"";if(source!="")break;}}catch{}
 source=new string(source.Where(c=>!char.IsControl(c)).Take(256).ToArray());
 var item=new AgentJournalEvent(channel+":"+cursor.Epoch+":"+e.RecordId,channel,e.ProviderName??"",e.Id,e.RecordId??0,(e.TimeCreated??DateTime.Now).ToUniversalTime(),message[..Math.Min(message.Length,1000)],source,e.Level);
 if(!state.Pending.Any(x=>x.Key==item.Key))state.Pending.Add(item);cursor.Record=item.RecordId;
 if(e.Id==4625&&System.Net.IPAddress.TryParse(source,out var failedIp)&&!System.Net.IPAddress.IsLoopback(failedIp)){var fresh=!state.FailedIPs.ContainsKey(source);if(state.FailedIPs.Count<16||!fresh)state.FailedIPs[source]=DateTime.UtcNow;if(fresh)break;}
 }
 if(exhausted)cursor.Record=latest;
 health.Add(new(channel,gap?"Gap":!exhausted?"Backlog":"OK",gap?"Журнал очищен или старые записи перезаписаны":!exhausted?"Накопленные события ещё передаются":"Чтение доступно"));
 }catch(UnauthorizedAccessException){health.Add(new(channel,"Denied","Нет права чтения; проверьте журнал установки и политику аудита"));}
 catch(SecurityException){health.Add(new(channel,"Denied","Нет права чтения"));}
 catch(EventLogNotFoundException){health.Add(new(channel,channel=="Microsoft-Windows-Windows Defender/Operational"?"Absent":"Unavailable","Журнал отсутствует"));}
 catch(Exception e)when(e is EventLogException or IOException){health.Add(new(channel,"Unavailable","Журнал временно недоступен"));}
 }
 var items=new List<AgentJournalEvent>();int size=0;foreach(var e in state.Pending.Take(32)){var n=JsonSerializer.Serialize(e,AgentWire.Json).Length;if(size+n>80000)break;items.Add(e);size+=n;}
 state.Batch=new(Guid.NewGuid().ToString("N"),items.ToArray(),health.ToArray());Save();return state.Batch;
 }
}
