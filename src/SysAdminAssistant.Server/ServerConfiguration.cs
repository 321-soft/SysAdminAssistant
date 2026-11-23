using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;

public sealed class ServerConfiguration
{
    public int InstallFormat { get; set; }
    public string OwnerSid { get; set; } = "";
    public bool MachineSecrets { get; set; }
    public string ServiceAccountName { get; set; } = "";
    public string BindIp { get; set; } = "";
    public int Port { get; set; } = 7443;
    public string Url => new UriBuilder("https", BindIp, Port).Uri.AbsoluteUri;
    public string[] Networks { get; set; } = [];
    public string User { get; set; } = "admin";
    public string Salt { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string ProtectedCertificatePassword { get; set; } = "";
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysAdminAssistantServer-0.6");
    public static ServerConfiguration Load(string directory)
    {
        var config = JsonSerializer.Deserialize<ServerConfiguration>(File.ReadAllText(Path.Combine(directory, "server.json"))) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Пустые настройки сервера"));
        if (config.InstallFormat != 6) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Версия 0.6 требует чистой установки. Старую конфигурацию нельзя использовать."));
        if (!IPAddress.TryParse(config.BindIp, out var ip) || !(LanNetwork.IsPrivate(ip) || IPAddress.IsLoopback(ip)) || config.Port is < 1 or > 65535 || config.Networks.Length == 0 || config.User.Length is < 1 or > 100 || Convert.FromBase64String(config.Salt).Length != 32 || Convert.FromBase64String(config.PasswordHash).Length != 32) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неверные настройки сервера"));
        foreach (var network in config.Networks) _ = new LanNetwork(network);
        return config;
    }
    public void Save(string directory)
    {
        var path = Path.Combine(directory, "server.json"); var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true);
    }
    public static void Setup(string directory, string? serviceSid = null, string serviceAccountName = "")
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(global::SysAdminAssistant.Core.ProductText.Get("Сервер рассчитан на Windows"));
        if (new[] { "server.json", "server.db", "settings.json", "server.pfx" }.Any(name => File.Exists(Path.Combine(directory, name)))) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Сервер уже настроен. Для новой установки выберите другой --data-dir; существующая база не удаляется."));
        string Ask(string caption, string fallback = "") { Console.Write(caption + (fallback.Length > 0 ? " [" + fallback + "]" : "") + ": "); var s = Console.ReadLine()?.Trim() ?? ""; return s.Length > 0 ? s : fallback; }
        LocalSecret.MachineScope = serviceSid != null;
        var c = new ServerConfiguration { InstallFormat = 6, OwnerSid = serviceSid ?? WindowsIdentity.GetCurrent().User!.Value, MachineSecrets = serviceSid != null, ServiceAccountName = serviceAccountName, BindIp = Ask("Локальный IP сервера"), User = Ask("Логин администратора", "admin") };
        c.Port = int.Parse(Ask("Порт HTTPS", "7443"));
        c.Networks = Ask("Разрешённые локальные подсети CIDR, через запятую").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!IPAddress.TryParse(c.BindIp, out var ip) || !LanNetwork.IsPrivate(ip) || c.Port is < 1 or > 65535 || c.Networks.Length == 0 || c.User.Length is < 1 or > 100) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите частный IP, допустимый порт, логин и подсети"));
        if (ip.IsIPv4MappedToIPv6) { ip = ip.MapToIPv4(); c.BindIp = ip.ToString(); }
        foreach (var network in c.Networks) _ = new LanNetwork(network);
        var password = ReadPassword("Пароль администратора: ");
        if (password != ReadPassword("Повторите пароль: ")) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пароли не совпадают"));
        (c.Salt, c.PasswordHash) = ServerPassword.Create(password);
        var app = new AppSettings { SqlitePath = Path.Combine(directory, "server.db"), ExternalWorker = false };
        if (Ask("Хранилище: sqlite или sqlserver", "sqlite").Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            app.DatabaseProvider = DatabaseProvider.SqlServer;
            var sqlHost = Ask("SQL Server (сервер\\экземпляр или сервер,порт)");
            var sqlDatabase = Ask("Имя новой базы", "SysAdminAssistantServer-0.6");
            var sqlUser = Ask("SQL-логин");
            var sqlPassword = ReadPassword("Пароль SQL-логина: ");
            var trustSql = Ask("Доверять сертификату SQL Server без проверки (yes/no)", "no").Equals("yes", StringComparison.OrdinalIgnoreCase);
            SqlStorage.Configure(app, sqlHost, sqlDatabase, sqlUser, sqlPassword, trustSql);
        }
        Directory.CreateDirectory(directory);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { new SecurityIdentifier(c.OwnerSid), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(acl);
        if (app.DatabaseProvider == DatabaseProvider.SqlServer)
        {
            Console.WriteLine("Создание SQL-базы и таблиц…");
            SqlStorage.CreateDatabaseAsync(app).GetAwaiter().GetResult();
        }
        using var rsa = RSA.Create(3072);
        var req = new CertificateRequest("CN=SysAdmin Assistant Server", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(ip); req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
        var pfxPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllBytes(Path.Combine(directory, "server.pfx"), cert.Export(X509ContentType.Pfx, pfxPassword));
        c.ProtectedCertificatePassword = LocalSecret.Protect(pfxPassword);
        SettingsFile.Save(app, Path.Combine(directory, "settings.json")); c.Save(directory); UserDirectory.Bootstrap(directory, c);
        Console.WriteLine("Готово. URL: " + c.Url);
        Console.WriteLine("SHA-256 сертификата: " + Convert.ToHexString(SHA256.HashData(cert.RawData)));
        Console.WriteLine("Каталог: " + directory + "\nУчётная запись сервера: " + (serviceSid != null ? serviceAccountName : WindowsIdentity.GetCurrent().Name));
    }
    public static string ReadPassword(string caption)
    {
        Console.Write(caption);
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var text = new System.Text.StringBuilder();
        while (true) { var key = Console.ReadKey(true); if (key.Key == ConsoleKey.Enter) break; if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; } else if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar); }
        Console.WriteLine(); return text.ToString();
    }
}
