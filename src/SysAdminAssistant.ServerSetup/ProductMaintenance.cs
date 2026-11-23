using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.ServerSetup;

internal static class ProductMaintenance
{
    private static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant Maintenance");
    private sealed record Configuration(string Data);
    private sealed class InlineProgress(Action<int> report):IProgress<int>{public void Report(int value)=>report(value);}
    public static async Task RegisterAsync(string data)
    {
        for(var parent=new DirectoryInfo(Root);parent!=null;parent=parent.Parent)if(parent.Exists&&(parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Maintenance path contains a filesystem link.");
        Directory.CreateDirectory(Root);
        var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));
        foreach(var sid in new[]{WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid,null),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));new DirectoryInfo(Root).SetAccessControl(acl);
        var config=Path.Combine(Root,"maintenance.json");
        if(File.Exists(config)){
            var existing=JsonSerializer.Deserialize<Configuration>(File.ReadAllText(config));
            if(existing?.Data!=data)throw new InvalidOperationException("Maintenance is registered for a different server data directory.");
        }else File.WriteAllText(config,JsonSerializer.Serialize(new Configuration(data)));
        // A running helper is never overwritten. The next task invocation uses the new version.
        var target=Path.Combine(Root,"versions",typeof(ProductMaintenance).Assembly.GetName().Version!.ToString());Directory.CreateDirectory(target);
        foreach(var source in Directory.GetFiles(AppContext.BaseDirectory).Where(p=>Path.GetFileName(p)=="CLEANUP_SERVER.ps1"||p.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".deps.json",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".runtimeconfig.json",StringComparison.OrdinalIgnoreCase))){
            var destination=Path.Combine(target,Path.GetFileName(source));
            if(!File.Exists(destination))File.Copy(source,destination,false);
            else if(!SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(destination))))throw new IOException("Maintenance version exists with different contents. Publish a new version.");
        }
        var path=Convert.ToBase64String(Encoding.Unicode.GetBytes(Path.Combine(target,"SysAdminAssistant.ServerSetup.exe")));
        var script="$exe=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('"+path+"'));$a=New-ScheduledTaskAction -Execute $exe -Argument '--maintenance';$t=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1);$s=New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -StartWhenAvailable;Register-ScheduledTask -TaskName 'SysAdminAssistantMaintenance' -Action $a -Trigger $t -Settings $s -User 'SYSTEM' -RunLevel Highest -Force|Out-Null";
        await SetupEngine.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),["-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
        await ServerShell.ApplyAsync();
    }
    public static Task RunAsync()=>Task.Run(()=>{
        // Mutex ownership is thread-affine: keep the owner on this thread across asynchronous work.
        using var mutex=new Mutex(false,@"Global\SysAdminAssistantProductMaintenance");bool owner=false;
        try{try{owner=mutex.WaitOne(0);}catch(AbandonedMutexException){owner=true;}if(owner)RunLockedAsync().GetAwaiter().GetResult();}
        finally{if(owner)mutex.ReleaseMutex();}
    });
    private static async Task RunLockedAsync()
    {
        var config=JsonSerializer.Deserialize<Configuration>(File.ReadAllText(Path.Combine(Root,"maintenance.json")))??throw new InvalidDataException("Maintenance configuration missing.");
        var queue=Path.Combine(config.Data,"product-update");var request=Path.Combine(queue,"request.json");if(!File.Exists(request))return;
        var staging=Path.Combine(Root,"staging",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);string? requestId=null;
        try{
            // The privileged helper reads this service-writable queue but NEVER writes/deletes there.
            for(var d=new DirectoryInfo(queue);d!=null;d=d.Parent)if((d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Maintenance queue contains a link.");
            if((File.GetAttributes(request)&FileAttributes.ReparsePoint)!=0)throw new IOException("Maintenance request is a link.");
            using var input=new FileStream(request,FileMode.Open,FileAccess.Read,FileShare.Read);if(input.Length>150_000)throw new InvalidDataException("Update request exceeds size limit.");
            using var reader=new StreamReader(input);var json=await reader.ReadToEndAsync();var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            var consumed=Path.Combine(Root,"consumed-request.txt");
            requestId=id;
            var rejected=Path.Combine(Root,"rejected-request.txt");if(File.Exists(rejected)&&File.ReadAllText(rejected)==id)return;
            if(File.Exists(consumed)&&File.ReadAllText(consumed)==id){
                var terminal=Path.Combine(Root,"status.json");if(File.Exists(terminal)){using var state=JsonDocument.Parse(File.ReadAllText(terminal));if(state.RootElement.GetProperty("Stage").GetString() is "completed" or "failed")return;}
            }
            using var document=JsonDocument.Parse(json);var root=document.RootElement;
            var release=ProductReleases.Verify(root.GetProperty("Manifest").GetString()!,ProductReleases.PublicKey,DateTime.UtcNow);
            if(File.Exists(consumed)&&File.ReadAllText(consumed)==id){
                var last=Path.Combine(Root,"status.json");
                if(File.Exists(last)){using var state=JsonDocument.Parse(File.ReadAllText(last));if(state.RootElement.GetProperty("Stage").GetString() is "completed" or "failed")return;}
                if(await UpdateEngine.VerifyInstalledAsync(Version.Parse(release.Server.Version)))Status("completed","Previous maintenance completed; installed version and HTTPS verified.",100);
                else Status("failed","Previous maintenance was interrupted. Check service, installed version and protected backup before retrying.",0);
                return;
            }
            var requested=root.GetProperty("RequestedUtc").GetDateTime();
            if(requested.Kind!=DateTimeKind.Utc||requested>DateTime.UtcNow.AddMinutes(1)||requested<DateTime.UtcNow.AddMinutes(-15))throw new InvalidDataException("Update authorization expired. Request it again from the client.");
            var installed=await UpdateEngine.DetectAsync();
            ProductReleases.RequireUpgrade(release.Server,AssemblyName.GetAssemblyName(Path.Combine(installed.Target,"SysAdminAssistant.Server.dll")).Version!,"server");
            File.WriteAllText(consumed,id);Status("downloading","Downloading and verifying the authorized server release.",0);
            var package=Path.Combine(staging,"ServerSetup.exe");var previous=-1;
            await ProductReleases.DownloadAsync(release.Server,package,new InlineProgress(p=>{if(p!=previous){previous=p;Status("downloading","Downloading the signed server package.",p);}}),CancellationToken.None);
            Status("installing","Backup, installation and HTTPS startup verification are running.",100);
            var start=new ProcessStartInfo(package){UseShellExecute=false,WorkingDirectory=staging};start.ArgumentList.Add("--verified-server-update");
            using var process=Process.Start(start)??throw new IOException("Cannot start verified server setup.");
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(25));await process.WaitForExitAsync(deadline.Token);
            if(process.ExitCode!=0)throw new IOException("Server setup returned code "+process.ExitCode+". Review the maintenance log and protected backup.");
            if(!await UpdateEngine.VerifyInstalledAsync(Version.Parse(release.Server.Version)))throw new IOException("Installed server version does not match the signed release.");
            Status("completed","Server updated; HTTPS verified; existing data and registration retained.",100);
        }catch(Exception error){if(requestId!=null)File.WriteAllText(Path.Combine(Root,"rejected-request.txt"),requestId);Status("failed",error.Message,0);}
        finally{try{Directory.Delete(staging,true);}catch(IOException){/* Do not kill an in-flight installation; retain staging for recovery. */}}
    }
    private static void Status(string stage,string message,int percent)
    {
        var target=Path.Combine(Root,"status.json");if(File.Exists(target)&&(File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0)throw new IOException("Maintenance status path is a link.");
        var temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(new{Stage=stage,Message=message,Percent=percent,TimeUtc=DateTime.UtcNow}));File.Move(temporary,target,true);
    }
}
