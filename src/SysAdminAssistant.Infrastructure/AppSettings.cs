using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Infrastructure;

public sealed class AppSettings
{
    public ProductUpdateSources ProductUpdates {get;set;}=new();
    public string NotificationLanguage {get;set;}="ru";
    public ServerHeartbeatSettings ServerHeartbeat {get;set;}=new();
    public DailySummarySettings DailySummary {get;set;}=new();
    public string TimeZoneId {get;set;}=TimeZoneInfo.Local.Id;
    public DatabaseProvider DatabaseProvider { get; set; }
    public string SqlitePath { get; set; } = Path.Combine(SettingsFile.DirectoryPath, "SysAdminAssistant.CSharp.db");
    public string SqlConnectionString { get; set; } = "Server=localhost;Database=SysAdminAssistant;Integrated Security=True;Encrypt=True;TrustServerCertificate=False";
    public string ProtectedSqlPassword { get; set; } = "";
    public bool ExternalWorker { get; set; }
    public bool MonitoringEnabled { get; set; } = true;
    public int PollSeconds { get; set; } = 60;
    public string LanPingHosts {get;set;}="";
    public int AvailabilityAlertMinutes {get;set;}=2;
    public string PingHost { get; set; } = "1.1.1.1";
    public string DnsName { get; set; } = "example.com";
    public MailSettings Mail { get; set; } = new(); // v0.2.4 migration only
    public List<MailSettings> Mailboxes { get; set; } = [];
    public List<CustomDiagnostic> CustomDiagnostics { get; set; } = [];
    public int EventRetentionDays { get; set; } // 0 = unlimited, 30 or 90
    public void NormalizeMailboxes()
    {
        if (Mailboxes.Count == 0 && Mail.Host.Length > 0) Mailboxes.Add(Mail);
        Mail = new();
    }
    public TelegramSettings Telegram { get; set; } = new();
    public SmtpSettings Smtp { get; set; } = new();
    public bool VaultEmailFactor { get; set; }
    public string VaultEmail { get; set; } = "";
    public InventorySettings Inventory { get; set; } = new();
    public CompanySettings Company { get; set; } = new();
    public RouterSettings Router { get; set; } = new();
}
public sealed class CompanySettings
{
    public int PaymentPolicyVersion {get;set;}
    public int PaymentSoonDays { get; set; } = 3;
    public int WeekendMask { get; set; } = PaymentCalendar.DefaultWeekendMask;
}
public sealed class InventorySettings
{
    public string RepresentativePosition { get; set; } = "";
    public string IssueReason { get; set; } = "Служебная необходимость";
    public string ReturnReason { get; set; } = "Возврат материальных ценностей работодателю";
    public string WriteOffReason { get; set; } = "Непригодность к дальнейшей эксплуатации";
    public string Organization { get; set; } = "";
    public string Representative { get; set; } = "";
    public string Place { get; set; } = "";
    public string Commission { get; set; } = "";
}
public enum MailboxPurpose { Reports, Dmarc }
public enum MailAuthReceiver { Custom, YandexMxFront }
public enum MailAuthPolicy { BothAligned, BothPassOneIdentity }
public sealed class MailSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public override string ToString() => Name.Length > 0 ? Name : User + " · " + Host;
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;
    public string User { get; set; } = "";
    public string ProtectedPassword { get; set; } = "";
    public string Folder { get; set; } = "INBOX";
    public bool UnseenOnly { get; set; } = true;
    public bool RequireSpfDkim { get; set; } = true;
    public string TrustedAuthservId { get; set; } = "";
    public List<string> TrustedAuthservIds { get; set; } = [];
    public MailAuthReceiver AuthReceiver { get; set; }
    public MailAuthPolicy AuthPolicy { get; set; }
    public bool AuthHeadersSanitized { get; set; }
    public MailboxPurpose Purpose { get; set; }
    public string PurposeName => Purpose == MailboxPurpose.Dmarc ? "DMARC" : "Отчёты";
    public List<string> AllowedDomains { get; set; } = [];
    public List<string> TrustedDmarcSenders { get; set; } = [];
}
public sealed class SmtpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 465;
    public string User { get; set; } = "";
    public string From { get; set; } = "";
    public string ProtectedPassword { get; set; } = "";
}
public sealed class RouterSettings
{
    public string CertificateSha256 {get;set;}="";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 8729;
    public string User { get; set; } = "";
    public string ProtectedPassword { get; set; } = "";
}
public static class LocalSecret
{
    public static bool MachineScope { get; set; }
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SysAdminAssistant.CSharp.Settings.v1");
    public static string Protect(string value)
    {
        if (value.Length == 0) return "";
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(global::SysAdminAssistant.Core.ProductText.Get("Защита сохранённых паролей доступна только в Windows"));
        return (MachineScope ? "machine:" : "") + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, MachineScope ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser));
    }
    public static string Unprotect(string value)
    {
        if (value.Length == 0) return "";
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(global::SysAdminAssistant.Core.ProductText.Get("Расшифровка сохранённых паролей доступна только в Windows"));
        var machine = value.StartsWith("machine:", StringComparison.Ordinal);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(machine ? value[8..] : value), Entropy, machine ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser));
    }
}
public static class SettingsFile
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysAdminAssistant");
    public static string PathName => Path.Combine(DirectoryPath, "settings.json");
    public static AppSettings Load(string? path = null)
    {
        path ??= PathName;
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Пустые настройки"));
        if (!Enum.IsDefined(settings.DatabaseProvider) || settings.Mail == null || settings.Smtp == null || settings.Router == null || settings.Inventory == null) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Несовместимые настройки"));
        if(settings.ServerHeartbeat!=null&&(settings.ServerHeartbeat.EndpointUrl.Length==0||settings.ServerHeartbeat.ProtectedSecret.Length==0))settings.ServerHeartbeat.Enabled=false;
        if(settings.ServerHeartbeat==null)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Несовместимые настройки внешнего контроля"));
        if (settings.Mailboxes == null || settings.CustomDiagnostics == null || settings.EventRetentionDays is not (0 or 30 or 90)) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Несовместимые настройки хранения или почты"));
        _=AppTime.Resolve(settings.TimeZoneId);
        settings.NormalizeMailboxes();
        foreach (var mailbox in settings.Mailboxes)
        {
            if (!Enum.IsDefined(mailbox.Purpose) || mailbox.AllowedDomains == null || mailbox.TrustedDmarcSenders == null) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Несовместимая политика IMAP"));
            mailbox.AllowedDomains = mailbox.AllowedDomains.Select(MailAdmission.NormalizeDomain).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            mailbox.TrustedDmarcSenders = mailbox.TrustedDmarcSenders.Select(MailAdmission.NormalizeAddress).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        if (settings.Company == null) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Несовместимые настройки компании"));
        if(settings.Company.PaymentPolicyVersion<1){settings.Company.PaymentSoonDays=3;settings.Company.PaymentPolicyVersion=1;}
        PaymentCalendar.Validate(settings.Company.WeekendMask);
        return settings;
    }
    public static void Save(AppSettings value, string? path = null)
    {
        path ??= PathName;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class ProductUpdateSources {public string Primary {get;set;}="https://assistant.321.su/releases/release.json";public string Mirror {get;set;}="";}
