using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace SysAdminAssistant.Core;

public sealed record StationRelease(Guid Station, string Nonce, string Version, long Length, string Sha256, long Expires);
public sealed record SignedStationRelease(string Payload, string Signature);

public static class StationReleaseWire
{
    public const long MaximumBytes = 200L * 1024 * 1024;
    public const string Header = "X-SAA-Station-Release";
    public static StationRelease Verify(string header, string authority, StationRequest request, Version current, DateTimeOffset now)
    {
        if (header.Length > 4096) throw new InvalidDataException("Слишком большой заголовок выпуска.");
        var signed = JsonSerializer.Deserialize<SignedStationRelease>(Convert.FromBase64String(header), AgentWire.Json)
            ?? throw new InvalidDataException("Нет подписи выпуска.");
        using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(authority));
        using var key = certificate.GetECDsaPublicKey() ?? throw new InvalidDataException("Неизвестный ключ подписи выпуска.");
        var payload = Convert.FromBase64String(signed.Payload);
        if (!key.VerifyData(payload, Convert.FromBase64String(signed.Signature), HashAlgorithmName.SHA256))
            throw new InvalidDataException("Подпись выпуска не совпадает.");
        var release = JsonSerializer.Deserialize<StationRelease>(payload, AgentWire.Json)
            ?? throw new InvalidDataException("Нет данных выпуска.");
        var unix = now.ToUnixTimeSeconds();
        if (release.Station != request.Id || release.Nonce != request.Nonce ||
            release.Expires <= unix || release.Expires > unix + 600 ||
            !Version.TryParse(release.Version, out var version) || version <= current ||
            release.Length is <= 0 or > MaximumBytes ||
            release.Sha256 is not { Length: 64 } || !release.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Недопустимый выпуск агента рабочей станции.");
        return release;
    }
}
