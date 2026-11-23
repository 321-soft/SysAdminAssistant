using System.Globalization;
using SysAdminAssistant.Core;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace SysAdminAssistant.Infrastructure;

public sealed record DmarcSenderCandidate(string Address, string Subject, string Date);

public static class MailAdmission
{
    public static bool HasDmarcSubject(string? subject) => subject != null && subject.Length <= 10000 &&
        Regex.IsMatch(subject, @"(?<![\p{L}\p{N}])dmarc(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string NormalizeDomain(string value)
    {
        var text = value.Trim();
        if (text.Length == 0 || text.Contains('*') || text.Contains('@') || text.Contains('/') || text.EndsWith('.'))
            throw new FormatException(global::SysAdminAssistant.Core.ProductText.Get("Укажите точный домен, например example.com, без @, ссылок и масок"));
        try { text = new IdnMapping().GetAscii(text).ToLowerInvariant(); }
        catch (ArgumentException) { throw new FormatException(global::SysAdminAssistant.Core.ProductText.Get("Некорректный домен отправителя")); }
        if (text.Length > 253 || !text.Contains('.') || text.Split('.').Any(x => x.Length is < 1 or > 63 || x.StartsWith('-') || x.EndsWith('-') || x.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new FormatException(global::SysAdminAssistant.Core.ProductText.Get("Некорректный домен отправителя"));
        return text;
    }

    public static string NormalizeAddress(string value)
    {
        var text = value.Trim();
        if (text.Length > 320 || !MailboxAddress.TryParse(text, out var parsed) || !parsed.Address.Equals(text, StringComparison.OrdinalIgnoreCase))
            throw new FormatException(global::SysAdminAssistant.Core.ProductText.Get("Укажите полный адрес отправителя без отображаемого имени"));
        var at = parsed.Address.LastIndexOf('@');
        if (at < 1) throw new FormatException(global::SysAdminAssistant.Core.ProductText.Get("Некорректный адрес отправителя"));
        return parsed.Address[..at].ToLowerInvariant() + "@" + NormalizeDomain(parsed.Address[(at + 1)..]);
    }

    public static string[] ParseList(string value, bool addresses) => value.Split(['\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => addresses ? NormalizeAddress(x) : NormalizeDomain(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static string? Sender(InternetAddressList? from)
    {
        var senders = from?.Mailboxes.Take(2).ToArray();
        if (senders == null || senders.Length != 1) return null;
        try { return NormalizeAddress(senders[0].Address); }
        catch (FormatException) { return null; }
    }

    public static bool Allows(MailSettings settings, InternetAddressList? from, string? subject)
    {
        var sender = Sender(from);
        if (sender == null) return false;
        if (settings.Purpose == MailboxPurpose.Dmarc)
            return HasDmarcSubject(subject) && settings.TrustedDmarcSenders.Contains(sender, StringComparer.OrdinalIgnoreCase);
        return settings.Purpose == MailboxPurpose.Reports && !HasDmarcSubject(subject) && settings.AllowedDomains.Contains(sender[(sender.LastIndexOf('@') + 1)..], StringComparer.OrdinalIgnoreCase);
    }

    // Header discovery never downloads MIME bodies or attachments and never grants trust.
    public static async Task<DmarcSenderCandidate[]> DiscoverAsync(MailSettings mailbox, CancellationToken token)
    {
        if (mailbox.Purpose != MailboxPurpose.Dmarc) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Подтверждение отправителя доступно для DMARC"));
        using var client = new ImapClient { Timeout = 30000 };
        await client.ConnectAsync(mailbox.Host, mailbox.Port, SecureSocketOptions.SslOnConnect, token);
        await client.AuthenticateAsync(mailbox.User, LocalSecret.Unprotect(mailbox.ProtectedPassword), token);
        var folder = await client.GetFolderAsync(mailbox.Folder, token);
        await folder.OpenAsync(FolderAccess.ReadOnly, token);
        var ids = (await folder.SearchAsync(SearchQuery.SubjectContains("dmarc"), token)).TakeLast(200).ToArray();
        if (ids.Length == 0) return [];
        var headers = await folder.FetchAsync(ids, MessageSummaryItems.Envelope, token);
        var result = headers.Where(x => HasDmarcSubject(x.Envelope?.Subject)).OrderByDescending(x => x.Envelope?.Date)
            .Select(x => new DmarcSenderCandidate(Sender(x.Envelope?.From) ?? "", x.Envelope?.Subject ?? "", x.Envelope?.Date?.ToServerTime().ToString("dd.MM.yyyy HH:mm") ?? ""))
            .Where(x => x.Address.Length > 0 && !mailbox.TrustedDmarcSenders.Contains(x.Address, StringComparer.OrdinalIgnoreCase))
            .DistinctBy(x => x.Address, StringComparer.OrdinalIgnoreCase).ToArray();
        await client.DisconnectAsync(true, token);
        return result;
    }
}
