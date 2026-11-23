using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class CatalogDeletionSpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var f=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"deletion.db")});await f.InitializeAsync();
        await using var db=f.Create();var device=new Device{Name="Keep links"};db.Add(device);await db.SaveChangesAsync();
        await CatalogDeletion.EnsureAllowedAsync(db,device);check("Unlinked device permits deletion",true);
        var service=new ServiceEntry{DeviceId=device.Id};db.Add(service);await db.SaveChangesAsync();
        async Task<bool> Blocked(Entity entity,string text){try{await CatalogDeletion.EnsureAllowedAsync(db,entity);return false;}catch(ArgumentException e){return e.Message.Contains(text);}}
        check("Device deletion explains linked service",await Blocked(device,"сервисы"));
        db.Remove(service);var job=new SqlBackupJob{DeviceId=device.Id};db.Add(job);await db.SaveChangesAsync();
        check("Device deletion protects linked job",await Blocked(device,"задания"));
        var report=new ReportEvent{JobId=job.Id,DeviceId=device.Id};db.Add(report);await db.SaveChangesAsync();
        check("Job deletion protects report history",await Blocked(job,"история отчётов"));
        var payment=new Payment{Title="History"};db.Add(payment);await db.SaveChangesAsync();
        db.Add(new PaymentReceipt{PaymentId=payment.Id,RequestKey=Guid.NewGuid().ToString(),Cancelled=true});await db.SaveChangesAsync();
        check("Payment deletion protects cancelled receipt history",await Blocked(payment,"история оплат"));
        check("Device deletion protects report links",await Blocked(device,"история писем"));
    }
}
