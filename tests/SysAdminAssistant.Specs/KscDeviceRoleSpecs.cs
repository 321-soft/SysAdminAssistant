using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

internal static class KscDeviceRoleSpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var device=new Device{Id=42,Name="KSC",Enabled=true};
        check("KSC unassigned legacy profile never starts monitoring",KscDeviceRole.BlockReason(device,0)!=null);
        check("KSC device without explicit role never starts monitoring",KscDeviceRole.BlockReason(device,42)!=null);
        device.IsKscServer=true;
        check("KSC active device with explicit role allows monitoring",KscDeviceRole.BlockReason(device,42)==null);
        device.Enabled=false;
        check("KSC disabled device blocks configured collection",KscDeviceRole.BlockReason(device,42)!=null);
        device.Enabled=true;device.IsKscServer=false;
        check("KSC removed role also rejects an in-flight batch before commit",KscDeviceRole.BlockReason(device,42)!=null);
        device.IsKscServer=true;
        check("KSC deleted or different owner is rejected",KscDeviceRole.BlockReason(null,42)!=null&&KscDeviceRole.BlockReason(device,43)!=null);
        device.Type="MikroTik";
        check("KSC network equipment cannot be an owner",KscDeviceRole.BlockReason(device,42)!=null);
        var factory=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"ksc-role.db")});
        await factory.InitializeAsync();
        await using(var db=factory.Create()){
            db.Devices.Add(new Device{Name="Legacy KSC"});await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_Device DROP COLUMN IsKscServer");
            await BindingSchema.EnsureAsync(db);await BindingSchema.EnsureAsync(db);db.ChangeTracker.Clear();
            var stored=await db.Devices.SingleAsync();
            check("KSC additive migration preserves device and defaults role off",stored.Name=="Legacy KSC"&&!stored.IsKscServer);
            var stamp=RemoteData.EntityStamp(stored);stored.IsKscServer=true;
            check("KSC role change invalidates device metadata revision",RemoteData.EntityStamp(stored)!=stamp);
            await db.SaveChangesAsync();
        }
        await using(var db=factory.Create())check("KSC role survives reopening device database",(await db.Devices.SingleAsync()).IsKscServer);
        var archive=Path.Combine(folder,"ksc-role.saa");await new DatabaseTransfer(factory).ExportAsync(archive);
        var copy=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"ksc-role-copy.db")});await copy.InitializeAsync();
        await new DatabaseTransfer(copy).ImportAsync(archive);
        await using(var db=copy.Create())check("KSC role survives portable device transfer",(await db.Devices.SingleAsync()).IsKscServer);
    }
}
