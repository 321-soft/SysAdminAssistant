using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Win32;
using SysAdminAssistant.Core;
using SysAdminAssistant.Desktop;

namespace SysAdminAssistant.Agent;

internal static class StationUpdater
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory,"Maintenance");
    private static string PackagePath => Path.Combine(DirectoryPath,"station-update.msi");

    internal static async Task TryUpdate(HttpClient client, Uri url, StationRequest request, string pin,
        string authority, string enrollment, Version installed, RegistryKey identity, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var post = new HttpRequestMessage(HttpMethod.Post,new Uri(url,"/agent/workstation-update")) {
            Content=JsonContent.Create(request,options:AgentWire.Json)
        };
        using var reply = await client.SendAsync(post,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        if (reply.StatusCode == HttpStatusCode.NoContent) return;
        reply.EnsureSuccessStatusCode();
        if (!reply.Headers.TryGetValues(StationReleaseWire.Header,out var values))
            throw new InvalidDataException("Нет подписи выпуска.");
        var release = StationReleaseWire.Verify(values.Single(),authority,request,installed,DateTimeOffset.UtcNow);
        if (reply.Content.Headers.ContentLength != release.Length) throw new InvalidDataException("Размер MSI не совпадает.");
        // A failed MSI is not launched repeatedly; the next release can be tried normally.
        if (identity.GetValue("AttemptedRelease") as string == release.Version) return;
        Directory.CreateDirectory(DirectoryPath);
        try
        {
            await using (var input = await reply.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(PackagePath,FileMode.Create,FileAccess.Write,FileShare.None,65536,true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[65536];
                long total=0;
                int count;
                while ((count=await input.ReadAsync(buffer,timeout.Token))!=0)
                {
                    total+=count;
                    if (total>release.Length) throw new InvalidDataException("MSI превышает заявленный размер.");
                    hash.AppendData(buffer,0,count);
                    await output.WriteAsync(buffer.AsMemory(0,count),timeout.Token);
                }
                if (total!=release.Length || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(),Convert.FromHexString(release.Sha256)))
                    throw new InvalidDataException("SHA-256 MSI не совпадает.");
                await output.FlushAsync(timeout.Token);
            }
            token.ThrowIfCancellationRequested();
            if (release.Expires<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()) throw new InvalidDataException("Срок выпуска истёк.");
            WorkstationMsi.Configure(PackagePath,url.AbsoluteUri,pin,authority,enrollment);
            var process = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe")) { UseShellExecute=false,CreateNoWindow=true };
            foreach (var arg in new[]{"/i",PackagePath,"/quiet","/norestart"}) process.ArgumentList.Add(arg);
            identity.SetValue("AttemptedRelease",release.Version);
            try { using var installer=Process.Start(process)??throw new InvalidOperationException("Не удалось запустить Windows Installer."); }
            catch { identity.DeleteValue("AttemptedRelease",false); throw; }
        }
        catch { CleanPackage(); throw; }
    }

    internal static void CleanPackage()
    {
        try { File.Delete(PackagePath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
