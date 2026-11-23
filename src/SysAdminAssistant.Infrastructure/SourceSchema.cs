using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using Microsoft.EntityFrameworkCore.Storage;

namespace SysAdminAssistant.Infrastructure;

public static class SourceSchema
{
    // DDL identifiers and declarations below are a closed, compile-time list; no external input.
#pragma warning disable EF1002
    public static async Task UpgradeAsync(AssistantDb db, CancellationToken token)
    {
        var columns = new (string Name, string Sqlite, string SqlServer)[]
        {
            ("AdditionalEventRulesJson", "TEXT NOT NULL DEFAULT ''", "nvarchar(max) NOT NULL DEFAULT N''"),
            ("EventRulesJson", "TEXT NOT NULL DEFAULT ''", "nvarchar(max) NOT NULL DEFAULT N''"),
            ("SenderTerms", "TEXT NOT NULL DEFAULT ''", "nvarchar(max) NOT NULL DEFAULT N''"),
            ("SubjectTerms", "TEXT NOT NULL DEFAULT ''", "nvarchar(max) NOT NULL DEFAULT N''"),
            ("BodyTerms", "TEXT NOT NULL DEFAULT ''", "nvarchar(max) NOT NULL DEFAULT N''"),
            ("MatchAllSourceFields", "INTEGER NOT NULL DEFAULT 1", "bit NOT NULL DEFAULT 1"),
            ("ResultScope", $"INTEGER NOT NULL DEFAULT {(int)SearchScope.Subject}", $"int NOT NULL DEFAULT {(int)SearchScope.Subject}")
        };
        // Use the enum value: Both=0, Subject=1, Body=2. Do not change existing columns or user choices.
        foreach (var column in columns)
        {
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlRawAsync($"IF COL_LENGTH(N'sa2_KeywordProfile', N'{column.Name}') IS NULL ALTER TABLE [sa2_KeywordProfile] ADD [{column.Name}] {column.SqlServer};", token);
            else
            {
                var connection = db.Database.GetDbConnection();
                await using var command = connection.CreateCommand();
                command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
                command.CommandText = "PRAGMA table_info('sa2_KeywordProfile');";
                var exists = false;
                await using (var reader = await command.ExecuteReaderAsync(token))
                    while (await reader.ReadAsync(token)) if (reader.GetString(1) == column.Name) exists = true;
                if (!exists) await db.Database.ExecuteSqlRawAsync($"ALTER TABLE [sa2_KeywordProfile] ADD [{column.Name}] {column.Sqlite};", token);
            }
        }
    }
#pragma warning restore EF1002
}
