using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;

public static class RouterLogIntake
{
 private static readonly CultureInfo Culture=CultureInfo.InvariantCulture;
 private static readonly string[] Formats=["yyyy-MM-dd HH:mm:ss","yyyy-MM-dd HH:mm:ss.fff","MMM/dd/yyyy HH:mm:ss","MMM/dd HH:mm:ss"];
 public static DateTime? RouterLocal(string raw,DateTime reference){
  if(DateTime.TryParseExact(raw,Formats,Culture,DateTimeStyles.AllowWhiteSpaces,out var date)){
   if(raw.Length>=6&&char.IsLetter(raw[0])&&raw.Split('/').Length==2){date=new DateTime(reference.Year,date.Month,date.Day,date.Hour,date.Minute,date.Second);if(date>reference.AddDays(1))date=date.AddYears(-1);}
   return DateTime.SpecifyKind(date,DateTimeKind.Unspecified);
  }
  if(TimeSpan.TryParseExact(raw,[@"hh\:mm\:ss",@"hh\:mm\:ss\.fff"],Culture,out var time)){
   var value=reference.Date+time;if(value>reference.AddSeconds(30))value=value.AddDays(-1);return value;
  }
  return null;
 }
 private static DateTime? LocalClock(Dictionary<string,string>? clock){
  if(clock==null)return null;var text=clock.GetValueOrDefault("date","")+" "+clock.GetValueOrDefault("time","");
  return DateTime.TryParseExact(text,Formats,Culture,DateTimeStyles.AllowWhiteSpaces,out var value)?DateTime.SpecifyKind(value,DateTimeKind.Unspecified):null;
 }
 public static DateTime? EventTime(string raw,Dictionary<string,string>? clock,DateTime now){
  var localNow=LocalClock(clock);if(localNow==null)return null;
  var at=RouterLocal(raw,localNow.Value);return at==null?null:now+(at.Value-localNow.Value);
 }
 public static List<NetworkLog> Collect(NetworkHistory h,RouterSnapshot snapshot,string user,NetworkDictionary dictionary,DateTime now){
  var fresh=new List<NetworkLog>();if(snapshot.LogError.Length>0)return fresh;
  var migration=h.LogIntakeVersion<1;var baseline=migration||!h.LogsInitialized;
  if(migration){foreach(var old in h.Logs)old.Baseline=true;h.LogIntakeVersion=1;}
  var clock=LocalClock(snapshot.Clock);var reference=clock??AppTime.FromUtc(now);
  var known=h.Logs.Select(l=>l.Key).ToHashSet(StringComparer.Ordinal);var ordinal=new Dictionary<string,int>(StringComparer.Ordinal);
  var rows=new List<NetworkLog>();
  foreach(var row in (snapshot.Logs??[]).TakeLast(1000)){
   var raw=row.GetValueOrDefault("time","");var topics=row.GetValueOrDefault("topics","");var message=row.GetValueOrDefault("message","");
   var local=RouterLocal(raw,reference);var identity=(local?.ToString("yyyy-MM-dd HH:mm:ss.fff",Culture)??raw)+"\0"+topics+"\0"+message;
   ordinal.TryGetValue(identity,out var occurrence);ordinal[identity]=occurrence+1;
   var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity+"\0"+occurrence)));
   rows.Add(new(){Key=key,RouterTime=raw,Topics=topics,Message=message,ReceivedAt=now,EventUtc=clock!=null&&local!=null?now+(local.Value-clock.Value):null});
  }
  // Untimed messages require an overlapping retained cursor. A disconnected/replaced log buffer is a baseline.
  var anchor=rows.FindLastIndex(l=>known.Contains(l.Key));
  for(int i=0;i<rows.Count;i++){
   var log=rows[i];if(!known.Add(log.Key))continue;
   var timely=log.EventUtc is DateTime at?at>=now.AddMinutes(-5)&&at<=now.AddSeconds(30):anchor>=0&&i>anchor;
   log.Baseline=baseline||!timely;NetworkSignals.Explain(log,dictionary,new Dictionary<string,string[]>());
   log.MonitoringLogin=log.Message.Contains("user "+user+" logged in from ",StringComparison.Ordinal)&&log.Topics.Split(',').Contains("account");
   h.Logs.Add(log);if(!log.Baseline)fresh.Add(log);
  }
  // Correct only alarms whose preserved evidence proves an old log was presented as a new event.
  foreach(var p in h.Problems.Where(p=>p.Open&&p.Kind=="Log"&&p.Evidence.Count>0)){
   if(p.Evidence.All(e=>EventTime(e.Log.RouterTime,snapshot.Clock,now) is DateTime eventAt&&eventAt<e.Log.ReceivedAt.AddMinutes(-5)))
    NetworkLogRules.Close(p,now,"Повторное событие RouterOS");
  }
  return fresh;
 }
}
