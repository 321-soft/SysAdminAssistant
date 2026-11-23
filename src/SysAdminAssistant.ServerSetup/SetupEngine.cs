using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.ServerSetup;
internal sealed class SetupOptions
{
    public string Target = "", Data = "", Ip = "", Networks = "", User = "", Password = "", Provider = "sqlite", SqlHost = "", SqlDatabase = "", SqlUser = "", SqlPassword = "";
    public int Port = 7443;
    public bool Existing;
    public bool TrustSqlCertificate;
    public bool NewServiceAccount = true;
}
internal sealed record SetupResult(string Url, string Pin, string User, string Data);
internal sealed class SetupConfig
{
    public string OwnerSid { get; set; } = "";
    public bool MachineSecrets { get; set; }
    public string ServiceAccountName { get; set; } = "";
    public string BindIp { get; set; } = "";
    public int Port { get; set; }
    public string[] Networks { get; set; } = [];
    public string User { get; set; } = "";
}
internal static class SetupEngine
{
    internal static async Task ConfigureBootAsync()
    {
        var sc=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"sc.exe");
        await RunAsync(sc,["config",WindowsService.Name,"start=","delayed-auto"]);
        await RunAsync(sc,["failure",WindowsService.Name,"reset=","86400","actions=","restart/10000/restart/30000/restart/60000"]);
        await RunAsync(sc,["failureflag",WindowsService.Name,"1"]);
    }
    public static string Source => Path.Combine(AppContext.BaseDirectory, "Server");
    public static IPAddress[] LocalIps() => NetworkInterface.GetAllNetworkInterfaces().SelectMany(x => x.GetIPProperties().UnicastAddresses)
        .Select(x => x.Address).Where(LanNetwork.IsPrivate).Distinct().ToArray();
    public static void Validate(SetupOptions o)
    {
        if (o.Existing) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Версия 0.6 поддерживает только чистую установку"));
        foreach (var path in new[] { o.Target, o.Data })
        {
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\") || path.Contains('"') || path.Contains('\r') || path.Contains('\n') || path.Contains('\0') || Path.GetFullPath(path).TrimEnd('\\') == Path.GetPathRoot(path)!.TrimEnd('\\'))
                throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выберите отдельные локальные папки, не корень диска и не сетевой путь."));
        }
        o.Target = Path.GetFullPath(o.Target).TrimEnd('\\'); o.Data = Path.GetFullPath(o.Data).TrimEnd('\\');
        if (Related(o.Target, o.Data) || Related(o.Target, Source) || Related(o.Data, Source)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Папки программы, данных и исходной сборки должны быть отдельными и не вложенными друг в друга."));
        foreach (var value in new[] { o.Ip, o.Networks, o.User, o.Password, o.SqlHost, o.SqlDatabase, o.SqlUser, o.SqlPassword })
            if (value.Contains('\r') || value.Contains('\n') || value.Contains('\0')) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Поля должны быть однострочными."));
        if (!o.Existing)
        {
            if (!IPAddress.TryParse(o.Ip, out var ip) || !LanNetwork.IsPrivate(ip) || !LocalIps().Contains(ip)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("IP должен принадлежать локальному интерфейсу сервера."));
            if (o.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(o.User) || o.User.Length > 100) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Проверьте порт и логин администратора."));
            if (o.Password.Length is < 9 or > 256) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пароль приложения: 9–256 символов."));
            var networks = o.Networks.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (networks.Length == 0) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите разрешённые CIDR-подсети."));
            foreach (var network in networks) _ = new LanNetwork(network);
            if (o.Provider is not ("sqlite" or "sqlserver")) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный тип базы."));
            if (o.Provider == "sqlserver")
            {
                if (string.IsNullOrWhiteSpace(o.SqlHost) || string.IsNullOrWhiteSpace(o.SqlUser) || string.IsNullOrEmpty(o.SqlPassword)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите SQL Server, SQL-логин и пароль."));
                if (string.IsNullOrWhiteSpace(o.SqlDatabase) || o.SqlDatabase.Length > 123 || o.SqlDatabase.Any(char.IsControl) || new[] { "master", "model", "msdb", "tempdb" }.Contains(o.SqlDatabase, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите имя новой пользовательской базы: 1–123 символа."));
            }
        }
    }
    private static bool Related(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || b.StartsWith(a.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    public static async Task<SetupResult> InstallAsync(SetupOptions o, IProgress<string> log)
    {
        Validate(o);
        log.Report("Настройка журнала событий сервера…");
        await ServerEventLog.EnsureAsync();
        if (!File.Exists(Path.Combine(Source, "SysAdminAssistant.Server.exe"))) throw new FileNotFoundException(global::SysAdminAssistant.Core.ProductText.Get("Не найден Server\\SysAdminAssistant.Server.exe. Перенесите папку publish\\ServerSetup целиком."));
        var managed = new ManagedAccount();
        var account = @"NT AUTHORITY\LocalService";
        var sid = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null).Value;
        using var service = new WindowsService(); using var firewall = new Firewall();
        bool created = false, ruleAdded = false;
        try
        {
            log.Report("Проверка службы и каталогов…");
            if (service.Exists()) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Служба уже установлена. Установка поверх работающего сервера не выполняется."));
            if (firewall.Exists()) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Правило firewall уже существует. Проверьте предыдущую установку."));
            if (!o.Existing && Directory.Exists(o.Data) && Directory.EnumerateFileSystemEntries(o.Data).Any()) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Для новой установки нужна пустая папка данных. Для повторной попытки выберите существующие настройки."));
            if (o.Existing)
            {
                foreach (var name in new[] { "server.json", "settings.json", "server.pfx" }) if (!File.Exists(Path.Combine(o.Data, name))) throw new FileNotFoundException(global::SysAdminAssistant.Core.ProductText.Get("Для повторной попытки отсутствует ") + name);
                var saved = ReadConfig(o.Data);
                if (!saved.MachineSecrets) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Для прежней защиты паролей выберите новый каталог данных. Перенос учётной записи автоматически не выполняется."));
                if (!o.NewServiceAccount && saved.OwnerSid != sid) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Данные принадлежат отдельной служебной учётной записи; включите её создание/использование."));
            }
            await EnsureInvoiceRuntime(log);
            log.Report("Копирование файлов сервера…"); CopyPayload(o.Target);
            var exe = Path.Combine(o.Target, "SysAdminAssistant.Server.exe");
            if (o.NewServiceAccount)
            {
                log.Report("Создание отдельной учётной записи службы…");
                managed.CreateOrResume(o.Existing ? ReadConfig(o.Data) : null);
                account = managed.Name; sid = managed.Sid;
            }
            ManagedAccount.GrantProgramAccess(o.Target, sid);
            if (!o.Existing)
            {
                log.Report("Создание конфигурации и HTTPS-сертификата…");
                var lines = new List<string> { o.Ip, o.User, o.Port.ToString(), o.Networks, o.Password, o.Password, o.Provider };
                if (o.Provider == "sqlserver") { log.Report("SQL Server: создание новой базы и таблиц…"); lines.AddRange([o.SqlHost, o.SqlDatabase, o.SqlUser, o.SqlPassword, o.TrustSqlCertificate ? "yes" : "no"]); }
                try { await RunAsync(exe, ["--setup", "--data-dir", o.Data, "--service-sid", sid, account], lines); }
                finally { lines.Clear(); o.Password = ""; o.SqlPassword = ""; }
            }
            var info = await RunAsync(exe, ["--info", "--data-dir", o.Data]);
            var config = ReadConfig(o.Data);
            var bind = IPAddress.Parse(config.BindIp);
            if (!LocalIps().Contains(bind)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("IP из настроек отсутствует на сервере."));
            var listener = new TcpListener(bind, config.Port);
            try { listener.Start(); } finally { listener.Stop(); }
            var pin = Regex.Match(info, @"SHA-256:\s*([A-Fa-f0-9]{64})").Groups[1].Value;
            if (pin.Length != 64) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Сервер не вернул отпечаток сертификата."));
            log.Report("Настройка учётной записи и автоматической службы…");
            service.Create(exe, o.Data, account, o.NewServiceAccount ? managed.Password : ""); created = true; managed.ClearPassword();
            await ConfigureBootAsync();
            log.Report("Настройка firewall для выбранных подсетей…");
            firewall.Add(exe, config.BindIp, config.Port, config.Networks); ruleAdded = true;
            log.Report("Запуск службы и проверка HTTPS/сертификата…"); service.Start();
            using var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate != null
                && DateTime.UtcNow >= certificate.NotBefore.ToUniversalTime() && DateTime.UtcNow < certificate.NotAfter.ToUniversalTime()
                && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pin), SHA256.HashData(certificate.RawData));
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            var probeUrl = new UriBuilder("https", config.BindIp, config.Port, "api/state").Uri;
            bool ready = false;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                if (service.Running())
                {
                    try
                    {
                        using var response = await http.GetAsync(probeUrl);
                        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { ready = true; break; }
                    }
                    catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { }
                }
                await Task.Delay(1000);
            }
            if (!ready) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Служба не прошла проверку HTTPS. Проверьте журнал Schannel, сертификат и доступ службы к закрытому ключу; открытый TCP-порт не подтверждает работу TLS."));
            log.Report("Установка завершена.");
            await ProductMaintenance.RegisterAsync(o.Data);
        return new(new UriBuilder("https", config.BindIp, config.Port).Uri.AbsoluteUri, pin, config.User, o.Data);
        }
        catch
        {
            bool removed = !created;
            if (created) { try { service.Rollback(); removed = true; log.Report("Созданная служба удалена."); } catch (Exception e) { log.Report("Не удалось откатить службу: " + e.Message); } }
            if (ruleAdded) { try { firewall.Remove(); } catch (Exception e) { log.Report("Не удалось удалить созданное правило firewall: " + e.Message); } }
            if (managed.Created && removed && !File.Exists(Path.Combine(o.Data, "server.json")))
            {
                try { managed.Remove(); } catch (Exception e) { log.Report("Не удалось удалить созданную учётную запись: " + e.Message); }
            }
            else if (managed.Created && removed) log.Report("Учётная запись сохранена вместе с настройками. Повторная попытка использует её автоматически.");
            log.Report("Файлы программы и данные сохранены. Для повторной попытки с готовой конфигурацией включите использование существующих данных.");
            throw;
        }
        finally { o.Password = ""; o.SqlPassword = ""; managed.ClearPassword(); }
    }
    private static SetupConfig ReadConfig(string folder) => JsonSerializer.Deserialize<SetupConfig>(File.ReadAllText(Path.Combine(folder, "server.json"))) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Пустая конфигурация."));
    internal static async Task EnsureInvoiceRuntime(IProgress<string> log)
    {
        using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");if(key?.GetValue("Installed") is int installed&&installed==1&&key.GetValue("Minor") is int minor&&minor>=29)return;
        var path=Path.Combine(AppContext.BaseDirectory,"InvoiceRuntimePackages","vc_redist.x64.exe");if(!File.Exists(path)){log.Report("VC++ runtime не включён: для OCR может потребоваться установка. Текстовые PDF доступны.");return;}
        log.Report("Установка Microsoft VC++ runtime для OCR…");
        using var process=Process.Start(new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,Arguments="/install /quiet /norestart"})??throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось запустить VC++ runtime"));using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));await process.WaitForExitAsync(timeout.Token);if(process.ExitCode is not(0 or 1638 or 3010))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Ошибка установки VC++ runtime: ")+process.ExitCode);if(process.ExitCode==3010)log.Report("VC++ runtime установлен; Windows запросила перезагрузку.");
    }
    private static void CopyPayload(string target)
    {
        var files = Directory.GetFiles(Source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint });
        bool occupied = Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any();
        foreach (var sourceFile in files)
        {
            var dest = Path.Combine(target, Path.GetRelativePath(Source, sourceFile));
            if (occupied)
            {
                if (!File.Exists(dest)) throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Папка программы занята другой сборкой; выберите пустую."));
                using var a = File.OpenRead(sourceFile); using var b = File.OpenRead(dest);
                if (!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Папка программы занята другой сборкой; выберите пустую."));
            }
            else { Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(sourceFile, dest, false); }
        }
    }
    internal static async Task<string> RunAsync(string exe, string[] args, IEnumerable<string>? input = null)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось запустить ") + Path.GetFileName(exe));
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            if (input != null) foreach (var line in input) await process.StandardInput.WriteLineAsync(line.AsMemory(), timeout.Token);
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
            var text = await output; var errorText = await error;
            if (process.ExitCode != 0) throw new InvalidOperationException(Path.GetFileName(exe) + ": " + (errorText.Length > 0 ? errorText : text).Trim());
            return text;
        }
        catch { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } throw; }
    }
}
