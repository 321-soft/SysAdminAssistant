using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
using SysAdminAssistant.Server;

internal static class MailSourceStorageSpecs
{
    public static async Task Run(Action<string,bool> check)
    {
        var folder=Directory.CreateTempSubdirectory("saa-mail-source-");
        try
        {
            SettingsFile.Save(new AppSettings{SqlitePath=Path.Combine(folder.FullName,"test.db")},Path.Combine(folder.FullName,"settings.json"));
            var server=new ServerState(folder.FullName);
            await server.InitializeAsync(default);
            var factory=server.Factory;
            await using(var legacy=factory.Create())await legacy.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_KeywordProfile DROP COLUMN EventRulesJson");
            await server.InitializeAsync(default);
            await using var db=factory.Create();
            var common=await db.Profiles.AsNoTracking().SingleAsync(x=>x.Provider=="Любой");
            var stamp=RemoteData.EntityStamp(common);
            var source=new KeywordProfile{
                Provider="win-acme",BodyTerms="Sent by win-acme version",
                EventRulesJson=MailSources.WriteEvents([new("Test","","Test message",true,Severity.Info,BackupResult.Event,"")])
            };
            RemoteChange Change(KeywordProfile profile,string state,string hash)=>new("profiles",state,profile.Id,hash,JsonSerializer.SerializeToElement(profile,RemoteData.Json));
            await ServerChanges.ApplyAsync(factory,[Change(source,"Added","")],default);
            check("Server creates an imported mail source as a separate profile",await db.Profiles.AnyAsync(p=>p.Provider=="win-acme"&&p.EventRulesJson==source.EventRulesJson));
            check("Schema 7 server adds the mail rule column to an existing database",await db.Profiles.AnyAsync(p=>p.Provider=="Любой"&&p.EventRulesJson==""));
            var pending=new ReportEvent{Provider="Неизвестно",IsPending=true};
            db.Reports.Add(pending);await db.SaveChangesAsync();
            db.Pending.Add(new PendingReport{EventId=pending.Id,Subject="Pending subject",Body=new string('x',100_000)});await db.SaveChangesAsync();
            var snapshot=await server.SnapshotAsync(default);
            var row=snapshot.Rows["pending"].Single().Deserialize<PendingReport>(RemoteData.Json)!;
            check("Client snapshot includes pending metadata and keeps large mail bodies on server",row.Subject=="Pending subject"&&row.Body==""&&await db.Pending.AsNoTracking().AnyAsync(x=>x.EventId==pending.Id&&x.Body.Length==100_000));
            common.Provider="Renamed common profile";
            bool refused=false;
            try{await ServerChanges.ApplyAsync(factory,[Change(common,"Modified",stamp)],default);}
            catch(ArgumentException){refused=true;}
            check("Server still protects the existing common mail profile from renaming",refused && await db.Profiles.AnyAsync(p=>p.Id==common.Id&&p.Provider=="Любой"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            folder.Delete(true);
        }
    }
}
