using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public static class RouterAuditRefresh
{
 public static async Task RunAsync(NetworkMonitor monitor,DatabaseFactory factory,int id,CancellationToken token){
  var store=monitor.Store;
  await using var db=factory.Create();var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==id&&d.Type=="MikroTik"&&d.Enabled,token)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Устройство выключено или удалено"));
  var connection=store.MonitorConnection(id)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сначала настройте подключение MikroTik"));
  var captured=await monitor.Sessions.AuditAsync(id,NetworkStore.Target(device.Host,connection,new()),token);
  await using var current=factory.Create();if(!await current.Devices.AnyAsync(d=>d.Id==id&&d.Type=="MikroTik"&&d.Enabled&&d.Host==device.Host,token))return;
  store.State(s=>{var latest=s.Connections.SingleOrDefault(c=>c.DeviceId==id);if(latest==null||RemoteData.Hash(latest)!=RemoteData.Hash(connection))return false;
   var h=s.Histories.SingleOrDefault(h=>h.DeviceId==id);if(h==null){h=new(){DeviceId=id};s.Histories.Add(h);}
   if(h.Audit.Host!=""&&h.Audit.Host!=device.Host)h.Audit=new();h.Audit.Host=device.Host;
   RouterAuditStore.Apply(s,h,RouterAudit.Analyze(captured,h.Audit.ManualWan),device.Name,device.MaintenanceUntil==null||device.MaintenanceUntil<=AppTime.Now);return true;
  },true);
 }
}
