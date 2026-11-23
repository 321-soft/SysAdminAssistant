using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed class NetworkNotification
{
    public int? DeviceId {get;set;}
    public string Topic {get;set;}="";
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string EntityKey {get;set;}="";public bool WorkingDaysOnly {get;set;}public int WeekendMask {get;set;}=PaymentCalendar.DefaultWeekendMask;
    public string Source {get;set;}="";public string Title {get;set;}="";
    public Severity Severity {get;set;}
    public string Transition {get;set;}="ПРОБЛЕМА";
    public DateTime CreatedAt {get;set;}=DateTime.UtcNow;
    public DateTime? HandledAt {get;set;}
    public string Result {get;set;}="";
}
public static class NetworkNotifications
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,SemaphoreSlim> DeliveryGates=new(StringComparer.OrdinalIgnoreCase);
    public static void Capture(NetworkState state,NetworkHistory history,string name,List<NetworkRule> rules,DateTime now,int availabilityDelaySeconds=0)
    {
        foreach(var problem in history.Problems){
            var rule=rules.SingleOrDefault(r=>r.Id==problem.RuleId);
            var availability=problem.Kind=="Interface";
            if(problem.Open&&!problem.OpenQueued){
                if(availability && problem.HealthyChecks>0)continue;
                if(availability && now-(problem.DownSince??problem.OpenedAt??now)<TimeSpan.FromSeconds(availabilityDelaySeconds))continue;
                problem.OpenQueued=true;
                if(rule?.Notify!=false)state.Notifications.Add(new(){Topic=TelegramTopics.Infer(availability?"network-availability:"+problem.Id:"","Сеть · "+name,problem.Title,problem.Severity),EntityKey=availability?"network-availability:"+problem.Id:"network:"+problem.Id,DeviceId=history.DeviceId,Source="Сеть · "+name,Title=problem.Title+"\n"+NetworkIssueAdvice.Describe(problem,rule,false),Severity=problem.Severity,CreatedAt=now});
            }
            if(!problem.Open&&problem.OpenQueued&&!problem.CloseQueued&&problem.RestoredAt!=null){
                problem.CloseQueued=true;
                if(availability){
                    var opening=state.Notifications.LastOrDefault(n=>n.EntityKey=="network-availability:"+problem.Id);
                    if(opening!=null&&opening.HandledAt==null){opening.HandledAt=now;opening.Result="Восстановлено до отправки";continue;}
                    if(opening!=null&&opening.Result!="Отправлено")continue;
                }
                if(rule?.Notify!=false&&rule?.NotifyRecovery!=false)state.Notifications.Add(new(){EntityKey=availability?"network-availability:"+problem.Id:"network:"+problem.Id,DeviceId=history.DeviceId,Topic=TelegramTopics.Infer("","Сеть · "+name,problem.Title,problem.Severity),Source="Сеть · "+name,Title=problem.Title+" · "+problem.Resolution,Severity=problem.Severity,Transition="ЗАКРЫТО / ВОССТАНОВЛЕНО",CreatedAt=now});
            }
        }
        // Never discard unsent notifications. Retain a bounded record of completed sends.
        state.Notifications=state.Notifications.Where(n=>n.HandledAt==null).Concat(state.Notifications.Where(n=>n.HandledAt!=null).OrderByDescending(n=>n.HandledAt).Take(1000)).ToList();
    }
    public static string Format(NetworkNotification n,string? subject=null)
    {
        string Cut(string s,int count)=>s.Length<=count?s:s[..count]+"…";
        var level=n.Severity==Severity.Critical?"КРИТИЧНО":n.Severity==Severity.Warning?"ПРЕДУПРЕЖДЕНИЕ":"ИНФОРМАЦИЯ";
        return $"{TelegramTopics.Emoji(n.Severity,n.Transition.StartsWith("ЗАКРЫТО"))} SysAdmin Assistant · {ProductText.Get(n.Transition)} · {ProductText.Get(level)}\n{Cut(ProductText.Get(n.Source),300)}\n{(string.IsNullOrWhiteSpace(subject)?"":Cut(ProductText.Get(subject),500)+"\n")}{Cut(ProductText.Language=="en"?ProductText.Get(n.Title):n.Title,2600)}\n{n.CreatedAt.ToServerTime():dd.MM.yyyy HH:mm:ss}";
    }
    private static bool Accepted(TelegramSettings s,NetworkNotification n)=>s.Enabled&&s.AcceptsEvent(n.Severity,n.Topic.Length>0?n.Topic:TelegramTopics.Infer(n.EntityKey,n.Source,n.Title,n.Severity),n.Transition.StartsWith("ЗАКРЫТО"));
    public static async Task FlushAsync(NetworkStore store,TelegramSettings settings,CancellationToken token,Func<string,CancellationToken,Task>? send=null,DateTime? deliveryTime=null,Func<NetworkNotification,string>? subject=null,Func<NetworkNotification,bool>? kscFilter=null)
    {
        bool Accept(NetworkNotification n)=>n.Topic=="ksc.security"&&kscFilter!=null?settings.Enabled&&kscFilter(n):Accepted(settings,n);
        var gate=DeliveryGates.GetOrAdd(Path.GetFullPath(store.DirectoryPath),_=>new(1,1));
        await gate.WaitAsync(token);
        try {
        var localNow=deliveryTime??AppTime.Now;
        var pending=store.State(s=>{
            RouterAuditStore.CoalescePending(s);
            BackupIncidentLifecycle.CoalescePending(s);
            if(!settings.TopicEnabled("updates.available"))UpdateNotifications.Suppress(s);
            foreach(var n in s.Notifications.Where(n=>n.HandledAt==null&&!Accept(n))){n.HandledAt=DateTime.UtcNow;n.Result="Отключено настройками Telegram";}
            return s.Notifications.Where(n=>n.HandledAt==null&&(!n.WorkingDaysOnly||PaymentCalendar.IsWorkingDay(localNow,n.WeekendMask))).OrderBy(n=>n.CreatedAt).Take(20).ToArray();
        },true);
        foreach(var n in pending){
            if(!store.State(s=>s.Notifications.Any(x=>x.Id==n.Id&&x.HandledAt==null)))continue;
            var accepted=Accept(n);
            if(accepted){var text=Format(n,subject?.Invoke(n));if(send!=null)await send(text,token);else await new TelegramMessageThreads(store.DirectoryPath).SendAsync(settings,"network:"+n.Id,n.EntityKey,n.Transition.StartsWith("ЗАКРЫТО",StringComparison.Ordinal),text,token);}
            store.State(s=>{var current=s.Notifications.SingleOrDefault(x=>x.Id==n.Id);if(current!=null&&current.HandledAt==null){current.HandledAt=DateTime.UtcNow;current.Result=accepted?"Отправлено":"Отключено настройками Telegram";}return true;},true);
            if(accepted)await Task.Delay(1100,token);
        }
        } finally {gate.Release();}
    }
}
