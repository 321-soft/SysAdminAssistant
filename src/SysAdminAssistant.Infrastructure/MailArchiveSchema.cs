using Microsoft.EntityFrameworkCore;
namespace SysAdminAssistant.Infrastructure;
public static class MailArchiveSchema
{
    public static async Task EnsureAsync(AssistantDb db,CancellationToken token=default)
    {
        var ddl=db.Database.IsSqlServer() ?
            "IF OBJECT_ID(N'dbo.sa2_MailSource',N'U') IS NULL BEGIN CREATE TABLE [sa2_MailSource] ([Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,[EventId] int NOT NULL,[Account] nvarchar(max) NOT NULL,[MessageKey] nvarchar(max) NOT NULL,[Sender] nvarchar(max) NOT NULL,[Subject] nvarchar(max) NOT NULL,[SentAt] datetime2 NULL,[ImapReceivedAt] datetime2 NULL,[Body] nvarchar(max) NOT NULL); CREATE UNIQUE INDEX [IX_sa2_MailSource_EventId] ON [sa2_MailSource]([EventId]); END" :
            "CREATE TABLE IF NOT EXISTS [sa2_MailSource] ([Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,[EventId] INTEGER NOT NULL,[Account] TEXT NOT NULL,[MessageKey] TEXT NOT NULL,[Sender] TEXT NOT NULL,[Subject] TEXT NOT NULL,[SentAt] TEXT NULL,[ImapReceivedAt] TEXT NULL,[Body] TEXT NOT NULL); CREATE UNIQUE INDEX IF NOT EXISTS [IX_sa2_MailSource_EventId] ON [sa2_MailSource]([EventId]);";
        await db.Database.ExecuteSqlRawAsync(ddl,token);
    }
}
