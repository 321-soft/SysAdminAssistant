using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class MailArchiveSpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var factory=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"mail-archive.db")});
        await factory.InitializeAsync();
        var store=new ReportStore(factory);
        var sent=new DateTime(2026,10,1,8,0,0);
        var receipt=sent.AddMinutes(7);
        var mail=new MailReport("imap/user/INBOX","77:19","Backup <backup@example.test>","[Success] Veeam fixture","Host: fixture\nStatus: Success\nReadable original body",sent,"",receipt);
        await using(var db=factory.Create()) {
            db.Devices.Add(new Device{Name="fixture"});
            await db.SaveChangesAsync();
            db.Rules.Add(new BindingRule{BodyPhrase="Host: fixture",TargetId=(await db.Devices.SingleAsync()).Id});
            await db.SaveChangesAsync();
        }
        check("Mail archive accepts recognized letter",await store.IngestAsync(mail));
        ReportEvent report;
        await using(var db=factory.Create()) {
            report=await db.Reports.SingleAsync();
            check("IMAP receipt differs from download and sender time",report.ReceivedAt==receipt && report.ReceivedAt!=sent);
            check("Recognized source remains readable after pending removal",!report.IsPending&&!await db.Pending.AnyAsync()&&(await db.MailSources.SingleAsync()).Body==mail.Body);
        }
        var view=await MailArchive.ReadAsync(factory,new AppSettings(),report.Id,default);
        check("Mail view contains sender subject body and both dates",view.Sender==mail.Sender&&view.Subject==mail.Subject&&view.Body==mail.Body&&view.SentAt==sent&&view.ImapReceivedAt==receipt);
        var export=Path.Combine(folder,"mail-archive.json");
        await new DatabaseTransfer(factory).ExportAsync(export);
        var copy=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"mail-archive-copy.db")});
        await copy.InitializeAsync();await new DatabaseTransfer(copy).ImportAsync(export);
        await using(var db=copy.Create())check("Portable source retains text and event link",(await db.MailSources.SingleAsync()).EventId==(await db.Reports.SingleAsync()).Id&&(await db.MailSources.SingleAsync()).Body==mail.Body);
        await using(var db=factory.Create()) {
            await db.MailSources.ExecuteDeleteAsync();
            await db.Reports.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ReceivedAt,DateTime.Now));
        }
        var repaired=await MailArchive.SetReceiptAsync(factory,mail.Account,mail.Key,receipt,mail.Sender,mail.Subject,sent);
        check("Header repair repairs existing receipt without reimport",repaired==1);
        check("Header repair is idempotent",await MailArchive.SetReceiptAsync(factory,mail.Account,mail.Key,receipt,mail.Sender,mail.Subject,sent)==0);
        await using(var db=factory.Create())check("Header repair preserves report and body absence",await db.Reports.CountAsync()==1&&(await db.MailSources.SingleAsync()).Body==""&&(await db.Reports.SingleAsync()).ReceivedAt==receipt);
        // Multipart routing keys point to separate events, all need their IMAP receipt corrected.
        var first=mail with {Key="77:20:xml:0",ImapReceivedAt=null};
        var second=mail with {Key="77:20:xml:1",ImapReceivedAt=null};
        check("Multipart source intake",await store.IngestMessageAsync(new[]{first,second},mail.Account,"77:20")==2);
        check("Receipt repair includes all multipart reports",await MailArchive.SetReceiptAsync(factory,mail.Account,"77:20",receipt,mail.Sender,mail.Subject,sent)==2);
        await EventRetention.ApplyAsync(factory,30,receipt.AddDays(60));
        await using(var db=factory.Create())check("Retention removes archived source text with reports",!await db.Reports.AnyAsync()&&!await db.MailSources.AnyAsync());
        // Additive patch on a 0.6.0-style DB: missing archive table must not reset business rows.
        await using(var db=factory.Create())await db.Database.ExecuteSqlRawAsync("DROP TABLE sa2_MailSource");
        await factory.InitializeAsync();
        await using(var db=factory.Create())check("Archive table creation preserves existing devices",await db.Devices.CountAsync()==1&&!await db.MailSources.AnyAsync());
    }
}
