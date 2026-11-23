using System.Security.Cryptography;
using System.Text.Json;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Server;

internal static class StationUpdateDelivery
{
    private static readonly SemaphoreSlim Hashing = new(1, 1);
    private static (long Length, DateTime Modified, string Hash)? cached;
    public static async Task<IResult> GetAsync(ServerState state, HttpContext context, CancellationToken token)
    {
        var request = await AgentEndpoints.ReadAsync<StationRequest>(context, token);
        if (!state.Workstations.AuthorizeUpdate(request, DateTime.UtcNow)) return Results.NoContent();
        var version = typeof(StationUpdateDelivery).Assembly.GetName().Version!;
        if (!Version.TryParse(request.Payload, out var installed) || installed.Major != 0 || installed.Minor < 46)
            return Results.BadRequest();
        if (installed >= version) return Results.NoContent();
        var path = Path.Combine(AppContext.BaseDirectory, "AgentTemplate", "Workstation", "SysAdminAssistant_Workstation_x64.msi");
        if (!File.Exists(path)) return Results.NoContent();
        var file = File.OpenRead(path);
        try
        {
            if (file.Length is <= 0 or > StationReleaseWire.MaximumBytes) throw new InvalidDataException("Недопустимый размер MSI.");
            string hash;
            await Hashing.WaitAsync(token);
            try
            {
                var modified = File.GetLastWriteTimeUtc(path);
                if (cached is not {} value || value.Length != file.Length || value.Modified != modified)
                {
                    hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
                    cached = (file.Length, modified, hash);
                    file.Position = 0;
                }
                else hash = value.Hash;
            }
            finally { Hashing.Release(); }
            var manifest = new StationRelease(request.Id, request.Nonce, version.ToString(), file.Length, hash, DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds());
            var signed = state.Agents.SignStationRelease(manifest);
            context.Response.Headers[StationReleaseWire.Header] = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(signed, AgentWire.Json));
            return Results.File(file, "application/x-msi");
        }
        catch { await file.DisposeAsync(); throw; }
    }
}
