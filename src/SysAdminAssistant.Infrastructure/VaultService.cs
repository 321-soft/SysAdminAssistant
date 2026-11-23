using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Infrastructure;

public sealed class VaultService(DatabaseFactory factory) : IDisposable
{
    private byte[]? key;
    private DateTime lastUseUtc;
    public bool IsUnlocked
    {
        get { if (key != null && DateTime.UtcNow - lastUseUtc > TimeSpan.FromMinutes(5)) Lock(); return key != null; }
    }
    public async Task UnlockAsync(string password, CancellationToken token = default)
    {
        if (password.Length < 12) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Мастер-пароль: минимум 12 символов"));
        Lock();
        await using var db = factory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        var metadata = await db.Vault.SingleOrDefaultAsync(token);
        var salt = metadata is null ? RandomNumberGenerator.GetBytes(32) : Convert.FromBase64String(metadata.Salt);
        var candidate = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
        try
        {
            if (metadata is null)
            {
                metadata = new VaultMetadata { Salt = Convert.ToBase64String(salt), Proof = Encrypt(candidate, "SysAdminAssistant.Vault.v1") };
                db.Vault.Add(metadata); await db.SaveChangesAsync(token);
            }
            else if (Decrypt(candidate, metadata.Proof) != "SysAdminAssistant.Vault.v1") throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Неверный мастер-пароль"));
            await transaction.CommitAsync(token);
            key = candidate; lastUseUtc = DateTime.UtcNow;
        }
        catch { CryptographicOperations.ZeroMemory(candidate); throw; }
    }
    public string Seal(CredentialPayload payload)
    {
        RequireUnlocked(); lastUseUtc = DateTime.UtcNow;
        return Encrypt(key!, JsonSerializer.Serialize(payload));
    }
    public CredentialPayload Open(string ciphertext)
    {
        RequireUnlocked(); lastUseUtc = DateTime.UtcNow;
        return JsonSerializer.Deserialize<CredentialPayload>(Decrypt(key!, ciphertext)) ?? throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Повреждённая запись"));
    }
    private void RequireUnlocked() { if (!IsUnlocked) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Разблокируй Vault")); }
    public void Lock() { if (key != null) CryptographicOperations.ZeroMemory(key); key = null; }
    public void Dispose() => Lock();
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("SysAdminAssistant.Vault.v1");
    private static string Encrypt(byte[] secret, string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12); var cipher = new byte[plain.Length]; var tag = new byte[16];
            using var aes = new AesGcm(secret, 16); aes.Encrypt(nonce, plain, cipher, tag, AssociatedData);
            return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private static string Decrypt(byte[] secret, string value)
    {
        var data = Convert.FromBase64String(value);
        if (data.Length < 28) throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Повреждённая запись"));
        var plain = new byte[data.Length - 28];
        try { using var aes = new AesGcm(secret, 16); aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain, AssociatedData); return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
