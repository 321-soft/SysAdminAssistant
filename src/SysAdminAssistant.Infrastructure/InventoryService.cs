using System.Data;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;

public sealed record InventoryRequest(InventoryOperation Operation, IReadOnlyList<int> AssetIds, int? EmployeeId, DateTime Date, string Organization, string Representative, string Place, string Reason, string Commission = "", IReadOnlyDictionary<int, int>? ExpectedRevisions = null, string RepresentativePosition = "", DateTime? DismissalDate = null);
public sealed class InventoryService(DatabaseFactory factory)
{
    public async Task<int> ConductAsync(InventoryRequest request, CancellationToken token = default)
    {
        if (factory.Remote is RemoteRepository remote) { var id = await remote.CommandAsync<int>("inventory-act", request, token); await remote.RefreshAsync(token); return id; }
        if (request.DismissalDate != null && request.Operation != InventoryOperation.Return) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Дата увольнения допускается только в акте возврата"));
        if (!Enum.IsDefined(request.Operation)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестная операция"));
        if (request.AssetIds.Count > 1000) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("В одном акте допускается до 1000 предметов"));
        if (request.AssetIds.Count == 0 || request.AssetIds.Distinct().Count() != request.AssetIds.Count) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Выберите имущество без повторов"));
        if (string.IsNullOrWhiteSpace(request.Organization) || string.IsNullOrWhiteSpace(request.Representative) || string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Заполните организацию, ответственного и основание"));
        await using var db = factory.Create();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var assets = await db.Assets.Where(a => request.AssetIds.Contains(a.Id)).OrderBy(a => a.InventoryNumber).ToArrayAsync(token);
        if (assets.Length != request.AssetIds.Count) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Часть имущества отсутствует"));
        var employee = request.EmployeeId is int employeeId ? await db.Employees.SingleOrDefaultAsync(e => e.Id == employeeId, token) : null;
        if (request.Operation != InventoryOperation.WriteOff && (employee == null || (request.Operation == InventoryOperation.Issue && !employee.Active))) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Выберите действующего сотрудника для выдачи или владельца для возврата"));
        var latestActDate = await (from line in db.ActLines join previousAct in db.Acts on line.ActId equals previousAct.Id where request.AssetIds.Contains(line.AssetId) select (DateTime?)previousAct.Date).MaxAsync(token);
        if (latestActDate != null && request.Date.Date < latestActDate.Value.Date) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Дата акта не может быть раньше предыдущего акта по выбранному имуществу"));
        foreach (var a in assets)
        {
            if (request.ExpectedRevisions != null && (!request.ExpectedRevisions.TryGetValue(a.Id, out var expected) || expected != a.Revision)) throw new InvalidOperationException($"{a.InventoryNumber}: карточка изменилась после выбора. Обновите список и составьте акт повторно");
            if (request.Operation == InventoryOperation.Issue && (a.State != AssetState.Stock || a.EmployeeId != null)) throw new InvalidOperationException($"{a.InventoryNumber}: выдавать можно только имущество на складе");
            if (request.Operation == InventoryOperation.Return && (a.State != AssetState.Assigned || a.EmployeeId != employee!.Id)) throw new InvalidOperationException($"{a.InventoryNumber}: имущество не числится за выбранным сотрудником");
            if (request.Operation == InventoryOperation.WriteOff && (a.EmployeeId != null || a.State is AssetState.Assigned or AssetState.WrittenOff)) throw new InvalidOperationException($"{a.InventoryNumber}: сначала верните имущество; повторное списание запрещено");
        }
        var act = new InventoryAct { Number = "DRAFT-" + Guid.NewGuid().ToString("N"), Operation = request.Operation, Date = request.Date.Date, EmployeeId = employee?.Id, EmployeeName = employee?.FullName ?? "", Department = employee?.Department ?? "", Position = employee?.Position ?? "", Organization = request.Organization.Trim(), Representative = request.Representative.Trim(), Place = request.Place.Trim(), Reason = request.Reason.Trim(), Commission = request.Commission.Trim(), RepresentativePosition = request.RepresentativePosition.Trim(), DismissalDate = request.DismissalDate?.Date };
        db.Acts.Add(act); await db.SaveChangesAsync(token); act.Number = $"INV-{act.Date:yyyy}-{act.Id:D6}";
        foreach (var a in assets)
        {
            db.ActLines.Add(new InventoryActLine { ActId = act.Id, AssetId = a.Id, ValueKopecks = a.ValueKopecks, InventoryNumber = a.InventoryNumber, Name = string.Join(" ", new[] { a.Category, a.Manufacturer, a.Model }.Where(x => x.Length > 0)), WindowsName = a.WindowsName, SerialNumber = a.SerialNumber, Specifications = InventoryDisplay.Specs(a) + (a.Notes.Length > 0 ? "\nКомплектность / примечания: " + a.Notes : ""), Condition = a.Condition, PreviousState = a.State });
            a.EmployeeId = request.Operation == InventoryOperation.Issue ? employee!.Id : null;
            a.State = request.Operation == InventoryOperation.Issue ? AssetState.Assigned : request.Operation == InventoryOperation.Return ? AssetState.Stock : AssetState.WrittenOff;
            a.Revision++;
        }
        // Employee revision also prevents concurrent archiving while an issue is committed.
        if (employee != null) employee.Revision++;
        await db.SaveChangesAsync(token); await tx.CommitAsync(token); return act.Id;
    }
    public async Task UnlinkAssetAsync(int id,int revision,CancellationToken token=default)
    {
        if(factory.Remote is RemoteRepository remote){await remote.CommandAsync<bool>("inventory-unlink",new{Id=id,Revision=revision},token);await remote.RefreshAsync(token);return;}
        await using var db=factory.Create();
        var asset=await db.Assets.SingleAsync(a=>a.Id==id,token);
        if(asset.Revision!=revision)throw new InvalidOperationException("Карточка изменилась. Обновите список.");
        asset.WorkstationId=null;asset.DeviceId=null;asset.Revision++;
        await db.SaveChangesAsync(token);
    }
    public async Task DeleteAssetAsync(int id,int revision,CancellationToken token=default)
    {
        if(factory.Remote is RemoteRepository remote){await remote.CommandAsync<bool>("inventory-delete",new {Id=id,Revision=revision},token);await remote.RefreshAsync(token);return;}
        await using var db=factory.Create();
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var asset=await db.Assets.SingleAsync(a=>a.Id==id,token);
        if(asset.Revision!=revision)throw new InvalidOperationException("Карточка изменилась. Обновите список.");
        if(asset.EmployeeId!=null||await db.ActLines.AnyAsync(l=>l.AssetId==id,token))
            throw new InvalidOperationException("Имущество участвует в учёте или актах. Его нельзя удалить без нарушения истории; используйте возврат или списание.");
        db.Assets.Remove(asset);await db.SaveChangesAsync(token);await tx.CommitAsync(token);
    }
    public async Task SaveAssetAsync(InventoryAsset asset, CancellationToken token = default)
    {
        if (factory.Remote is RemoteRepository remote) { await remote.CommandAsync<bool>("inventory-asset", asset, token); await remote.RefreshAsync(token); return; }
        asset.InventoryNumber = asset.InventoryNumber.Trim().ToUpperInvariant();
        if (asset.InventoryNumber.Length is 0 or > 100 || string.IsNullOrWhiteSpace(asset.Category)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Укажите инвентарный номер (до 100 символов) и категорию"));
        if (asset.ValueKopecks is < 0 or > InventoryMoney.MaximumKopecks) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Неверная стоимость имущества"));
        if (asset.RamGb < 0 || asset.DiskGb < 0) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Объём не может быть отрицательным"));
        await using var db = factory.Create(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        if (await db.Assets.AnyAsync(a => a.InventoryNumber == asset.InventoryNumber && a.Id != asset.Id, token)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Инвентарный номер уже используется"));
        if (asset.DeviceId != null && !await db.Devices.AnyAsync(d => d.Id == asset.DeviceId, token)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Связанное устройство отсутствует"));
        if (asset.DeviceId != null && asset.WorkstationId != null)
            throw new InvalidOperationException(ProductText.Get("Выберите один источник мониторинга"));
        if (asset.WorkstationId is Guid stationId && await db.Assets.AnyAsync(a => a.WorkstationId == stationId && a.Id != asset.Id && a.State != AssetState.WrittenOff, token))
            throw new InvalidOperationException(ProductText.Get("Рабочая станция уже связана с другой карточкой имущества"));
        if (asset.Id == 0) { asset.State = AssetState.Stock; asset.EmployeeId = null; db.Assets.Add(asset); }
        else
        {
            var original = await db.Assets.SingleAsync(a => a.Id == asset.Id, token);
            if (original.Revision != asset.Revision) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Карточка уже изменена. Обновите список и откройте её повторно"));
            if (original.State == AssetState.WrittenOff) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Карточка списанного имущества доступна только для просмотра"));
            asset.EmployeeId = original.EmployeeId; asset.State = original.State; asset.Revision++;
            db.Entry(original).CurrentValues.SetValues(asset);
        }
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
    }
    public async Task SaveEmployeeAsync(Employee employee, CancellationToken token = default)
    {
        if (factory.Remote is RemoteRepository remote) { await remote.CommandAsync<bool>("inventory-employee", employee, token); await remote.RefreshAsync(token); return; }
        employee.PersonnelNumber = employee.PersonnelNumber.Trim().ToUpperInvariant();
        if (employee.PersonnelNumber.Length > 100 || employee.FullName.Length > 200) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Табельный номер: до 100 символов; ФИО: до 200"));
        if (string.IsNullOrWhiteSpace(employee.FullName)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Укажите ФИО"));
        await using var db = factory.Create(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        if (!employee.Active && await db.Assets.AnyAsync(a => a.EmployeeId == employee.Id, token)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Перед увольнением или архивированием верните всё имущество сотрудника"));
        if (employee.PersonnelNumber.Length > 0 && await db.Employees.AnyAsync(e => e.PersonnelNumber == employee.PersonnelNumber && e.Id != employee.Id, token)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Табельный номер уже используется"));
        if (employee.Id == 0) db.Employees.Add(employee);
        else
        {
            var original = await db.Employees.SingleAsync(e => e.Id == employee.Id, token);
            if (original.Revision != employee.Revision) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Сотрудник уже изменён. Обновите список"));
            employee.Revision++; db.Entry(original).CurrentValues.SetValues(employee);
        }
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
    }
    public async Task SetRepairAsync(int id, int revision, bool repair, CancellationToken token = default)
    {
        if (factory.Remote is RemoteRepository remote) { await remote.CommandAsync<bool>("inventory-repair", new RemoteRepair(id, revision, repair), token); await remote.RefreshAsync(token); return; }
        await using var db = factory.Create(); var a = await db.Assets.SingleAsync(a => a.Id == id, token);
        if (a.Revision != revision || a.EmployeeId != null || a.State is AssetState.Assigned or AssetState.WrittenOff) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Сначала верните имущество и обновите список"));
        a.State = repair ? AssetState.Repair : AssetState.Stock; a.Revision++; await db.SaveChangesAsync(token);
    }
}
