using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Uninstall;
internal sealed class AppSettings {public DatabaseProvider DatabaseProvider {get;set;}public string SqlitePath {get;set;}="";public string SqlConnectionString {get;set;}="";public string ProtectedSqlPassword {get;set;}="";}
internal sealed record RemovalPlan(string Kind,string Target,string Data,string Service,string Image,string SettingsHash,AppSettings? Settings,string Account,bool DeleteDatabase,bool DeleteData,bool DeleteAccount,bool English,string Summary);
internal static class RemovalEngine
{
    private static string T(bool en,string ru,string english)=>en?english:ru;
    private static string Programs=>Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static string DataRoot=>Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private const string UninstallRegistry=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";
    private static string ServiceName(string kind)=>kind switch{"server"=>"SysAdminAssistantServer","agent"=>"SysAdminAssistantAgent","workstation"=>"SysAdminAssistantWorkstation",_=>""};
    private static string ServiceImage(string service){using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+service);return key?.GetValue("ImagePath") as string??"";}
    private static (string Target,string Data) ServerPaths()
    {
        var image=ServiceImage(ServiceName("server"));
        if(image.Length==0)return("",Path.Combine(DataRoot,"SysAdminAssistantServer"));
        var match=Regex.Match(image,"^\"(?<exe>[^\"]+SysAdminAssistant\\.Server\\.exe)\"\\s+--data-dir\\s+\"(?<data>[^\"]+)\"\\s*$",RegexOptions.IgnoreCase);
        if(!match.Success)throw new IOException("Неизвестная командная строка службы / Unrecognized service command line.");
        return(Path.GetDirectoryName(match.Groups["exe"].Value)!,match.Groups["data"].Value);
    }
    public static string DefaultData(string kind)=>kind switch{"server"=>ServerPaths().Data,"client"=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SysAdminAssistantClient"),"agent"=>Path.Combine(DataRoot,"SysAdminAssistantAgent"),_=>Path.Combine(DataRoot,"SysAdminAssistantWorkstation")};
    private static string Target(string kind)=>kind switch{"server"=>ServerPaths().Target,"client"=>Path.Combine(Programs,"SysAdmin Assistant"),"agent"=>Path.Combine(Programs,"SysAdmin Assistant Agent"),_=>Path.Combine(Programs,"SysAdmin Assistant Workstation")};
    public static string Inspect(string kind,string data,bool en)=>Plan(kind,data,false,false,false,en).Summary;
    internal static RemovalPlan Plan(string kind,string data,bool db,bool local,bool account,bool en)
    {
        if(kind is not ("server" or "client" or "agent" or "workstation"))throw new ArgumentException("Unknown component.");
        if(kind!="server")data=DefaultData(kind);
        var service=ServiceName(kind);var image=service.Length>0?ServiceImage(service):"";var target=Target(kind);AppSettings? settings=null;string hash="",user="";
        if(kind=="server"){
            data=Safe(data);
            var path=Path.Combine(data,"settings.json");var config=Path.Combine(data,"server.json");
            if(!File.Exists(path)||!File.Exists(config))throw new IOException(T(en,"Выберите папку с settings.json и server.json сервера.","Select the folder containing the server's settings.json and server.json."));
            settings=JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path),new JsonSerializerOptions{Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}})??throw new InvalidDataException();hash=Hash(path);
            using var json=JsonDocument.Parse(File.ReadAllText(config));if(json.RootElement.TryGetProperty("ServiceAccountName",out var value))user=value.GetString()??"";
            if(image.Length>0&&!string.Equals(Safe(ServerPaths().Data),data,StringComparison.OrdinalIgnoreCase))throw new IOException(T(en,"Служба использует другую папку данных. Её удаление не разрешено этим выбором.","The service uses another data folder. This selection cannot remove that installation."));
            if(local&&!db)throw new IOException(T(en,"Сохраните папку данных вместе с настройками базы или отдельно подтвердите удаление базы.","Keep the data folder and database configuration, or explicitly select database deletion."));
            if(db){
                ValidateDatabase(settings,en);
                foreach(var other in Directory.EnumerateDirectories(DataRoot,"SysAdminAssistantServer*")){
                    if(string.Equals(Path.GetFullPath(other).TrimEnd('\\'),data,StringComparison.OrdinalIgnoreCase))continue;
                    var otherPath=Path.Combine(other,"settings.json");if(!File.Exists(otherPath))continue;
                    var otherSettings=JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(otherPath),new JsonSerializerOptions{Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}});
                    if(otherSettings==null||otherSettings.DatabaseProvider!=settings.DatabaseProvider)continue;
                    bool same;
                    if(settings.DatabaseProvider==DatabaseProvider.SqlServer){var a=new SqlConnectionStringBuilder(settings.SqlConnectionString);var b=new SqlConnectionStringBuilder(otherSettings.SqlConnectionString);same=a.DataSource.Equals(b.DataSource,StringComparison.OrdinalIgnoreCase)&&a.InitialCatalog.Equals(b.InitialCatalog,StringComparison.OrdinalIgnoreCase);}
                    else same=Safe(settings.SqlitePath).Equals(Safe(otherSettings.SqlitePath),StringComparison.OrdinalIgnoreCase);
                    if(same)throw new IOException(T(en,"База используется другой найденной конфигурацией. Сохраните её: ","The database is used by another detected configuration. Keep it: ")+other);
                }
            }
        }
        if(target.Length>0){Safe(target);CheckTree(target);}
        if(local)CheckTree(data);
        if(kind is "agent" or "workstation"){
            var expected=Path.Combine(target,"SysAdminAssistant.Agent.exe");
            if(image.Length>0&&!image.StartsWith('"'+expected+'"',StringComparison.OrdinalIgnoreCase))throw new IOException(T(en,"Служба указывает на другую программу.","The service points to another application."));
        }
        if(kind=="client"&&!File.Exists(Path.Combine(target,"SysAdminAssistant.exe")))throw new IOException(T(en,"Клиент не найден в стандартной папке.","The desktop client was not found in its standard folder."));
        if(db&&settings?.DatabaseProvider==DatabaseProvider.SqlServer){using var connection=SqlConnection(settings);connection.Open();CheckSql(connection);}
        if(local&&kind=="server"){
            var selected=settings?.DatabaseProvider==DatabaseProvider.SQLite?Safe(settings.SqlitePath):"";
            foreach(var file in Directory.EnumerateFiles(data,"*",SearchOption.AllDirectories).Where(f=>new[]{".db",".sqlite",".sqlite3",".mdf",".ldf",".bak"}.Contains(Path.GetExtension(f).ToLowerInvariant())))
                if(!string.Equals(Safe(file),selected,StringComparison.OrdinalIgnoreCase))throw new IOException(T(en,"В папке найдена дополнительная база или резервная копия. Сохраните её отдельно: ","An additional database or backup exists. Preserve it separately: ")+file);
        }
        var database=settings==null?"":settings.DatabaseProvider==DatabaseProvider.SqlServer?new SqlConnectionStringBuilder(settings.SqlConnectionString).InitialCatalog:settings.SqlitePath;
        var summary=T(en,"Компонент: ","Component: ")+kind+"\n"+T(en,"Программа: ","Program: ")+target+"\n"+T(en,"Данные: ","Data: ")+data;
        if(settings!=null)summary+="\n"+T(en,"База: ","Database: ")+database+"\n"+T(en,"База будет: ","Database will be: ")+T(en,db?"УДАЛЕНА":"сохранена",db?"DELETED":"retained");
        summary+="\n"+T(en,"Локальные данные будут: ","Local data will be: ")+T(en,local?"УДАЛЕНЫ":"сохранены",local?"DELETED":"retained");
        if(account)ValidateAccount(user,en);
        return new(kind,target,data,service,image,hash,settings,user,db,local,account,en,summary);
    }
    internal static string Safe(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)||path.StartsWith(@"\\"))throw new IOException("Нужен абсолютный локальный путь / An absolute local path is required.");
        var full=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var blocked=new[]{Path.GetPathRoot(full),Programs,DataRoot,Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)};
        if(blocked.Any(p=>!string.IsNullOrEmpty(p)&&string.Equals(full,p.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)))throw new IOException("Защищённая папка / Protected folder: "+full);
        var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if(full.StartsWith(windows+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Системная папка / System folder.");
        for(var parent=new DirectoryInfo(full);parent!=null;parent=parent.Parent)if(parent.Exists&&(parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылка в пути / Filesystem link: "+parent.FullName);
        return UninstallPolicy.PathForRemoval(full,blocked.Where(p=>p!=null).Cast<string>());
    }
    private static void CheckTree(string path){Safe(path);UninstallPolicy.CheckTree(path,[Programs,DataRoot,Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)]);}
    private static void DeleteTree(string path){if(string.IsNullOrEmpty(path)||!Directory.Exists(path))return;CheckTree(path);Directory.Delete(path,true);}
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Unprotect(string value){var machine=value.StartsWith("machine:",StringComparison.Ordinal);return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(machine?value[8..]:value),Encoding.UTF8.GetBytes("SysAdminAssistant.CSharp.Settings.v1"),machine?DataProtectionScope.LocalMachine:DataProtectionScope.CurrentUser));}
    private static SqlConnection SqlConnection(AppSettings settings){var b=new SqlConnectionStringBuilder(settings.SqlConnectionString){ConnectTimeout=10,Pooling=false};if(!b.IntegratedSecurity&&settings.ProtectedSqlPassword.Length>0)b.Password=Unprotect(settings.ProtectedSqlPassword);return new(b.ConnectionString);}
    private static void CheckSql(SqlConnection connection){using var command=connection.CreateCommand();command.CommandText="SELECT CASE WHEN OBJECT_ID(N'dbo.sa2_SchemaInfo',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.sa2_Device',N'U') IS NOT NULL THEN 1 ELSE 0 END";if(Convert.ToInt32(command.ExecuteScalar())!=1)throw new IOException("База не подтверждена как SysAdmin Assistant / Database is not verified as SysAdmin Assistant.");}
    private static void ValidateDatabase(AppSettings settings,bool en)
    {
        if(settings.DatabaseProvider==DatabaseProvider.SqlServer){var b=new SqlConnectionStringBuilder(settings.SqlConnectionString);_=UninstallPolicy.DatabaseName(b.InitialCatalog);}
        else{SQLitePCL.Batteries_V2.Init();var path=Safe(settings.SqlitePath);using var connection=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=path,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());connection.Open();using var command=connection.CreateCommand();command.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('sa2_SchemaInfo','sa2_Device')";if(Convert.ToInt64(command.ExecuteScalar())!=2)throw new IOException(T(en,"Не подтверждена принадлежность базы SysAdmin Assistant.","The database could not be verified as a SysAdmin Assistant database."));}
    }
    private static void DeleteDatabase(RemovalPlan plan)
    {
        var settings=plan.Settings!;ValidateDatabase(settings,plan.English);
        if(settings.DatabaseProvider==DatabaseProvider.SqlServer){using(var verify=SqlConnection(settings)){verify.Open();CheckSql(verify);}using var connection=SqlConnection(settings);var b=new SqlConnectionStringBuilder(connection.ConnectionString);var name=b.InitialCatalog;b.InitialCatalog="master";connection.ConnectionString=b.ConnectionString;connection.Open();using var command=connection.CreateCommand();var quoted=UninstallPolicy.DatabaseName(name);command.CommandTimeout=60;command.CommandText="ALTER DATABASE "+quoted+" SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE "+quoted+";";command.ExecuteNonQuery();}
        else{foreach(var suffix in new[]{"","-wal","-shm"}){var file=settings.SqlitePath+suffix;Safe(file);if(File.Exists(file)){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Database file is a link.");File.Delete(file);}}}
    }
    private static void Run(string exe,params string[] args){var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in args)start.ArgumentList.Add(arg);using var process=Process.Start(start)??throw new IOException("Could not start "+exe);var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();if(Path.GetFileName(exe).Equals("msiexec.exe",StringComparison.OrdinalIgnoreCase))process.WaitForExit();else if(!process.WaitForExit(60000))throw new System.TimeoutException("Операция не завершилась / Operation timed out.");Task.WaitAll(output,errors);if(process.ExitCode is not (0 or 3010))throw new IOException(exe+": "+output.Result+errors.Result);}
    private static void StopService(string service){if(ServiceImage(service).Length==0)return;using var controller=new ServiceController(service);if(controller.Status!=ServiceControllerStatus.Stopped){controller.Stop();controller.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(45));}}
    private static void RemoveTask(string name,string expectedRoot)
    {
        dynamic? scheduler=null,folder=null,task=null;
        try{scheduler=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;scheduler.Connect();folder=scheduler.GetFolder(@"\");try{task=folder.GetTask(name);}catch(Exception e)when(InstallerTaskErrors.Missing(e)){return;}
            dynamic definition=task.Definition;if((int)definition.Actions.Count!=1||(int)definition.Actions[1].Type!=0||!((string)definition.Actions[1].Path).StartsWith(expectedRoot+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Имя задачи занято другой программой / Task name belongs to another application: "+name);
            task.Enabled=false;if((int)task.State==4)throw new IOException("Идёт обслуживание, повторите позже / Maintenance is running; retry later.");folder.DeleteTask(name,0);
        }finally{foreach(object? item in new object?[]{task,folder,scheduler})if(item!=null&&Marshal.IsComObject(item))Marshal.ReleaseComObject(item);}
    }
    private static void RemoveFirewall(){dynamic? policy=null;try{policy=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;policy.Rules.Remove("SysAdminAssistantServer-LAN");}finally{if(policy!=null&&Marshal.IsComObject(policy))Marshal.ReleaseComObject(policy);}}
    internal static void Remove(RemovalPlan plan)
    {
        using var mutex=new Mutex(false,plan.Kind switch{"server"=>@"Global\SysAdminAssistantProductMaintenance","client"=>@"Global\SysAdminAssistantClientInstall",_=>@"Global\SysAdminAssistantAgentMaintenance"});bool held=false;
        try{try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException(T(plan.English,"Идёт установка или обслуживание. Повторите позже.","Installation or maintenance is running. Retry later."));
            if(plan.Service.Length>0&&ServiceImage(plan.Service)!=plan.Image)throw new IOException("Конфигурация службы изменилась / Service configuration changed.");
            if(plan.Settings!=null&&Hash(Path.Combine(plan.Data,"settings.json"))!=plan.SettingsHash)throw new IOException("Настройки изменились / Settings changed.");
            if(plan.Kind=="workstation"){RemoveWorkstation();if(plan.DeleteData){DeleteTree(plan.Data);Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\SysAdminAssistant\WorkstationIdentity",false);}return;}
            if(plan.Kind=="client"){
                foreach(var process in Process.GetProcessesByName("SysAdminAssistant")){using(process){if(!process.HasExited){try{using var signal=System.Threading.EventWaitHandleAcl.OpenExisting(@"Local\SAA.Client.Close."+process.Id,System.Security.AccessControl.EventWaitHandleRights.Modify);signal.Set();}catch(Exception e)when(e is WaitHandleCannotBeOpenedException or UnauthorizedAccessException){process.CloseMainWindow();}if(!process.WaitForExit(10000))throw new IOException(T(plan.English,"Выберите «Выход» в трее клиента и повторите удаление.","Choose Exit in the client's tray menu, then retry removal."));}}}
                if(Process.GetProcessesByName("SysAdminAssistant.ProductMaintenance").Length>0)throw new IOException("Client update is running.");
            }
            if(plan.Kind=="server")RemoveTask("SysAdminAssistantMaintenance",Path.Combine(Programs,"SysAdmin Assistant Maintenance"));
            if(plan.Kind=="agent"){RemoveTask("SysAdminAssistantAgentAutoUpdate",plan.Target);RemoveTask("SysAdminAssistantAgentUpdateRead",plan.Target);}
            if(plan.Service.Length>0)StopService(plan.Service);
            if(plan.DeleteDatabase)DeleteDatabase(plan);
            if(plan.Kind=="agent"&&plan.Image.Length>0)AgentSetup.JournalPermissions.Apply(plan.Service,plan.Data,true);
            if(plan.Service.Length>0&&plan.Image.Length>0){Run(Path.Combine(Environment.SystemDirectory,"sc.exe"),"delete",plan.Service);for(int i=0;i<30&&ServiceImage(plan.Service).Length>0;i++)Thread.Sleep(500);if(ServiceImage(plan.Service).Length>0)throw new IOException(T(plan.English,"Служба помечена для удаления. Закройте services.msc и повторите после перезагрузки.","The service is marked for deletion. Close services.msc and retry after restarting Windows."));}
            if(plan.Kind=="server")RemoveFirewall();
            DeleteTree(plan.Target);if(plan.DeleteData)DeleteTree(plan.Data);
            if(plan.DeleteAccount)DeleteAccount(plan.Account,plan.English);
            var label=plan.Kind=="server"?"SysAdmin Assistant Server":"SysAdmin Assistant";
            if(plan.Kind is "server" or "client")foreach(var folder in new[]{Environment.SpecialFolder.CommonPrograms,Environment.SpecialFolder.CommonDesktopDirectory,Environment.SpecialFolder.CommonStartup})foreach(var title in new[]{label,label+" — Удаление",label+" — Uninstall"}){var link=Path.Combine(Environment.GetFolderPath(folder),title+".lnk");if(File.Exists(link))File.Delete(link);}
            if(plan.Kind=="server")DeleteTree(Path.Combine(Programs,"SysAdmin Assistant Maintenance"));
            if(plan.Kind=="client")DeleteTree(Path.Combine(Programs,"SysAdmin Assistant Client Maintenance"));
            Registry.LocalMachine.DeleteSubKeyTree(UninstallRegistry+(plan.Kind switch{"server"=>"SysAdminAssistantServer","client"=>"SysAdminAssistantClient",_=>"SysAdminAssistantAgent"}),false);
        }finally{if(held)mutex.ReleaseMutex();}
    }
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]private static extern uint MsiEnumRelatedProducts(string code,uint reserved,uint index,StringBuilder product);
    private static void RemoveWorkstation(){var code=new StringBuilder(39);var products=new List<string>();for(uint i=0;;i++){var result=MsiEnumRelatedProducts("{CA2D822A-C915-4374-A05F-39AADE147BD4}",0,i,code);if(result==259)break;if(result!=0)throw new System.ComponentModel.Win32Exception((int)result);products.Add(code.ToString());}if(products.Count==0)throw new IOException("MSI-пакет агента не найден / Workstation MSI registration was not found.");foreach(var product in products)Run(Path.Combine(Environment.SystemDirectory,"msiexec.exe"),"/x",product,"/qn","/norestart");}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct UserInfo10{[MarshalAs(UnmanagedType.LPWStr)]public string Name;[MarshalAs(UnmanagedType.LPWStr)]public string Comment;[MarshalAs(UnmanagedType.LPWStr)]public string UserComment;[MarshalAs(UnmanagedType.LPWStr)]public string FullName;}
    [DllImport("netapi32.dll",CharSet=CharSet.Unicode)]private static extern int NetUserGetInfo(string? server,string name,int level,out IntPtr buffer);
    [DllImport("netapi32.dll")]private static extern int NetApiBufferFree(IntPtr buffer);
    [DllImport("netapi32.dll",CharSet=CharSet.Unicode)]private static extern int NetUserDel(string? server,string name);
    private static string ValidateAccount(string account,bool en){var name=account.Split('\\').Last();if(!Regex.IsMatch(name,@"^SysAdminSvc(?:04|05|06)?$",RegexOptions.IgnoreCase))throw new IOException(T(en,"Учётная запись не принадлежит установщику.","The account is not owned by this installer."));var result=NetUserGetInfo(null,name,10,out var buffer);if(result==2221)return name;if(result!=0)throw new System.ComponentModel.Win32Exception(result);try{if(Marshal.PtrToStructure<UserInfo10>(buffer).Comment!="SysAdmin Assistant managed service account")throw new IOException("Неизвестный владелец учётной записи / Unknown account ownership.");}finally{NetApiBufferFree(buffer);}return name;}
    private static void DeleteAccount(string account,bool en){var name=ValidateAccount(account,en);using var services=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");foreach(var service in services?.GetSubKeyNames()??[]){using var key=services!.OpenSubKey(service);if((key?.GetValue("ObjectName") as string??"").EndsWith("\\"+name,StringComparison.OrdinalIgnoreCase))throw new IOException(T(en,"Учётная запись ещё используется службой.","The account is still used by a service."));}var result=NetUserDel(null,name);if(result is not (0 or 2221))throw new System.ComponentModel.Win32Exception(result);}
}
