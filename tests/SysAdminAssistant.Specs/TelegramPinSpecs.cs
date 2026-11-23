using System.Net;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class TelegramPinSpecs
{
 sealed class Api:ITelegramPinApi{
  public int Sends,Edits,Pins,Unpins;int next;public bool Missing;public TelegramPinException? Failure;
  public HashSet<string> Active=[];public List<(string Chat,int Id)> Removed=[];
  public Task<int> SendAsync(string chat,string text,CancellationToken token){Sends++;return Task.FromResult(++next);}
  public Task EditAsync(string chat,int id,string text,CancellationToken token){Edits++;if(Missing){Missing=false;Active.Remove(chat+":"+id);throw new TelegramPinException("missing-message");}return Task.CompletedTask;}
  public Task PinAsync(string chat,int id,CancellationToken token){Pins++;if(Failure!=null)throw Failure;Active.Add(chat+":"+id);return Task.CompletedTask;}
  public Task UnpinAsync(string chat,int id,CancellationToken token){Unpins++;Removed.Add((chat,id));Active.Remove(chat+":"+id);return Task.CompletedTask;}
 }
 sealed class Handler:HttpMessageHandler{
  public List<(string Method,JsonElement Body)> Calls=[];public int Code=200;public string? Description;public int Retry=120;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){var method=request.RequestUri!.Segments.Last();var body=JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(token));Calls.Add((method,body));var response=Code==200?method=="sendMessage"?"{\"ok\":true,\"result\":{\"message_id\":321}}":"{\"ok\":true,\"result\":true}":JsonSerializer.Serialize(new{ok=false,description=Description??"fixture",parameters=new{retry_after=Retry}});return new((HttpStatusCode)Code){Content=new StringContent(response,Encoding.UTF8,"application/json")};}
 }
 public static async Task Run(string folder,Action<string,bool> check){
  var now=new DateTime(2026,10,23,8,0,0,DateTimeKind.Utc);var settings=new TelegramSettings{Enabled=true,ChatId="-100123",ProtectedToken="test-secret-never-persist",Warning=false,PinActiveProblems=true};
  SummaryProblem[] problems=[new("a","routers",Severity.Critical,"Router","API lost"),new("b","backup",Severity.Warning,"Backup","Report missing"),new("c","agents",Severity.Info,"Agent","info")];
  var rendered=TelegramProblemPins.Render(problems);check("Active pin includes warnings independently of regular notification filter",rendered.Length==1&&rendered[0].Contains("API lost")&&rendered[0].Contains("Report missing")&&!rendered[0].Contains(" · info"));
  check("Resolved empty problem set creates no pinned message",TelegramProblemPins.Render([]).Length==0);
  var many=Enumerable.Range(0,120).Select(i=>new SummaryProblem("key"+i,"routers",Severity.Warning,"Device"+i,"Problem-"+i+"-"+new string('x',210))).ToArray();var pages=TelegramProblemPins.Render(many);
  check("All active issues paginate within Telegram size limit",pages.Length>1&&pages.All(p=>p.Length<4096)&&many.All(m=>pages.Any(p=>p.Contains(m.Source+" · Problem-"))));
  check("Reordering input never edits unchanged pinned content",TelegramProblemPins.Render(problems.Reverse()).SequenceEqual(rendered));
  var path=Path.Combine(folder,"pins200");var store=new TelegramProblemPins(path);var api=new Api();
  Task Sync(IEnumerable<SummaryProblem> rows,DateTime? at=null)=>store.SyncAsync(settings,rows,at??now,default,api,"123",TimeSpan.Zero);
  await Sync(problems);check("Initial problems send and pin one managed summary",api.Sends==1&&api.Pins==1&&api.Active.Count==1);
  await Sync(problems);check("Unchanged pin causes no duplicate message edit or pin",api.Sends==1&&api.Edits==0&&api.Pins==1);
  await Sync(problems.Take(1));check("Resolving one issue edits existing pin",api.Sends==1&&api.Edits==1&&api.Unpins==0);
  await Sync([]);check("Resolving final issue unpins only owned message",api.Unpins==1&&api.Removed.Single()==("-100123",1)&&api.Active.Count==0);
  await Sync(problems);check("New problems reuse persisted message rather than spam",api.Sends==1&&api.Pins==2&&api.Active.Count==1);
  await new TelegramProblemPins(path).SyncAsync(settings,problems,now,default,api,"123",TimeSpan.Zero);check("Server restart reuses pin state",api.Sends==1&&api.Pins==2);
  check("Persisted pin state never contains bot credential",!File.ReadAllText(Path.Combine(path,"telegram-problem-pins.json")).Contains(settings.ProtectedToken));
  settings.PinActiveProblems=false;await Sync(problems);check("Disabling pin removes managed pin",api.Active.Count==0);settings.PinActiveProblems=true;
  var failPath=Path.Combine(folder,"pins-permission");var failed=new TelegramProblemPins(failPath);var denied=new Api{Failure=new("rights")};
  try{await failed.SyncAsync(settings,problems,now,default,denied,"123",TimeSpan.Zero);}catch(TelegramPinException){}
  check("Pin permission failure preserves sent message ID and useful status",denied.Sends==1&&failed.Status().Contains("право"));denied.Failure=null;
  await failed.SyncAsync(settings,problems,now.AddSeconds(30),default,denied,"123",TimeSpan.Zero);check("Permission failure backs off without repeated sends",denied.Sends==1&&denied.Pins==1);
  await failed.SyncAsync(settings,problems,now.AddSeconds(61),default,denied,"123",TimeSpan.Zero);check("Restored pin permission never resends existing message",denied.Sends==1&&denied.Pins==2&&denied.Active.Count==1);
  var limited=new TelegramProblemPins(Path.Combine(folder,"pins-rate"));var rate=new Api{Failure=new("rate-limit",120)};
  try{await limited.SyncAsync(settings,problems,now,default,rate,"123",TimeSpan.Zero);}catch(TelegramPinException){}
  rate.Failure=null;await limited.SyncAsync(settings,problems,now.AddSeconds(119),default,rate,"123",TimeSpan.Zero);check("Telegram retry_after is respected",rate.Pins==1);
  await limited.SyncAsync(settings,problems,now.AddSeconds(121),default,rate,"123",TimeSpan.Zero);check("Rate limit retry keeps same message",rate.Sends==1&&rate.Pins==2);
  await Sync(problems);api.Missing=true;try{await Sync(problems.Take(1));}catch(TelegramPinException){}
  await Sync(problems.Take(1),now.AddSeconds(61));check("Deleted managed message is recreated and pinned",api.Sends==2&&api.Active.Count==1);
  settings.ChatId="-100456";await Sync(problems,now.AddSeconds(122));check("Destination change retires old own pin",api.Active.Count==1&&api.Active.Single().StartsWith("-100456:")&&api.Removed.Any(r=>r.Chat=="-100123"));
  var parallel=new TelegramProblemPins(Path.Combine(folder,"pins-concurrent"));var same=new Api();await Task.WhenAll(parallel.SyncAsync(settings,problems,now,default,same,"123",TimeSpan.Zero),new TelegramProblemPins(Path.Combine(folder,"pins-concurrent")).SyncAsync(settings,problems,now,default,same,"123",TimeSpan.Zero));check("Concurrent pin workers coalesce first send",same.Sends==1&&same.Pins==1);
  using var handler=new Handler();using var http=new HttpClient(handler);var transport=new TelegramPinApi("123:abcdefghijklmnopqrstuvwxyz",http);
  check("Telegram transport captures returned message ID",await transport.SendAsync("-1","fixture",default)==321);
  await transport.PinAsync("-1",321,default);await transport.UnpinAsync("-1",321,default);
  check("Pin is silent and unpin always targets explicit managed message",handler.Calls.Any(c=>c.Method=="pinChatMessage"&&c.Body.GetProperty("disable_notification").GetBoolean())&&handler.Calls.Single(c=>c.Method=="unpinChatMessage").Body.GetProperty("message_id").GetInt32()==321);
  handler.Code=400;handler.Description="Bad Request: message is not modified";await transport.EditAsync("-1",321,"fixture",default);check("Unchanged Telegram edit is idempotent",true);
  handler.Code=429;try{await transport.PinAsync("-1",321,default);throw new Exception("Expected limit");}catch(TelegramPinException e){check("Telegram rate limit decoded without leaking token",e.RetrySeconds==120&&!e.Message.Contains("abcdefghijklmnopqrstuvwxyz"));}
  handler.Code=403;handler.Description="Forbidden";try{await transport.PinAsync("-1",321,default);throw new Exception("Expected rights failure");}catch(TelegramPinException e){check("Telegram permission error has distinct safe kind",e.Kind=="rights");}
 }
}
