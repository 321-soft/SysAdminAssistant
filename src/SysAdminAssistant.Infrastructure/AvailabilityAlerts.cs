using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed class AvailabilityAlertState
{
    public DateTime FailedSinceUtc {get;set;}
    public DateTime LastCheckedUtc {get;set;}
    public string? NotificationId {get;set;}
    public string? ErrorSignature {get;set;}
}
public static class AvailabilityAlerts
{
    public static void Observe(NetworkState state,string key,bool bad,DateTime checkedAt,int delayMinutes,int pollSeconds,Severity severity,string source,string title,string recovery,string? errorSignature=null)
    {
        var at=AppTime.ToUtc(checkedAt);
        state.AvailabilityAlerts.TryGetValue(key,out var pending);
        if(pending!=null&&at<=pending.LastCheckedUtc)return;
        if(!bad){
            if(pending?.NotificationId is string id){
                var opening=state.Notifications.SingleOrDefault(n=>n.Id==id);
                if(opening?.Result=="Отправлено")state.Notifications.Add(new(){EntityKey=key,Source=source,Title=recovery,Severity=severity,Transition="ЗАКРЫТО / ВОССТАНОВЛЕНО",CreatedAt=at});
                else if(opening!=null&&opening.HandledAt==null){opening.HandledAt=at;opening.Result="Восстановлено до отправки";}
            }
            state.AvailabilityAlerts.Remove(key);return;
        }
        if(pending==null || pending.NotificationId==null&&at-pending.LastCheckedUtc>TimeSpan.FromSeconds(Math.Max(600,pollSeconds*3)))
            state.AvailabilityAlerts[key]=pending=new(){FailedSinceUtc=at};
        pending.LastCheckedUtc=at;
        var signatureChanged=errorSignature!=null&&pending.ErrorSignature!=null&&errorSignature!=pending.ErrorSignature;pending.ErrorSignature=errorSignature;
        if(pending.NotificationId is string previousId&&state.Notifications.FirstOrDefault(n=>n.Id==previousId) is {} previous&&(severity>previous.Severity||signatureChanged)){var escalation=new NetworkNotification{EntityKey=key,Source=source,Title=title,Severity=severity,CreatedAt=at};pending.NotificationId=escalation.Id;state.Notifications.Add(escalation);return;}
        if(pending.NotificationId==null && at-pending.FailedSinceUtc>=TimeSpan.FromMinutes(delayMinutes)){
            var n=new NetworkNotification{EntityKey=key,Source=source,Title=title,Severity=severity,CreatedAt=at};
            pending.NotificationId=n.Id;state.Notifications.Add(n);
        }
    }
    public static void Retain(NetworkState state,HashSet<string> active,DateTime now)
    {
        foreach(var key in state.AvailabilityAlerts.Keys.Where(k=>!k.StartsWith("agent:",StringComparison.Ordinal)&&!k.StartsWith("agent-link:",StringComparison.Ordinal)&&!active.Contains(k)).ToArray()){
            var pending=state.AvailabilityAlerts[key];
            foreach(var n in state.Notifications.Where(n=>n.Id==pending.NotificationId&&n.HandledAt==null)){n.HandledAt=AppTime.ToUtc(now);n.Result="Проверка отключена или изменена";}
            state.AvailabilityAlerts.Remove(key);
        }
    }
}
