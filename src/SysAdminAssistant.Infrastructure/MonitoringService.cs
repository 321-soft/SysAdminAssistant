using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Infrastructure;

public sealed record Connectivity(string Ping, string Dns, DateTime CheckedAt);
public sealed class MonitoringService(DatabaseFactory factory, AppSettings settings)
{
    private readonly string owner = Guid.NewGuid().ToString("N");
    public async Task<Connectivity> CheckAsync(CancellationToken token = default)
    {
        if (factory.Remote != null) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Проверки выполняет сервер"));
        if (!await AcquireLeaseAsync("monitor", token)) return new("Проверяет другой процесс", "Проверяет другой процесс", AppTime.Now);
        try
        {
            await using var db = factory.Create();
            var devices = await db.Devices.AsNoTracking().Where(d => d.Enabled && d.Host != "").ToArrayAsync(token);
            foreach (var device in devices)
            {
                var result = await PingAsync(device.Host, token);
                // Confirm a transient failure before changing the device state.
                if (result != "OK") result = await PingAsync(device.Host, token);
                var previous = device.Status;
                var status = result == "OK" ? "ONLINE" : "OFFLINE";
                var error = result == "OK" ? "" : result; var checkedAt = AppTime.Now;
                var since = previous == status ? device.StatusSince ?? checkedAt : checkedAt;
                var target = db.Devices.Where(d => d.Id == device.Id && d.Host == device.Host && d.Enabled);
                var changed = result == "OK"
                    ? await target.ExecuteUpdateAsync(s => s.SetProperty(d => d.CheckedAt, (DateTime?)checkedAt).SetProperty(d => d.StatusSince, (DateTime?)since).SetProperty(d => d.Status, status).SetProperty(d => d.LastError, error).SetProperty(d => d.LastSeen, (DateTime?)checkedAt), token)
                    : await target.ExecuteUpdateAsync(s => s.SetProperty(d => d.CheckedAt, (DateTime?)checkedAt).SetProperty(d => d.StatusSince, (DateTime?)since).SetProperty(d => d.Status, status).SetProperty(d => d.LastError, error), token);
                if (changed > 0 && previous == "ONLINE" && status == "OFFLINE" && !(device.MaintenanceUntil > checkedAt)) db.Incidents.Add(new Incident { Severity = Severity.Warning, Source = "ICMP", Title = device.Name + " недоступно", Details = device.Host + ": " + result });
                await RenewLeaseAsync("monitor", token);
            }
            foreach (var service in await db.Services.AsNoTracking().ToArrayAsync(token))
            {
                try
                {
                    if (service.Type == "TCP")
                    {
                        var uri = new Uri("tcp://" + service.Address);
                        using var client = new TcpClient(); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        if (uri.Port < 1) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Укажите host:port"));
                        await client.ConnectAsync(uri.Host, uri.Port, timeout.Token); service.Status = "ONLINE";
                    }
                    else
                    {
                        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                        using var response = await client.GetAsync(service.Address, HttpCompletionOption.ResponseHeadersRead, token);
                        service.Status = response.IsSuccessStatusCode ? "ONLINE" : "HTTP " + (int)response.StatusCode;
                    }
                }
                catch (Exception e) when (!token.IsCancellationRequested) { service.Status = e is HttpRequestException ? "OFFLINE" : "ERROR"; }
                await db.Services.Where(s => s.Id == service.Id && s.Address == service.Address && s.Type == service.Type).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, service.Status).SetProperty(v => v.CheckedAt, (DateTime?)AppTime.Now), token);
                await RenewLeaseAsync("monitor", token);
            }
            await db.SaveChangesAsync(token);
            var probes=await ConnectivityProbes.CheckAsync(settings,PingAsync,token);
            var ping=ConnectivityProbes.Aggregate(probes.Where(p=>p.Group=="WAN"));
            string dns;
            try { _ = await Dns.GetHostAddressesAsync(settings.DnsName, token); dns = "OK"; }
            catch (Exception) when (!token.IsCancellationRequested) { dns = "ERROR"; }
            probes=probes.Append(new ConnectivityProbe("DNS",settings.DnsName,dns,AppTime.Now)).ToArray();
            var persisted = await db.Connectivity.FirstOrDefaultAsync(token);
            if (persisted == null) { persisted = new(); db.Connectivity.Add(persisted); }
            persisted.Lan=ConnectivityProbes.Aggregate(probes.Where(p=>p.Group=="LAN"));
            persisted.ProbeResultsJson=System.Text.Json.JsonSerializer.Serialize(probes,RemoteData.Json);
            persisted.Ping = ping; persisted.Dns = dns; persisted.CheckedAt = AppTime.Now;
            await db.SaveChangesAsync(token);
            return new(ping, dns, persisted.CheckedAt);
        }
        finally { await ReleaseLeaseAsync("monitor"); }
    }
    public event Action<MailboxPollInfo[]>? MailProgress;
    private DateTime lastMailProgress;
    private void Progress(MailboxPollInfo info,string stage) {
        info.Stage=stage;
        if(info.FinishedUtc==null && lastMailProgress!=default && DateTime.UtcNow-lastMailProgress<TimeSpan.FromSeconds(2))return;
        lastMailProgress=DateTime.UtcNow;MailProgress?.Invoke(MailChecks.Select(x=>x.Copy()).ToArray());
    }
    public List<MailboxPollInfo> MailChecks { get; } = new();
    private readonly List<string> authWarnings = new();
    public string[] MailErrors { get; private set; } = [];
    public async Task<int> PollMailAsync(CancellationToken token = default)
    {
        if (factory.Remote != null) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Проверки выполняет сервер"));
        authWarnings.Clear(); MailChecks.Clear();lastMailProgress=default;
        var errors = new List<string>(); int count = 0;
        IEnumerable<MailSettings> mailboxes = settings.Mailboxes.Count > 0 ? settings.Mailboxes : settings.Mail.Host.Length > 0 ? new[] { settings.Mail } : Array.Empty<MailSettings>();
        foreach (var mailbox in mailboxes.Where(x => x.Enabled))
        {
            var info=new MailboxPollInfo { Id=mailbox.Id,Name=mailbox.ToString(),Purpose=mailbox.Purpose,Folder=mailbox.Folder,UnseenOnly=mailbox.UnseenOnly }; MailChecks.Add(info);
            try { count += await PollMailboxAsync(mailbox, token, info); Progress(info,"Завершено"); }
            catch (Exception e) when (!token.IsCancellationRequested) { info.Error=info.Stage+": "+e.Message;Progress(info,"Ошибка");errors.Add(mailbox + ": " + e.Message); }
            finally { info.FinishedUtc=DateTime.UtcNow;Progress(info,info.Stage); }
        }
        errors.AddRange(authWarnings); MailErrors = errors.ToArray(); return count;
    }
    private async Task<int> PollMailboxAsync(MailSettings m, CancellationToken token, MailboxPollInfo info)
    {
        Progress(info,"Проверка настроек допуска"); MailAuthentication.Validate(m);
        if (m.Purpose == MailboxPurpose.Reports && m.AllowedDomains.Count == 0)
            throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Для ящика отчётов укажите разрешённые домены. Пока список пуст, письма не обрабатываются."));
        if (m.Purpose == MailboxPurpose.Dmarc && m.TrustedDmarcSenders.Count == 0)
            throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Для DMARC сначала подтвердите отправителя. Пока список пуст, вложения не загружаются."));
        var account = MailArchive.Account(m);
        var leaseName = ReportStore.Digest(account);
        if (!await AcquireLeaseAsync(leaseName, token)) { info.Error="Ящик уже проверяется другим циклом";return 0; }
        try
        {
            using var client = new ImapClient { Timeout = 30_000 };
            Progress(info,"TLS-соединение");await client.ConnectAsync(m.Host, m.Port, SecureSocketOptions.SslOnConnect, token);
            Progress(info,"Вход в ящик");await client.AuthenticateAsync(m.User, LocalSecret.Unprotect(m.ProtectedPassword), token);
            Progress(info,"Открытие папки");var folder = await client.GetFolderAsync(m.Folder, token);
            await folder.OpenAsync(FolderAccess.ReadOnly, token);info.TotalInFolder=folder.Count;
            var query = m.UnseenOnly ? SearchQuery.NotSeen : SearchQuery.All;
            if (m.Purpose == MailboxPurpose.Dmarc) query = query.And(SearchQuery.SubjectContains("dmarc"));
            Progress(info,"Поиск писем");var ids = await folder.SearchAsync(query, token);info.Found=ids.Count;
            var store = new ReportStore(factory, settings.CustomDiagnostics); int processed = 0; var leaseRenewed = DateTime.UtcNow;
            foreach (var uid in ids.OrderByDescending(x=>x.Id))
            {
                token.ThrowIfCancellationRequested();Progress(info,"Чтение UID "+uid.Id);
                if (DateTime.UtcNow - leaseRenewed > TimeSpan.FromSeconds(30)) { await RenewLeaseAsync(leaseName, token); leaseRenewed = DateTime.UtcNow; }
                // IMAP UIDVALIDITY + UID survives absent/reused Message-ID and does not depend on Seen.
                var messageKey = folder.UidValidity + ":" + uid.Id;
                await using (var db = factory.Create())
                {
                    var hash = ReportStore.Digest(account + "\0" + messageKey);
                    if (await db.Intake.AnyAsync(x => x.Digest == hash, token) || await db.RetiredKeys.AnyAsync(x => x.Digest == hash, token)) {info.AlreadyProcessed++;continue;}
                }
                var summaries = await folder.FetchAsync(new[] { uid }, MessageSummaryItems.Size | MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate, token);
                var summary = summaries.SingleOrDefault(x=>x.UniqueId==uid);
                var envelope = summary?.Envelope;
                var imapReceivedAt = AppTime.FromOffset(summary?.InternalDate);
                if (envelope == null) {info.MissingHeaders++;continue;}
                // Rejected headers never create a receipt: trust can be granted later and old mail revisited.
                if (!MailAdmission.Allows(m, envelope.From, envelope.Subject)) { info.Filtered++;continue; }
                if (summary?.Size == null || summary.Size.Value > MailPayloadReader.MaxMessageBytes)
                {
                    var warning = new MailReport(account, messageKey, summary?.Envelope?.From.ToString() ?? "", summary?.Envelope?.Subject ?? "",
                        (m.Purpose == MailboxPurpose.Dmarc ? DmarcParser.ErrorMarker : MailPayloadReader.ErrorMarker) + "Письмо не загружено: размер неизвестен или превышает лимит 16 МБ. Вложения не обработаны.", AppTime.FromOffset(envelope.Date), envelope.MessageId??"",imapReceivedAt);
                    var importedWarning=await store.IngestMessageAsync(new[] { warning }, account, messageKey, token);processed+=importedWarning;info.ImportedRecords+=importedWarning;info.AcceptedMessages++;
                    continue;
                }
                if (m.RequireSpfDkim)
                {
                    Progress(info,"Проверка настроек допуска"); MailAuthentication.Validate(m);
                    var auth = await folder.FetchAsync(new[] { uid }, MessageSummaryItems.None, new[] { "Authentication-Results" }, token);
                    var authHeaders = auth.SingleOrDefault(x => x.UniqueId == uid)?.Headers;
                    var authentication=MailAuthentication.Evaluate(m, authHeaders?.Where(h => h.Field.Equals("Authentication-Results", StringComparison.OrdinalIgnoreCase)).Select(h => h.Value) ?? Enumerable.Empty<string>(), MailAdmission.Sender(envelope.From));
                    if(!authentication.Allowed)
                    {
                        info.AuthenticationRejected++;info.LastAuthenticationFailure="UID "+uid.Id+"\n"+authentication.Describe();
                        if (authWarnings.Count < 50) authWarnings.Add("SPF/DKIM: письмо UID " + uid.Id + " в ящике " + m.Name + " отклонено: "+authentication.Reason+"; тело и вложения не загружены");
                        continue;
                    }
                }
                var bodies = await folder.FetchAsync(new[] { uid }, MessageSummaryItems.BodyStructure, token);
                var bodySummary = bodies.SingleOrDefault(x=>x.UniqueId==uid);
                var message = new MimeMessage { Subject = envelope.Subject ?? "", Date = envelope.Date ?? default };
                message.From.AddRange(envelope.From);
                if (envelope.MessageId is string messageId) message.MessageId = messageId;
                if (m.Purpose == MailboxPurpose.Reports)
                {
                    // Fetch only the plain/HTML body part; images and all attachments stay on IMAP.
                    var part = bodySummary?.TextBody ?? bodySummary?.HtmlBody;
                    if (part == null) {
                        info.NoBody++;
                        var warning=new MailReport(account,messageKey,envelope.From.ToString(),envelope.Subject??"",MailPayloadReader.ErrorMarker+"Нет текстовой части: письмо содержит только вложения или неподдерживаемый формат. Вложения не загружались.",AppTime.FromOffset(envelope.Date),envelope.MessageId??"",imapReceivedAt);
                        var importedWarning=await store.IngestMessageAsync(new[]{warning},account,messageKey,token);processed+=importedWarning;info.ImportedRecords+=importedWarning;info.AcceptedMessages++;continue;
                    }
                    Progress(info,"Чтение текстовой части");var entity = await folder.GetBodyPartAsync(uid, part, token);
                    if (entity is not TextPart text || text.IsAttachment || text.ContentType.MimeType is not ("text/plain" or "text/html")) {info.NoBody++;continue;}
                    message.Body = text;
                }
                else
                {
                    var parts = bodySummary?.BodyParts.OfType<BodyPartBasic>().Where(x => MailPayloadReader.IsDmarcPart(x.FileName ?? "", x.ContentType.MimeType)).Take(33).ToArray() ?? [];
                    if (parts.Length > 32) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком много вложений DMARC"));
                    var body = new Multipart("mixed");
                    foreach (var part in parts)
                    {
                        var entity = await folder.GetBodyPartAsync(uid, part, token);
                        if (entity is MimePart mime && MailPayloadReader.IsDmarcPart(mime.FileName ?? "", mime.ContentType.MimeType)) body.Add(mime);
                    }
                    if (parts.Length == 0 && bodySummary?.TextBody is BodyPartText inline)
                    {
                        var entity = await folder.GetBodyPartAsync(uid, inline, token);
                        if (entity is TextPart text && !text.IsAttachment && text.ContentType.MimeType == "text/plain") body.Add(text);
                    }
                    message.Body = body;
                }
                if (!MailAdmission.Allows(m, message.From, message.Subject) || MailAdmission.Sender(message.From) != MailAdmission.Sender(summary?.Envelope?.From)) {info.Filtered++;continue;}
                IReadOnlyList<MailReport> payloads = MailPayloadReader.ReadSafe(message, account, messageKey, m.Purpose == MailboxPurpose.Dmarc).Select(x=>x with {ImapReceivedAt=imapReceivedAt}).ToArray();
                if (m.Purpose == MailboxPurpose.Dmarc)
                    payloads = payloads.Where(x => x.Body.StartsWith(DmarcParser.Marker, StringComparison.Ordinal) || x.Body.StartsWith(DmarcParser.ErrorMarker, StringComparison.Ordinal)).ToArray();
                if (payloads.Count > 0) {Progress(info,"Сохранение отчёта");var imported=await store.IngestMessageAsync(payloads, account, messageKey, token);processed+=imported;info.ImportedRecords+=imported;info.AcceptedMessages++;} else info.NoPayload++;
                await RenewLeaseAsync(leaseName, token);
            }
            await client.DisconnectAsync(true, token); return processed;
        }
        finally { await ReleaseLeaseAsync(leaseName); }
    }
    private static async Task<string> PingAsync(string host, CancellationToken token)
    {
        try { using var ping = new Ping(); var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(2), cancellationToken: token); return reply.Status == IPStatus.Success ? "OK" : reply.Status.ToString(); }
        catch (Exception) when (!token.IsCancellationRequested) { return "ERROR"; }
    }
    private async Task<bool> AcquireLeaseAsync(string name, CancellationToken token)
    {
        await using var db = factory.Create(); var now = DateTime.UtcNow;
        if (await db.Leases.Where(x => x.Name == name && x.UntilUtc < now).ExecuteUpdateAsync(s => s.SetProperty(x => x.Owner, owner).SetProperty(x => x.UntilUtc, now.AddMinutes(2)), token) > 0) return true;
        if (await db.Leases.AnyAsync(x => x.Name == name, token)) return false;
        db.Leases.Add(new PollLease { Name = name, Owner = owner, UntilUtc = now.AddMinutes(2) });
        try { await db.SaveChangesAsync(token); return true; }
        catch (DbUpdateException) { return false; }
    }
    private async Task RenewLeaseAsync(string name, CancellationToken token)
    {
        await using var db = factory.Create();
        var changed = await db.Leases.Where(x => x.Name == name && x.Owner == owner).ExecuteUpdateAsync(s => s.SetProperty(x => x.UntilUtc, DateTime.UtcNow.AddMinutes(2)), token);
        if (changed == 0) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Право опроса передано другому процессу"));
    }
    private async Task ReleaseLeaseAsync(string name)
    {
        await using var db = factory.Create();
        await db.Leases.Where(x => x.Name == name && x.Owner == owner).ExecuteDeleteAsync();
    }
    public Task SendTestMailAsync(string to, CancellationToken token = default) => SendMailAsync(to, "SysAdmin Assistant — проверка SMTP", "SMTP работает. " + Display.Stamp(AppTime.Now), token);
    public async Task SendMailAsync(string to, string subject, string body, CancellationToken token = default)
    {
        if (factory.Remote is RemoteRepository remote) { await remote.CommandAsync<bool>("sendmail", new RemoteMessage(to, subject, body), token); return; }
        var s = settings.Smtp;
        var message = new MimeMessage(); message.From.Add(MailboxAddress.Parse(s.From)); message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject; message.Body = new TextPart("plain") { Text = body };
        using var client = new SmtpClient();
        await client.ConnectAsync(s.Host, s.Port, s.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, token);
        if (s.User.Length > 0) await client.AuthenticateAsync(s.User, LocalSecret.Unprotect(s.ProtectedPassword), token);
        await client.SendAsync(message, token); await client.DisconnectAsync(true, token);
    }
}
