using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SysAdminAssistant.Core;

public static class LicenseAgreement
{
    public const string Version = "1.2";
    public static string Text(string language)
    {
        using var stream = typeof(LicenseAgreement).Assembly.GetManifestResourceStream("SAA.EULA." + (string.Equals(language,"EN",StringComparison.OrdinalIgnoreCase) ? "EN" : "RU"))
            ?? throw new InvalidOperationException("License agreement resource is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
    public static string CanonicalText(string text)=>text.Replace("\r\n","\n").Replace("\r","\n").TrimEnd();
    public static string Hash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalText(Text("RU")) + "\n" + CanonicalText(Text("EN")))));
    private sealed record Acceptance(string Version, string Sha256, string Language, DateTime AcceptedUtc);
    public static bool IsAccepted(string path)
    {
        try { var entry = JsonSerializer.Deserialize<Acceptance>(File.ReadAllText(path)); return entry?.Version == Version && entry.Sha256 == Hash; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
    }
    public static void Accept(string path, string language)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Acceptance(Version, Hash, language, DateTime.UtcNow)), Encoding.UTF8);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
