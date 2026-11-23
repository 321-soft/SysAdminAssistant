using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Server;
internal static class InventoryHardwareSync
{
    public static async Task SyncAsync(ServerState state,CancellationToken token)
    {
        await using var db=state.Factory.Create();
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        var assets=await db.Assets.Where(a=>a.State!=AssetState.WrittenOff&&(a.WorkstationId!=null||a.DeviceId!=null)).ToArrayAsync(token);
        var stations=state.Workstations.List(false,DateTime.UtcNow).Where(s=>s.Status=="Approved").ToDictionary(s=>s.Id);
        var changed=false;
        foreach(var asset in assets){
            bool updated=false;
            if(asset.WorkstationId is Guid id&&stations.TryGetValue(id,out var station)&&station.Sample is {} sample){
                updated=InventoryMonitoringData.Apply(asset,station.Name,sample.Os,sample.Serial,sample.Hardware?.Cpu,sample.Hardware?.MemoryBytes??0,sample.Hardware?.DiskBytes??0,sample.Hardware);
            }else if(asset.DeviceId is int device&&state.Agents.Get(device) is {Approved:true,Revoked:false,Report:{} report}){
                updated=InventoryMonitoringData.Apply(asset,report.MachineName,report.Os,null,report.CpuName,report.MemoryTotalBytes,report.Disks.Sum(d=>d.TotalBytes),report.Hardware);
            }
            if(updated){asset.Revision++;changed=true;}
        }
        if(changed){await db.SaveChangesAsync(token);state.Touch();}
        await tx.CommitAsync(token);
    }
}
