using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed class DailySummaryDay {public bool Enabled {get;set;}=true;public string Time {get;set;}="18:00";}
public sealed class DailySummarySettings {
 public bool Enabled {get;set;}
 public DailySummaryDay[] Days {get;set;}=Enumerable.Range(0,7).Select(_=>new DailySummaryDay()).ToArray(); // Monday first
 public string[] Categories {get;set;}=["mail","backup","payments","availability","routers","agents"];
 public static readonly string[] Known=["mail","backup","payments","availability","routers","agents"];
 public static void Validate(DailySummarySettings s){if(s==null||s.Days==null||s.Days.Length!=7||s.Days.Any(d=>d==null||!TimeOnly.TryParseExact(d.Time,"HH:mm",out _))||s.Categories==null||s.Categories.Length>6||s.Categories.Any(c=>!Known.Contains(c)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сводка: укажите время HH:mm для семи дней недели и корректные разделы"));}
 public static bool Due(DailySummarySettings s,DateTime local,string sentDay){Validate(s);var day=s.Days[((int)local.DayOfWeek+6)%7];return s.Enabled&&day.Enabled&&local.ToString("yyyy-MM-dd")!=sentDay&&TimeOnly.FromDateTime(local)>=TimeOnly.ParseExact(day.Time,"HH:mm");}
}
public sealed record SummaryProblem(string Key,string Category,Severity Severity,string Source,string Title);
public sealed record SummaryTransition(DateTime Utc,string Category,bool Resolved);
public sealed class DailySummaryState {
 public DateTime SinceUtc {get;set;}=DateTime.UtcNow;public bool Initialized {get;set;}
 public Dictionary<string,SummaryProblem> Active {get;set;}=[];public List<SummaryTransition> Changes {get;set;}=[];
 public string SentDay {get;set;}="";public string LastResult {get;set;}="Ещё не отправлялась";public DateTime? SentUtc {get;set;}
 public string PendingDay {get;set;}="";public string PendingText {get;set;}="";public DateTime PendingCutoffUtc {get;set;}
}
public sealed class DailySummaryStore(string directory) {
 private string PathName=>Path.Combine(directory,"daily-summary.json");
 private T State<T>(Func<DailySummaryState,T> action,bool write=false)=>NetworkStore.Locked(PathName,()=>{var s=File.Exists(PathName)?JsonSerializer.Deserialize<DailySummaryState>(File.ReadAllText(PathName))??throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Повреждена история сводок")):new();var answer=action(s);if(write){var temp=PathName+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(s));File.Move(temp,PathName,true);}return answer;});
 public void Observe(IEnumerable<SummaryProblem> problems,DateTime utc)=>State(s=>{
  var next=problems.Where(p=>p.Severity is Severity.Warning or Severity.Critical).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.OrderByDescending(p=>p.Severity).First());
  if(s.Initialized){foreach(var p in next.Values.Where(p=>!s.Active.ContainsKey(p.Key)))s.Changes.Add(new(utc,p.Category,false));foreach(var p in s.Active.Values.Where(p=>!next.ContainsKey(p.Key)))s.Changes.Add(new(utc,p.Category,true));}else{s.Initialized=true;s.SinceUtc=utc;}
  s.Active=next;return true;
 },true);
 public string Preview(DailySummarySettings settings,string zone,DateTime utc)=>State(s=>Render(s,settings,zone,utc,false));
 public void Failed()=>State(s=>{s.LastResult="Сводка не подготовлена или не отправлена; повторная попытка автоматически";return true;},true);
 public string Status()=>State(s=>s.LastResult+(s.SentUtc is DateTime at?" · "+AppTime.FromUtc(at).ToString("dd.MM.yyyy HH:mm"):""));
 private static string Render(DailySummaryState s,DailySummarySettings settings,string zone,DateTime utc,bool telegram){
  var changes=s.Changes.Where(c=>c.Utc>s.SinceUtc&&c.Utc<=utc&&settings.Categories.Contains(c.Category)).ToArray();var active=s.Active.Values.Where(p=>settings.Categories.Contains(p.Category)).OrderByDescending(p=>p.Severity).ThenBy(p=>p.Source).ThenBy(p=>p.Key).ToArray();
  var text=$"📋 {ProductText.Get("Ежедневная сводка")}\n{ProductText.Get("Период:")} {AppTime.InZone(s.SinceUtc,zone):dd.MM HH:mm} — {AppTime.InZone(utc,zone):dd.MM HH:mm} ({zone})\n{ProductText.Get("Новых проблем:")} {changes.Count(c=>!c.Resolved)}\n{ProductText.Get("Решено / снято с контроля:")} {changes.Count(c=>c.Resolved)}\n{ProductText.Get("Активных:")} {active.Length} · 🔴 {active.Count(p=>p.Severity==Severity.Critical)} · 🟠 {active.Count(p=>p.Severity==Severity.Warning)}\n";
  if(active.Length==0)return text+ProductText.Get("🟢 Активных проблем в выбранных разделах нет.");
  text+="\n"+ProductText.Get("Остаются активными:")+"\n";var shown=0;foreach(var p in active){string Cut(string a,int n)=>a.Length>n?a[..n]+"…":a;var line=(p.Severity==Severity.Critical?"🔴 ":"🟠 ")+Cut(ProductText.Get(p.Source).Replace('\n',' '),80)+" · "+Cut((ProductText.Language=="en"?ProductText.Get(p.Title):p.Title).Replace('\n',' '),160)+"\n";if(telegram&&text.Length+line.Length>3250)break;text+=line;shown++;}
  if(shown<active.Length)text+="Ещё "+(active.Length-shown)+". Полный список: Отчёты → Сводка Telegram → Предпросмотр.";
  return text;
 }
 private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,SemaphoreSlim> Gates=new(StringComparer.OrdinalIgnoreCase);
 public async Task SendDueAsync(DailySummarySettings settings,TelegramSettings telegram,string zone,DateTime utc,CancellationToken token,Func<string,CancellationToken,Task>? send=null){
  DailySummarySettings.Validate(settings);if(!settings.Enabled||!telegram.Enabled)return;var gate=Gates.GetOrAdd(Path.GetFullPath(directory),_=>new(1,1));await gate.WaitAsync(token);try{
   var local=AppTime.InZone(utc,zone);var text=State(s=>{if(!s.Initialized||!DailySummarySettings.Due(settings,local,s.SentDay))return "";var today=local.ToString("yyyy-MM-dd");if(s.PendingDay!=today){s.PendingDay=today;s.PendingCutoffUtc=utc;s.PendingText=Render(s,settings,zone,utc,true);}return s.PendingText;},true);
   if(text=="")return;
   try{await(send?.Invoke(text,token)??TelegramSender.SendAsync(telegram,text,token));State(s=>{s.SentDay=s.PendingDay;s.SentUtc=utc;s.SinceUtc=s.PendingCutoffUtc;s.Changes.RemoveAll(c=>c.Utc<=s.SinceUtc);s.PendingDay="";s.PendingText="";s.LastResult="Отправлена";return true;},true);}
   catch{State(s=>{s.LastResult="Не отправлена: повторная попытка автоматически";return true;},true);throw;}
  }finally{gate.Release();}
 }
}
