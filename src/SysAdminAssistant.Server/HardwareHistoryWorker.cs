using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;
internal sealed class HardwareHistoryWorker(ServerState state,ILogger<HardwareHistoryWorker> log):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
        do{
            try{
                await state.Gate.WaitAsync(token);
                try{await CaptureAsync(state,token);}finally{state.Gate.Release();}
            }catch(OperationCanceledException)when(token.IsCancellationRequested){return;}
            catch(Exception e){log.LogError(e,"Не удалось записать историю оборудования");}
        }while(await timer.WaitForNextTickAsync(token));
    }
    internal static async Task CaptureAsync(ServerState state,CancellationToken token)
    {
        var history=new HardwareHistoryStore(state.DirectoryPath);var now=DateTime.UtcNow;
        foreach(var station in state.Workstations.List(false,now).Where(s=>s.Status=="Approved"))
            history.Observe(HardwareHistoryStore.Station(station.Id),station.Sample?.Hardware,now);
        await using var db=state.Factory.Create();
        foreach(var id in await db.Devices.Select(d=>d.Id).ToArrayAsync(token))
            if(state.Agents.Get(id) is {Approved:true,Revoked:false,Report:{} report})
                history.Observe(HardwareHistoryStore.Device(id),report.Hardware,now);
        foreach(var asset in await db.Assets.Where(a=>a.DeviceId!=null||a.WorkstationId!=null).AsNoTracking().ToArrayAsync(token))
            history.Link(asset.Id,asset.WorkstationId is Guid station?HardwareHistoryStore.Station(station):HardwareHistoryStore.Device(asset.DeviceId!.Value));
    }
}
