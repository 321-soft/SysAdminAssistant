using System.Security.Cryptography;
namespace SysAdminAssistant.Core;
public static class ServerPassword
{
    public static (string Salt, string Hash) Create(string password)
    {
        if (password.Length is < 9 or > 256) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пароль сервера: 9–256 символов"));
        var salt = RandomNumberGenerator.GetBytes(32);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32)));
    }
    public static bool Verify(string password, string salt, string hash)
    {
        if (password.Length is < 9 or > 256) return false;
        try
        {
            var s = Convert.FromBase64String(salt); var h = Convert.FromBase64String(hash);
            if (s.Length != 32 || h.Length != 32) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, s, 600_000, HashAlgorithmName.SHA256, 32);
            try { return CryptographicOperations.FixedTimeEquals(actual, h); }
            finally { CryptographicOperations.ZeroMemory(actual); }
        }
        catch (FormatException) { return false; }
    }
}
