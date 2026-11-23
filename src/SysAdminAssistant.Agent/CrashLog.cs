using System.Diagnostics;
using System.Text.Json;

namespace SysAdminAssistant.Agent;

internal static class CrashLog
{
    public static void Record(string directory,string stage,Exception error)
    {
        try
        {
            var causes=new List<object>();
            for(Exception? current=error;current!=null&&causes.Count<8;current=current.InnerException)
            {
                // Method names only: omit messages, source paths, arguments and payloads.
                var methods=(new StackTrace(current,false).GetFrames()??[]).Take(32).Select(frame=>{
                    var method=frame.GetMethod();
                    return method?.DeclaringType?.FullName+"."+method?.Name;
                });
                causes.Add(new {Type=current.GetType().FullName,HResult=$"0x{current.HResult:X8}",Methods=methods,
                    HttpError=current is HttpRequestException request ? request.HttpRequestError.ToString() : null,
                    HttpStatus=current is HttpRequestException {StatusCode: {} status} ? (int?)status : null,
                    NativeError=current is System.ComponentModel.Win32Exception native ? (int?)native.NativeErrorCode : null});
            }
            var data=new {TimeUtc=DateTime.UtcNow,Version=typeof(CrashLog).Assembly.GetName().Version?.ToString(),ProcessId=Environment.ProcessId,Stage=stage,Causes=causes};
            Directory.CreateDirectory(directory);
            var path=Path.Combine(directory,"crash.json");
            var temp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream,data,new JsonSerializerOptions{WriteIndented=true});
                    stream.Flush(true);
                }
                if(File.Exists(path))File.Replace(temp,path,Path.Combine(directory,"crash.previous.json"));
                else File.Move(temp,path);
            }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
        catch(Exception) { /* Failure to write diagnostics must not stop SCM recovery. */ }
    }
}
