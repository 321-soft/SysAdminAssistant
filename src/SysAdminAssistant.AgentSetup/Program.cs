using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.AgentSetup;
internal static partial class Program
{
    private const string Service="SysAdminAssistantAgent";
    private static readonly string Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant Agent");
    private static readonly string Data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"SysAdminAssistantAgent");
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CreateService(IntPtr manager,string name,string display,uint access,uint type,uint start,uint error,string binary,string? group,IntPtr tag,string? dependencies,string account,string? password);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool ChangeServiceConfig2(IntPtr service,uint level,ref int value);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr OpenService(IntPtr manager,string name,uint access);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool DeleteService(IntPtr service);
    [DllImport("advapi32.dll")]private static extern bool CloseServiceHandle(IntPtr handle);
    private static string installStage="Подготовка";
    private static readonly string InstallLog=Path.Combine(Path.GetTempPath(),"SysAdminAssistant-AgentSetup-"+Guid.NewGuid().ToString("N")+".log");
    private static void Log(string value){try{File.AppendAllText(InstallLog,DateTime.UtcNow.ToString("O")+" "+value+Environment.NewLine);}catch{}}
    private static void Stage(string value){installStage=value;Log(value);}
    private static Exception Error()=>new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    [STAThread]public static void Main(string[] args)
    {
        if(args.Length==1&&args[0]=="--auto-update"){try{using var lease=MaintenanceLease();Environment.ExitCode=RunAutoUpdate().GetAwaiter().GetResult();}catch(Exception e){AutoStatus("Ошибка запуска обслуживания: "+e.Message);Environment.ExitCode=1;}return;}
        if(args.Length!=0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неподдерживаемые аргументы"));
        var language=SysAdminAssistant.Setup.SetupLanguage.Choose();if(language==null)return;
        ProductText.DefaultLanguage=language;SysAdminAssistant.Desktop.LanguageUi.Initialize(language);
        if(!SysAdminAssistant.Setup.SetupAgreement.ConfirmAgent(language))return;
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/SysAdminAssistant.AgentSetup;component/AgentSetupTheme.xaml",UriKind.Relative)});var root=new StackPanel{Margin=new(24)};var window=new Window{Title="SysAdmin Assistant · Windows Agent",Width=740,Height=580,Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},WindowStartupLocation=WindowStartupLocation.CenterScreen};
        root.Children.Add(new TextBlock{Text="Агент мониторинга Windows",FontSize=23,Margin=new(0,0,0,15)});
        var info=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,16)};root.Children.Add(info);
        AgentSignedBootstrap? signed=null;AgentBootstrap? bootstrap=null;
        try{signed=JsonSerializer.Deserialize<AgentSignedBootstrap>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"bootstrap.json")),AgentWire.Json)!;bootstrap=AgentWire.Verify(signed);info.Text=$"Устройство: {bootstrap.DeviceName}\nСервер: {bootstrap.ServerUrl}\nОтпечаток HTTPS: {bootstrap.ServerSha256}\nИздатель пакета SHA-256: {Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(bootstrap.IssuerCertificate)))}\nРазрешённые подсети: {string.Join(", ",bootstrap.AllowedNetworks)}\nРегистрация до: {bootstrap.ExpiresUtc.ToLocalTime():dd.MM.yyyy HH:mm}\n\nСлужба LocalService с отдельным SID. Только чтение состояния, без входящих портов и команд. После установки подтвердите агента в карточке устройства.";}catch(Exception){info.Text="Нет действительного bootstrap.json рядом с установщиком. Создайте пакет из карточки устройства в Assistant.";}
        var verified=new CheckBox{Content="Сверил сервер и отпечаток издателя пакета с Assistant",Margin=new(0,0,0,12)};root.Children.Add(verified);
        var auto=new CheckBox{Content="Автоматически обновлять агент с этого сервера",IsChecked=true,Margin=new(0,0,0,12)};root.Children.Add(auto);
        var install=new Button{Style=(Style)app.FindResource("PrimaryButton"),Content=Directory.Exists(Data)?"Обновить локальный агент · сохранить ключ":"Установить агент",Padding=new(12),Margin=new(0,0,0,10),IsEnabled=false};root.Children.Add(install);
        verified.Checked+=(_,_)=>install.IsEnabled=bootstrap!=null;verified.Unchecked+=(_,_)=>install.IsEnabled=false;
        install.Click+=async(_,_)=>{
            install.IsEnabled=false;var automatic=auto.IsChecked==true;try{await Task.Run(()=>{using var lease=MaintenanceLease();Install(signed!,bootstrap!);Stage("Настройка автоматического обновления");ConfigureAutomatic(signed!,automatic);if(bootstrap!.Token.Length==0)ClearPendingUpdateMarkers();});SysAdminAssistant.Desktop.AppMessageBox.Show(window,"Служба установлена и запущена. Откройте status.json в ProgramData\\SysAdminAssistantAgent: сверьте отпечаток ключа с карточкой устройства и подтвердите регистрацию в Assistant.","Агент");}catch(Exception e){Log(e.ToString());SysAdminAssistant.Desktop.AppMessageBox.Show(window,"Установка не завершена. Этап: "+installStage+"\n"+e.Message+"\nHRESULT: 0x"+e.HResult.ToString("X8")+"\nЖурнал: "+InstallLog,"Агент");install.IsEnabled=true;}
        };
        var trustButton=new Button{Content="Импорт доверия HTTPS · без переустановки",Padding=new(10),Margin=new(0,0,0,10)};root.Children.Add(trustButton);
        trustButton.Click+=(_,_)=>{try{ImportTrust(window);}catch(Exception e){SysAdminAssistant.Desktop.AppMessageBox.Show(window,e.Message,"Доверие HTTPS",MessageBoxButton.OK,MessageBoxImage.Error);}};
        var remove=new Button{Style=(Style)app.FindResource("DangerButton"),Content="Удалить локальный агент",Padding=new(10)};root.Children.Add(remove);remove.Click+=(_,_)=>{
            try{var uninstaller=Path.Combine(AppContext.BaseDirectory,"Uninstall.exe");if(!File.Exists(uninstaller))throw new FileNotFoundException(global::SysAdminAssistant.Core.ProductText.Get("Графический деинсталлятор не найден."));var start=new ProcessStartInfo(uninstaller){UseShellExecute=true};start.ArgumentList.Add("--component");start.ArgumentList.Add("agent");Process.Start(start);window.Close();}catch(Exception e){SysAdminAssistant.Desktop.AppMessageBox.Show(window,e.Message);}
        };app.Run(window);
    }
    private static void Safe(string path){for(var p=new DirectoryInfo(path);p!=null;p=p.Parent)if(p.Exists&&(p.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Путь содержит ссылку или junction: ")+p.FullName);}
    private static void SecureDirectory(string path,SecurityIdentifier sid,FileSystemRights rights)
    {
        Safe(path);Directory.CreateDirectory(path);Safe(path);var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
        foreach(var principal in new[]{new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null)})acl.AddAccessRule(new(principal,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        acl.AddAccessRule(new(sid,rights,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));new DirectoryInfo(path).SetAccessControl(acl);
    }
    private static void Install(AgentSignedBootstrap signed,AgentBootstrap b)
    {
        if(b.ExpiresUtc<DateTime.UtcNow)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Разрешение регистрации просрочено. Создайте новый пакет."));

        if(!b.Binaries.ContainsKey("SysAdminAssistant.UpdateProbe.exe"))throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неполный пакет: отсутствует локальный сборщик обновлений"));
        if(!b.Binaries.ContainsKey("SysAdminAssistant.Agent.exe"))throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("В пакете нет агента"));
        // Read verified bytes before writing: replacement of source files cannot race hashing/copying.
        var files=new Dictionary<string,byte[]>();long total=0;
        foreach(var file in b.Binaries){var path=Path.Combine(AppContext.BaseDirectory,"Agent",file.Key);Safe(Path.GetDirectoryName(path)!);if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Ссылки в пакете запрещены"));var size=new FileInfo(path).Length;if(size>30000000||total+size>80000000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой пакет агента"));var bytes=File.ReadAllBytes(path);total+=bytes.Length;if(Convert.ToHexString(SHA256.HashData(bytes))!=file.Value)throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Файл агента изменён: ")+file.Key);files[file.Key]=bytes;}
        if(Directory.Exists(Target)||Directory.Exists(Data)){UpdateLocal(b,files);return;}
        var manager=OpenSCManager(null,null,2);if(manager==IntPtr.Zero)throw Error();IntPtr service=IntPtr.Zero;
        try{
            service=CreateService(manager,Service,"SysAdmin Assistant · Agent",0xF01FF,0x10,2,1,"\""+Path.Combine(Target,"SysAdminAssistant.Agent.exe")+"\"",null,IntPtr.Zero,null,@"NT AUTHORITY\LocalService",null);if(service==IntPtr.Zero)throw Error();
            Stage("Настройка восстановления службы агента");ConfigureRecovery();
            var sidType=1;if(!ChangeServiceConfig2(service,5,ref sidType))throw Error();
            var sid=(SecurityIdentifier)new NTAccount("NT SERVICE",Service).Translate(typeof(SecurityIdentifier));SecureDirectory(Target,sid,FileSystemRights.ReadAndExecute);SecureDirectory(Data,sid,FileSystemRights.Modify);
            foreach(var file in files)File.WriteAllBytes(Path.Combine(Target,file.Key),file.Value);
            var configPath=Path.Combine(Data,"bootstrap.json");File.WriteAllText(configPath,JsonSerializer.Serialize(signed,AgentWire.Json));
            var configAcl=new FileSecurity();configAcl.SetAccessRuleProtection(true,false);configAcl.AddAccessRule(new(sid,FileSystemRights.Read,AccessControlType.Allow));foreach(var admin in new[]{new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null)})configAcl.AddAccessRule(new(admin,FileSystemRights.FullControl,AccessControlType.Allow));new FileInfo(configPath).SetAccessControl(configAcl);
            RegisterUninstaller();Stage("Права чтения журналов");JournalPermissions.Apply(Service,Data);Stage("Регистрация задачи чтения Windows Update");UpdateProbeTask.Install();
            using var controller=new ServiceController(Service);controller.Start();controller.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));
        }catch{if(service!=IntPtr.Zero){using var controller=new ServiceController(Service);try{if(controller.Status!=ServiceControllerStatus.Stopped){controller.Stop();controller.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(20));}}catch(Exception){}try{UpdateProbeTask.Remove();}catch(Exception){}try{JournalPermissions.Apply(Service,Data,true);}catch(Exception){}DeleteService(service);if(Directory.Exists(Target))Directory.Delete(Target,true);if(Directory.Exists(Data))Directory.Delete(Data,true);}throw;}
        finally{if(service!=IntPtr.Zero)CloseServiceHandle(service);CloseServiceHandle(manager);}
    }
    private static AgentBootstrap Installed(){Safe(Target);Safe(Data);return AgentWire.Verify(JsonSerializer.Deserialize<AgentSignedBootstrap>(File.ReadAllText(Path.Combine(Data,"bootstrap.json")),AgentWire.Json)??throw new InvalidDataException());}
    private static void ImportTrust(Window window)
    {
        var b=Installed();var open=new Microsoft.Win32.OpenFileDialog{Title="Подписанное доверие из Assistant",Filter="Доверие HTTPS (*.json)|*.json"};if(open.ShowDialog(window)!=true)return;
        if(new FileInfo(open.FileName).Length>10000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой файл доверия"));
        var raw=File.ReadAllText(open.FileName);var signed=JsonSerializer.Deserialize<AgentSignedTrust>(raw,AgentWire.Json)??throw new InvalidDataException();
        var path=Path.Combine(Data,"trust.json");long current=0;
        if(File.Exists(path)){if(new FileInfo(path).Length>10000)throw new InvalidDataException();current=AgentTrust.Verify(JsonSerializer.Deserialize<AgentSignedTrust>(File.ReadAllText(path),AgentWire.Json)!,b.IssuerCertificate).Version;}
        var document=AgentTrust.Verify(signed,b.IssuerCertificate,current);
        if(SysAdminAssistant.Desktop.AppMessageBox.Show(window,"Применить доверие версии "+document.Version+" к серверу "+b.ServerUrl+"?\n"+string.Join("\n",document.Pins),"Доверие HTTPS",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        Safe(path);if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Ссылки запрещены"));
        var temp=Path.Combine(Data,"trust-"+Guid.NewGuid().ToString("N")+".tmp");try{File.WriteAllText(temp,raw);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        SysAdminAssistant.Desktop.AppMessageBox.Show(window,"Файл принят. Агент версии 0.12.2 прочитает его при следующей попытке связи. Служба и ключ не меняются.");
    }
    private static void ClearPendingUpdateMarkers()
    {
        Safe(Maintenance);if(!Directory.Exists(Maintenance))return;
        foreach(var directory in Directory.EnumerateDirectories(Maintenance,"update-*"))
        {
            var marker=Path.Combine(directory,"pending");
            try{SafeFile(marker);File.Delete(marker);}catch(IOException e){Log(e.Message);}catch(UnauthorizedAccessException e){Log(e.Message);}
        }
    }

    private static void UpdateLocal(AgentBootstrap package,Dictionary<string,byte[]> files,Action? finish=null)
    {
        var installed=Installed();
        if(package.Token.Length!=0||package.EnrollmentId!=installed.EnrollmentId||!ServerTrust.SameAuthority(package.IssuerCertificate,installed.IssuerCertificate))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нужен пакет обновления именно этого агента, созданный прежним сервером. Ключ и привязку менять нельзя."));
        RequireInstalledService();
        Safe(Maintenance);
        if(finish!=null&&Directory.Exists(Maintenance))
        {
            var pending=Directory.EnumerateDirectories(Maintenance,"update-*").FirstOrDefault(path=>File.Exists(Path.Combine(path,"pending")));
            if(pending!=null)throw new IOException("Предыдущая замена прервалась. Автообновление приостановлено; сохранённая копия: "+Path.Combine(pending,"previous")+". Используйте подписанный пакет обновления.");
        }
        Stage("Настройка восстановления службы агента");ConfigureRecovery();
        var installedFiles=new List<string>();long total=0;
        foreach(var p in Directory.GetFiles(Target)){
            var name=Path.GetFileName(p);if(!AgentRelease.FileName(name))continue;
            SafeFile(p);total+=new FileInfo(p).Length;
            if(total>100000000)throw new IOException("Слишком большая установленная папка");
            installedFiles.Add(name);
        }
        var workspace=Path.Combine(Maintenance,"update-"+Guid.NewGuid().ToString("N"));
        var update=new AgentFileUpdate(Target,workspace,SafeFile,TimeSpan.FromSeconds(30));
        using var service=new ServiceController(Service);
        var wasRunning=service.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending);
        void RequireStopped(){service.Refresh();if(service.Status!=ServiceControllerStatus.Stopped)throw new InvalidOperationException("Служба агента запустилась во время обслуживания. Замена файлов прервана.");}
        Stage("Поиск и остановка задачи чтения обновлений Windows");var taskXml=UpdateProbeTask.Pause();
        bool preserve=false,probeMayBeRunning=false;
        try{
            Stage("Ожидание выхода процесса агента");StopAgentAndWait(service);
            Stage("Проверка блокировок и резервная копия файлов");update.Prepare(files,installedFiles);
            Stage("Замена файлов агента");File.WriteAllText(Path.Combine(workspace,"pending"),"");update.Apply(files,RequireStopped);
            RegisterUninstaller();Stage("Права чтения журналов");JournalPermissions.Apply(Service,Data);
            Stage("Регистрация задачи чтения Windows Update");probeMayBeRunning=true;UpdateProbeTask.Install();
            Stage("Проверка запуска агента");service.Start();service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));Thread.Sleep(2000);service.Refresh();
            if(service.Status!=ServiceControllerStatus.Running)throw new InvalidOperationException("Новый агент не остался запущенным");
            finish?.Invoke();
        }catch(Exception updateFailure){
            try{
                if(update.HasChanges){
                    if(probeMayBeRunning)UpdateProbeTask.Pause();
                    StopAgentAndWait(service);update.Rollback(RequireStopped);
                }
                UpdateProbeTask.Restore(taskXml);
                service.Refresh();
                if(wasRunning&&service.Status==ServiceControllerStatus.Stopped)service.Start();
            }catch(Exception rollback){
                preserve=update.HasChanges;
                throw new AggregateException("Обновление не завершено; восстановление тоже завершилось ошибкой."+(preserve?" Резервные файлы: "+Path.Combine(workspace,"previous"):""),updateFailure,rollback);
            }
            if(updateFailure is IOException io&&(io.HResult & 0xffff) is 32 or 33 or 1224)
                throw new IOException("Файлы агента заняты другим процессом. Обновление отложено; состояние файлов восстановлено или замена не начиналась. "+io.Message,io);
            throw;
        }finally{
            if(!preserve)try{if(Directory.Exists(workspace)){File.Delete(Path.Combine(workspace,"pending"));Directory.Delete(workspace,true);}}catch(IOException e){Log(e.Message);}catch(UnauthorizedAccessException e){Log(e.Message);}
        }
    }
    private static void RegisterUninstaller()
    {
        var source=Path.Combine(AppContext.BaseDirectory,"Uninstall.exe");var destination=Target;
        if(!File.Exists(source))throw new FileNotFoundException("Uninstaller package is missing.",source);
        Safe(destination);Directory.CreateDirectory(destination);File.Copy(source,Path.Combine(destination,"Uninstall.exe"),true);
        using var key=Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SysAdminAssistantAgent");
        key.SetValue("DisplayName","SysAdmin Assistant Device Agent");key.SetValue("DisplayVersion",typeof(Program).Assembly.GetName().Version!.ToString(3));key.SetValue("Publisher","321.su");key.SetValue("URLInfoAbout","https://assistant.321.su");key.SetValue("UninstallString","\""+Path.Combine(destination,"Uninstall.exe")+"\" --component agent");
    }
    private static void Uninstall()
    {
        Safe(Target);Safe(Data);
        if(!File.Exists(Path.Combine(Data,"bootstrap.json")))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нет признаков установки агента. Автоматическое удаление запрещено."));
        var installed=JsonSerializer.Deserialize<AgentSignedBootstrap>(File.ReadAllText(Path.Combine(Data,"bootstrap.json")),AgentWire.Json)??throw new InvalidDataException();_=AgentWire.Verify(installed);
        using(var serviceKey=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+Service))if(serviceKey!=null&&!string.Equals((serviceKey.GetValue("ImagePath") as string??"").Trim('"'),Path.Combine(Target,"SysAdminAssistant.Agent.exe"),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Служба указывает на другую программу. Удаление запрещено."));
        UpdateProbeTask.Remove();
        var manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)throw Error();var service=OpenService(manager,Service,0x10000|0x20|4);
        try{if(service!=IntPtr.Zero){using var controller=new ServiceController(Service);if(controller.Status!=ServiceControllerStatus.Stopped){controller.Stop();controller.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30));}JournalPermissions.Apply(Service,Data,true);if(!DeleteService(service))throw Error();}else if(Marshal.GetLastWin32Error()!=1060)throw Error();}
        finally{if(service!=IntPtr.Zero)CloseServiceHandle(service);CloseServiceHandle(manager);}
        // Refuse reparse points anywhere below the installation before recursive deletion.
        foreach(var dir in new[]{Target,Data})if(Directory.Exists(dir)){var stack=new Stack<string>();stack.Push(dir);while(stack.Count>0)foreach(var item in Directory.EnumerateFileSystemEntries(stack.Pop())){var attributes=File.GetAttributes(item);if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Ссылка внутри каталога агента"));if((attributes&FileAttributes.Directory)!=0)stack.Push(item);}Directory.Delete(dir,true);}
    }
}
