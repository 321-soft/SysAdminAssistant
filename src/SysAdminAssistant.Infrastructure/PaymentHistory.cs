using System.Data;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record PaymentOperation(int PaymentId,string ExpectedHash,string RequestKey,int ReceiptId=0);
public static class PaymentHistory
{
    public static Task EnsureAsync(AssistantDb db,CancellationToken token=default)=>db.Database.ExecuteSqlRawAsync(db.Database.IsSqlServer()?"""
        IF OBJECT_ID(N'sa2_PaymentReceipt',N'U') IS NULL CREATE TABLE [sa2_PaymentReceipt](
        [Id] int IDENTITY PRIMARY KEY,[PaymentId] int NOT NULL,[Title] nvarchar(max) NOT NULL,[ScheduledDate] datetime2 NOT NULL,[PaidAt] datetime2 NOT NULL,
        [AmountKopecks] bigint NULL,[PaidBy] nvarchar(max) NOT NULL,[RequestKey] nvarchar(100) NOT NULL UNIQUE,[Cancelled] bit NOT NULL,[CancelledAt] datetime2 NULL,[CancelledBy] nvarchar(max) NOT NULL);
        """:"""
        CREATE TABLE IF NOT EXISTS sa2_PaymentReceipt(Id INTEGER PRIMARY KEY AUTOINCREMENT,PaymentId INTEGER NOT NULL,Title TEXT NOT NULL,ScheduledDate TEXT NOT NULL,PaidAt TEXT NOT NULL,AmountKopecks INTEGER NULL,PaidBy TEXT NOT NULL,RequestKey TEXT NOT NULL UNIQUE,Cancelled INTEGER NOT NULL,CancelledAt TEXT NULL,CancelledBy TEXT NOT NULL);
        """,token);
    public static async Task ApplyAsync(DatabaseFactory factory,PaymentOperation request,string actor,bool cancel,CancellationToken token=default)
    {
        if(!Guid.TryParse(request.RequestKey,out _))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный ключ операции"));
        await using var db=factory.Create();await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var p=await db.Payments.SingleOrDefaultAsync(p=>p.Id==request.PaymentId,token)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Счёт не найден"));
        if(!cancel&&await db.PaymentReceipts.AnyAsync(r=>r.RequestKey==request.RequestKey&&r.PaymentId==p.Id,token))return;
        if(cancel&&await db.PaymentReceipts.AnyAsync(r=>r.Id==request.ReceiptId&&r.PaymentId==p.Id&&r.Cancelled,token))return;
        if(RemoteData.EntityStamp(p)!=request.ExpectedHash)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Счёт изменился. Обновите данные перед оплатой или отменой."));
        if(cancel){
            var receipt=await db.PaymentReceipts.SingleOrDefaultAsync(r=>r.Id==request.ReceiptId&&r.PaymentId==p.Id,token)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Оплата не найдена"));
            if(receipt.Cancelled)return;receipt.Cancelled=true;receipt.CancelledAt=AppTime.Now;receipt.CancelledBy=actor;
            if(p.DueDate.Date>receipt.ScheduledDate.Date)p.DueDate=receipt.ScheduledDate.Date;p.Paid=false;
        }else{
            if(p.Paid)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Счёт уже оплачен"));
            var paid=await db.PaymentReceipts.Where(r=>r.PaymentId==p.Id&&!r.Cancelled).Select(r=>r.ScheduledDate).ToArrayAsync(token);
            if(paid.Any(d=>d.Date==p.DueDate.Date))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Этот период уже оплачен"));
            db.PaymentReceipts.Add(new(){PaymentId=p.Id,Title=p.Title,ScheduledDate=p.DueDate.Date,PaidAt=AppTime.Now,AmountKopecks=p.AmountKopecks,PaidBy=actor,RequestKey=request.RequestKey});
            p.MarkPaid();if(p.Repeat!=RepeatPeriod.None)while(paid.Any(d=>d.Date==p.DueDate.Date))p.MarkPaid();
        }
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);
    }
}
