using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;

internal sealed class KscMonitor(ServerState state,ILogger<KscMonitor> log):BackgroundService
{
    sealed record Stored(bool Active,Guid Generation,KscConnection? Profile,KscStatus Status,KscTelegramOptions Telegram,Dictionary<string,string>? ComputerNames=null);
    readonly object gate=new();
    int nameOffset;
    // Separate from the retired, device-bound configuration. Do not import its credentials or cursor.
    string PathName=>Path.Combine(state.DirectoryPath,"ksc-standalone.json");
    Stored Load()=>File.Exists(PathName)
        ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(PathName))??throw new InvalidDataException("Настройки KSC недоступны.")
        : new(false,Guid.Empty,null,new(false,null,0,null,"",[]),new());
    void Save(Stored s){var tmp=PathName+".tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(s));File.Move(tmp,PathName,true);}
    static void RequireActive(Stored s){if(!s.Active)throw new InvalidOperationException("Сначала активируйте раздел Kaspersky.");}
    static KscConnection Resolve(KscConnection request,Stored s)
    {
        RequireActive(s);
        var c=(request with{DeviceId=0}).Normalize();
        if(c.Password.Length>0)return c;
        if(s.Profile is {} prior&&c.SameAccount(prior)&&prior.Password.Length>0)
            return c with{Password=LocalSecret.Unprotect(prior.Password)};
        throw new ArgumentException("Введите пароль KSC. При изменении адреса или учётной записи сохранённый пароль не используется.");
    }
    private static string DisplayTitle(KscIssue issue)=>issue.LatestEvent?.RdpBruteForce==true?"Подбор пароля RDP":issue.Title=="Обнаружен вирус"?issue.LatestEvent?.EventName??"Срабатывание защиты":issue.Title;
    public KscSectionView View()
    {
        lock(gate){
            var s=Load();
            var status=s.Status with{Source=s.Generation,Configured=s.Profile!=null,Enabled=s.Active&&s.Profile!=null};
            KscIssue[] problems=s.Generation==Guid.Empty?[]:state.Routers.Store.State(net=>net.Ksc.Open.Where(p=>p.Source==s.Generation.ToString("N")).OrderByDescending(p=>p.LastUtc).ToArray());
            return new(s.Active,status with{Recent=status.Recent.Select(e=>e with{ComputerName=s.ComputerNames?.GetValueOrDefault(e.Host)}).ToArray()},s.Telegram,
                problems.Select(e=>e with{Title=DisplayTitle(e),ComputerName=s.ComputerNames?.GetValueOrDefault(e.Host)}).ToArray()){
                Resolved=state.Routers.Store.State(net=>net.Ksc.Resolved.Where(p=>p.Source==s.Generation.ToString("N"))
                    .OrderByDescending(p=>p.ResolvedUtc).Select(e=>e with{Title=DisplayTitle(e),ComputerName=s.ComputerNames?.GetValueOrDefault(e.Host)??e.ComputerName}).ToArray())
            };
        }
    }
    public bool Activate(KscActivation request)
    {
        lock(gate){var s=Load();if(s.Active!=request.Active){Save(s with{Active=request.Active});state.Touch();}return true;}
    }
    public KscConnectionView Connection()
    {
        lock(gate){var s=Load();RequireActive(s);return s.Profile?.PublicView(false)??new("","","",true,false,false);}
    }
    public bool SaveTelegram(KscTelegramOptions options)
    {
        lock(gate){var s=Load();RequireActive(s);Save(s with{Telegram=options});state.Touch();return true;}
    }
    public Func<NetworkNotification,bool> TelegramFilter()
    {
        lock(gate){var s=Load();return n=>s.Active&&s.Generation!=Guid.Empty&&
            n.EntityKey.StartsWith("ksc:"+s.Generation.ToString("N")+":",StringComparison.Ordinal)&&
            s.Telegram.Allows(n.Severity,n.Transition.StartsWith("ЗАКРЫТО",StringComparison.Ordinal));}
    }
    public async Task<int> Probe(KscConnection request,CancellationToken token)
    {
        KscConnection c;lock(gate)c=Resolve(request,Load());
        return await KscReader.ProbeAsync(c,token);
    }
    public async Task<bool> Enable(KscConnection request,CancellationToken token)
    {
        Stored original;KscConnection c;
        lock(gate){original=Load();c=Resolve(request,original);}
        var b=await KscReader.Read(c,null,token);
        var profile=c with{Password=LocalSecret.Protect(c.Password)};
        lock(gate){
            var old=Load();RequireActive(old);
            if(old.Generation!=original.Generation||old.Profile!=original.Profile)
                throw new InvalidOperationException("Настройки KSC уже изменились. Откройте форму заново.");
            if(old.Profile is {} prior&&c.SameAccount(prior))
                Save(old with{Profile=profile,Status=old.Status with{Error=""}});
            else{
                var generation=Guid.NewGuid();
                state.Routers.Store.State(net=>{net.Ksc.Generation=generation;net.Ksc.Position=b.Cursor;net.Ksc.Hosts.Clear();net.Ksc.Open.Clear();return true;},true);
                Save(old with{Generation=generation,Profile=profile,ComputerNames=null,Status=new(true,b.Cursor,0,DateTime.UtcNow,"",[])});
            }
            state.Touch();return true;
        }
    }
    public async Task<KscFindingList> Findings(KscIssueResolution request,CancellationToken token)
    {
        if(string.IsNullOrEmpty(request.Key)||request.Key.Length>512)throw new ArgumentException("Неверный идентификатор проблемы KSC.");
        Stored s;KscIssue issue;string code;long first,through;
        lock(gate){
            s=Load();RequireActive(s);
            var snapshot=state.Routers.Store.State(net=>(
                Issue:net.Ksc.Open.Concat(net.Ksc.Resolved).SingleOrDefault(x=>x.Key==request.Key&&x.Source==s.Generation.ToString("N")),
                Rules:net.Ksc.Rules.ToArray(),Through:net.Ksc.Position));
            issue=snapshot.Issue??throw new ArgumentException("Проблема KSC уже закрыта или недоступна. Обновите список.");
            var parts=issue.Key.Split(':');
            if(parts.Length!=6||!long.TryParse(parts[^1],out first)||first<=0)
                throw new InvalidDataException("Для проблемы не сохранён номер исходного события KSC.");
            code=issue.EventCode??snapshot.Rules.Concat(KscRules.Standard()).FirstOrDefault(r=>r.Id==parts[^2])?.OpenEvent
                ??throw new InvalidDataException("Для проблемы не сохранён код исходного события KSC.");
            through=snapshot.Through;
        }
        var profile=s.Profile??throw new InvalidOperationException("Подключение KSC не настроено.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        KscFindingList result;
        try{
            result=await KscReader.ReadFindings(profile with{Password=LocalSecret.Unprotect(profile.Password)},issue.Host,code,first,
                issue.LastEventId>0?issue.LastEventId:through,through,deadline.Token);
        }catch(OperationCanceledException)when(!token.IsCancellationRequested){
            throw new KscConnectionException("KSC не успел вернуть подробности события. Повторите запрос или откройте журнал KSC.");
        }
        lock(gate){
            var live=Load();
            if(!live.Active||live.Generation!=s.Generation||live.Profile!=profile)
                throw new InvalidOperationException("Подключение KSC изменилось. Обновите список.");
        }
        // Reclassify legacy groups only with complete evidence; close only if every attack is blocked.
        if(issue.AttackerIp==null&&!result.Limited&&result.Events.Length>0&&result.Events.LongLength==issue.Count&&
            result.Events.All(e=>e.RdpBruteForce)){
            lock(gate){
                var live=Load();
                if(live.Active&&live.Generation==s.Generation&&live.Profile==profile){
                    var changed=state.Routers.Store.State(net=>{
                        var updated=issue with{Title="Подбор пароля RDP",Severity=Severity.Warning,ComputerName=s.ComputerNames?.GetValueOrDefault(issue.Host)};
                        var index=net.Ksc.Open.FindIndex(e=>e.Key==issue.Key);
                        if(index>=0){
                            if(net.Ksc.Open[index]!=issue)return false;
                            if(result.Events.All(e=>e.IpBlocked&&e.AttackerIp!=null))net.Ksc.Close(updated,DateTime.UtcNow,"KSC подтвердил блокировку IP для всех событий группы");
                            else net.Ksc.Open[index]=updated;
                            return true;
                        }
                        index=net.Ksc.Resolved.FindIndex(e=>e.Key==issue.Key);
                        if(index<0||net.Ksc.Resolved[index]!=issue)return false;
                        net.Ksc.Resolved[index]=updated;return true;
                    },true);
                    if(changed)state.Touch();
                }
            }
        }
        return result;
    }
    public bool ResolveIssue(string key)
    {
        lock(gate){
            var s=Load();RequireActive(s);
            var result=state.Routers.Store.State(net=>{
                var p=net.Ksc.Open.SingleOrDefault(x=>x.Key==key&&x.Source==s.Generation.ToString("N"));
                if(p==null)return false;
                net.Ksc.Close(p,DateTime.UtcNow,"Отмечено решённым вручную");
                if(p.Notify)net.Notifications.Add(new(){EntityKey=p.Key,Topic="ksc.security",Source="KSC · "+(s.ComputerNames?.GetValueOrDefault(p.Host)??p.Computer),Title=p.Title+" · отмечено решённым",Severity=p.Severity,Transition="ЗАКРЫТО / ВОССТАНОВЛЕНО"});
                return true;
            },true);
            if(result)state.Touch();
            return result;
        }
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while(!token.IsCancellationRequested){
            var delay=60;
            try{
                Stored s;lock(gate)s=Load();
                if(s.Active&&s.Profile is {} p){
                    try{
                        // The ledger is the durable cursor; a restart between the two writes cannot duplicate notifications.
                        var cursor=state.Routers.Store.State(net=>net.Ksc.Generation==s.Generation?(long?)net.Ksc.Position:s.Status.Cursor);
                        var existing=state.Routers.Store.State(net=>net.Ksc.Open.Where(e=>e.Source==s.Generation.ToString("N")).Select(e=>e.Host).ToArray());
                        var hosts=existing.Concat(s.Status.Recent.Select(e=>e.Host)).Where(h=>s.ComputerNames?.ContainsKey(h)!=true).Distinct(StringComparer.Ordinal).ToArray();
                        var b=await KscReader.ReadWithNames(p with{Password=LocalSecret.Unprotect(p.Password)},cursor,hosts,nameOffset,s.ComputerNames,token);
                        nameOffset=nameOffset==int.MaxValue?0:nameOffset+1;
                        lock(gate){
                            var live=Load();
                            if(live.Active&&live.Generation==s.Generation&&live.Profile==p){
                                var names=live.ComputerNames??new(StringComparer.Ordinal);
                                foreach(var (host,name) in b.ComputerNames)names[host]=name;
                                if(b.Rows.Length>0)state.Routers.Store.State(net=>{
                                    var named=b with{Rows=b.Rows.Select(e=>e with{ComputerName=names.GetValueOrDefault(e.Host)}).ToArray()};
                                    net.Ksc.ApplyStandalone(s.Generation,named,net.Notifications,DateTime.UtcNow);return true;
                                },true);
                                var recent=live.Status.Recent.Concat(b.Rows).TakeLast(64).ToArray();
                                var open=state.Routers.Store.State(net=>net.Ksc.Open.Where(e=>e.Source==s.Generation.ToString("N")).Select(e=>e.Host).ToArray());
                                var kept=open.Concat(recent.Select(e=>e.Host)).Distinct(StringComparer.Ordinal).Where(names.ContainsKey).Take(4096).ToDictionary(h=>h,h=>names[h],StringComparer.Ordinal);
                                Save(live with{ComputerNames=kept,Status=new(true,b.Cursor,live.Status.Total+b.Rows.Length,DateTime.UtcNow,"",recent)});
                                if(b.Rows.Length>0||b.ComputerNames.Count>0||live.Status.Error.Length>0)state.Touch();
                                if(b.More)delay=1;
                            }
                        }
                    }catch(Exception e)when(!token.IsCancellationRequested){
                        lock(gate){var live=Load();if(live.Active&&live.Generation==s.Generation&&live.Profile==p){
                            var error=e is IOException or InvalidDataException?e.Message:"KSC: "+e.GetType().Name;
                            if(live.Status.Error!=error){Save(live with{Status=live.Status with{Error=error}});state.Touch();}
                        }}
                    }
                }
            }catch(Exception e)when(!token.IsCancellationRequested){log.LogWarning("KSC collector: {Failure}",e.GetType().Name);}
            await Task.Delay(TimeSpan.FromSeconds(delay),token);
        }
    }
}
