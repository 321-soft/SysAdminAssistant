using System.Security.Cryptography;
namespace SysAdminAssistant.Core;
public static class RuntimePackageCache
{
 private static readonly SemaphoreSlim Gate=new(1);
 public static bool Valid(string path,string sha512){try{if(!File.Exists(path))return false;using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);return stream.Length is >0 and <=150000000&&Convert.ToHexString(SHA512.HashData(stream)).Equals(sha512,StringComparison.OrdinalIgnoreCase);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){return false;}}
 public static async Task<string> EnsureAsync(string target,IEnumerable<string> candidates,string sha512,Func<Task<bool>> confirm,Func<string,CancellationToken,Task> download,IProgress<string>? progress,CancellationToken token){await Gate.WaitAsync(token);try{
 progress?.Report("Проверка локального установщика .NET (SHA-512)…");if(await Task.Run(()=>Valid(target,sha512),token)){progress?.Report("Используется сохранённый локальный установщик .NET");return target;}
 foreach(var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase)){token.ThrowIfCancellationRequested();if(!await Task.Run(()=>Valid(candidate,sha512),token))continue;progress?.Report("Установщик .NET найден в локальном комплекте");
 var temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";try{Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(candidate,temp);if(!Valid(temp,sha512))throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Локальный установщик .NET изменился при копировании"));File.Move(temp,target,true);return target;}catch(Exception e)when(e is IOException or UnauthorizedAccessException){progress?.Report("Кэш недоступен для записи; используется проверенный файл комплекта");return candidate;}finally{if(File.Exists(temp))File.Delete(temp);}}
 if(!await confirm())throw new OperationCanceledException(global::SysAdminAssistant.Core.ProductText.Get("Установщик .NET не найден или повреждён. Загрузка отменена; пакет агента не создан."));
 Directory.CreateDirectory(Path.GetDirectoryName(target)!);var pending=target+"."+Guid.NewGuid().ToString("N")+".download";try{await download(pending,token);if(!Valid(pending,sha512))throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("SHA-512 установщика .NET не совпал с опубликованным Microsoft"));File.Move(pending,target,true);progress?.Report("Установщик .NET сохранён для следующих пакетов");return target;}finally{if(File.Exists(pending))File.Delete(pending);}
 }finally{Gate.Release();}}
}
