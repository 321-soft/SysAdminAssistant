using System.Data;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;
public sealed partial class OrganizationVault
{
    public async Task<ExcelFile> ExportExcel(UserView actor,string kind,CancellationToken token)
    {
        AccessRules.Require(actor,ExcelExchange.Module(kind));await using var db=state.Factory.Create();
        IEnumerable<string[]> rows;
        if(kind=="devices")rows=(await db.Devices.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(token)).Where(x=>!NetworkDevices.IsNetwork(x.Type)).Select(x=>new[]{x.Id.ToString(),ExcelExchange.DeviceVersion(x),x.Name,x.Type,x.Host,x.Site,x.Role,x.Enabled?"Да":"Нет"});
        else if(kind=="employees")rows=(await db.Employees.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(token)).Select(x=>new[]{x.Id.ToString(),x.Revision.ToString(),x.FullName,x.PersonnelNumber,x.Department,x.Position,x.Contact,x.Active?"Да":"Нет"});
        else {
            var entries=await db.Credentials.AsNoTracking().Where(x=>actor.Administrator||x.OwnerId==actor.Id).OrderBy(x=>x.Id).ToArrayAsync(token);
            Audit(actor,"vault.excel.export.request");rows=entries.Select(x=>{Allow(actor,x);var p=Open(actor,x);return new[]{x.Id.ToString(),x.Revision.ToString(),x.Name,p.Login,p.Password,p.Notes,x.DeviceId?.ToString()??"",x.ServiceId?.ToString()??""};}).ToArray();
        }
        return new(ExcelExchange.Write(kind,rows),$"SAA-{kind}-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }
    public async Task<ExcelPreview> ImportExcel(UserView actor,string kind,ExcelRequest request,bool commit,CancellationToken token)
    {
        AccessRules.Require(actor,ExcelExchange.Module(kind),2);
        var lines=ExcelExchange.Read(request.File,kind);if(lines.Length==0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("На листе «Данные» нет заполненных строк"));
        await using var db=state.Factory.Create();await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var devices=await db.Devices.ToDictionaryAsync(x=>x.Id,token);var people=kind=="employees"?await db.Employees.ToListAsync(token):[];
        var entries=kind=="vault"?await db.Credentials.Where(x=>actor.Administrator||x.OwnerId==actor.Id).ToDictionaryAsync(x=>x.Id,token):[];
        var assigned=kind=="employees"?await db.Assets.Where(x=>x.EmployeeId!=null).Select(x=>x.EmployeeId!.Value).Distinct().ToArrayAsync(token):[];
        var duplicateIds=lines.Where(x=>x.Values[0].Length>0).GroupBy(x=>x.Values[0].TrimStart('0')).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
        var result=new List<ExcelPreviewRow>();var changedEmployees=new Dictionary<Employee,int>();
        foreach(var line in lines){var v=line.Values;var name=v[2];var error="";var operation=v[0].Length==0?"Добавить":"Обновить";
            try{
                var id=ExcelExchange.Id(v[0]);if(duplicateIds.Contains(v[0].TrimStart('0')))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("ID повторяется в файле"));
                if(id>0&&!request.UpdateExisting)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Для строк с ID включите «Обновлять существующие» либо очистите ID и версию для новой записи"));
                if(id==0&&v[1].Length>0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Для новой записи очистите ID и версию"));
                ExcelExchange.Text(name,200,true);
                if(kind=="devices"){
                    var d=id==0?new Device():devices.GetValueOrDefault(id)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Устройство не найдено"));
                    if(NetworkDevices.IsNetwork(d.Type)||NetworkDevices.IsNetwork(v[3]))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сетевые устройства управляются в разделе «Коммутаторы / сеть»"));
                    if(id>0&&v[1]!=ExcelExchange.DeviceVersion(d))throw new ConflictException(global::SysAdminAssistant.Core.ProductText.Get("Устройство изменено. Сделайте новую выгрузку"));
                    var type=ExcelExchange.Text(v[3],100);if(type.Length==0)type="Сервер";
                    var host=ExcelExchange.Text(v[4],1000);var site=ExcelExchange.Text(v[5],1000);var role=ExcelExchange.Text(v[6],10000);var enabled=ExcelExchange.Flag(v[7]);
                    d.Name=name;d.Type=type;d.Host=host;d.Site=site;d.Role=role;d.Enabled=enabled;if(id==0)db.Devices.Add(d);
                }else if(kind=="employees"){
                    var e=id==0?new Employee():people.SingleOrDefault(x=>x.Id==id)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сотрудник не найден"));
                    if(id>0&&ExcelExchange.Revision(v[1])!=e.Revision)throw new ConflictException(global::SysAdminAssistant.Core.ProductText.Get("Сотрудник изменён. Сделайте новую выгрузку"));
                    var number=ExcelExchange.Text(v[3],100).Trim().ToUpperInvariant();var dep=ExcelExchange.Text(v[4],1000);var pos=ExcelExchange.Text(v[5],1000);var contact=ExcelExchange.Text(v[6],1000);var active=ExcelExchange.Flag(v[7]);
                    if(!active&&id>0&&assigned.Contains(id))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сначала верните имущество сотрудника"));
                    e.FullName=name;e.PersonnelNumber=number;e.Department=dep;e.Position=pos;e.Contact=contact;e.Active=active;e.Revision++;changedEmployees[e]=line.Row;if(id==0){db.Employees.Add(e);people.Add(e);}
                }else{
                    var e=id==0?new CredentialEntry{OwnerId=actor.Id}:entries.GetValueOrDefault(id)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Запись не найдена или недоступна"));Allow(actor,e);
                    if(id>0&&ExcelExchange.Revision(v[1])!=e.Revision)throw new ConflictException(global::SysAdminAssistant.Core.ProductText.Get("Запись изменена. Сделайте новую выгрузку"));
                    var login=ExcelExchange.Text(v[3],1000);var password=ExcelExchange.Text(v[4],4000);var notes=ExcelExchange.Text(v[5],10000);int? deviceId=v[6].Length==0?null:ExcelExchange.Id(v[6],false);
                    if(deviceId is int did){var device=devices.GetValueOrDefault(did)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Устройство не найдено"));if(deviceId!=e.DeviceId)AccessRules.Require(actor,NetworkDevices.Module(device.Type));}
                    int? serviceId=line.LegacyVault?e.ServiceId:v[7].Length==0?null:ExcelExchange.Id(v[7],false);if(serviceId is int sid){if(!await db.Services.AnyAsync(x=>x.Id==sid,token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сервис не найден"));if(serviceId!=e.ServiceId)AccessRules.Require(actor,"services");}e.ServiceId=serviceId;
                    e.Name=name;e.DeviceId=deviceId;e.Revision++;if(commit)Seal(e,new(login,password,notes));if(id==0)db.Credentials.Add(e);
                }
            }catch(Exception e)when(e is ArgumentException or ConflictException or UnauthorizedAccessException){error=e.Message;}
            result.Add(new(line.Row,kind=="vault"?"Запись пароля":name,operation,error));
        }
        if(kind=="employees"){
            var duplicates=people.Where(x=>x.PersonnelNumber.Length>0).GroupBy(x=>x.PersonnelNumber,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).SelectMany(g=>g).Where(changedEmployees.ContainsKey).Select(x=>changedEmployees[x]).ToHashSet();
            result=result.Select(x=>duplicates.Contains(x.Row)?x with{Error="Табельный номер уже используется"}:x).ToList();
        }
        var preview=new ExcelPreview(result.ToArray());
        if(commit){if(!preview.Valid)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Импорт отменён: исправьте ошибки в предпросмотре; ни одна запись не сохранена"));Audit(actor,kind+".excel.import.request");await db.SaveChangesAsync(token);await tx.CommitAsync(token);state.Touch();}
        return preview;
    }
}
