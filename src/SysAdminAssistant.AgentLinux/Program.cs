using SysAdminAssistant.AgentLinux;
if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException(global::SysAdminAssistant.Core.ProductText.Get("Требуется Linux"));
if(args.Length==1&&args[0]=="--preflight"){Console.WriteLine("SysAdmin Assistant Linux "+typeof(AgentWorker).Assembly.GetName().Version+" · runtime "+Environment.Version);return;}
if(args.Length==1&&args[0]=="--apply-update"){
    try{var message=LinuxUpdateApply.Run();if(message.StartsWith("Агент обновлён",StringComparison.Ordinal))LinuxUpdateStatus.Write(message);Console.WriteLine(message);}
    catch(Exception e){var message="Обновление не выполнено: "+(e is SysAdminAssistant.Core.LinuxReleaseException releaseError?releaseError.Code+": "+releaseError.Message:e is IOException?"QueueIO: "+e.Message:e.Message);LinuxUpdateStatus.Write(message);Console.Error.WriteLine(message);Environment.ExitCode=1;}
    return;
}
if(args.Length==1&&args[0]=="--collect"){var c=new LinuxCollector();c.Read(new());await Task.Delay(1200);Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(c.Read(new()),SysAdminAssistant.Core.AgentWire.Json));return;}
if(args.Length!=2||args[0]!="--data-dir"||!Path.IsPathFullyQualified(args[1]))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Использование: --data-dir /путь или --collect"));
var dir=Path.GetFullPath(args[1]);if(!Directory.Exists(dir))throw new DirectoryNotFoundException(dir);
var mode=File.GetUnixFileMode(dir);if((mode&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherWrite|UnixFileMode.OtherRead|UnixFileMode.OtherExecute))!=0)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Каталог данных должен быть доступен только владельцу"));
using var stop=new CancellationTokenSource();
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
AppDomain.CurrentDomain.ProcessExit+=(_,_)=>stop.Cancel();
await new AgentWorker(dir).RunAsync(stop.Token);
