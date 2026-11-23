using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.AgentLinux;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public static class LinuxUpdateApply
{
    private const string ProgramFile="/opt/sysadminassistant-agent/agent";
    private const string TrustedBootstrap="/etc/sysadminassistant-agent/bootstrap.json";
    private const string Staging="/var/lib/sysadminassistant-agent/update-staging";
    [DllImport("libc",EntryPoint="open",SetLastError=true)] private static extern int OpenNative(string path,int flags);
    [DllImport("libc",EntryPoint="openat",SetLastError=true)] private static extern int OpenAt(int fd,string path,int flags);
    [DllImport("libc",EntryPoint="renameat",SetLastError=true)] private static extern int RenameAt(int from,string oldName,int to,string newName);
    [DllImport("libc",EntryPoint="unlinkat",SetLastError=true)] private static extern int UnlinkAt(int fd,string name,int flags);
    private static FileStream OpenInput(string path,long maximum,SafeFileHandle? directory=null)
    {
        const int readOnly=0,nonblock=0x800,noFollow=0x20000,closeOnExec=0x80000;
        int flags=readOnly|nonblock|noFollow|closeOnExec;int fd=directory==null?OpenNative(path,flags):OpenAt((int)directory.DangerousGetHandle(),Path.GetFileName(path),flags);
        if(fd<0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось безопасно открыть файл"));
        var handle=new SafeFileHandle((IntPtr)fd,true);
        try{
            var stream=new FileStream(handle,FileAccess.Read,81920,false);
            try{if(!stream.CanSeek||stream.Length<1||stream.Length>maximum)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Неверный размер файла"));return stream;}catch{stream.Dispose();throw;}
        }catch{handle.Dispose();throw;}
    }
    private static byte[] ReadLimited(string path,int maximum,SafeFileHandle? directory=null)
    {
        using var stream=OpenInput(path,maximum,directory);using var result=new MemoryStream();var block=new byte[4096];int n;
        while((n=stream.Read(block,0,Math.Min(block.Length,maximum+1-(int)result.Length)))>0){
            result.Write(block,0,n);if(result.Length>maximum)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой файл"));
        }
        return result.ToArray();
    }
    private static readonly UnixFileMode ExecutableMode=UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute|UnixFileMode.GroupRead|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherExecute;
    public static string Run()=>Apply(Staging,TrustedBootstrap,ProgramFile,typeof(LinuxUpdateApply).Assembly.GetName().Version!.ToString(),Systemctl);
    private static bool Systemctl(string operation)
    {
        var pi=new ProcessStartInfo("/usr/bin/systemctl"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        pi.ArgumentList.Add(operation);pi.ArgumentList.Add("sysadminassistant-agent.service");
        using var process=Process.Start(pi)??throw new InvalidOperationException();var a=process.StandardOutput.ReadToEndAsync();var b=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(20000)){process.Kill(true);throw new TimeoutException(global::SysAdminAssistant.Core.ProductText.Get("systemctl не ответил"));}
        return process.ExitCode==0;
    }
    private static void DurableCopy(string source,string target){using(var input=OpenInput(source,150_000_000))using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.WriteThrough)){input.CopyTo(output);output.Flush(true);}File.SetUnixFileMode(target,ExecutableMode);}
    private sealed record Transaction(string Claimed,string Version,string Sha256);
    private sealed record Receipt(string Version,string Sha256,DateTime TimeUtc);
    private static void Atomic<T>(string path,T data){var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){JsonSerializer.Serialize(f,data,AgentWire.Json);f.Flush(true);}File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead|UnixFileMode.OtherRead);File.Move(temp,path,true);LinuxUpdateQueue.SyncDirectory(Path.GetDirectoryName(path)!);}finally{if(File.Exists(temp))File.Delete(temp);}}
    private static bool Matches(string file,string hash){try{using var input=OpenInput(file,150_000_000);return Convert.ToHexString(SHA256.HashData(input)).Equals(hash,StringComparison.OrdinalIgnoreCase);}catch(IOException){return false;}}
    private static void Outcome(string folder,string state,string code,string message,string version="",string packageHash="")
    {
        var path=Path.Combine(folder,"update-result.json");AgentUpdateDelivery? prior=null;try{if(File.Exists(path))prior=JsonSerializer.Deserialize<AgentUpdateDelivery>(ReadLimited(path,10000),AgentWire.Json);}catch(Exception e)when(e is IOException or JsonException){}
        var signature=AgentWire.Hash(code+"|"+version+"|"+packageHash);var now=DateTime.UtcNow;
        var value=new AgentUpdateDelivery(state,code,message,prior?.Signature==signature?prior.FirstUtc:now,now,prior?.Signature==signature?prior.Count+1:1,signature,version,packageHash);
        Atomic(path,value);
        var historyPath=Path.Combine(folder,"update-history.ndjson");
        if(new FileInfo(historyPath) is {Exists:true,Length:>=1_048_576})File.Move(historyPath,historyPath+".previous",true);
        using var history=new FileStream(historyPath,FileMode.Append,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough);var bytes=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value,AgentWire.Json)+"\n");history.Write(bytes);history.Flush(true);
    }
    private static SafeFileHandle OpenDirectory(string path){var fd=OpenNative(path,0x10000|0x20000|0x80000);if(fd<0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Каталог пакета недоступен или является ссылкой"));return new SafeFileHandle((IntPtr)fd,true);}
    private static bool UpdateEntry(string name,string prefix)=>
        name.StartsWith(prefix,StringComparison.Ordinal)&&Guid.TryParseExact(name[prefix.Length..],"N",out _);
    private static void RemovePackage(SafeFileHandle parent,string name,SafeFileHandle? handle=null)
    {
        const int directoryFlags=0x10000|0x20000|0x80000,removeDirectory=0x200;
        var root=(int)parent.DangerousGetHandle();
        SafeFileHandle? owned=null;
        try{
            if(handle==null){
                var fd=OpenAt(root,name,directoryFlags);
                if(fd<0){UnlinkAt(root,name,0);return;} // A symlink is removed as an entry only.
                handle=owned=new SafeFileHandle((IntPtr)fd,true);
            }
            var dir=(int)handle.DangerousGetHandle();
            UnlinkAt(dir,"release.bin",0);
            UnlinkAt(dir,"release.json",0);
            UnlinkAt(root,name,removeDirectory); // Never recurse into unexpected contents.
        }finally{owned?.Dispose();}
    }
    private static void PurgeRetiredPackages(string staging,SafeFileHandle root)
    {
        var parent=Path.GetDirectoryName(staging)!;
        var name=Path.GetFileName(staging);
        foreach(var path in Directory.EnumerateFileSystemEntries(parent,name+".*")){
            var entry=Path.GetFileName(path);
            if(UpdateEntry(entry,name+".quarantine-")||UpdateEntry(entry,name+".consumed-")||UpdateEntry(entry,name+".claimed-"))RemovePackage(root,entry);
        }
    }
    public static string Apply(string staging,string bootstrapPath,string program,string current,Func<string,bool> service,DateTime? clock=null)
    {
        var folder=Path.GetDirectoryName(program)??throw new IOException();var parent=Path.GetDirectoryName(staging)??throw new IOException();
        using var queue=LinuxUpdateQueue.TryAcquire(parent);if(queue==null)return "Очередь занята загрузкой или другим обновлятором; повтор позже";
        using var root=OpenDirectory(parent);
        var backup=Path.Combine(folder,"agent.previous");var marker=Path.Combine(folder,".update-in-progress");var receiptPath=Path.Combine(folder,"update-last-success.json");
        if(File.Exists(marker)){
            Transaction? tx=null;try{tx=JsonSerializer.Deserialize<Transaction>(ReadLimited(marker,5000),AgentWire.Json);}catch(JsonException){}
            bool committed=false;try{if(tx!=null&&File.Exists(receiptPath)){var receipt=JsonSerializer.Deserialize<Receipt>(ReadLimited(receiptPath,5000),AgentWire.Json);committed=receipt?.Version==tx.Version&&receipt.Sha256==tx.Sha256&&Matches(program,tx.Sha256)&&service("is-active");}}catch(JsonException){}
            if(!committed){
                if(!File.Exists(backup))throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Прерванная установка: резервная версия отсутствует; текущая программа не заменялась повторно"));
                if(!service("stop"))throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось остановить службу для восстановления после прерывания"));
                var recovery=Path.Combine(folder,".recovery-"+Guid.NewGuid().ToString("N"));try{DurableCopy(backup,recovery);File.Move(recovery,program,true);LinuxUpdateQueue.SyncDirectory(folder);}finally{if(File.Exists(recovery))File.Delete(recovery);}
                if(!service("start")||!service("is-active"))throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось запустить предыдущую версию после прерывания; маркер сохранён"));
                Outcome(folder,"RolledBack","InterruptedUpdate","Прерванная установка: предыдущая версия восстановлена; данные и регистрация сохранены",tx?.Version??"",tx?.Sha256??"");
            }
            // Only a root-written transaction marker may name a claimed sibling.
            if(tx!=null&&Path.GetDirectoryName(tx.Claimed)==parent&&UpdateEntry(Path.GetFileName(tx.Claimed),Path.GetFileName(staging)+".claimed-"))RemovePackage(root,Path.GetFileName(tx.Claimed));
            File.Delete(marker);
        }
        PurgeRetiredPackages(staging,root);
        if(new DirectoryInfo(staging).LinkTarget!=null){var entry=Path.GetFileName(staging)+".quarantine-"+Guid.NewGuid().ToString("N");var fd=(int)root.DangerousGetHandle();if(RenameAt(fd,Path.GetFileName(staging),fd,entry)==0)RemovePackage(root,entry);Outcome(folder,"Rejected","QueueSymlink","Активная очередь была ссылкой; цель ссылки не изменялась");throw new LinuxReleaseException("QueueSymlink",global::SysAdminAssistant.Core.ProductText.Get("Очередь обновлений не должна быть ссылкой"));}
        if(!Directory.Exists(staging))return "Обновление не загружено";
        if((File.GetAttributes(staging)&FileAttributes.ReparsePoint)!=0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Очередь обновлений не должна быть ссылкой"));
        if(!Directory.EnumerateFileSystemEntries(staging).Any())return "Обновление не загружено";
        var claimed=staging+".claimed-"+Guid.NewGuid().ToString("N");Directory.Move(staging,claimed);LinuxUpdateQueue.SyncDirectory(parent);
        var manifestPath=Path.Combine(claimed,"release.json");var inputPath=Path.Combine(claimed,"release.bin");
        SafeFileHandle openedClaim;try{openedClaim=OpenDirectory(claimed);}catch(IOException){Outcome(folder,"Rejected","ClaimDirectory","Каталог пакета изменился или недоступен; программа не заменялась");RemovePackage(root,Path.GetFileName(claimed));throw new LinuxReleaseException("ClaimDirectory",global::SysAdminAssistant.Core.ProductText.Get("Каталог пакета изменился или недоступен; программа не заменялась"));}using var claimDirectory=openedClaim;
        var prepared=Path.Combine(folder,".update-"+Guid.NewGuid().ToString("N"));bool stopped=false,replaced=false,armed=false;LinuxAgentReleaseDocument? release=null;
        try{
            if(!File.Exists(manifestPath)||!File.Exists(inputPath))throw new LinuxReleaseException("IncompletePackage",global::SysAdminAssistant.Core.ProductText.Get("Очередь не содержит одновременно release.json и release.bin"));
            var signedBootstrap=JsonSerializer.Deserialize<AgentSignedBootstrap>(ReadLimited(bootstrapPath,200000),AgentWire.Json)??throw new LinuxReleaseException("BootstrapJson",global::SysAdminAssistant.Core.ProductText.Get("Доверенная конфигурация пуста"));
            var bootstrap=AgentWire.Verify(signedBootstrap);
            var signed=JsonSerializer.Deserialize<LinuxAgentSignedRelease>(ReadLimited(manifestPath,15000,claimDirectory),AgentWire.Json)??throw new LinuxReleaseException("EnvelopeJson",global::SysAdminAssistant.Core.ProductText.Get("Файл release.json не содержит конверт подписи"));
            release=LinuxAgentRelease.Authenticate(signed,bootstrap.IssuerCertificate,bootstrap.EnrollmentId);
            using(var stagedCheck=OpenInput(inputPath,release.Length,claimDirectory))LinuxAgentRelease.VerifyFile(stagedCheck,release);
            var comparison=Version.Parse(release.Version).CompareTo(Version.Parse(current));
            if(comparison<0||comparison==0&&Matches(program,release.Sha256)){
                RemovePackage(root,Path.GetFileName(claimed),claimDirectory);return "Новая версия не требуется; очередь завершена";
            }
            LinuxAgentRelease.Validate(release,clock);
            if(comparison==0)throw new LinuxReleaseException("InstalledHashMismatch",global::SysAdminAssistant.Core.ProductText.Get("Версия совпадает, но SHA256 установленного файла отличается от подписанного пакета; автоматическая замена отменена"));
            if(!File.Exists(program)||(File.GetAttributes(program)&FileAttributes.ReparsePoint)!=0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Не найден текущий агент"));
            using(var input=OpenInput(inputPath,release.Length,claimDirectory))
            using(var output=new FileStream(prepared,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.WriteThrough)){
                if(input.Length!=release.Length)throw new LinuxReleaseException("BinaryLength",global::SysAdminAssistant.Core.ProductText.Get("Размер release.bin не соответствует манифесту"));
                var chunk=new byte[81920];long remaining=release.Length;int count;while(remaining>0&&(count=input.Read(chunk,0,(int)Math.Min(chunk.Length,remaining)))>0){output.Write(chunk,0,count);remaining-=count;}
                if(remaining!=0||input.ReadByte()!=-1)throw new LinuxReleaseException("BinaryChanged",global::SysAdminAssistant.Core.ProductText.Get("Файл обновления изменился во время копирования"));output.Flush(true);
            }
            using(var check=File.OpenRead(prepared))LinuxAgentRelease.VerifyFile(check,release);File.SetUnixFileMode(prepared,ExecutableMode);
            var backupTemp=Path.Combine(folder,".backup-"+Guid.NewGuid().ToString("N"));try{DurableCopy(program,backupTemp);File.Move(backupTemp,backup,true);LinuxUpdateQueue.SyncDirectory(folder);}finally{if(File.Exists(backupTemp))File.Delete(backupTemp);}
            Atomic(marker,new Transaction(claimed,release.Version,release.Sha256));armed=true;
            stopped=true;if(!service("stop"))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось остановить агент"));
            File.Move(prepared,program,true);replaced=true;LinuxUpdateQueue.SyncDirectory(folder);
            if(!service("start"))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось запустить обновлённый агент"));Thread.Sleep(TimeSpan.FromSeconds(2));
            if(!service("is-active"))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Обновлённый агент не работает"));stopped=false;
            Atomic(receiptPath,new Receipt(release.Version,release.Sha256,DateTime.UtcNow));
            Outcome(folder,"Installed","Installed","Подпись и SHA256 проверены; агент запущен, очередь завершена",release.Version,release.Sha256);
            RemovePackage(root,Path.GetFileName(claimed),claimDirectory);File.Delete(marker);LinuxUpdateQueue.SyncDirectory(folder);armed=false;return "Агент обновлён до "+release.Version;
        }catch(Exception e){
            if(replaced){try{service("stop");}catch{}var rollback=Path.Combine(folder,".rollback-"+Guid.NewGuid().ToString("N"));try{DurableCopy(backup,rollback);File.Move(rollback,program,true);LinuxUpdateQueue.SyncDirectory(folder);}finally{if(File.Exists(rollback))File.Delete(rollback);}stopped=true;}
            bool restored=!stopped;if(stopped){try{restored=service("start")&&service("is-active");}catch{restored=false;}}
            if(armed&&restored){File.Delete(marker);armed=false;}
            var code=e is LinuxReleaseException specific?specific.Code:e is JsonException?"EnvelopeJson":e is IOException?"PackageIO":"ApplyFailed";
            var message=e is LinuxReleaseException?e.Message:e is JsonException?"Неверный JSON манифеста; пакет отклонён":e is IOException?"Ошибка чтения/записи пакета; проверьте размер, тип файлов, место и права":e is InvalidOperationException?e.Message:"Ошибка применения пакета; предыдущая версия сохранена";
            Outcome(folder,restored?"Rejected":"RollbackFailed",code,message+(restored?"; пакет выведен из активной очереди":"; восстановление службы требует проверки"),release?.Version??"",release?.Sha256??"");
            if(!armed)RemovePackage(root,Path.GetFileName(claimed),claimDirectory);
            throw new LinuxReleaseException(code,message);
        }finally{if(File.Exists(prepared))File.Delete(prepared);}
    }
}
