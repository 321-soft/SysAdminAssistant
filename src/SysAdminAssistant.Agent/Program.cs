using System.ServiceProcess;
namespace SysAdminAssistant.Agent;
internal static class Program
{
    public static readonly string Data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"SysAdminAssistantAgent");
    public static async Task Main(string[] args)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        if(args.Length==1&&args[0]=="--console"){await new AgentWorker(Data).RunAsync(CancellationToken.None);return;}
        if(args is ["--workstation"]){ServiceBase.Run(new StationService());return;}
        if(args.Length!=0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неподдерживаемые аргументы"));
        ServiceBase.Run(new AgentService());
    }
}
internal sealed class AgentService:ServiceBase
{
    private readonly CancellationTokenSource stop=new();private Task? worker;
    public AgentService(){ServiceName="SysAdminAssistantAgent";AutoLog=false;CanStop=true;}
    protected override void OnStart(string[] args)=>worker=Task.Run(Run);
    private async Task Run()
    {
        var agent=new AgentWorker(Program.Data);
        try
        {
            await agent.RunAsync(stop.Token);
            if(!stop.IsCancellationRequested)throw new InvalidOperationException("Agent worker stopped unexpectedly");
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested) {}
        catch(Exception error)
        {
            CrashLog.Record(Program.Data,agent.Stage,error);
            if(!stop.IsCancellationRequested)Environment.Exit(1);
        }
    }
    protected override void OnStop(){stop.Cancel();try{worker?.Wait(TimeSpan.FromSeconds(20));}catch(AggregateException){} }
}

internal sealed class StationService:ServiceBase
{
    private readonly CancellationTokenSource stop=new();private Task? worker;
    public StationService(){ServiceName=StationWorker.Service;AutoLog=false;}
    protected override void OnStart(string[] args)=>worker=Task.Run(async()=>{
        try{await StationWorker.Run(stop.Token);}
        catch(OperationCanceledException) when(stop.IsCancellationRequested){}
        catch(Exception e){CrashLog.Record(Program.Data,"Рабочие станции",e);Environment.Exit(1);}
    });
    protected override void OnStop(){stop.Cancel();try{worker?.Wait(TimeSpan.FromSeconds(20));}catch(AggregateException){}}
}
