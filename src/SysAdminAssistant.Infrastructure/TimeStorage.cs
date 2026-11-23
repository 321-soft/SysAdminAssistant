using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public static class TimeStorage
{
 private static readonly HashSet<string> Calendar=["DueDate","ScheduledDate","PurchasedAt","WarrantyUntil","DismissalDate","Date"];
 public static bool Timestamp(string name)=>!Calendar.Contains(name)&&!name.EndsWith("Utc",StringComparison.Ordinal);
 public static void Configure(ModelBuilder b){var converter=new ValueConverter<DateTime,DateTime>(v=>AppTime.ToUtc(v),v=>AppTime.FromUtc(v));foreach(var entity in b.Model.GetEntityTypes())foreach(var p in entity.GetProperties())if((p.ClrType==typeof(DateTime)||p.ClrType==typeof(DateTime?))&&Timestamp(p.Name))p.SetValueConverter(converter);}
 // Called inside startup's transaction, before the first data read.
 public static async Task EnsureAsync(AssistantDb db,TimeZoneInfo legacyZone,CancellationToken token=default){
  await db.Database.ExecuteSqlRawAsync(db.Database.IsSqlServer()?"IF OBJECT_ID(N'dbo.sa2_TimeStorage',N'U') IS NULL CREATE TABLE [sa2_TimeStorage]([Id] int PRIMARY KEY,[Version] int NOT NULL);":"CREATE TABLE IF NOT EXISTS [sa2_TimeStorage]([Id] INTEGER PRIMARY KEY,[Version] INTEGER NOT NULL);",token);
  await db.Database.OpenConnectionAsync(token);var connection=db.Database.GetDbConnection();
  using(var q=connection.CreateCommand()){q.Transaction=db.Database.CurrentTransaction?.GetDbTransaction();q.CommandText="SELECT [Version] FROM [sa2_TimeStorage] WHERE [Id]=1";var version=await q.ExecuteScalarAsync(token);if(version!=null){if(Convert.ToInt32(version)!=1)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестный формат времени БД"));return;}}
  if(!legacyZone.HasSameRules(TimeZoneInfo.Utc))foreach(var entity in db.Model.GetEntityTypes())foreach(var p in entity.GetProperties().Where(p=>(p.ClrType==typeof(DateTime)||p.ClrType==typeof(DateTime?))&&Timestamp(p.Name))){
   var table=entity.GetTableName()!;var column=p.GetColumnName(StoreObjectIdentifier.Table(table,null))!;int after=0;
   while(true){var rows=new List<(int Id,DateTime Value)>();using(var q=connection.CreateCommand()){q.Transaction=db.Database.CurrentTransaction?.GetDbTransaction();q.CommandText=db.Database.IsSqlServer()?$"SELECT TOP(500) [Id],[{column}] FROM [{table}] WHERE [Id]>@after AND [{column}] IS NOT NULL ORDER BY [Id]":$"SELECT [Id],[{column}] FROM [{table}] WHERE [Id]>@after AND [{column}] IS NOT NULL ORDER BY [Id] LIMIT 500";var a=q.CreateParameter();a.ParameterName="@after";a.Value=after;q.Parameters.Add(a);using var reader=await q.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))rows.Add((reader.GetInt32(0),reader.GetValue(1) is DateTime d?d:DateTime.Parse(reader.GetString(1),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind)));}
    if(rows.Count==0)break;foreach(var row in rows){using var q=connection.CreateCommand();q.Transaction=db.Database.CurrentTransaction?.GetDbTransaction();q.CommandText=$"UPDATE [{table}] SET [{column}]=@value WHERE [Id]=@id";var v=q.CreateParameter();v.ParameterName="@value";var utc=AppTime.ToUtc(row.Value,legacyZone);v.Value=db.Database.IsSqlServer()?utc:utc.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF",CultureInfo.InvariantCulture);q.Parameters.Add(v);var id=q.CreateParameter();id.ParameterName="@id";id.Value=row.Id;q.Parameters.Add(id);await q.ExecuteNonQueryAsync(token);}after=rows[^1].Id;
   }
  }
  foreach(var (table,column,names) in new[]{("sa2_Device","MailUpdatesJson",new[]{"FirstReceived","LastReceived"}),("sa2_ConnectivityRecord","ProbeResultsJson",new[]{"CheckedAt"})}){
   int after=0;while(true){var rows=new List<(int Id,string Json)>();using(var q=connection.CreateCommand()){q.Transaction=db.Database.CurrentTransaction?.GetDbTransaction();q.CommandText=db.Database.IsSqlServer()?$"SELECT TOP(200) [Id],[{column}] FROM [{table}] WHERE [Id]>@after ORDER BY [Id]":$"SELECT [Id],[{column}] FROM [{table}] WHERE [Id]>@after ORDER BY [Id] LIMIT 200";var a=q.CreateParameter();a.ParameterName="@after";a.Value=after;q.Parameters.Add(a);using var reader=await q.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))rows.Add((reader.GetInt32(0),reader.GetString(1)));}if(rows.Count==0)break;
    foreach(var row in rows){if(row.Json=="")continue;var json=System.Text.Json.Nodes.JsonNode.Parse(row.Json) as System.Text.Json.Nodes.JsonArray??throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Повреждена история времени"));foreach(var item in json){if(item==null)continue;foreach(var name in names)if(item[name] is System.Text.Json.Nodes.JsonValue value)item[name]=AppTime.ToUtc(DateTime.Parse(value.GetValue<string>(),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),legacyZone).ToString("O");}using var q=connection.CreateCommand();q.Transaction=db.Database.CurrentTransaction?.GetDbTransaction();q.CommandText=$"UPDATE [{table}] SET [{column}]=@value WHERE [Id]=@id";var v=q.CreateParameter();v.ParameterName="@value";v.Value=json.ToJsonString();q.Parameters.Add(v);var id=q.CreateParameter();id.ParameterName="@id";id.Value=row.Id;q.Parameters.Add(id);await q.ExecuteNonQueryAsync(token);}after=rows[^1].Id;
   }
  }
  await db.Database.ExecuteSqlRawAsync("INSERT INTO [sa2_TimeStorage]([Id],[Version]) VALUES(1,1)",token);
 }
}
