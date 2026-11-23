using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
namespace SysAdminAssistant.Infrastructure;
public sealed class MailboxPollInfo
{
    public string Folder {get;set;}="";
    public bool UnseenOnly {get;set;}
    public MailboxPurpose Purpose {get;set;}
    public int MissingHeaders {get;set;}
    public int NoPayload {get;set;}
    public MailboxPollInfo Copy() => (MailboxPollInfo)MemberwiseClone();
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public DateTime StartedUtc {get;set;}=DateTime.UtcNow;
    public DateTime? FinishedUtc {get;set;}
    public string Stage {get;set;}="Ожидание";
    public int TotalInFolder {get;set;}
    public int Found {get;set;}
    public int AlreadyProcessed {get;set;}
    public int Filtered {get;set;}
    public int AuthenticationRejected {get;set;}
    public string LastAuthenticationFailure {get;set;}="";
    public int NoBody {get;set;}
    public int AcceptedMessages {get;set;}
    public int ImportedRecords {get;set;}
    public string Error {get;set;}="";
}
public sealed record MailConnectionTest(MailSettings Mailbox,string Password);
public static class MailDiagnostics
{
    public static async Task<string> TestAsync(MailSettings m,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(m.Host)||string.IsNullOrWhiteSpace(m.User)||string.IsNullOrWhiteSpace(m.Folder)||m.Port is <1 or >65535)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Заполните сервер, порт, логин и папку"));
        var stage="TLS-соединение";
        try {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(40));
            using var client=new ImapClient {Timeout=30000};
            await client.ConnectAsync(m.Host,m.Port,SecureSocketOptions.SslOnConnect,timeout.Token);
            stage="Вход в ящик";await client.AuthenticateAsync(m.User,LocalSecret.Unprotect(m.ProtectedPassword),timeout.Token);
            stage="Открытие папки";var folder=await client.GetFolderAsync(m.Folder,timeout.Token);await folder.OpenAsync(FolderAccess.ReadOnly,timeout.Token);
            var count=folder.Count;await client.DisconnectAsync(true,timeout.Token);
            return "Успешно: TLS, вход и папка «"+m.Folder+"». Писем в папке: "+count+". Тела и вложения не загружались. Фильтры проверяются при опросе.";
        } catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(Exception e) { throw new InvalidOperationException(stage+global::SysAdminAssistant.Core.ProductText.Get(": проверка не пройдена (")+e.GetType().Name+global::SysAdminAssistant.Core.ProductText.Get("). Проверьте адрес, сертификат, пароль и доступ к папке. Пароль в диагностике не выводится.")); }
    }
}
