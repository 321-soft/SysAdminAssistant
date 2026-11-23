using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public static class NotificationSubjects
{
 public static string Device(Device d)=>d.Name+(string.IsNullOrWhiteSpace(d.Host)?"":" · "+d.Host);
 public static string Incident(Incident incident,ReportEvent? report,IReadOnlyDictionary<int,Device> devices,IReadOnlyDictionary<int,SqlBackupJob> jobs){
  if(report==null)return "";var deviceId=report.DeviceId;SqlBackupJob? job=null;if(report.JobId is int jid&&jobs.TryGetValue(jid,out job))deviceId??=job.DeviceId;
  var label=deviceId is int id&&devices.TryGetValue(id,out var d)?Device(d):!string.IsNullOrWhiteSpace(report.Host)?report.Host:report.TargetName;
  return (string.IsNullOrWhiteSpace(label)?"":"Устройство: "+label)+(job==null?"":(string.IsNullOrWhiteSpace(label)?"":"\n")+"Задание: "+job.Title);
 }
 public static string Network(NetworkNotification n,IReadOnlyDictionary<int,Device> devices,IReadOnlyDictionary<int,SqlBackupJob> jobs){
  var parts=n.EntityKey.Split(':');int? deviceId=n.DeviceId;SqlBackupJob? job=null;
  if(parts.Length>1&&int.TryParse(parts[1],out var id)){
   if(parts[0] is "agent" or "agent-link" or "journal" or "audit-summary" or "audit" or "router-audit" or "os-updates")deviceId??=id;
   if(parts[0] is "schedule" or "backup-incident"&&jobs.TryGetValue(id,out job))deviceId??=job.DeviceId;
  }
  if(parts.Length>2&&parts[0]=="availability"&&parts[1]=="device"&&int.TryParse(parts[2],out var did))deviceId??=did;
  var label=deviceId is int key&&devices.TryGetValue(key,out var device)?Device(device):job?.Host??"";
  return (string.IsNullOrWhiteSpace(label)?"":"Устройство: "+label)+(job==null?"":(string.IsNullOrWhiteSpace(label)?"":"\n")+"Задание: "+job.Title);
 }
}
