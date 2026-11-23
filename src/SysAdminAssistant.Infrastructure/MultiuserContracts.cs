using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record UserView(string Id, string Login, bool Administrator, bool Enabled, int Revision, Dictionary<string,int>? Permissions = null);
public sealed record UserCreate(string Login, string Password, Dictionary<string,int>? Permissions = null);
public sealed record UserUpdate(string Id, bool Enabled, int Revision, string NewPassword, Dictionary<string,int>? Permissions = null);
public sealed record PasswordChange(string CurrentPassword, string NewPassword);
public sealed record VaultItem(int Id, string Name, string OwnerId, string OwnerLogin, int? DeviceId, int Revision, int? ServiceId=null);
public sealed record VaultSave(int Id, int Revision, string Name, int? DeviceId, CredentialPayload Payload, int? ServiceId=null);
public sealed record VaultTarget(int Id, int Revision);
public sealed record AuditView(DateTime Utc, string ActorId, string ActorLogin, string Action, string OwnerId, int EntryId);

public sealed record RouterTarget(int Id,string Name,string Host) {public override string ToString()=>Name+" · "+Host;}
