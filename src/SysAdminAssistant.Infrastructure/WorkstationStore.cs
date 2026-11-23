using System.Security.Cryptography;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed class StationRecord
{
    public bool AutoUpdate {get;set;}
    public string Key {get;set;}="";
    public string Name {get;set;}="";
    public string Status {get;set;}="Pending";
    public DateTime Seen {get;set;}
    public DateTime? SampleAt {get;set;}
    public StationSample? Sample {get;set;}
    public Dictionary<string,DateTime> Since {get;set;}=[];
    public Dictionary<string,DateTime> Sent {get;set;}=[];
    public Dictionary<string,DateTime> Nonces {get;set;}=[];
}
public sealed class StationState
{
    public StationConfig Config {get;set;}=new();
    public HashSet<string> EnrollmentHashes {get;set;}=[];
    public string EnrollmentHash {get;set;}="";
    public Dictionary<Guid,StationRecord> Stations {get;set;}=[];
    public HashSet<string> BlockedKeys {get;set;}=[];
}
public sealed record StationNotification(Guid Id,string Name,StationAlert Alert);
public sealed class WorkstationStore(string directory)
{
    private readonly string path=Path.Combine(directory,"workstations.json");
    private T State<T>(Func<StationState,T> action,bool write=false)=>NetworkStore.Locked(path,()=>{
        var s=File.Exists(path)?JsonSerializer.Deserialize<StationState>(File.ReadAllBytes(path),AgentWire.Json)
            ??throw new InvalidDataException("Пустой реестр станций"):new();
        var result=action(s);
        if(write){
            var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllBytes(temp,JsonSerializer.SerializeToUtf8Bytes(s,AgentWire.Json));File.Move(temp,path,true);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        return result;
    });
    public string IssueEnrollment()=>State(NewEnrollment,true);
    private static string NewEnrollment(StationState s){
        var key=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        s.EnrollmentHashes.Add(StationWire.Hash(key));return key;
    }
    public StationConfig Config()=>State(s=>s.Config);
    public string Save(StationConfig c)=>State(s=>{
        if(c.LatestAgent is null||c.LatestAgent.Length>32||c.LatestAgent!=""&&!Version.TryParse(c.LatestAgent,out _)
           ||c.Thread is <=0||c.Telegram&&(!(c.Critical||c.Warning)||!c.UseServerTelegram&&string.IsNullOrWhiteSpace(c.Chat)))
            throw new ArgumentException("Проверьте версию агента, чат, тему и уровни Telegram");
        new TelegramSettings{ChatId=c.Chat??""}.Validate();
        var token=c.Rotate?NewEnrollment(s):"";
        s.Config=c with {Rotate=false,Chat=c.Chat??""};
        return token;
    },true);
    public StationReply Report(StationRequest e,DateTime now)
    {
        StationWire.Verify(e,now);
        return State(s=>{
            if(s.BlockedKeys.Contains(StationWire.Hash(e.Key)))return new StationReply("Denied");
            if(!s.Stations.TryGetValue(e.Id,out var r)){
                if(e.Payload!=null||e.Enrollment.Length!=64||
                    !(s.EnrollmentHashes.Contains(StationWire.Hash(e.Enrollment))||s.EnrollmentHash==StationWire.Hash(e.Enrollment)))
                    throw new UnauthorizedAccessException("Регистрация станции запрещена");
                if(s.Stations.Values.Any(x=>x.Key==e.Key))throw new UnauthorizedAccessException("Ключ уже зарегистрирован");
                if(s.Stations.Count>=2000)throw new ArgumentException("Достигнут лимит 2000 рабочих станций");
                r=new(){Key=e.Key,Name=e.Name,Seen=now};s.Stations.Add(e.Id,r);
            }
            if(r.Key!=e.Key)throw new UnauthorizedAccessException("Ключ станции не совпадает");
            if(r.Status=="Denied")return new StationReply("Denied");
            foreach(var old in r.Nonces.Where(x=>x.Value<now.AddMinutes(-10)).Select(x=>x.Key).ToArray())r.Nonces.Remove(old);
            if(r.Nonces.Count>=60||!r.Nonces.TryAdd(e.Nonce,now))throw new UnauthorizedAccessException("Повтор запроса или превышена частота");
            if(r.Status=="Pending"&&e.Payload!=null)throw new UnauthorizedAccessException("Сначала подтвердите станцию");
            r.Name=e.Name;r.Seen=now;
            if(e.Payload is {} payload){
                var sample=StationWire.Sample(payload);
                if(sample.Peripherals is {Partial:true,Devices.Length:0} failed && r.Sample?.Peripherals is {Devices.Length:>0} previous)
                    sample=sample with{Peripherals=previous with{Partial=true,Errors=failed.Errors,AttemptedUtc=failed.AttemptedUtc??failed.CapturedUtc}};
                if(r.SampleAt==null||now-r.SampleAt>TimeSpan.FromMinutes(10))
                    foreach(var code in r.Since.Keys.Where(x=>x!="reboot").ToArray())r.Since.Remove(code);
                var conditions=StationWire.Conditions(sample).Select(x=>x.Code).ToHashSet();
                foreach(var code in r.Since.Keys.Where(x=>!conditions.Contains(x)).ToArray())r.Since.Remove(code);
                foreach(var code in conditions)r.Since.TryAdd(code,now);
                foreach(var code in r.Sent.Keys.Where(x=>!r.Since.TryGetValue(x,out var at)||r.Sent[x]!=at).ToArray())r.Sent.Remove(code);
                r.Sample=sample;r.SampleAt=now;
            }
            return new StationReply(r.Status);
        },true);
    }
    private static StationAlert[] Alerts(StationRecord r,DateTime now)
    {
        if(r.Status!="Approved")return [];
        var last=r.SampleAt??r.Seen;
        if(now-last>TimeSpan.FromMinutes(10))
            return now>=StationWire.Workdays(last,3)?[new("offline","Нет связи три рабочих дня",Severity.Warning,last)]:[];
        return r.Sample==null?[]:StationWire.Conditions(r.Sample).Where(x=>r.Since.TryGetValue(x.Code,out var at)
            &&(x.Code=="reboot"?now>=StationWire.Workdays(at,2):now-at>=TimeSpan.FromMinutes(x.Minutes)))
            .Select(x=>new StationAlert(x.Code,x.Title,x.Level,r.Since[x.Code])).OrderByDescending(x=>x.Level).ToArray();
    }
    public StationView[] List(bool denied,DateTime now)=>State(s=>s.Stations.Where(x=>(x.Value.Status=="Denied")==denied)
        .Select(x=>new StationView(x.Key,x.Value.Name,x.Value.Status,x.Value.SampleAt??x.Value.Seen,x.Value.Sample,Alerts(x.Value,now),x.Value.AutoUpdate))
        .OrderByDescending(x=>x.Alerts.Any(a=>a.Level==Severity.Critical)).ThenByDescending(x=>x.Alerts.Length).ThenBy(x=>x.Name).ToArray());
    public bool SetAutoUpdate(StationUpdatePolicy policy)=>State(s=>{
        if(!s.Stations.TryGetValue(policy.Id,out var station)||station.Status!="Approved")
            throw new InvalidOperationException("Автообновление доступно только подтверждённым станциям.");
        station.AutoUpdate=policy.Enabled;return true;
    },true);
    public bool AuthorizeUpdate(StationRequest request,DateTime now)
    {
        StationWire.Verify(request,now);
        if(request.Enrollment.Length!=0)throw new UnauthorizedAccessException("Запрос обновления не должен содержать ключ регистрации.");
        return State(s=>{
            if(!s.Stations.TryGetValue(request.Id,out var station)||station.Status!="Approved"||station.Key!=request.Key||s.BlockedKeys.Contains(StationWire.Hash(request.Key)))
                throw new UnauthorizedAccessException("Станция не подтверждена.");
            if(!station.AutoUpdate)return false;
            foreach(var old in station.Nonces.Where(x=>x.Value<now.AddMinutes(-10)).Select(x=>x.Key).ToArray())station.Nonces.Remove(old);
            if(station.Nonces.Count>=60||!station.Nonces.TryAdd(request.Nonce,now))
                throw new UnauthorizedAccessException("Повтор запроса или превышена частота.");
            return true;
        },true);
    }
    public bool Decide(Guid id,string action)=>State(s=>{
        if(!s.Stations.TryGetValue(id,out var r))throw new ArgumentException("Станция не найдена");
        if(action=="approve"&&r.Status=="Pending")r.Status="Approved";
        else if(action=="deny"&&r.Status!="Denied"){r.Status="Denied";r.AutoUpdate=false;r.Sample=null;r.SampleAt=null;r.Since.Clear();r.Sent.Clear();r.Nonces.Clear();}
        else if(action=="delete"&&r.Status=="Denied"){s.BlockedKeys.Add(StationWire.Hash(r.Key));s.Stations.Remove(id);}
        else throw new ArgumentException("Действие недоступно для текущего состояния");
        return true;
    },true);
    public StationNotification[] Pending(DateTime now)=>State(s=>!s.Config.Telegram?[]:s.Stations
        .SelectMany(x=>Alerts(x.Value,now).Where(a=>(a.Level==Severity.Critical?s.Config.Critical:s.Config.Warning)
            &&x.Value.Sent.GetValueOrDefault(a.Code)!=a.Since).Select(a=>new StationNotification(x.Key,x.Value.Name,a)))
        .OrderByDescending(x=>x.Alert.Level).Take(10).ToArray());
    public bool Sent(StationNotification[] items,DateTime now)=>State(s=>{
        foreach(var item in items)
            if(s.Stations.TryGetValue(item.Id,out var r)&&Alerts(r,now).Contains(item.Alert))r.Sent[item.Alert.Code]=item.Alert.Since;
        return true;
    },true);
}
