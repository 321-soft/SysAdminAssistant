using System.Windows;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private async Task InventoryFromMonitoring(InventoryAsset original)
    {
        if (await PickInventorySource(original, this) is { } asset)
            await EditAsset(asset);
    }

    private async Task<InventoryAsset?> PickInventorySource(InventoryAsset original, Window owner)
    {
        AccessRules.Require(remote.CurrentUser, "inventory", 2);
        if (original.State == AssetState.WrittenOff)
            throw new InvalidOperationException(ProductText.Get("Карточка списанного имущества доступна только для просмотра"));

        await using var db = factory.Create();
        var canUseDevices = AccessRules.Level(remote.CurrentUser, "devices") > 0;
        var canUseStations = AccessRules.Level(remote.CurrentUser, "workstations") > 0;
        var devices = canUseDevices
            ? await db.Devices.AsNoTracking().OrderBy(x => x.Name).ToArrayAsync(lifetime.Token) : [];
        var stations = canUseStations
            ? (await remote.CommandAsync<StationView[]>("workstations-list", new {}, lifetime.Token))
                .Where(x => x.Status == "Approved").OrderBy(x => x.Name).ToArray() : [];
        var linked = await db.Assets.AsNoTracking()
            .Where(x => x.Id != original.Id && x.State != AssetState.WrittenOff && x.WorkstationId != null)
            .Select(x => x.WorkstationId).ToArrayAsync(lifetime.Token);
        var choices = devices.Select(x => new Choice(x.Id, $"Устройство · {x}"))
            .Concat(stations.Where(x => !linked.Contains(x.Id)).Select(x => new Choice(x.Id, $"Рабочая станция · {x.Name}")))
            .ToList();
        const string unlink = "unlink";
        if (original.WorkstationId != null ? canUseStations : original.DeviceId != null && canUseDevices)
            choices.Add(new Choice(unlink, "Снять привязку"));
        if (choices.Count == 0)
            throw new InvalidOperationException(ProductText.Get("Нет доступных устройств или подтверждённых рабочих станций"));

        object? current = (object?)original.WorkstationId ?? original.DeviceId;
        var dialog = new FormDialog(owner, original.Id == 0 ? "Создать на основании" : "Привязать к мониторингу", 620)
            .Note("Известные характеристики заменяются данными мониторинга. Учётные поля сохраняются.")
            .Select("source", "Источник мониторинга", choices, current ?? (choices.Count == 1 ? choices[0].Key : null));
        dialog.Validate = () => dialog.Selected("source") is null ? ProductText.Get("Выберите источник мониторинга") : null;
        if (dialog.ShowDialog() != true) return null;

        var asset = Copy(original);
        if (dialog.Selected("source") is Guid workstationId)
        {
            var station = stations.Single(x => x.Id == workstationId);
            asset.DeviceId = null;
            asset.WorkstationId = station.Id;
            InventoryMonitoringData.Apply(asset,station.Name,station.Sample?.Os,station.Sample?.Serial,station.Sample?.Hardware?.Cpu,
                station.Sample?.Hardware?.MemoryBytes??0,station.Sample?.Hardware?.DiskBytes??0,station.Sample?.Hardware);
        }
        else if (dialog.Selected("source") is int deviceId)
        {
            var device = devices.Single(x => x.Id == deviceId);
            asset.WorkstationId = null;
            asset.DeviceId = device.Id;
            asset.WindowsName = Fill(asset.WindowsName, device.Name);
            asset.Location = Fill(asset.Location, device.Site);
            if (asset.Id == 0 && string.IsNullOrWhiteSpace(asset.Category) && !string.IsNullOrWhiteSpace(device.Type)) asset.Category = device.Type;
            if (!NetworkDevices.IsNetwork(device.Type))
            {
                var view = await remote.CommandAsync<AgentViewResult>("agent-view", new { DeviceId = deviceId }, lifetime.Token);
                if (view.Agent is { Approved: true, Revoked: false, Report: {} report })
                {
                    InventoryMonitoringData.Apply(asset,report.MachineName,report.Os,null,report.CpuName,
                        report.MemoryTotalBytes,report.Disks.Sum(d=>d.TotalBytes),report.Hardware);
                }
            }
        }
        else
        {
            asset.DeviceId = null;
            asset.WorkstationId = null;
        }
        asset.Category = Fill(asset.Category, "Компьютер");
        return asset;
    }

    private async Task CreateInventoryFromStation(StationView station)
    {
        if(!await UnlockInventory())return;
        await using var db=factory.Create();
        if(await db.Assets.AnyAsync(a=>a.WorkstationId==station.Id&&a.State!=AssetState.WrittenOff,lifetime.Token))
            throw new InvalidOperationException("Станция уже связана с карточкой имущества.");
        var asset=new InventoryAsset{Category="Компьютер",WorkstationId=station.Id,DiskType=""};
        var sample=station.Sample;
        InventoryMonitoringData.Apply(asset,station.Name,sample?.Os,sample?.Serial,sample?.Hardware?.Cpu,sample?.Hardware?.MemoryBytes??0,sample?.Hardware?.DiskBytes??0,sample?.Hardware);
        await EditAsset(asset);
    }
    private async Task CreateInventoryFromDevice(Device device)
    {
        if(!await UnlockInventory())return;
        await using var db=factory.Create();
        if(await db.Assets.AnyAsync(a=>a.DeviceId==device.Id&&a.State!=AssetState.WrittenOff,lifetime.Token))
            throw new InvalidOperationException("Устройство уже связано с карточкой имущества.");
        var asset=new InventoryAsset{Category=device.Type,DeviceId=device.Id,WindowsName=device.Name,Location=device.Site,DiskType=""};
        if(!NetworkDevices.IsNetwork(device.Type)){
            var view=await remote.CommandAsync<AgentViewResult>("agent-view",new{DeviceId=device.Id},lifetime.Token);
            if(view.Agent is {Approved:true,Revoked:false,Report:{} report})
                InventoryMonitoringData.Apply(asset,report.MachineName,report.Os,null,report.CpuName,report.MemoryTotalBytes,report.Disks.Sum(d=>d.TotalBytes),report.Hardware);
        }
        await EditAsset(asset);
    }

    private static string Fill(string value, string? observed) =>
        string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(observed) ? observed : value;
}
