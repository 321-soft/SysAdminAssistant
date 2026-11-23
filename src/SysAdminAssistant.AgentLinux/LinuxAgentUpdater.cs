using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.AgentLinux;

// Downloads only server-signed releases; installing them requires a separate trusted mechanism.
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class LinuxAgentUpdater
{
    private readonly string directory;
    private DateTime next=DateTime.MinValue;
    private Task? running;
    public AgentUpdateDelivery? Delivery {get;private set;}
    public string Status {get;private set;}="Ожидание проверки обновления агента";
    public LinuxAgentUpdater(string directory){this.directory=directory;}
    public void Schedule(HttpClient http,AgentBootstrap bootstrap,string agentId,ECDsa key,string current,CancellationToken token)
    {
        if(running is {IsCompleted:false}||DateTime.UtcNow<next)return;
        var raw=key.ExportPkcs8PrivateKey();var copy=ECDsa.Create();
        try{copy.ImportPkcs8PrivateKey(raw,out _);}catch{copy.Dispose();throw;}finally{CryptographicOperations.ZeroMemory(raw);}
        running=Task.Run(async()=>{using(copy)using(var limit=CancellationTokenSource.CreateLinkedTokenSource(token)){limit.CancelAfter(TimeSpan.FromMinutes(2));try{await CheckAsync(http,bootstrap,agentId,copy,current,limit.Token);}catch(OperationCanceledException)when(!token.IsCancellationRequested){Status="DownloadTimeout: загрузка/проверка не завершилась за 2 минуты; проверьте сеть и публикацию сервера";var now=DateTime.UtcNow;Delivery=new("Rejected","DownloadTimeout",Status,now,now,1,AgentWire.Hash("DownloadTimeout"));next=now.AddMinutes(15);}catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(Exception){Status="InternalUpdateError: не удалось завершить обработку локальной очереди; проверьте права, место и журнал службы";} }},CancellationToken.None);
    }
    private static AgentReleaseRequest Request(string id,ECDsa key,string scope)
    {
        var req=new AgentReleaseRequest(id,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),Guid.NewGuid().ToString("N"),"",scope);
        return req with{Signature=Convert.ToBase64String(key.SignData(AgentRelease.Challenge(req),HashAlgorithmName.SHA256))};
    }
    public async Task CheckAsync(HttpClient http,AgentBootstrap bootstrap,string agentId,ECDsa key,string current,CancellationToken token)
    {
        if(DateTime.UtcNow<next)return;next=DateTime.UtcNow.AddHours(1);
        using var queue=LinuxUpdateQueue.TryAcquire(directory);if(queue==null){Status="Применение обновления уже выполняется; загрузка отложена";next=DateTime.UtcNow.AddMinutes(5);return;}
        string? temporary=null;
        try{
            var request=Request(agentId,key,"linux-manifest");
            using var data=JsonContent.Create(request,options:AgentWire.Json);
            using var response=await http.PostAsync("agent/linux-update-manifest",data,token);response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength>15000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Манифест слишком большой"));
            await using var source=await response.Content.ReadAsStreamAsync(token);using var buffer=new MemoryStream();var block=new byte[4096];int n;
            while((n=await source.ReadAsync(block,token))>0){if(buffer.Length+n>15000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Манифест слишком большой"));buffer.Write(block,0,n);}
            var signed=JsonSerializer.Deserialize<LinuxAgentSignedRelease>(buffer.ToArray(),AgentWire.Json)??throw new InvalidDataException();
            var release=LinuxAgentRelease.Verify(signed,bootstrap.IssuerCertificate,agentId,request.Nonce,current);
            if(Version.Parse(release.Version)<=Version.Parse(current)){Status="Версия агента актуальна";var own=Environment.ProcessPath;using var installed=own!=null&&File.Exists(own)?File.OpenRead(own):null;if(installed!=null&&Convert.ToHexString(SHA256.HashData(installed)).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))Delivery=new("Installed","VerifiedCurrent","Свежий подписанный манифест подтверждает установленный файл",DateTime.UtcNow,DateTime.UtcNow,1,AgentWire.Hash("VerifiedCurrent|"+release.Sha256),release.Version,release.Sha256);return;}
            var active=Path.Combine(directory,"update-staging");if(Directory.Exists(active)){if((File.GetAttributes(active)&FileAttributes.ReparsePoint)!=0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Очередь является ссылкой"));if(Directory.EnumerateFileSystemEntries(active).Any()){Status="Ожидается применение ранее загруженного пакета";next=DateTime.UtcNow.AddMinutes(5);return;}Directory.Delete(active);}
            var folder=Path.Combine(directory,"update-incoming-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);File.SetUnixFileMode(folder,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);temporary=folder;
            var binaryTemp=Path.Combine(folder,"release.bin");
            using var download=JsonContent.Create(Request(agentId,key,"linux-file"),options:AgentWire.Json);
            using var message=new HttpRequestMessage(HttpMethod.Post,"agent/linux-update-file"){Content=download};
            using var fileResponse=await http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,token);
            if(fileResponse.StatusCode==System.Net.HttpStatusCode.ServiceUnavailable){
                Status="Сервер раздаёт обновления другим агентам; загрузка отложена";
                next=DateTime.UtcNow.AddSeconds(Random.Shared.Next(60,181));return;
            }
            fileResponse.EnsureSuccessStatusCode();
            if(fileResponse.Content.Headers.ContentLength is long length&&length!=release.Length)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Размер обновления не совпадает"));
            await using(var input=await fileResponse.Content.ReadAsStreamAsync(token))
            await using(var output=new FileStream(binaryTemp,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous|FileOptions.WriteThrough)){
                File.SetUnixFileMode(binaryTemp,UnixFileMode.UserRead|UnixFileMode.UserWrite);long count=0;var chunk=new byte[81920];
                while((n=await input.ReadAsync(chunk,token))>0){count+=n;if(count>release.Length)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Обновление больше ожидаемого"));await output.WriteAsync(chunk.AsMemory(0,n),token);}
                await output.FlushAsync(token);output.Flush(true);if(count!=release.Length)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неполное обновление"));
            }
            using(var check=File.OpenRead(binaryTemp))LinuxAgentRelease.VerifyFile(check,release);
            var manifestPath=Path.Combine(folder,"release.json");
            using(var file=new FileStream(manifestPath,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){var bytes=JsonSerializer.SerializeToUtf8Bytes(signed,AgentWire.Json);file.Write(bytes);file.Flush(true);}File.SetUnixFileMode(manifestPath,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            Directory.Move(folder,active);temporary=null;LinuxUpdateQueue.SyncDirectory(directory);
            Status="Проверенное обновление "+release.Version+" загружено и ожидает автоматического применения";
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(Exception e)when(e is HttpRequestException or IOException or CryptographicException or JsonException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OperationCanceledException){var code=e is LinuxReleaseException releaseError?releaseError.Code:e is HttpRequestException?"DownloadHTTP":e is IOException?"DownloadIO":"DownloadValidation";var message=e is LinuxReleaseException?e.Message:e is HttpRequestException httpError?"Ошибка HTTPS загрузки: "+(httpError.StatusCode?.ToString()??"нет соединения; проверьте TLS, маршрут и время"):e is IOException?"Ошибка записи загрузки; проверьте место, тип файлов и права":"Пакет не прошёл проверку формата/целостности";Status=code+": "+message;var signature=AgentWire.Hash(code);var now=DateTime.UtcNow;Delivery=new("Rejected",code,message,Delivery?.Signature==signature?Delivery.FirstUtc:now,now,Delivery?.Signature==signature?Delivery.Count+1:1,signature);next=DateTime.UtcNow.AddMinutes(15);}
        finally{if(temporary!=null&&Directory.Exists(temporary))Directory.Delete(temporary,true);}
    }
}
