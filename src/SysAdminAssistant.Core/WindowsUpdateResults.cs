namespace SysAdminAssistant.Core;
public sealed record WindowsUpdateSnapshot(DateTime GeneratedUtc,OsUpdateStatus Status);
public static class WindowsUpdateResults
{
 public static OsUpdateStatus Build(string installed,IEnumerable<string> titles,int count,DateTime? checkedUtc,bool complete,bool reboot,string diagnostic){var items=titles.Select(s=>Clean(s,200)).Where(s=>s.Length>0).Take(20).ToArray();return new(){State=count>0?"Available":complete&&checkedUtc!=null?"Current":"Unknown",Installed=Clean(installed,100),CheckedUtc=checkedUtc,RebootRequired=reboot,PendingCount=count,Items=items,Detail=Clean(diagnostic+(checkedUtc==null?"; дата штатного поиска Windows не получена":""),500)};}
 public static string Clean(string s,int limit)=>new(s.Where(c=>!char.IsControl(c)).Take(limit).ToArray());
 public static DateTime? Date(object? value){if(value is not DateTime d||d.Year<2000||d.Year>9998)return null;var utc=System.DateTime.SpecifyKind(d,DateTimeKind.Utc);return utc>DateTime.UtcNow.AddMinutes(10)?null:utc;}
 public static OsUpdateStatus? Snapshot(WindowsUpdateSnapshot? snapshot,DateTime now){if(snapshot==null||snapshot.GeneratedUtc.Kind!=DateTimeKind.Utc||snapshot.GeneratedUtc>now.AddMinutes(5)||now-snapshot.GeneratedUtc>TimeSpan.FromMinutes(45))return null;return snapshot.Status;}
}
