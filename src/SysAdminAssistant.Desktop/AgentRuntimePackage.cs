using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
namespace SysAdminAssistant.Desktop;
internal static class AgentRuntimePackage
{
    internal const string Name=SysAdminAssistant.Core.AgentDesktopRuntime.FileName;
    // Pinned release metadata; packages never come from agent/server input.
    internal const string Sha512=SysAdminAssistant.Core.AgentDesktopRuntime.Sha512;
    public static Task<string> EnsureAsync(string directory,IProgress<(int Percent,string Stage)> progress,CancellationToken token,Func<Task<bool>> confirm)
    {
        var cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SysAdminAssistant","RuntimePackages",Name);
        var candidates=new List<string>{Path.Combine(directory,"Prerequisites",Name),Path.Combine(directory,"RuntimePackages",Name)};
        var parent=new DirectoryInfo(directory);for(int i=0;i<3&&parent.Parent!=null;i++){parent=parent.Parent;candidates.Add(Path.Combine(parent.FullName,"RuntimePackages",Name));}
        var updates=new Progress<string>(stage=>progress.Report((3,stage)));
        return SysAdminAssistant.Core.RuntimePackageCache.EnsureAsync(cache,candidates,Sha512,confirm,(temp,ct)=>DownloadAsync(temp,progress,ct),updates,token);
    }
    private static async Task DownloadAsync(string destination,IProgress<(int Percent,string Stage)> progress,CancellationToken token)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromMinutes(10));token=deadline.Token;
        using var handler=new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.None};using var http=new HttpClient(handler){Timeout=TimeSpan.FromMinutes(10)};
        Exception? last=null;
        foreach(var host in new[]{"dotnetcli.blob.core.windows.net","builds.dotnet.microsoft.com"}){
            last=null;
            try{
                progress.Report((2,"Загрузка .NET Desktop Runtime x64 с Microsoft…"));
                using var response=await http.GetAsync("https://"+host+"/dotnet/WindowsDesktop/"+SysAdminAssistant.Core.AgentDesktopRuntime.Version+"/"+Name,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
                const long limit=150000000;if(response.Content.Headers.ContentLength>limit)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Установщик слишком велик"));
                await using(var input=await response.Content.ReadAsStreamAsync(token))await using(var output=new FileStream(destination,FileMode.Create,FileAccess.Write,FileShare.None,65536,true)){
                    var bytes=new byte[65536];long total=0;int count;var length=response.Content.Headers.ContentLength;
                    while((count=await input.ReadAsync(bytes,token))>0){total+=count;if(total>limit)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Установщик слишком велик"));await output.WriteAsync(bytes.AsMemory(0,count),token);progress.Report((length>0?2+(int)(13*total/length.Value):5,"Загрузка Runtime: "+(total/1048576)+" МБ"));}
                    if(length.HasValue&&total!=length.Value)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Загрузка неполная"));
                }
                if(!SysAdminAssistant.Core.RuntimePackageCache.Valid(destination,Sha512))throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("SHA-512 Runtime не совпал с опубликованным Microsoft"));return;
            }catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
            catch(Exception e){last=e;}
            finally{if(last!=null&&File.Exists(destination))File.Delete(destination);}
        }
        throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось сохранить установщик .NET. Проверьте связь с Microsoft; неполный пакет агента не сохранён."),last);
    }
}
