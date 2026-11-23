using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;
public sealed partial class OrganizationVault(ServerState state, UserDirectory users)
{
    private readonly object auditGate = new();
    private readonly string auditPath = Path.Combine(state.DirectoryPath, "audit.jsonl");
    public void Audit(UserView actor, string action, string owner = "", int id = 0)
    {
        lock (auditGate)
        {
        // Failure to record audit blocks the operation. No payloads/passwords are logged.
        using var file = new FileStream(auditPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new AuditView(DateTime.UtcNow, actor.Id, actor.Login, action, owner, id)) + "\n");
        file.Write(bytes); file.Flush(true);
        }
    }
    public AuditView[] AuditRows()
    {
        lock (auditGate)
        {
        if (!File.Exists(auditPath)) return [];
        var rows = new Queue<string>(); foreach (var line in File.ReadLines(auditPath)) { rows.Enqueue(line); if (rows.Count > 1000) rows.Dequeue(); }
        return rows.Select(x => JsonSerializer.Deserialize<AuditView>(x)!).Reverse().ToArray();
        }
    }
    public async Task<VaultItem[]> List(UserView actor, CancellationToken token)
    {
        AccessRules.Require(actor,"vault");
        await using var db = state.Factory.Create();
        var entries = await db.Credentials.AsNoTracking().Where(x => actor.Administrator || x.OwnerId == actor.Id).OrderBy(x => x.Name).ToArrayAsync(token);
        var names = users.List().ToDictionary(x => x.Id, x => x.Login);
        return entries.Select(x => new VaultItem(x.Id, x.Name, x.OwnerId, names.GetValueOrDefault(x.OwnerId, "Удалённый пользователь"), x.DeviceId, x.Revision,x.ServiceId)).ToArray();
    }
    private void Allow(UserView actor, CredentialEntry entry)
    { if (users.Current(actor.Id,actor.Revision)==null) throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Сеанс отозван")); if (!actor.Administrator && entry.OwnerId != actor.Id) throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Запись другого пользователя недоступна")); }
    private static byte[] Aad(CredentialEntry e) => System.Text.Encoding.UTF8.GetBytes("SysAdminAssistant.0.5/Vault/" + e.OwnerId);
    private byte[] Unwrap(UserView actor, CredentialEntry e)
    {
        using var key = users.Key(actor.Administrator ? users.AdministratorId : actor.Id);
        return key.Decrypt(Convert.FromBase64String(actor.Administrator ? e.AdministratorKey : e.OwnerKey), RSAEncryptionPadding.OaepSHA256);
    }
    private CredentialPayload Open(UserView actor, CredentialEntry e)
    {
        AccessRules.Require(actor,"vault");
        var key = Unwrap(actor, e); var bytes = Convert.FromBase64String(e.Ciphertext); var plain = new byte[bytes.Length - 28];
        try { using var aes = new AesGcm(key, 16); try { aes.Decrypt(bytes.AsSpan(0,12), bytes.AsSpan(28), bytes.AsSpan(12,16), plain, Aad(e)); }
            catch(AuthenticationTagMismatchException) {
                // 0.4 records use the same owner/key scheme with an earlier authenticated context.
                aes.Decrypt(bytes.AsSpan(0,12),bytes.AsSpan(28),bytes.AsSpan(12,16),plain,System.Text.Encoding.UTF8.GetBytes("SysAdminAssistant.0.4/Vault/"+e.OwnerId));
            }
            return JsonSerializer.Deserialize<CredentialPayload>(plain)!; }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    private void Seal(CredentialEntry e, CredentialPayload payload)
    {
        var key = RandomNumberGenerator.GetBytes(32); var plain = JsonSerializer.SerializeToUtf8Bytes(payload); var bytes = new byte[28 + plain.Length]; RandomNumberGenerator.Fill(bytes.AsSpan(0,12));
        try {
            using var aes = new AesGcm(key,16); aes.Encrypt(bytes.AsSpan(0,12), plain, bytes.AsSpan(28), bytes.AsSpan(12,16), Aad(e));
            using var owner = users.Key(e.OwnerId); using var admin = users.Key(users.AdministratorId);
            e.OwnerKey = Convert.ToBase64String(owner.Encrypt(key,RSAEncryptionPadding.OaepSHA256)); e.AdministratorKey = Convert.ToBase64String(admin.Encrypt(key,RSAEncryptionPadding.OaepSHA256)); e.Ciphertext = Convert.ToBase64String(bytes);
        } finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    public async Task<CredentialPayload> Read(UserView actor, VaultTarget request, CancellationToken token)
    {
        await using var db = state.Factory.Create(); var e = await db.Credentials.SingleOrDefaultAsync(x => x.Id == request.Id, token) ?? throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Запись не найдена"));
        Allow(actor,e); Audit(actor,"vault.read",e.OwnerId,e.Id); return Open(actor,e);
    }
    public async Task<bool> Save(UserView actor, VaultSave request, CancellationToken token)
    {
        AccessRules.Require(actor,"vault",2);
        if (request.Name.Length is < 1 or > 200 || request.Payload.Login.Length > 1000 || request.Payload.Password.Length > 4000 || request.Payload.Notes.Length > 10000) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный размер записи"));
        await using var db = state.Factory.Create();
        await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        var e = request.Id == 0 ? new CredentialEntry { OwnerId = actor.Id } : await db.Credentials.SingleOrDefaultAsync(x => x.Id == request.Id, token) ?? throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Запись не найдена"));
        Allow(actor,e);
        if (request.Id != 0 && e.Revision != request.Revision) throw new ConflictException(global::SysAdminAssistant.Core.ProductText.Get("Запись изменена; обновите Vault"));
        if(request.DeviceId is int targetId && request.DeviceId!=e.DeviceId){var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==targetId,token)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Устройство не найдено"));AccessRules.Require(actor,NetworkDevices.Module(device.Type));}
        if (request.DeviceId != null && !await db.Devices.AnyAsync(x => x.Id == request.DeviceId,token)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Устройство не найдено"));
        if(request.ServiceId is int serviceId){if(serviceId<1||!await db.Services.AnyAsync(x=>x.Id==serviceId,token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сервис не найден"));if(request.ServiceId!=e.ServiceId)AccessRules.Require(actor,"services");}
        Audit(actor,"vault.save.request",e.OwnerId,e.Id);
        e.Name=request.Name; e.DeviceId=request.DeviceId;e.ServiceId=request.ServiceId; Seal(e,request.Payload); e.Revision++; if (e.Id==0) db.Add(e); await db.SaveChangesAsync(token);await transaction.CommitAsync(token); state.Touch(); return true;
    }
    public async Task<bool> Delete(UserView actor, VaultTarget request, CancellationToken token)
    {
        AccessRules.Require(actor,"vault",2);
        await using var db = state.Factory.Create(); var e = await db.Credentials.SingleOrDefaultAsync(x => x.Id==request.Id,token) ?? throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Запись не найдена"));
        Allow(actor,e); if(e.Revision!=request.Revision) throw new ConflictException(global::SysAdminAssistant.Core.ProductText.Get("Запись изменена; обновите Vault")); Audit(actor,"vault.delete.request",e.OwnerId,e.Id); db.Remove(e); await db.SaveChangesAsync(token); state.Touch(); return true;
    }
}
