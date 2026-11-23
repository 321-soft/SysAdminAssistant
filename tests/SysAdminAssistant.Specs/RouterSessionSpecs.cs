using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class RouterSessionSpecs
{
    private sealed class Fake:IRouterReadSession {
        public int Connections,Reads,Disposed;public bool Fail;
        public Task ConnectAsync(RouterSettings settings,CancellationToken token=default){Connections++;return Task.CompletedTask;}
        public async Task<RouterSnapshot> SnapshotAsync(CancellationToken token=default){Reads++;await Task.Yield();if(Fail)throw new IOException("lost");return new(new(),new(),new(),[]);}
        public Task<RouterAuditConfig> AuditAsync(CancellationToken token)=>Task.FromResult(new RouterAuditConfig());
        public Task<IReadOnlyList<Dictionary<string,string>>> GetFirewallAsync(CancellationToken token=default)=>Task.FromResult<IReadOnlyList<Dictionary<string,string>>>([]);
        public ValueTask DisposeAsync(){Disposed++;return ValueTask.CompletedTask;}
    }
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var made=new List<Fake>();await using var pool=new RouterSessionPool(()=>{var f=new Fake();made.Add(f);return f;});var config=new RouterSettings{Host="router",User="read"};
        await Task.WhenAll(pool.ReadAsync(1,config,default),pool.ReadAsync(1,config,default));check("Router persistent session authenticates once across concurrent polls",made.Count==1&&made[0].Connections==1&&made[0].Reads==2);
        await pool.AuditAsync(1,config,default);await pool.FirewallAsync(1,config,default);await pool.ReadAsync(1,config,default);check("Router audit, firewall and poll retain one authenticated session",made.Count==1&&made[0].Disposed==0&&made[0].Connections==1);
        config.CertificateSha256=new string('A',64);await pool.ReadAsync(1,config,default);check("Router pin change replaces persistent session",made.Count==2&&made[0].Disposed==1);
        made.Last().Fail=true;try{await pool.ReadAsync(1,config,default);}catch(IOException){}await pool.ReadAsync(1,config,default);check("Router broken session reconnects on next poll",made.Count==3&&made[1].Disposed==1);
        await pool.RetainAsync([]);check("Router removed device closes session",made.Last().Disposed==1);
        var rule=NetworkLogRules.Standard().Single(r=>r.Id=="auth-bruteforce");check("Router brute-force quiet timer defaults to one hour",rule.AutoCloseSeconds==3600);
        var now=DateTime.UtcNow;var logs=Enumerable.Range(0,20).Select(i=>new NetworkLog{ReceivedAt=now,Message="login failure from 192.0.2.1 via api"}).ToList();var problems=new List<NetworkProblem>();NetworkLogRules.Evaluate(problems,logs,logs,[rule],now);
        var fresh=new NetworkLog{ReceivedAt=now.AddMinutes(50),Message="login failure from 192.0.2.1 via api"};logs.Add(fresh);NetworkLogRules.Evaluate(problems,logs,[fresh],[rule],now.AddMinutes(50));NetworkLogRules.Evaluate(problems,logs,[],[rule],now.AddMinutes(70));check("Router new attempt restarts hour without adding hours",problems.Single().Open);NetworkLogRules.Evaluate(problems,logs,[],[rule],now.AddMinutes(111));check("Router quiet hour auto-closes from last attempt",!problems.Single().Open);
        var history=new NetworkHistory();var rules=new List<NetworkRule>{new(){Id="wan",Name="WAN",Interfaces=["ether1"]}};
        for(int i=0;i<5;i++){var snap=new RouterSnapshot(new(),new(),new(),[new(){{"name","ether1"},{"running",i%2==0?"true":"false"}}]);var events=new List<NetworkLog>();var effective=rules.ToList();NetworkFlaps.Collect(history,snap,effective,events,now.AddSeconds(i*30));NetworkLogRules.Evaluate(history.Problems,history.Logs,events,effective,now.AddSeconds(i*30));}
        check("Router flapping important interface warns even when currently up",history.Problems.Any(p=>p.Open&&p.Severity==Severity.Warning));
        var accountSuccess=new NetworkLog{MonitoringLogin=true,ReceivedAt=now.AddSeconds(1),Message="user read logged in from 192.0.2.1 via api"};logs.Add(accountSuccess);
        check("Router monitor account ordinary login is quiet",!NetworkLogRules.Matches(accountSuccess,NetworkLogRules.Standard().Single(r=>r.Id=="management-login")));
        check("Router monitor account suspicious login still evaluated",NetworkLogRules.ContextMatches(accountSuccess,logs,NetworkLogRules.Standard().Single(r=>r.Id=="login-after-failures")));

        var rapidHistory=new NetworkHistory();var nativeEvents=Enumerable.Range(0,4).Select(i=>new NetworkLog{Key="native-"+i,ReceivedAt=now,Message="ether1 link "+(i%2==0?"down":"up")}).ToList();rapidHistory.Logs.AddRange(nativeEvents);var rapidRules=rules.ToList();NetworkFlaps.Collect(rapidHistory,new(new(),new(),new(),[new(){{"name","ether1"},{"running","true"}}]),rapidRules,nativeEvents,now);NetworkLogRules.Evaluate(rapidHistory.Problems,rapidHistory.Logs,nativeEvents,rapidRules,now);
        check("Router flaps between polls are detected from native logs",rapidHistory.Problems.Any(p=>p.Open&&p.Severity==Severity.Warning));

    }
}
