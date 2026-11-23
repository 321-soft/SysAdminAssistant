using System.Security.Cryptography;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record RemoteLogin(string User, string Password, bool RememberSession=false);
public sealed record RemoteToken(string Token, DateTime ExpiresUtc);
public sealed record RemoteSnapshot(long Revision, DateTime ServerTime, AppSettings Settings, Dictionary<string, JsonElement[]> Rows, string[] MonitorErrors, string SettingsStamp, Dictionary<int,KscCounts>? Ksc=null,Dictionary<int,string>? AgentVersions=null);
public sealed record RemoteChange(string Resource, string State, int Id, string ExpectedHash, JsonElement Value);
public sealed record RemoteChangeResult(string Resource, string State, int OldId, JsonElement Value);
public sealed record RemoteSettingsResult(AppSettings Settings, string SettingsStamp);
public sealed record RemoteSettingsChange(AppSettings Value, string ExpectedHash, Dictionary<string, string> Secrets);
public sealed record RemoteMail(IReadOnlyList<MailReport> Payloads, string Account, string Key);
public sealed record RemoteRepair(int Id, int Revision, bool Repair);
public sealed record RemoteMessage(string To, string Subject, string Body);
public sealed record RemoteRetention(int Days, bool Confirm, string ExpectedHash);
public static class RemoteData
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public static readonly Dictionary<string, Type> Types = new()
    {
        ["devices"] = typeof(Device), ["services"] = typeof(ServiceEntry), ["payments"] = typeof(Payment), ["jobs"] = typeof(SqlBackupJob),
        ["rules"] = typeof(BindingRule), ["profiles"] = typeof(KeywordProfile), ["reports"] = typeof(ReportEvent), ["pending"] = typeof(PendingReport),
        ["incidents"] = typeof(Incident), ["credentials"] = typeof(CredentialEntry), ["vault"] = typeof(VaultMetadata),
        ["employees"] = typeof(Employee), ["assets"] = typeof(InventoryAsset), ["acts"] = typeof(InventoryAct), ["actLines"] = typeof(InventoryActLine),
        ["connectivity"] = typeof(ConnectivityRecord), ["schema"] = typeof(SchemaInfo)
    };
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), Json)));
    public static string EntityStamp(Entity value) => value switch
    {
        Device d => Hash(new { d.IsKscServer,d.ConnectionsJson,d.Id, d.Name, d.Type, d.Host, d.Site, d.Role, d.Enabled, d.EquipmentFamily }),
        ServiceEntry s => Hash(new { s.Id, s.Name, s.Type, s.Address, s.Notes }),
        _ => Hash(value)
    };
    public static AppSettings PublicSettings(AppSettings original)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(original, Json), Json)!;
        s.Mail.ProtectedPassword = s.Mail.ProtectedPassword.Length > 0 ? "REMOTE" : ""; foreach (var m in s.Mailboxes) m.ProtectedPassword = m.ProtectedPassword.Length > 0 ? "REMOTE" : "";
        s.ServerHeartbeat.ProtectedSecret=s.ServerHeartbeat.ProtectedSecret.Length>0?"REMOTE":"";
        s.Telegram.ProtectedToken = s.Telegram.ProtectedToken.Length > 0 ? "REMOTE" : "";
        s.Smtp.ProtectedPassword = s.Smtp.ProtectedPassword.Length > 0 ? "REMOTE" : ""; s.Router.ProtectedPassword = s.Router.ProtectedPassword.Length > 0 ? "REMOTE" : "";
        s.ProtectedSqlPassword = ""; s.SqlConnectionString = ""; s.SqlitePath = ""; s.ExternalWorker = true;
        return s;
    }
}
