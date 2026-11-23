using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace SysAdminAssistant.Infrastructure;
// Table/column names and DDL below are a closed constant list, never user input.
#pragma warning disable EF1002
public static class BindingSchema
{
    public static async Task EnsureAsync(AssistantDb db,CancellationToken token=default)
    {
        var columns=new[]{
            ("sa2_Device","IsKscServer","INTEGER NOT NULL DEFAULT 0","bit NOT NULL DEFAULT 0"),
            ("sa2_Device","ConnectionsJson","TEXT NOT NULL DEFAULT ''","nvarchar(max) NOT NULL DEFAULT ''"),
            ("sa2_CredentialEntry","ServiceId","INTEGER NULL","int NULL"),
            ("sa2_Device","MailUpdatesJson","TEXT NOT NULL DEFAULT ''","nvarchar(max) NOT NULL DEFAULT ''"),
            ("sa2_BindingRule","MailCategory","TEXT NOT NULL DEFAULT 'Report'","nvarchar(max) NOT NULL DEFAULT 'Report'"),
            ("sa2_BindingRule","UpdateProduct","TEXT NOT NULL DEFAULT ''","nvarchar(max) NOT NULL DEFAULT ''"),
            ("sa2_ReportEvent","MailCategory","TEXT NOT NULL DEFAULT 'Report'","nvarchar(max) NOT NULL DEFAULT 'Report'"),
            ("sa2_ReportEvent","UpdateProduct","TEXT NOT NULL DEFAULT ''","nvarchar(max) NOT NULL DEFAULT ''"),
            ("sa2_ReportEvent","UpdateVersion","TEXT NOT NULL DEFAULT ''","nvarchar(max) NOT NULL DEFAULT ''"),
            ("sa2_ReportEvent","UpdateKey","TEXT NOT NULL DEFAULT ''","nvarchar(max) NOT NULL DEFAULT ''"),
            ("sa2_KeywordProfile","UpdateSubjectTerms","TEXT NOT NULL DEFAULT ''","nvarchar(max) NOT NULL DEFAULT ''"),
            ("sa2_KeywordProfile","UpdateBodyTerms","TEXT NOT NULL DEFAULT ''","nvarchar(max) NOT NULL DEFAULT ''"),
            ("sa2_KeywordProfile","UpdateProduct","TEXT NOT NULL DEFAULT ''","nvarchar(max) NOT NULL DEFAULT ''"),
            ("sa2_KeywordProfile","UpdateMatchAll","INTEGER NOT NULL DEFAULT 0","bit NOT NULL DEFAULT 0"),
            ("sa2_KeywordProfile","UpdateDictionaryVersion","INTEGER NOT NULL DEFAULT 0","int NOT NULL DEFAULT 0"),
            ("sa2_Device","AgentLastSeen","TEXT NULL","datetime2 NULL"),
            ("sa2_Device","OsUpdatesJson","TEXT NOT NULL DEFAULT ''","nvarchar(max) NOT NULL DEFAULT ''"),
            ("sa2_Device","AgentHealth","TEXT NOT NULL DEFAULT ''","nvarchar(50) NOT NULL DEFAULT ''"),
            ("sa2_Device","AgentSummary","TEXT NOT NULL DEFAULT ''","nvarchar(max) NOT NULL DEFAULT ''"),
            ("sa2_ConnectivityRecord","Lan","TEXT NOT NULL DEFAULT 'DISABLED'","nvarchar(max) NOT NULL DEFAULT 'DISABLED'"),
            ("sa2_ConnectivityRecord","ProbeResultsJson","TEXT NOT NULL DEFAULT '[]'","nvarchar(max) NOT NULL DEFAULT '[]'"),
            ("sa2_SqlBackupJob","EarlyMinutes","INTEGER NULL","int NULL"),
            ("sa2_BindingRule","SubjectMatchAll","INTEGER NOT NULL DEFAULT 1","bit NOT NULL DEFAULT 1"),
            ("sa2_Device","MailSystem","TEXT NOT NULL DEFAULT ''","nvarchar(100) NOT NULL DEFAULT ''"),
            ("sa2_SqlBackupJob","BackupSystem","TEXT NOT NULL DEFAULT ''","nvarchar(100) NOT NULL DEFAULT ''"),
            ("sa2_BindingRule","BodyMatchAll","INTEGER NOT NULL DEFAULT 1","bit NOT NULL DEFAULT 1"),
            ("sa2_SqlBackupJob","CompletionTimes","TEXT NOT NULL DEFAULT ''","nvarchar(400) NOT NULL DEFAULT ''"),
            ("sa2_ReportEvent","BindingLocked","INTEGER NOT NULL DEFAULT 0","bit NOT NULL DEFAULT 0"),
            ("sa2_ReportEvent","ScheduledSlotMinutes","INTEGER NULL","int NULL")
        };
        foreach(var (table,name,sqlite,sql) in columns) {
            if(db.Database.IsSqlServer()) {
                await db.Database.ExecuteSqlRawAsync($"IF OBJECT_ID(N'dbo.{table}',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.{table}',N'{name}') IS NULL ALTER TABLE [{table}] ADD [{name}] {sql};",token);
            } else {
                var connection=db.Database.GetDbConnection();var opened=connection.State!=ConnectionState.Open;
                if(opened)await connection.OpenAsync(token);
                bool found=false,exists=false;
                try {
                    await using var command=connection.CreateCommand();command.Transaction=db.Database.CurrentTransaction?.GetDbTransaction();
                    command.CommandText=$"PRAGMA table_info([{table}]);";
                    await using var reader=await command.ExecuteReaderAsync(token);
                    while(await reader.ReadAsync(token)){exists=true;if(reader.GetString(1)==name)found=true;}
                } finally {if(opened)await connection.CloseAsync();}
                if(exists&&!found)await db.Database.ExecuteSqlRawAsync($"ALTER TABLE [{table}] ADD COLUMN [{name}] {sqlite};",token);
            }
        }
    }
}

#pragma warning restore EF1002
