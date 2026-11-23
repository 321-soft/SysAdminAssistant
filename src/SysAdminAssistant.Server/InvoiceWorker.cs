using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;
public sealed class InvoiceWorker(ServerState state):BackgroundService
{
    public static readonly SemaphoreSlim PollGate=new(1,1);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested){int delay=300;var store=new InvoiceSettingsStore(state.DirectoryPath);try{var config=store.Load();delay=config.PollSeconds;if(config.Enabled){using var budget=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);budget.CancelAfter(TimeSpan.FromMinutes(10));await Poll(state,config,budget.Token);}}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}catch(Exception e){store.Log("Ошибка проверки: "+(e is ImapCommandException or System.Security.Authentication.AuthenticationException or InvalidDataException?e.Message:e.GetType().Name));}try{await Task.Delay(TimeSpan.FromSeconds(delay),stoppingToken);}catch(OperationCanceledException){break;}}
    }
    public static async Task Test(InvoiceSettings config,string password,CancellationToken token){using var client=new ImapClient{Timeout=30000};await client.ConnectAsync(config.Mailbox.Host,config.Mailbox.Port,SecureSocketOptions.SslOnConnect,token);await client.AuthenticateAsync(config.Mailbox.User,password,token);var f=await client.GetFolderAsync(config.Mailbox.Folder,token);await f.OpenAsync(FolderAccess.ReadOnly,token);await client.DisconnectAsync(true,token);}
    public static async Task Poll(ServerState state,InvoiceSettings config,CancellationToken token)
    {
        if(!await PollGate.WaitAsync(0,token))return;var store=new InvoiceSettingsStore(state.DirectoryPath);
        try{
            using var timeScope=AppTime.Scope(state.Settings.TimeZoneId);var m=config.Mailbox;using var client=new ImapClient{Timeout=30000};await client.ConnectAsync(m.Host,m.Port,SecureSocketOptions.SslOnConnect,token);await client.AuthenticateAsync(m.User,LocalSecret.Unprotect(m.ProtectedPassword),token);var folder=await client.GetFolderAsync(m.Folder,token);await folder.OpenAsync(FolderAccess.ReadOnly,token);
            var source=InvoiceRecognition.Hash(m.Host+":"+m.Port+"/"+m.User+"/"+m.Folder+"/"+config.OnlyNew+"/"+config.StartFromUtc?.ToString("O"));var cursor=store.Cursor(c=>new InvoiceCursor{Source=c.Source,Validity=c.Validity,LastUid=c.LastUid,Ready=c.Ready});
            if(!cursor.Ready||cursor.Source!=source||cursor.Validity!=folder.UidValidity){cursor=new(){Source=source,Validity=folder.UidValidity,Ready=true};if(config.OnlyNew){cursor.LastUid=folder.UidNext is UniqueId next&&next.IsValid?next.Id-1:(await folder.SearchAsync(SearchQuery.All,token)).LastOrDefault().Id;store.Cursor(c=>{c.Source=cursor.Source;c.Validity=cursor.Validity;c.LastUid=cursor.LastUid;c.Ready=true;return true;});store.Log("Начальная отметка сохранена: только новые письма после этого подключения");return;}}
            if(cursor.LastUid==uint.MaxValue)return;SearchQuery query=SearchQuery.Uids(new UniqueIdRange(new UniqueId(cursor.LastUid+1),UniqueId.MaxValue));if(!config.OnlyNew&&config.StartFromUtc is DateTime start)query=query.And(SearchQuery.DeliveredAfter(start.Date));var ids=(await folder.SearchAsync(query,token)).OrderBy(x=>x.Id).Take(20).ToArray();int count=0;
            foreach(var uid in ids){var summaries=await folder.FetchAsync(new[]{uid},MessageSummaryItems.Size|MessageSummaryItems.InternalDate,token);var summary=summaries.Single();var key=source+"/"+folder.UidValidity+"/"+uid.Id;var received=summary.InternalDate?.UtcDateTime;
                if(summary.Size==null||summary.Size>16_000_000||received==null||!config.OnlyNew&&received<config.StartFromUtc){store.Log("UID "+uid.Id+": пропущено по размеру / дате получения");}
                else{
                    var message=await folder.GetMessageAsync(uid,token);var sender=message.From.Mailboxes.Take(2).ToArray() is var senders && senders.Length==1?senders[0].Address:"";var body=message.TextBody??HtmlText.Convert(message.HtmlBody??"");var subject=message.Subject??"";if(subject.Length>500)subject=subject[..500];if(body.Length>100000)body=body[..100000];
                    var templates=config.Templates.Where(t=>t.Sender.Equals(sender,StringComparison.OrdinalIgnoreCase)&&(t.SubjectContains.Length==0||subject.Contains(t.SubjectContains,StringComparison.OrdinalIgnoreCase))).ToArray();InvoiceTemplate? template=templates.Length==1?templates[0]:null;bool trusted=config.AllowedSenders.Any(s=>s.Equals(sender,StringComparison.OrdinalIgnoreCase))&&m.RequireSpfDkim&&MailAuthentication.Allows(m,message.Headers.Where(h=>h.Field.Equals("Authentication-Results",StringComparison.OrdinalIgnoreCase)).Select(h=>h.Value),sender);int files=0,total=0;if(!trusted&&message.BodyParts.OfType<MimePart>().Any(p=>p.IsAttachment||p.ContentType.MimeType.Equals("application/pdf",StringComparison.OrdinalIgnoreCase)))store.Log("UID "+uid.Id+": вложения пропущены — отправитель не подтверждён.");
                    foreach(var part in message.BodyParts.OfType<MimePart>().Where(_=>trusted).Where(p=>p.IsAttachment||p.ContentType.MimeType.Equals("application/pdf",StringComparison.OrdinalIgnoreCase)).Take(9)){
                        if(++files>8){store.Log("UID "+uid.Id+": превышено число вложений");break;}
                        using var memory=new LimitedInvoiceStream(8_000_000);try{if(part.Content==null)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Пустое вложение"));part.Content.DecodeTo(memory);var bytes=memory.ToArray();total+=bytes.Length;if(total>16_000_000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой набор файлов"));var type=InvoiceExtraction.Type(bytes);InvoiceExtract extracted;try{var result=await InvoiceExtraction.Run(state.DirectoryPath,bytes,false,token);extracted=JsonSerializer.Deserialize<InvoiceExtract>(result,RemoteData.Json)!;}catch(Exception e)when(!token.IsCancellationRequested){extracted=new("","Ошибка извлечения",0,"Распознавание не завершено: "+e.GetType().Name);}
                            if(extracted.Error.Length==0&&!InvoiceRecognition.IsCandidate(subject,extracted.Text)){store.Log("UID "+uid.Id+": документ не похож на счет");continue;}var data=InvoiceRecognition.Parse(extracted,sender,subject,received.Value,template,state.Settings.Company.WeekendMask);data.MailBody=body;data.SenderDateUtc=message.Date==default?null:message.Date.UtcDateTime;var name=Path.GetFileName((part.FileName??"Счёт").Replace('\\','/'));if(name.Length>150)name=name[..150];name=new string(Path.GetFileNameWithoutExtension(name).Where(c=>char.IsLetterOrDigit(c)||" ._-()".Contains(c)).Take(100).ToArray()).Trim(' ','.');if(name.Length==0)name="Счёт";name+=type=="application/pdf"?".pdf":type=="image/png"?".png":".jpg";var digest=Convert.ToHexString(SHA256.HashData(bytes));await Save(key+"/"+files,data,digest,name,type,bytes,trusted);count++;
                        }catch(Exception e)when(e is InvalidDataException or IOException or FormatException){store.Log("UID "+uid.Id+": вложение не принято · "+e.GetType().Name);}
                    }
                    if(files==0&&body.Length>0&&InvoiceRecognition.IsCandidate(subject,body)){var text=body.Length>100000?body[..100000]:body;var extracted=new InvoiceExtract(text,"Тело письма",1);var data=InvoiceRecognition.Parse(extracted,sender,subject,received.Value,template,state.Settings.Company.WeekendMask);data.MailBody=body;data.SenderDateUtc=message.Date==default?null:message.Date.UtcDateTime;await Save(key+"/body",data,InvoiceRecognition.Hash(sender+"|"+subject+"|"+text),"","",[],trusted);count++;}
                }
                cursor.LastUid=uid.Id;store.Cursor(c=>{c.Source=cursor.Source;c.Validity=cursor.Validity;c.LastUid=cursor.LastUid;c.Ready=true;return true;});
            }
            store.Log("Проверено писем: "+ids.Length+" · документов: "+count);await client.DisconnectAsync(true,token);
            async Task Save(string key,InvoiceData data,string digest,string name,string type,byte[] bytes,bool trusted){await state.Gate.WaitAsync(token);try{var id=await new InvoiceStore(state.Factory).Ingest(InvoiceRecognition.Hash(key),data,digest,name,type,bytes,trusted,state.Settings.Company.WeekendMask,token);state.Touch();if(id<0)store.Log("Дубликат документа #"+(-id)+": повторный платёж не создан");}finally{state.Gate.Release();}}
        }finally{PollGate.Release();}
    }
}
