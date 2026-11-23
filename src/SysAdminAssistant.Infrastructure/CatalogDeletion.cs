using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;

// Must run inside the server change transaction, after permissions and stamp checks.
public static class CatalogDeletion
{
    public static async Task EnsureAllowedAsync(AssistantDb db,Entity entity,CancellationToken token=default)
    {
        var links=new List<string>();
        if(entity is Device d)
        {
            if(await db.Services.AnyAsync(x=>x.DeviceId==d.Id,token))links.Add("сервисы");
            if(await db.Jobs.AnyAsync(x=>x.DeviceId==d.Id,token))links.Add("задания резервного копирования");
            if(await db.Reports.AnyAsync(x=>x.DeviceId==d.Id,token))links.Add("история писем");
            if(await db.Credentials.AnyAsync(x=>x.DeviceId==d.Id,token))links.Add("пароли");
            if(await db.Assets.AnyAsync(x=>x.DeviceId==d.Id,token))links.Add("имущество");
            if(await db.Rules.AnyAsync(x=>x.TargetKind==TargetKind.Device && x.TargetId==d.Id,token))links.Add("правила привязки");
        }
        else if(entity is ServiceEntry service&&await db.Credentials.AnyAsync(x=>x.ServiceId==service.Id,token))links.Add("пароли сервиса — сначала уберите привязку в менеджере паролей");
        else if(entity is SqlBackupJob j)
        {
            if(await db.Reports.AnyAsync(x=>x.JobId==j.Id,token))links.Add("история отчётов");
            if(await db.Rules.AnyAsync(x=>x.TargetKind!=TargetKind.Device && x.TargetId==j.Id,token))links.Add("правила привязки");
        }
        else if(entity is Payment p && await db.PaymentReceipts.AnyAsync(x=>x.PaymentId==p.Id,token))links.Add("история оплат, включая отменённые");
        if(links.Count>0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Удаление запрещено: есть связанные записи — ")+string.Join(", ",links)+global::SysAdminAssistant.Core.ProductText.Get(". Сначала уберите связи. Историю сохраняй; для устройства используйте «Архивировать», для задания отключите контроль по расписанию."));
    }
}
