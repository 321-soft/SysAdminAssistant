using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;

public static class LinuxInstallBundles
{
    private sealed record Bundle(byte[] Data,DateTime Expires);
    private static readonly ConcurrentDictionary<string,Bundle> Pending=new();
    private static readonly object Gate=new();
    private static void Entry(TarWriter tar,string name,Stream data){var e=new PaxTarEntry(TarEntryType.RegularFile,name){DataStream=data};tar.WriteEntry(e);}
    public static LinuxInstallCommand Create(ServerState state,int deviceId,string deviceName)
        =>CreateForStore(state.Agents,state.Certificates.Current,state.ServerUrl,deviceId,deviceName,Path.Combine(AppContext.BaseDirectory,"AgentTemplate","Linux","SysAdminAssistant.AgentLinux"),Path.Combine(AppContext.BaseDirectory,"LinuxInstall","install.sh"));
    public static LinuxInstallCommand CreateForStore(AgentStore agents,X509Certificate2 cert,string serverUrl,int deviceId,string deviceName,string exe,string installer)
    {
        if(!Uri.TryCreate(serverUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!IPAddress.TryParse(uri.Host.Trim('[',']'),out var ip)||uri.AbsolutePath!="/")throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Адрес сервера должен содержать IP и HTTPS"));
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var expiry=DateTime.UtcNow.AddMinutes(15);
        lock(Gate){
            foreach(var item in Pending.Where(x=>x.Value.Expires<DateTime.UtcNow))Pending.TryRemove(item.Key,out _);
            if(Pending.Count>=4)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Слишком много созданных установочных пакетов; дождитесь истечения срока действия"));
            var hashes=agents.LinuxTemplateHashes();
            var network=ip+"/"+(ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork?32:128);
            var signed=agents.Package(new AgentPackageRequest(deviceId,uri.ToString(),[network],hashes,Platform:"Linux"),deviceName);
            using var output=new MemoryStream();
            using(var gzip=new GZipStream(output,CompressionLevel.Fastest,true))
            using(var tar=new TarWriter(gzip,leaveOpen:true))
            using(var agent=File.OpenRead(exe))
            using(var boot=new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(signed,AgentWire.Json)))
            using(var setup=File.OpenRead(installer)){Entry(tar,"agent",agent);Entry(tar,"bootstrap.json",boot);Entry(tar,"install.sh",setup);}
            var bytes=output.ToArray();
            var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            using var rsa=cert.GetRSAPublicKey();using var ec=cert.GetECDsaPublicKey();
            var spki=rsa?.ExportSubjectPublicKeyInfo()??ec?.ExportSubjectPublicKeyInfo()??throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Неподдерживаемый ключ TLS"));
            var pin=Convert.ToBase64String(SHA256.HashData(spki));
            var url=uri.GetLeftPart(UriPartial.Authority)+"/agent/linux-install";
            var script=$$"""
set -euo pipefail
asroot(){ if [ "$(id -u)" -eq 0 ]; then "$@"; elif command -v sudo >/dev/null; then sudo "$@"; else echo 'Установите sudo или запустите от root.' >&2; exit 1; fi; }
if [ "$(id -u)" -ne 0 ]; then command -v sudo >/dev/null || { echo 'Установите sudo или запустите от root.' >&2; exit 1; }; sudo -v; fi
if ! command -v curl >/dev/null; then asroot apt-get update && asroot apt-get install -y curl; fi
command -v tar >/dev/null && command -v sha256sum >/dev/null || { echo 'Нужны tar и sha256sum.' >&2; exit 1; }
umask 077
d=$(mktemp -d)
trap 'rm -rf "$d"' EXIT
curl -kfsS --proto '=https' --max-time 300 --pinnedpubkey 'sha256//{{pin}}' -H 'X-SAA-Install: {{token}}' '{{url}}' -o "$d/package"
printf '%s  %s\n' '{{hash}}' "$d/package" | sha256sum --check --status || { echo 'Ошибка проверки пакета.' >&2; exit 1; }
tar -xzf "$d/package" -C "$d"
asroot bash "$d/install.sh" "$d"
""";
            // Quote the complete script for an ordinary interactive shell.
            var command="bash -c '"+script.Replace("'","'\\''")+"'";
            Pending[token]=new(bytes,expiry);
            return new(deviceId,command,expiry);
        }
    }
    public static byte[] Take(string? token)
    {
        if(token==null||token.Length!=64||!token.All(Uri.IsHexDigit))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Неверный ключ"));
        if(!Pending.TryRemove(token,out var bundle)||bundle.Expires<DateTime.UtcNow)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Ключ использован или истёк"));
        return bundle.Data;
    }
}
