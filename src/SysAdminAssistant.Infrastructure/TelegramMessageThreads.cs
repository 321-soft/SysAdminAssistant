using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed class TelegramThread {public int MessageId {get;set;}public bool Closed {get;set;}public DateTime UpdatedUtc {get;set;}}
public sealed class TelegramThreadReceipts {public Dictionary<string,int> Events {get;set;}=[];public Dictionary<string,TelegramThread> Threads {get;set;}=[];}
public sealed class TelegramMessageThreads(string directory)
{
 private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,SemaphoreSlim> Gates=new(StringComparer.OrdinalIgnoreCase);
 private string PathName=>Path.Combine(directory,"telegram-message-threads.json");
 public static string IssueKey(string key){if(key.EndsWith(":recovery",StringComparison.Ordinal))key=key[..^9];var parts=key.Split(':');return parts.Length==4&&parts[0]=="router-audit"&&parts[2]=="summary"?string.Join(":",parts.Take(3)):key;}
 public string[] OpenIssues(TelegramSettings settings){if(!File.Exists(PathName))return [];var secret=TelegramPinApi.Resolve(settings);var prefix=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret.Split(':')[0]+"|"+settings.ChatId)))+"|";return NetworkStore.Locked(PathName,()=>{var state=JsonSerializer.Deserialize<TelegramThreadReceipts>(File.ReadAllText(PathName))??throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Повреждены квитанции Telegram"));return state.Threads.Where(p=>p.Key.StartsWith(prefix,StringComparison.Ordinal)&&!p.Value.Closed).Select(p=>p.Key[prefix.Length..]).ToArray();});}
 public async Task<int> SendAsync(TelegramSettings settings,string eventKey,string issueKey,bool recovered,string text,CancellationToken token,Func<string,int?,CancellationToken,Task<int>>? send=null,string? botId=null){
  string? secret=null;if(send==null){secret=TelegramPinApi.Resolve(settings);botId=secret.Split(':')[0];}botId??="test-bot";
  var destination=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(botId+"|"+settings.ChatId)));var eventId=destination+"|"+eventKey;var issueId=destination+"|"+IssueKey(issueKey);
  var gate=Gates.GetOrAdd(System.IO.Path.GetFullPath(PathName),_=>new(1,1));await gate.WaitAsync(token);
  try{
   var state=NetworkStore.Locked(PathName,()=>File.Exists(PathName)?JsonSerializer.Deserialize<TelegramThreadReceipts>(File.ReadAllText(PathName))??throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Повреждены квитанции Telegram")):new());
   if(state.Events.TryGetValue(eventId,out var delivered))return delivered;
   state.Threads.TryGetValue(issueId,out var thread);int? reply=recovered&&thread is {Closed:false,MessageId:>0}?thread.MessageId:null;
   var id=send!=null?await send(text,reply,token):await TelegramSender.SendMessageAsync(settings,text,token,resolvedToken:secret,replyToMessageId:reply);
   if(id<=0)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Telegram не вернул ID сообщения"));
   state.Events[eventId]=id;
   if(issueKey.Length>0){if(!recovered&&(thread==null||thread.Closed))thread=new(){MessageId=id};if(thread!=null){thread.Closed=recovered;thread.UpdatedUtc=DateTime.UtcNow;state.Threads[issueId]=thread;}}
   // Keep open threads; age only completed threads and old event receipts.
   if(state.Events.Count>20000)state.Events=state.Events.Skip(state.Events.Count-20000).ToDictionary(x=>x.Key,x=>x.Value);
   foreach(var old in state.Threads.Where(x=>x.Value.Closed&&x.Value.UpdatedUtc<DateTime.UtcNow.AddDays(-90)).Select(x=>x.Key).ToArray())state.Threads.Remove(old);
   NetworkStore.Locked(PathName,()=>{Directory.CreateDirectory(directory);var tmp=PathName+".tmp";using(var f=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){JsonSerializer.Serialize(f,state);f.Flush(true);}File.Move(tmp,PathName,true);return true;});return id;
  }finally{gate.Release();}
 }
}
