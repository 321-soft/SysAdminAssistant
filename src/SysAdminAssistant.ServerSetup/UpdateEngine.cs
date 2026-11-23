using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SysAdminAssistant.ServerSetup;

internal sealed record InstalledServer(string Target,string Data,string Account,string StartMode,string State);
internal static class UpdateEngine
{
    public static async Task<InstalledServer> DetectAsync()
    {
        var command="Get-CimInstance Win32_Service -Filter \"Name='SysAdminAssistantServer'\" | Select-Object PathName,StartName,StartMode,State | ConvertTo-Json -Compress";
        var json=await SetupEngine.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),["-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-Command",command]);
        if(string.IsNullOrWhiteSpace(json))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Установленная служба SysAdminAssistantServer не найдена."));
        using var doc=JsonDocument.Parse(json);var item=doc.RootElement;
        var path=item.GetProperty("PathName").GetString()??"";
        var match=Regex.Match(path,"^\"([^\"]+SysAdminAssistant\\.Server\\.exe)\"\\s+--data-dir\\s+\"([^\"]+)\"\\s*$",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        if(!match.Success)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестная командная строка службы. Автоматическое обновление отменено."));
        return new(Path.GetDirectoryName(Path.GetFullPath(match.Groups[1].Value))!,Path.GetFullPath(match.Groups[2].Value),item.GetProperty("StartName").GetString()??"",item.GetProperty("StartMode").GetString()??"",item.GetProperty("State").GetString()??"");
    }
    private static Version VersionAt(string folder) => AssemblyName.GetAssemblyName(Path.Combine(folder,"SysAdminAssistant.Server.dll")).Version??throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Нет версии сервера."));
    private static bool Related(string a,string b) => a.Equals(b,StringComparison.OrdinalIgnoreCase)||a.StartsWith(b.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)||b.StartsWith(a.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
    private static void SafeTree(string folder)
    {
        if(!Path.IsPathFullyQualified(folder)||folder.StartsWith(@"\\")||Path.GetPathRoot(folder)!.TrimEnd('\\').Equals(folder.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нужен отдельный локальный каталог."));
        for(var parent=Directory.GetParent(folder);parent!=null;parent=parent.Parent)
            if((parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Родительский каталог является ссылкой: ")+parent.FullName);
        if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Каталог является ссылкой: ")+folder);
        foreach(var entry in Directory.EnumerateFileSystemEntries(folder)) {
            var attributes=File.GetAttributes(entry);
            if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Обнаружена ссылка, обновление отменено: ")+entry);
            if((attributes&FileAttributes.Directory)!=0)SafeTree(entry);
        }
    }
    private static void CopyTree(string source,string target,string? excluded=null)
    {
        Directory.CreateDirectory(target);
        foreach(var file in Directory.EnumerateFiles(source))File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
        foreach(var dir in Directory.EnumerateDirectories(source)) {
            if(excluded!=null && Path.GetFullPath(dir).Equals(Path.GetFullPath(excluded),StringComparison.OrdinalIgnoreCase))continue;
            CopyTree(dir,Path.Combine(target,Path.GetFileName(dir)));
        }
    }
    private static string Pin(string info)
    {
        var value=Regex.Match(info,@"SHA-256:\s*([A-Fa-f0-9]{64})",RegexOptions.None,TimeSpan.FromSeconds(1)).Groups[1].Value;
        return value.Length==64?value:throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Сервер не вернул SHA-256 сертификата."));
    }
    private static async Task ProbeAsync(WindowsService service,string ip,int port,string pin)
    {
        using var handler=new HttpClientHandler {UseProxy=false,AllowAutoRedirect=false};
        handler.ServerCertificateCustomValidationCallback=(_,certificate,_,_)=>certificate!=null && DateTime.UtcNow>=certificate.NotBefore.ToUniversalTime() && DateTime.UtcNow<certificate.NotAfter.ToUniversalTime() && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pin),SHA256.HashData(certificate.RawData));
        using var http=new HttpClient(handler) {Timeout=TimeSpan.FromSeconds(2)};
        for(var attempt=0;attempt<30;attempt++) {
            if(service.Running())try {
                using var response=await http.GetAsync(new UriBuilder("https",ip,port,"api/state").Uri);
                if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)return;
            }catch(Exception e)when(e is HttpRequestException or OperationCanceledException){}
            await Task.Delay(1000);
        }
        throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Новая версия не прошла проверку HTTPS."));
    }
    internal static async Task<bool> VerifyInstalledAsync(Version expected)
    {
        var installed=await DetectAsync();if(VersionAt(installed.Target)!=expected)return false;
        using var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(installed.Data,"server.json")));
        var pin=Pin(await SetupEngine.RunAsync(Path.Combine(installed.Target,"SysAdminAssistant.Server.exe"),["--info","--data-dir",installed.Data]));
        using var service=new WindowsService();service.OpenInstalled();
        await ProbeAsync(service,config.RootElement.GetProperty("BindIp").GetString()!,config.RootElement.GetProperty("Port").GetInt32(),pin);return true;
    }
    public static Task<string> UpdateAsync(IProgress<string> log) => Task.Run(() =>
    {
        using var updateMutex=new Mutex(false,@"Global\SysAdminAssistantServer.Update");
        bool owns=false;
        try { try{owns=updateMutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
            if(!owns)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Другой установщик уже обновляет сервер."));
            return UpdateLockedAsync(log).GetAwaiter().GetResult();
        }finally{if(owns)updateMutex.ReleaseMutex();}
    });
    private static async Task<string> UpdateLockedAsync(IProgress<string> log)
    {
        var installed=await DetectAsync();var source=SetupEngine.Source;
        log.Report("Настройка журнала событий сервера…");
        await ServerEventLog.EnsureAsync();
        if(Related(source,installed.Target)||Related(source,installed.Data)||Related(installed.Target,installed.Data))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Распакуйте новый ServerSetup отдельно от установленной программы и данных."));
        SafeTree(source);SafeTree(installed.Target);SafeTree(installed.Data);
        foreach(var name in new[]{"SysAdminAssistant.Server.exe","SysAdminAssistant.Server.dll","SysAdminAssistant.Server.deps.json","SysAdminAssistant.Server.runtimeconfig.json"})
            if(!File.Exists(Path.Combine(source,name)))throw new FileNotFoundException(global::SysAdminAssistant.Core.ProductText.Get("Неполный комплект Server: ")+name);
        var oldVersion=VersionAt(installed.Target);var nextVersion=VersionAt(source);
        SysAdminAssistant.Core.ServerUpgradeVersions.Validate(oldVersion,nextVersion,typeof(UpdateEngine).Assembly.GetName().Version!);
        if(installed.StartMode!="Auto")throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("У службы должен быть автоматический запуск. Включите его в свойствах службы и повторите обновление."));
        var configPath=Path.Combine(installed.Data,"server.json");
        using var config=JsonDocument.Parse(File.ReadAllText(configPath));var c=config.RootElement;
        if(c.GetProperty("InstallFormat").GetInt32() != 6 || !c.GetProperty("MachineSecrets").GetBoolean())throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нужна установка 0.6 с серверной защитой секретов. Учётка службы не меняется."));
        foreach(var name in new[]{"settings.json","users.json","server.pfx"})if(!File.Exists(Path.Combine(installed.Data,name)))throw new FileNotFoundException(global::SysAdminAssistant.Core.ProductText.Get("Не найден ")+name);
        var ip=c.GetProperty("BindIp").GetString()!;var port=c.GetProperty("Port").GetInt32();
        var exe=Path.Combine(installed.Target,"SysAdminAssistant.Server.exe");
        await SetupEngine.EnsureInvoiceRuntime(log);
        log.Report("Проверка установленной версии "+oldVersion+" и сертификата…");
        var pin=Pin(await SetupEngine.RunAsync(exe,["--info","--data-dir",installed.Data]));
        var originalSettings=SHA256.HashData(File.ReadAllBytes(Path.Combine(installed.Data,"settings.json")));
        var originalCertificate=SHA256.HashData(File.ReadAllBytes(Path.Combine(installed.Data,"server.pfx")));
        var backupRoot=Path.Combine(installed.Data,"update-backups");
        // Inherit the protected data-directory ACL; no secrets are copied into ProgramData's public root.
        var backup=Path.Combine(backupRoot,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
        using var service=new WindowsService();service.OpenInstalled();var wasRunning=service.IsRunning();
        bool stopped=false,changed=false;
        try {
            log.Report("Остановка службы…");service.StopInstalled();stopped=true;
            // Capture the final saved settings after shutdown, not the live worker's earlier snapshot.
            originalSettings=SHA256.HashData(File.ReadAllBytes(Path.Combine(installed.Data,"settings.json")));
            log.Report("Резервная копия программы и локальных данных: "+backup);
            Directory.CreateDirectory(backup);
            CopyTree(installed.Target,Path.Combine(backup,"program"));
            CopyTree(installed.Data,Path.Combine(backup,"data"),backupRoot);
            File.WriteAllText(Path.Combine(backup,"update.json"),JsonSerializer.Serialize(new{Old=oldVersion.ToString(),Next=nextVersion.ToString(),installed.Target,installed.Data,installed.Account,SqlDatabase="SQL data is retained. SQL backup must be made separately; startup may add an archive table."}));
            log.Report("Замена файлов; существующие данные, учётка службы, firewall и настройки сохраняются…");
            changed=true;CopyTree(source,installed.Target);
            var nextPin=Pin(await SetupEngine.RunAsync(exe,["--info","--data-dir",installed.Data]));
            if(!nextPin.Equals(pin,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Отпечаток сертификата изменился."));
            log.Report("Запуск и проверка HTTPS…");service.Start();await ProbeAsync(service,ip,port,pin);
            if(!originalSettings.SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(installed.Data,"settings.json")))))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Настройки неожиданно изменились."));
            if(!originalCertificate.SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(installed.Data,"server.pfx")))))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Сертификат неожиданно изменился."));
            log.Report("Обновление завершено. Учётка службы: "+installed.Account+". Резервная копия: "+backup);
            await ProductMaintenance.RegisterAsync(installed.Data);
            await SetupEngine.ConfigureBootAsync();
            return backup;
        }catch(Exception failure) {
            if(changed) {
                try {
                    log.Report("Ошибка обновления. Возврат прежних файлов…");service.StopInstalled();
                    var savedProgram=Path.Combine(backup,"program");
                    foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories)) {
                        var relative=Path.GetRelativePath(source,file);var target=Path.Combine(installed.Target,relative);
                        if(!File.Exists(Path.Combine(savedProgram,relative)) && File.Exists(target))File.Delete(target);
                    }
                    CopyTree(savedProgram,installed.Target);
                    File.Copy(Path.Combine(backup,"data","users.json"),Path.Combine(installed.Data,"users.json"),true);
                    if(wasRunning){service.Start();await ProbeAsync(service,ip,port,pin);}
                    log.Report("Прежние файлы восстановлены. Данные и настройки не заменялись; добавленная таблица архива может остаться.");
                }catch(Exception rollback){throw new AggregateException(global::SysAdminAssistant.Core.ProductText.Get("Обновление и автоматический возврат не завершены. Резервная копия: ")+backup,failure,rollback);}
            }else if(stopped && wasRunning){service.Start();}
            throw;
        }
    }
}
