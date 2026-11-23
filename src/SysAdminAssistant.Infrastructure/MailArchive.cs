using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;

public sealed record MailViewRequest(int Id);
public sealed record MailReceiptRepair(string MailboxId);
public sealed record MailView(int Id,string Sender,string Subject,DateTime? SentAt,DateTime? ImapReceivedAt,string Body,string Analysis,string Note);
public static class MailArchive
{
    public static string Account(MailSettings m) => m.Host+(m.Port==993?"":":"+m.Port)+"/"+m.User+"/"+m.Folder+(m.Purpose==MailboxPurpose.Dmarc?"/DMARC":"");
    public static async Task<int> SetReceiptAsync(DatabaseFactory factory,string account,string key,DateTime receipt,string sender,string subject,DateTime? sentAt,CancellationToken token=default)
    {
        var keys=new[]{key}.Concat(Enumerable.Range(0,32).Select(i=>key+":xml:"+i)).Select(k=>ReportStore.Digest(account+"\0"+k)).ToArray();
        await using var db=factory.Create();
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        var ids=await db.Intake.Where(x=>keys.Contains(x.Digest)).Select(x=>x.EventId).Distinct().ToArrayAsync(token);
        var changed=0;
        foreach(var id in ids)
        {
            var report=await db.Reports.SingleOrDefaultAsync(x=>x.Id==id,token);
            if(report==null)continue;
            var source=await db.MailSources.SingleOrDefaultAsync(x=>x.EventId==id,token);
            if(source==null){source=new MailSource {EventId=id};db.MailSources.Add(source);}
            if(report.ReceivedAt!=receipt || source.ImapReceivedAt!=receipt)changed++;
            source.Account=account;source.MessageKey=key;source.Sender=sender;source.Subject=subject;source.SentAt=sentAt;source.ImapReceivedAt=receipt;
            report.ReceivedAt=receipt;
            foreach(var incident in await db.Incidents.Where(x=>x.ReportId==id).ToArrayAsync(token))incident.CreatedAt=receipt;
        }
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return changed;
    }
    public static async Task<string> RepairAsync(DatabaseFactory factory,MailSettings m,CancellationToken token)
    {
        using var client=new ImapClient {Timeout=30000};
        await client.ConnectAsync(m.Host,m.Port,SecureSocketOptions.SslOnConnect,token);
        await client.AuthenticateAsync(m.User,LocalSecret.Unprotect(m.ProtectedPassword),token);
        var folder=await client.GetFolderAsync(m.Folder,token);await folder.OpenAsync(FolderAccess.ReadOnly,token);
        var account=Account(m);
        var ids=await folder.SearchAsync(SearchQuery.All,token);
        var changed=0;var checkedCount=0;var unavailable=0;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        foreach(var chunk in ids.OrderByDescending(x=>x.Id).Chunk(100))
        {
            if(watch.Elapsed>TimeSpan.FromSeconds(50))return $"Обновлено записей: {changed}; проверено UID: {checkedCount}. Проверка остановлена по лимиту времени. Запустите сверку повторно; уже сверенные записи будут пропущены.";
            var candidates=new List<UniqueId>();
            await using(var db=factory.Create())
            {
                var keys=chunk.ToDictionary(uid=>ReportStore.Digest(account+"\0"+folder.UidValidity+":"+uid.Id),uid=>uid);
                var links=await db.Intake.Where(x=>keys.Keys.Contains(x.Digest)).ToArrayAsync(token);
                var eventIds=links.Select(x=>x.EventId).ToArray();
                var complete=await db.MailSources.Where(x=>eventIds.Contains(x.EventId)&&x.ImapReceivedAt!=null).Select(x=>x.EventId).ToArrayAsync(token);
                candidates.AddRange(links.Where(x=>!complete.Contains(x.EventId)).Select(x=>keys[x.Digest]));
            }
            checkedCount+=chunk.Length;if(candidates.Count==0)continue;
            var summaries=await folder.FetchAsync(candidates,MessageSummaryItems.UniqueId|MessageSummaryItems.InternalDate|MessageSummaryItems.Envelope,token);
            foreach(var summary in summaries.Where(x=>candidates.Contains(x.UniqueId)))
            {
                if(summary.Envelope==null || !MailAdmission.Allows(m,summary.Envelope.From,summary.Envelope.Subject))continue;
                if(summary.InternalDate==null){unavailable++;continue;}
                changed+=await SetReceiptAsync(factory,account,folder.UidValidity+":"+summary.UniqueId.Id,AppTime.FromOffset(summary.InternalDate.Value),
                    summary.Envelope?.From.ToString()??"",summary.Envelope?.Subject??"",AppTime.FromOffset(summary.Envelope?.Date),token);
            }
        }
        await client.DisconnectAsync(true,token);
        return $"Сверка завершена. Обновлено записей: {changed}; проверено UID: {checkedCount}; без INTERNALDATE: {unavailable}. Письма, удалённые из ящика, исправить по IMAP невозможно.";
    }
    public static async Task<MailView> ReadAsync(DatabaseFactory factory,AppSettings settings,int id,CancellationToken token)
    {
        await using var db=factory.Create();
        var report=await db.Reports.AsNoTracking().SingleAsync(x=>x.Id==id,token);
        var source=await db.MailSources.SingleOrDefaultAsync(x=>x.EventId==id,token);
        var pending=await db.Pending.AsNoTracking().SingleOrDefaultAsync(x=>x.EventId==id,token);
        var body=source?.Body??"";
        if(body.Length==0)body=pending?.Body??"";
        var fetchError="";
        if(body.Length==0 && source!=null)
        {
            var mailbox=settings.Mailboxes.FirstOrDefault(m=>m.Enabled&&m.Purpose==MailboxPurpose.Reports&&Account(m)==source.Account);
            var parts=source.MessageKey.Split(':');
            if(mailbox!=null&&parts.Length>=2&&uint.TryParse(parts[0],out var validity)&&uint.TryParse(parts[1],out var uid)&&uid>0)
            {
                try {
                using var client=new ImapClient {Timeout=30000};
                await client.ConnectAsync(mailbox.Host,mailbox.Port,SecureSocketOptions.SslOnConnect,token);
                await client.AuthenticateAsync(mailbox.User,LocalSecret.Unprotect(mailbox.ProtectedPassword),token);
                var folder=await client.GetFolderAsync(mailbox.Folder,token);await folder.OpenAsync(FolderAccess.ReadOnly,token);
                if(folder.UidValidity==validity)
                {
                    var uniqueId=new UniqueId(uid);
                    var summaries=await folder.FetchAsync(new[]{uniqueId},MessageSummaryItems.UniqueId|MessageSummaryItems.Size|MessageSummaryItems.Envelope|MessageSummaryItems.BodyStructure,token);
                    var summary=summaries.SingleOrDefault(x=>x.UniqueId==uniqueId);
                    if(summary?.Envelope==null || !MailAdmission.Allows(mailbox,summary.Envelope.From,summary.Envelope.Subject))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Письмо больше не проходит фильтры ящика"));
                    if(mailbox.RequireSpfDkim) {
                        MailAuthentication.Validate(mailbox);
                        var auth=await folder.FetchAsync(new[]{uniqueId},MessageSummaryItems.None,new[]{"Authentication-Results"},token);
                        var headers=auth.SingleOrDefault(x=>x.UniqueId==uniqueId)?.Headers;
                        var authentication=MailAuthentication.Evaluate(mailbox,headers?.Where(h=>h.Field.Equals("Authentication-Results",StringComparison.OrdinalIgnoreCase)).Select(h=>h.Value)??Enumerable.Empty<string>(),MailAdmission.Sender(summary.Envelope.From));
                        if(!authentication.Allowed)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Текст не загружен. ")+authentication.Describe());
                    }
                    var part=summary.TextBody??summary.HtmlBody;
                    if(summary?.Size<=MailPayloadReader.MaxMessageBytes && part!=null)
                    {
                        var entity=await folder.GetBodyPartAsync(uniqueId,part,token);
                        if(entity is TextPart text&&!text.IsAttachment&&text.ContentType.MimeType is "text/plain" or "text/html")
                        {
                            var message=new MimeMessage {Body=text};
                            body=MailPayloadReader.Read(message,"","").Single().Body;
                            source.Body=body;await db.SaveChangesAsync(token);
                        }
                    }
                }
                await client.DisconnectAsync(true,token);
                } catch(Exception e) when(!token.IsCancellationRequested) { fetchError="Не удалось прочитать текст из IMAP: "+e.Message+"\n"; }
            }
        }
        return new(id,source?.Sender??pending?.Sender??"Не сохранён",source?.Subject??pending?.Subject??report.Task,
            source?.SentAt??pending?.MessageDate,source?.ImapReceivedAt,body,report.Description,
            body.Length==0?fetchError+"Исходный текст не сохранён в старой версии. Сначала выполните сверку дат с IMAP; для обычного отчёта текст будет прочитан из ящика при следующем открытии. Если письмо удалено или это старый DMARC, доступен только сохранённый разбор.":"Текстовый просмотр. Вложения и внешние изображения не открываются. Время показано в часовом поясе сервера.");
    }
}
