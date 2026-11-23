using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Server;

internal static class ProductUpdateEndpoints
{
    public static void Map(WebApplication app,ServerState state)
    {
        var updateGate=new SemaphoreSlim(1,1);
        var maintenance=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant Maintenance");
        app.MapGet("/api/product-updates/check",async(CancellationToken token)=>{
            var source=state.Settings.ProductUpdates;var manifest=await ProductReleases.ManifestAsync(source.Primary,source.Mirror,token);
            return Results.Json(new{Manifest=manifest,ServerVersion=typeof(ServerState).Assembly.GetName().Version!.ToString(),MaintenanceInstalled=File.Exists(Path.Combine(maintenance,"maintenance.json"))});
        });
        app.MapGet("/api/product-updates/status",()=>{
            var path=Path.Combine(maintenance,"status.json");var queue=Path.Combine(state.DirectoryPath,"product-update");var queued=Path.Combine(queue,"status.json");
            if(!File.Exists(path)||File.Exists(Path.Combine(queue,"request.json"))&&File.Exists(queued)&&File.GetLastWriteTimeUtc(queued)>File.GetLastWriteTimeUtc(path))path=queued;
            return File.Exists(path)?Results.Text(File.ReadAllText(path),"application/json"):Results.Json(new{Stage="idle",Message="No update requested.",Percent=0});
        });
        app.MapPost("/api/product-updates/server",async(HttpContext context,CancellationToken token)=>{
            var actor=(UserView)context.Items["Actor"]!;if(!actor.Administrator)return Results.StatusCode(403);
            if(!File.Exists(Path.Combine(maintenance,"maintenance.json")))throw new InvalidOperationException("Install this server release once using GUI setup to enable the maintenance task.");
            await updateGate.WaitAsync(token);
            try{
                var queue=Path.Combine(state.DirectoryPath,"product-update");Directory.CreateDirectory(queue);var request=Path.Combine(queue,"request.json");
                if(File.Exists(request)){
                    var status=Path.Combine(maintenance,"status.json");if(!File.Exists(status)||File.GetLastWriteTimeUtc(status)<File.GetLastWriteTimeUtc(request))throw new InvalidOperationException("Server update already queued.");
                    using var last=JsonDocument.Parse(File.ReadAllText(status));if(last.RootElement.GetProperty("Stage").GetString() is not ("completed" or "failed"))throw new InvalidOperationException("Server update already running.");
                    File.Delete(request);
                }
                var json=await ProductReleases.ManifestAsync(state.Settings.ProductUpdates.Primary,state.Settings.ProductUpdates.Mirror,token);
                var release=ProductReleases.Verify(json,ProductReleases.PublicKey,DateTime.UtcNow);ProductReleases.RequireUpgrade(release.Server,typeof(ServerState).Assembly.GetName().Version!,"server");
                string sqlBackup="";
                if(state.Settings.DatabaseProvider==DatabaseProvider.SqlServer){
                    await using var db=state.Factory.Create();var connection=db.Database.GetDbConnection();await connection.OpenAsync(token);
                    await using var directory=connection.CreateCommand();directory.CommandText="SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath'))";
                    var backupDirectory=Convert.ToString(await directory.ExecuteScalarAsync(token));if(string.IsNullOrWhiteSpace(backupDirectory))throw new IOException("SQL Server default backup directory is not configured.");
                    sqlBackup=backupDirectory.TrimEnd('\\','/')+"/SAA-before-update-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N")+".bak";
                    await using var command=connection.CreateCommand();command.CommandTimeout=600;command.CommandText="BACKUP DATABASE ["+connection.Database.Replace("]","]]")+"] TO DISK = @path WITH COPY_ONLY, CHECKSUM";
                    var parameter=command.CreateParameter();parameter.ParameterName="@path";parameter.Value=sqlBackup;command.Parameters.Add(parameter);await command.ExecuteNonQueryAsync(token);
                }
                File.WriteAllText(Path.Combine(queue,"status.json"),JsonSerializer.Serialize(new{Stage="queued",Message="Authorized by "+actor.Login+"; waiting for local maintenance (up to one minute).",Percent=0,TimeUtc=DateTime.UtcNow,SqlBackup=sqlBackup}));
                var temporary=Path.Combine(queue,"request.tmp");File.WriteAllText(temporary,JsonSerializer.Serialize(new{Manifest=json,RequestedUtc=DateTime.UtcNow,RequestedBy=actor.Login,SqlBackup=sqlBackup}));File.Move(temporary,request);return Results.Ok(true);
            }finally{updateGate.Release();}
        });
    }
}
