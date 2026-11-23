using Microsoft.EntityFrameworkCore;
namespace SysAdminAssistant.Infrastructure;
public static class InvoiceSchema
{
    public static async Task EnsureAsync(AssistantDb db,CancellationToken token=default)
    {
        var sql=db.Database.IsSqlServer();string Create(string name,string cols)=>sql?$"IF OBJECT_ID(N'[sa2_{name}]',N'U') IS NULL CREATE TABLE [sa2_{name}] ({cols})":$"CREATE TABLE IF NOT EXISTS [sa2_{name}] ({cols})";
        var id=sql?"[Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY":"[Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT";var txt=sql?"nvarchar(max)":"TEXT";var key=sql?"nvarchar(64)":"TEXT";var integer=sql?"int":"INTEGER";var date=sql?"datetime2":"TEXT";var blob=sql?"varbinary(max)":"BLOB";
        await db.Database.ExecuteSqlRawAsync(Create("InvoiceRecord",$"{id},[Key] {key} NOT NULL UNIQUE,[BusinessKey] {key} NOT NULL,[Digest] {key} NOT NULL,[Json] {txt} NOT NULL,[Status] {txt} NOT NULL,[PaymentId] {integer} NULL,[CreatedUtc] {date} NOT NULL,[Revision] {integer} NOT NULL"),token);
        await db.Database.ExecuteSqlRawAsync(Create("InvoiceAttachment",$"{id},[DocumentId] {integer} NOT NULL,[Name] {txt} NOT NULL,[Digest] {key} NOT NULL,[MediaType] {txt} NOT NULL,[Data] {blob} NOT NULL"),token);
    }
}
