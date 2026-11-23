using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;
internal static class LocalAdministration
{
    public static Task RunAsync(string directory,ServerConfiguration config,string[] args,bool admin)
    {
        if(!args.Contains("--local-admin-backup"))return RunCoreAsync(directory,config,args,admin);
        return Task.Run(()=>{using var mutex=new Mutex(false,@"Global\SysAdminAssistantProductMaintenance");bool held=false;try{try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Server maintenance is already running.");RunCoreAsync(directory,config,args,admin).GetAwaiter().GetResult();}finally{if(held)mutex.ReleaseMutex();}});
    }
    private static async Task RunCoreAsync(string directory,ServerConfiguration config,string[] args,bool admin)
    {
        if(!admin)throw new UnauthorizedAccessException("Local server administration requires Windows administrator rights.");
        var settings=SettingsFile.Load(Path.Combine(directory,"settings.json"));await using var db=new DatabaseFactory(settings).Create();
        if(args.Contains("--local-admin-status")){
            bool database=false;string databaseError="";try{database=await db.Database.CanConnectAsync();}catch{databaseError="Database connection check failed; inspect the server log.";}
            string pin="",expires="";try{using var cert=X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(directory,"server.pfx"),LocalSecret.Unprotect(config.ProtectedCertificatePassword),X509KeyStorageFlags.EphemeralKeySet);pin=cert.GetCertHashString(HashAlgorithmName.SHA256);expires=cert.NotAfter.ToUniversalTime().ToString("O");}catch{expires="Certificate unavailable";}
            // Deliberate allowlist: never serialize settings/configuration or connection strings.
            Console.WriteLine(JsonSerializer.Serialize(new{Version=typeof(LocalAdministration).Assembly.GetName().Version!.ToString(),Url=config.Url,Networks=config.Networks,Provider=settings.DatabaseProvider.ToString(),DatabaseAvailable=database,DatabaseError=databaseError,CertificateSha256=pin,CertificateExpiresUtc=expires,CheckedUtc=DateTime.UtcNow}));return;
        }
        var i=Array.IndexOf(args,"--local-admin-backup");if(i<0||i+1>=args.Length)throw new ArgumentException("Backup destination is required.");
        if(settings.DatabaseProvider==DatabaseProvider.SqlServer){
            var connection=db.Database.GetDbConnection();await connection.OpenAsync();await using var location=connection.CreateCommand();location.CommandText="SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath'))";var folder=Convert.ToString(await location.ExecuteScalarAsync());if(string.IsNullOrWhiteSpace(folder))throw new IOException("SQL Server default backup directory is not configured.");
            var path=folder.TrimEnd('\\','/')+"/SAA-manual-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N")+".bak";
            await using var command=connection.CreateCommand();command.CommandTimeout=600;command.CommandText="BACKUP DATABASE ["+connection.Database.Replace("]","]]")+"] TO DISK = @path WITH COPY_ONLY, CHECKSUM";var parameter=command.CreateParameter();parameter.ParameterName="@path";parameter.Value=path;command.Parameters.Add(parameter);await command.ExecuteNonQueryAsync();
            Console.WriteLine(JsonSerializer.Serialize(new{Provider="SqlServer",Path=path,Location="SQL Server host",CompletedUtc=DateTime.UtcNow}));return;
        }
        var destination=Path.GetFullPath(args[i+1]);if(Path.GetExtension(destination)!=".db"||destination.StartsWith(@"\\"))throw new ArgumentException("Select a new local .db file.");
        for(var parent=new DirectoryInfo(Path.GetDirectoryName(destination)!);parent!=null;parent=parent.Parent)if(parent.Exists&&(parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Backup destination contains a filesystem link.");
        // Complete the SQLite online backup in protected staging, then copy through a pinned new output handle.
        var staging=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant Maintenance","backups");
        for(var parent=new DirectoryInfo(staging);parent!=null;parent=parent.Parent)if(parent.Exists&&(parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Backup staging contains a filesystem link.");
        Directory.CreateDirectory(staging);var local=Path.Combine(staging,"backup-"+Guid.NewGuid().ToString("N")+".db");
        try{
            await db.Database.OpenConnectionAsync();using(var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=local,Pooling=false}.ToString())){await target.OpenAsync();((SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(target);}
            await using var input=new FileStream(local,FileMode.Open,FileAccess.Read,FileShare.Read);await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);await input.CopyToAsync(output);await output.FlushAsync();
        }finally{if(File.Exists(local))File.Delete(local);}
        Console.WriteLine(JsonSerializer.Serialize(new{Provider="SQLite",Path=destination,Location="Local server",CompletedUtc=DateTime.UtcNow}));
    }
}
