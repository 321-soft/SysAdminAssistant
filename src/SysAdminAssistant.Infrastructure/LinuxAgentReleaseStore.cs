using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed partial class AgentStore
{
    private sealed record LinuxArtifact(long Length,DateTime LastWriteUtc,DateTime CreatedUtc,string Hash);
    private LinuxArtifact? linuxArtifact;
    // Accessed under the existing agent store lock. Only authenticated Linux IDs can add entries.
    private readonly Dictionary<(string Agent,Guid Nonce),(long Expires,bool Download)> linuxProofs=new();

    private static FileInfo LinuxBinary()
    {
        var f=new FileInfo(Path.Combine(AppContext.BaseDirectory,"AgentTemplate","Linux","SysAdminAssistant.AgentLinux"));
        if(!f.Exists||(f.Attributes&FileAttributes.ReparsePoint)!=0||f.Length is <1 or >150_000_000)
            throw new InvalidDataException(ProductText.Get("Недоступен Linux-выпуск"));
        return f;
    }
    private void AuthorizeLinux(AgentIndex s,AgentReleaseRequest r,bool download)
    {
        Authorize(s,r,download?"linux-file":"linux-manifest",linux:true);
        var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach(var entry in linuxProofs.Where(x=>x.Value.Expires<now).ToArray())linuxProofs.Remove(entry.Key);
        var nonce=(r.AgentId,Guid.ParseExact(r.Nonce,"N"));
        if(linuxProofs.ContainsKey(nonce))throw new UnauthorizedAccessException(ProductText.Get("Запрос обновления уже использован"));
        var recent=linuxProofs.Where(x=>x.Key.Agent==r.AgentId).Select(x=>x.Value).ToArray();
        if(recent.Length>=16||download&&recent.Count(x=>x.Download)>=3)
            throw new UnauthorizedAccessException(ProductText.Get("Превышен лимит запросов выпуска Linux"));
        linuxProofs.Add(nonce,(Math.Max(r.Timestamp,now)+300,download));
    }
    public LinuxAgentSignedRelease LinuxRelease(AgentReleaseRequest request)=>State(s=>{
        AuthorizeLinux(s,request,false);
        var f=LinuxBinary();
        var artifact=linuxArtifact;
        if(artifact==null||artifact.Length!=f.Length||artifact.LastWriteUtc!=f.LastWriteTimeUtc||artifact.CreatedUtc!=f.CreationTimeUtc){
            using var stream=File.OpenRead(f.FullName);
            artifact=linuxArtifact=new(f.Length,f.LastWriteTimeUtc,f.CreationTimeUtc,Convert.ToHexString(SHA256.HashData(stream)));
        }
        var version=typeof(AgentStore).Assembly.GetName().Version?.ToString()??throw new InvalidDataException(ProductText.Get("Нет версии выпуска"));
        var release=new LinuxAgentReleaseDocument(request.AgentId,request.Nonce,version,DateTime.UtcNow.AddMinutes(10),artifact.Length,artifact.Hash);
        LinuxAgentRelease.Validate(release);var payload=JsonSerializer.Serialize(release,AgentWire.Json);
        using var ca=Ca(s);using var key=ca.GetECDsaPrivateKey()!;
        return new LinuxAgentSignedRelease(payload,Convert.ToBase64String(key.SignData(LinuxAgentRelease.Content(payload),HashAlgorithmName.SHA256)));
    },true);
    public FileStream OpenLinuxReleaseFile(AgentReleaseRequest request)
    {
        State(s=>{AuthorizeLinux(s,request,true);return true;});
        var f=LinuxBinary();
        return new FileStream(f.FullName,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
    }
}
