using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;

public sealed class TelegramPinException:Exception
{
 public string Kind {get;}public int RetrySeconds {get;}
 public TelegramPinException(string kind,int retrySeconds=60):base(kind){Kind=kind;RetrySeconds=retrySeconds;}
}
public interface ITelegramPinApi
{
 Task<int> SendAsync(string chat,string text,CancellationToken token);
 Task EditAsync(string chat,int id,string text,CancellationToken token);
 Task PinAsync(string chat,int id,CancellationToken token);
 Task UnpinAsync(string chat,int id,CancellationToken token);
}
public sealed class TelegramPinApi(string secret,HttpClient? http=null):ITelegramPinApi
{
 private static readonly HttpClient Client=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20)};
 public static string Resolve(TelegramSettings settings){var secret=LocalSecret.Unprotect(settings.ProtectedToken);if(!Regex.IsMatch(secret,@"^[0-9]+:[A-Za-z0-9_-]{20,200}$",RegexOptions.CultureInvariant))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный токен Telegram"));return secret;}
 private async Task<JsonElement> Call(string method,object payload,CancellationToken token){
  try{
   if(method=="sendMessage")await TelegramSender.WaitBeforeSendAsync(token);
   using var response=await (http??Client).PostAsJsonAsync("https://api.telegram.org/bot"+secret+"/"+method,payload,token);
   var body=await response.Content.ReadFromJsonAsync<JsonElement>(token);
   if(body.ValueKind==JsonValueKind.Object&&body.TryGetProperty("ok",out var ok)&&ok.ValueKind==JsonValueKind.True)return body.GetProperty("result").Clone();
   if(body.ValueKind!=JsonValueKind.Object)throw new TelegramPinException("invalid-response");
   var code=(int)response.StatusCode;var description=body.ValueKind==JsonValueKind.Object&&body.TryGetProperty("description",out var desc)?desc.GetString()??"":"";
   if(code==400&&method=="editMessageText"&&description.Contains("message is not modified",StringComparison.OrdinalIgnoreCase))return default;
   if(method=="unpinChatMessage"&&(description.Contains("message to unpin not found",StringComparison.OrdinalIgnoreCase)||description.Contains("message is not pinned",StringComparison.OrdinalIgnoreCase)))return default;
   if(code==429){var seconds=60;if(body.TryGetProperty("parameters",out var parameters)&&parameters.TryGetProperty("retry_after",out var retry)&&retry.TryGetInt32(out var n))seconds=Math.Clamp(n,1,3600);throw new TelegramPinException("rate-limit",seconds);}
   if(description.Contains("message to edit not found",StringComparison.OrdinalIgnoreCase)||description.Contains("message to pin not found",StringComparison.OrdinalIgnoreCase)||description.Contains("message_id_invalid",StringComparison.OrdinalIgnoreCase))throw new TelegramPinException("missing-message");
   throw new TelegramPinException(code is 401 or 403||description.Contains("rights",StringComparison.OrdinalIgnoreCase)?"rights":"api-error");
  }catch(HttpRequestException){throw new TelegramPinException("network");}catch(JsonException){throw new TelegramPinException("invalid-response");}catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new TelegramPinException("timeout");}
 }
 public async Task<int> SendAsync(string chat,string text,CancellationToken token){var result=await Call("sendMessage",new{chat_id=chat,text=ProductText.Language=="en"?ProductText.Get(text):text,disable_notification=true,link_preview_options=new{is_disabled=true}},token);if(result.ValueKind!=JsonValueKind.Object||!result.TryGetProperty("message_id",out var id)||!id.TryGetInt32(out var n)||n<=0)throw new TelegramPinException("invalid-response");return n;}
 public async Task EditAsync(string chat,int id,string text,CancellationToken token)=>_ =await Call("editMessageText",new{chat_id=chat,message_id=id,text=ProductText.Language=="en"?ProductText.Get(text):text,link_preview_options=new{is_disabled=true}},token);
 public async Task PinAsync(string chat,int id,CancellationToken token)=>_ =await Call("pinChatMessage",new{chat_id=chat,message_id=id,disable_notification=true},token);
 public async Task UnpinAsync(string chat,int id,CancellationToken token)=>_ =await Call("unpinChatMessage",new{chat_id=chat,message_id=id},token);
}
public sealed class TelegramPinPage {public int MessageId {get;set;}public string Text {get;set;}="";public bool Pinned {get;set;}}
public sealed class TelegramPinDestination {public string Chat {get;set;}="";public string BotId {get;set;}="";public List<TelegramPinPage> Pages {get;set;}=[];}
public sealed class TelegramPinState {public DateTime? CheckedUtc {get;set;}public Dictionary<string,TelegramPinDestination> Destinations {get;set;}=[];public string RetryKey {get;set;}="";public DateTime RetryUtc {get;set;}public string Status {get;set;}="Ещё не проверялось";}
public sealed class TelegramProblemPins(string directory)
{
 private string PathName=>Path.Combine(directory,"telegram-problem-pins.json");
 private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,SemaphoreSlim> Gates=new(StringComparer.OrdinalIgnoreCase);
 private TelegramPinState Read()=>NetworkStore.Locked(PathName,()=>File.Exists(PathName)?JsonSerializer.Deserialize<TelegramPinState>(File.ReadAllText(PathName))??throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Повреждено состояние закрепа")):new());
 private void Save(TelegramPinState state)=>NetworkStore.Locked(PathName,()=>{var temp=PathName+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(state));File.Move(temp,PathName,true);return true;});
 public string Status(){var s=Read();return s.Status+(s.CheckedUtc is DateTime at?" · проверено "+AppTime.FromUtc(at).ToString("dd.MM.yyyy HH:mm:ss"):"");}
 public static string[] Render(IEnumerable<SummaryProblem> problems){
  static string Cut(string value,int n)=>value.Replace('\r',' ').Replace('\n',' ').Length>n?value.Replace('\r',' ').Replace('\n',' ')[..n]+"…":value.Replace('\r',' ').Replace('\n',' ');
  var rows=problems.Where(p=>p.Severity is Severity.Critical or Severity.Warning).GroupBy(p=>p.Key).Select(g=>g.OrderByDescending(p=>p.Severity).First()).OrderByDescending(p=>p.Severity).ThenBy(p=>p.Category,StringComparer.Ordinal).ThenBy(p=>p.Source,StringComparer.Ordinal).ThenBy(p=>p.Key,StringComparer.Ordinal).ToArray();
  if(rows.Length==0)return [];
  var pages=new List<string>();var body=new StringBuilder();
  foreach(var p in rows){var line=(p.Severity==Severity.Critical?"🔴 ":"🟠 ")+Cut(ProductText.Get(p.Source),100)+" · "+Cut(ProductText.Language=="en"?ProductText.Get(p.Title):p.Title,230)+"\n";if(body.Length+line.Length>3300){pages.Add(body.ToString());body.Clear();}body.Append(line);}if(body.Length>0)pages.Add(body.ToString());
  return pages.Select((page,i)=>$"📌 SysAdmin Assistant · {ProductText.Get("АКТИВНЫЕ ПРОБЛЕМЫ")}\n{ProductText.Get("Всего:")} {rows.Length} · 🔴 {rows.Count(p=>p.Severity==Severity.Critical)} · 🟠 {rows.Count(p=>p.Severity==Severity.Warning)}\n{ProductText.Get("Страница")} {i+1} / {pages.Count}\n\n"+page+"\n"+ProductText.Get("Список обновляется сервером. Решённые проблемы убираются автоматически.")).ToArray();
 }
 public async Task SyncAsync(TelegramSettings settings,IEnumerable<SummaryProblem> problems,DateTime now,CancellationToken token,ITelegramPinApi? api=null,string? botId=null,TimeSpan? throttle=null){
  var gate=Gates.GetOrAdd(Path.GetFullPath(PathName),_=>new(1,1));await gate.WaitAsync(token);
  try{
   var state=Read();
   if(settings.ChatId.Length==0||settings.ProtectedToken.Length==0)return;
   if(api==null){var secret=TelegramPinApi.Resolve(settings);botId=secret.Split(':')[0];api=new TelegramPinApi(secret);}botId??="test-bot";
   var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(botId+"|"+settings.ChatId)));
   if(state.RetryKey==key&&now<state.RetryUtc)return;
   if(!state.Destinations.TryGetValue(key,out var destination)){destination=new(){Chat=settings.ChatId,BotId=botId};state.Destinations[key]=destination;}
   async Task Pause(){var pause=throttle??TimeSpan.FromMilliseconds(1100);if(pause>TimeSpan.Zero)await Task.Delay(pause,token);}
   async Task Retire(TelegramPinDestination prior){foreach(var p in prior.Pages.Where(p=>p.Pinned)){try{await api.UnpinAsync(prior.Chat,p.MessageId,token);}catch(TelegramPinException e)when(e.Kind=="missing-message"){}p.Pinned=false;Save(state);await Pause();}}
   try{
    var cleanupFailed=false;foreach(var prior in state.Destinations.Where(p=>p.Key!=key&&p.Value.BotId==botId).Select(p=>p.Value))try{await Retire(prior);}catch(TelegramPinException){cleanupFailed=true;}
    var pages=settings.Enabled&&settings.PinActiveProblems?Render(problems):[];
    for(int i=0;i<Math.Max(pages.Length,destination.Pages.Count);i++){
     if(i==destination.Pages.Count){destination.Pages.Add(new());Save(state);}
     var p=destination.Pages[i];var active=i<pages.Length;var text=active?pages[i]:"🟢 SysAdmin Assistant · Активных проблем на этой странице нет. Закреп снят.";
     try{
      if(active&&p.MessageId==0){p.MessageId=await api.SendAsync(destination.Chat,text,token);p.Text=text;Save(state);await Pause();}
      if(p.MessageId!=0&&p.Text!=text){await api.EditAsync(destination.Chat,p.MessageId,text,token);p.Text=text;Save(state);await Pause();}
      if(active&&!p.Pinned){await api.PinAsync(destination.Chat,p.MessageId,token);p.Pinned=true;Save(state);await Pause();}
      if(!active&&p.Pinned){await api.UnpinAsync(destination.Chat,p.MessageId,token);p.Pinned=false;Save(state);await Pause();}
     }catch(TelegramPinException e)when(e.Kind=="missing-message"){
      p.MessageId=0;p.Text="";p.Pinned=false;Save(state);throw;
     }
    }
    state.Status=pages.Length==0?"Активных проблем нет или закреп выключен; закреп снят":"Закреп активных проблем обновлён · страниц: "+pages.Length;
    if(state.Destinations.Values.Any(d=>d.BotId!=botId&&d.Pages.Any(p=>p.Pinned)))state.Status+=". После смены бота проверьте старый закреп вручную.";
    if(cleanupFailed)state.Status+=". Старый чат недоступен: проверьте его закреп вручную.";
    state.CheckedUtc=now;state.RetryUtc=DateTime.MinValue;Save(state);
   }catch(TelegramPinException e){state.RetryKey=key;state.RetryUtc=now.AddSeconds(e.RetrySeconds);state.Status=e.Kind=="rights"?"Закреп не обновлён: проверьте токен, доступ бота и право закреплять сообщения. Обычные уведомления работают независимо.":"Закреп не обновлён; повторная попытка автоматически ("+e.Kind+")";Save(state);throw;}
  }finally{gate.Release();}
 }
}
