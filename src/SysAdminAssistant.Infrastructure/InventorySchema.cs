using Microsoft.EntityFrameworkCore.Storage;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
namespace SysAdminAssistant.Infrastructure;

public static class InventorySchema
{
    // Use provider-generated DDL so SQLite and SQL Server match the actual EF model.
    public static IReadOnlyList<string> Statements(AssistantDb db)
    {
        var names = "(?:Employee|InventoryAsset|InventoryAct|InventoryActLine)";
        var script = db.Database.GenerateCreateScript();
        var pattern = "CREATE TABLE [\\[\"]sa2_" + names + "[\\]\"]\\s*\\([\\s\\S]*?\\);|CREATE (?:UNIQUE )?INDEX[^;]*?ON [\\[\"]sa2_" + names + "[\\]\"][^;]*?;";
        var statements = Regex.Matches(script, pattern).Select(m => m.Value).ToArray();
        if (statements.Count(x => x.StartsWith("CREATE TABLE")) != 4) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось подготовить обновление схемы инвентаризации"));
        return statements;
    }
    public static IReadOnlyList<string> ActTemplateStatements(bool sqlServer)
    {
        return sqlServer
            ? ["ALTER TABLE [sa2_InventoryAsset] ADD [ValueKopecks] bigint NULL;", "ALTER TABLE [sa2_InventoryActLine] ADD [ValueKopecks] bigint NULL;", "ALTER TABLE [sa2_InventoryAct] ADD [RepresentativePosition] nvarchar(max) NOT NULL DEFAULT N'';", "ALTER TABLE [sa2_InventoryAct] ADD [DismissalDate] datetime2 NULL;"]
            : ["ALTER TABLE sa2_InventoryAsset ADD COLUMN ValueKopecks INTEGER NULL;", "ALTER TABLE sa2_InventoryActLine ADD COLUMN ValueKopecks INTEGER NULL;", "ALTER TABLE sa2_InventoryAct ADD COLUMN RepresentativePosition TEXT NOT NULL DEFAULT '';", "ALTER TABLE sa2_InventoryAct ADD COLUMN DismissalDate TEXT NULL;"];
    }
    public static async Task UpgradeActTemplatesAsync(AssistantDb db, CancellationToken token = default)
    {
        foreach (var statement in ActTemplateStatements(db.Database.IsSqlServer())) await db.Database.ExecuteSqlRawAsync(statement, token);
    }
    public static async Task EnsureMonitoringLinkAsync(AssistantDb db, CancellationToken token)
    {
        if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH(N'sa2_InventoryAsset', N'WorkstationId') IS NULL ALTER TABLE [sa2_InventoryAsset] ADD [WorkstationId] uniqueidentifier NULL;", token);
            return;
        }
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "PRAGMA table_info('sa2_InventoryAsset');";
        var exists = false;
        await using (var reader = await cmd.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token))
                if (reader.GetString(1) == "WorkstationId") exists = true;
        if (!exists)
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_InventoryAsset ADD COLUMN WorkstationId TEXT NULL;", token);
    }
    public static async Task EnsureModelFieldsAsync(AssistantDb db, CancellationToken token = default)
    {
        foreach (var column in new[] { "Manufacturer", "Model", "HardwareJson" })
        {
            if (db.Database.IsSqlServer())
            {
                var sql = $"IF COL_LENGTH(N'sa2_InventoryAsset', N'{column}') IS NULL ALTER TABLE [sa2_InventoryAsset] ADD [{column}] nvarchar(max) NOT NULL DEFAULT N'';";
                await db.Database.ExecuteSqlRawAsync(sql, token);
                continue;
            }
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = "PRAGMA table_info('sa2_InventoryAsset');";
            var exists = false;
            await using (var reader = await cmd.ExecuteReaderAsync(token))
                while (await reader.ReadAsync(token))
                    if (reader.GetString(1) == column) exists = true;
            if (!exists)
            {
                var sql = $"ALTER TABLE sa2_InventoryAsset ADD COLUMN [{column}] TEXT NOT NULL DEFAULT '';";
                await db.Database.ExecuteSqlRawAsync(sql, token);
            }
        }
    }
    public static async Task UpgradeAsync(AssistantDb db, CancellationToken token = default)
    {
        foreach (var statement in Statements(db)) await db.Database.ExecuteSqlRawAsync(statement, token);
    }
}
