using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class PaymentHistorySpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var now=new DateTime(2026,10,19);var mask=PaymentCalendar.DefaultWeekendMask;
        check("Payment distant unpaid period is green",DashboardState.Payment(new(){DueDate=now.AddDays(14)},now,mask,3).Lamp==LampState.Green);
        check("Payment three working days is amber",DashboardState.Payment(new(){DueDate=new DateTime(2026,10,26)},new DateTime(2026,10,21),mask,3).Lamp==LampState.Amber);
        check("Payment four working days is green",DashboardState.Payment(new(){DueDate=new DateTime(2026,10,26)},new DateTime(2026,10,20),mask,3).Lamp==LampState.Green);
        check("Payment overdue is red",DashboardState.Payment(new(){DueDate=now.AddDays(-4)},now,mask,3).Lamp==LampState.Red);
        var f=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"payment-history.db")});await f.InitializeAsync();await f.InitializeAsync();int id;
        await using(var db=f.Create()){var p=new Payment{Title="Monthly",DueDate=new DateTime(2026,10,31),Repeat=RepeatPeriod.Monthly,AmountKopecks=50000};db.Payments.Add(p);await db.SaveChangesAsync();id=p.Id;}
        async Task<Payment> Current(){await using var db=f.Create();return await db.Payments.AsNoTracking().SingleAsync(p=>p.Id==id);}
        var first=new PaymentOperation(id,RemoteData.EntityStamp(await Current()),Guid.NewGuid().ToString());await PaymentHistory.ApplyAsync(f,first,"admin",false);await PaymentHistory.ApplyAsync(f,first,"admin",false);
        check("Payment early mark records paid period and advances once",(await Current()).DueDate==new DateTime(2026,11,30));
        await PaymentHistory.ApplyAsync(f,new(id,RemoteData.EntityStamp(await Current()),Guid.NewGuid().ToString()),"admin",false);
        PaymentReceipt october;
        await using(var db=f.Create()){october=await db.PaymentReceipts.SingleAsync(r=>r.PaymentId==id&&r.ScheduledDate.Month==10);check("Payment history stores immutable amount and operator",october.AmountKopecks==50000&&october.PaidBy=="admin"&&await db.PaymentReceipts.CountAsync()==2);}
        await PaymentHistory.ApplyAsync(f,new(id,RemoteData.EntityStamp(await Current()),Guid.NewGuid().ToString(),october.Id),"other-admin",true);
        check("Payment cancelling older receipt restores its unpaid period",(await Current()).DueDate==new DateTime(2026,10,31));
        await PaymentHistory.ApplyAsync(f,new(id,RemoteData.EntityStamp(await Current()),Guid.NewGuid().ToString()),"admin",false);
        check("Payment repayment skips later already-paid month",(await Current()).DueDate==new DateTime(2026,12,31));
        await using(var db=f.Create())check("Payment cancelled receipt remains in audit trail",await db.PaymentReceipts.CountAsync()==3&&(await db.PaymentReceipts.SingleAsync(r=>r.Id==october.Id)).CancelledBy=="other-admin");
        var read=new UserView("r","r",false,true,1,new(){["payments"]=1});AccessRules.Command(read,"payment-history");bool denied=false;try{AccessRules.Command(read,"payment-cancel");}catch(UnauthorizedAccessException){denied=true;}check("Payment reader cannot cancel paid history",denied);
    }
}
