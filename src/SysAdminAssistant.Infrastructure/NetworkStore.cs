using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed class NetworkConnection
{
    public int DeviceId {get;set;}public int Port {get;set;}=8729;public string User {get;set;}="";public string ProtectedPassword {get;set;}="";public string CertificateSha256 {get;set;}="";
    public string? BoundHost {get;set;}
    public string UpdateChannel {get;set;}="stable";
    public int RulePolicyVersion {get;set;}
    public List<NetworkRule>? Rules {get;set;}
    public string DictionaryId {get;set;}="generic-network";public Dictionary<string,string[]> Interfaces {get;set;}=[];
}
public sealed record NetworkConnectionSave(int DeviceId,int Port,string User,string Password,string CertificateSha256,string DictionaryId,Dictionary<string,string[]> Interfaces,string ExpectedHash,List<NetworkRule>? Rules=null,string UpdateChannel="stable");
public sealed record NetworkConnectionView(int DeviceId,int Port,string User,bool HasPassword,string CertificateSha256,string DictionaryId,Dictionary<string,string[]> Interfaces,string Hash,List<NetworkRule>? Rules=null,string UpdateChannel="stable");
public sealed record RouterPollEntry(DateTime StartedUtc,DateTime CompletedUtc,bool Success,string Error);
public sealed class NetworkHistory
{
    public List<RouterPollEntry> Polls {get;set;}=[];
    public int LogIntakeVersion {get;set;}
    public DateTime? LastSuccessfulPollUtc {get;set;}
    public RouterAuditState Audit {get;set;}=new();
    public Dictionary<string,bool> InterfaceStates {get;set;}=[];
    public DateTime? LastCompleteLogs {get;set;}public DateTime? LogBlindSince {get;set;}public bool LogsInitialized {get;set;}public string Host {get;set;}="";public int DeviceId {get;set;}public DateTime? CheckedAt {get;set;}public RouterSnapshot? Snapshot {get;set;}public string Error {get;set;}="";public bool Configured {get;set;}
    public List<NetworkProblem> Problems {get;set;}=[];public List<NetworkLog> Logs {get;set;}=[];
}
public sealed class NetworkState {public KscLedger Ksc {get;set;}=new();public Dictionary<string,AvailabilityAlertState> AvailabilityAlerts {get;set;}=[];public List<NetworkNotification> Notifications {get;set;}=[];public Dictionary<string,string> ApplicationStates {get;set;}=[];public List<NetworkConnection> Connections {get;set;}=[];public List<NetworkHistory> Histories {get;set;}=[];}
public sealed class NetworkStore(string directory)
{
    public string DirectoryPath=>directory;
    public static T Locked<T>(string path,Func<T> action)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        FileStream lease;
        while(true){try{lease=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);break;}catch(IOException)when(!timeout.IsCancellationRequested){Thread.Sleep(50);}}
        using(lease)return action();
    }
    private static T Read<T>(string path) where T:new()=>File.Exists(path)?JsonSerializer.Deserialize<T>(File.ReadAllText(path),RemoteData.Json)??throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Повреждён файл сетевого мониторинга")):new();
    private static void Write<T>(string path,T value){var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(tmp,JsonSerializer.Serialize(value,RemoteData.Json));File.Move(tmp,path,true);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
    public T State<T>(Func<NetworkState,T> action,bool write=false){var path=Path.Combine(directory,"network-state.json");return Locked(path,()=>{var state=Read<NetworkState>(path);var result=action(state);if(write)Write(path,state);return result;});}
    public NetworkDictionary[] Dictionaries(){var path=Path.Combine(directory,"network-dictionaries.json");return Locked(path,()=>new[]{NetworkDictionaries.Generic()}.Concat(Read<NetworkDictionaryPackage>(path).Dictionaries.Where(d=>d.Id!="generic-network")).ToArray());}
    public string ExportPackage()
    {
        var path=Path.Combine(directory,"network-dictionaries.json");
        return Locked(path,()=>JsonSerializer.Serialize(Read<NetworkDictionaryPackage>(path),RemoteData.Json));
    }
    public int Import(string json)
    {
        var imported=NetworkDictionaries.Parse(json);if(imported.Dictionaries.Any(d=>d.Id=="generic-network"))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Типовой словарь нельзя перезаписать импортом"));
        var path=Path.Combine(directory,"network-dictionaries.json");return Locked(path,()=>{var saved=Read<NetworkDictionaryPackage>(path);foreach(var item in imported.Dictionaries){saved.Dictionaries.RemoveAll(d=>d.Id==item.Id);saved.Dictionaries.Add(item);}Write(path,saved);return imported.Dictionaries.Count;});
    }
    public DictionaryChanges ApplyPackage(DictionaryImport request,bool commit)
    {
        var incoming=NetworkDictionaries.Parse(request.Json);
        if(incoming.Dictionaries.Any(d=>d.Id=="generic-network"))throw new ArgumentException("Built-in dictionary cannot be replaced.");
        var path=Path.Combine(directory,"network-dictionaries.json");
        return Locked(path,()=>{
            var saved=Read<NetworkDictionaryPackage>(path);
            string Encode<T>(T item)=>JsonSerializer.Serialize(item,RemoteData.Json);
            var revision=AgentWire.Hash(Encode(saved));var changes=new List<string>();int changed=0;
            if(commit&&request.ExpectedRevision!=revision)throw new InvalidOperationException("Dictionary collection changed; preview again.");
            var original=Encode(saved);
            foreach(var d in incoming.Dictionaries){
                var old=saved.Dictionaries.SingleOrDefault(v=>v.Id==d.Id);
                if(old!=null&&Encode(old)==Encode(d)){changes.Add("= "+d.Name);continue;}
                changes.Add((old==null?"+ ":"Δ ")+d.Name+" ["+d.Id+"]");changed++;
                foreach(var rule in d.Rules){var prior=old?.Rules.SingleOrDefault(v=>v.Id==rule.Id);if(prior==null||Encode(prior)!=Encode(rule))changes.Add("   "+(prior==null?"+ ":"Δ ")+rule.Name+" ["+rule.Id+"]");}
                if(old!=null){foreach(var rule in old.Rules.Where(v=>!d.Rules.Any(n=>n.Id==v.Id)))changes.Add("   − "+rule.Name);saved.Dictionaries.Remove(old);}
                saved.Dictionaries.Add(d);
            }
            if(commit&&changed>0){Write(path+".previous",JsonSerializer.Deserialize<NetworkDictionaryPackage>(original,RemoteData.Json)!);Write(path,saved);}
            return new DictionaryChanges(revision,changes.ToArray(),changed);
        });
    }
    public NetworkConnection? MonitorConnection(int id){
        var current=State(s=>s.Connections.SingleOrDefault(c=>c.DeviceId==id));if(current==null||current.RulePolicyVersion>=1)return current;
        return State(s=>{var c=s.Connections.SingleOrDefault(c=>c.DeviceId==id);if(c==null)return null;
            if(c.RulePolicyVersion<1){if(c.Rules!=null)foreach(var rule in c.Rules.Where(r=>r.Id is "auth-failure-info" or "auth-failure-warning" or "auth-bruteforce"))if(rule.AutoCloseSeconds is 300 or 900 or 1800)rule.AutoCloseSeconds=3600;c.RulePolicyVersion=1;}return c;
        },true);
    }
    public NetworkConnectionView Connection(int id)=>State(s=>View(s.Connections.SingleOrDefault(c=>c.DeviceId==id)??new(){DeviceId=id}));
    private static NetworkConnectionView View(NetworkConnection c)=>new(c.DeviceId,c.Port,c.User,c.ProtectedPassword.Length>0,c.CertificateSha256,c.DictionaryId,c.Interfaces,RemoteData.Hash(c),c.Rules,c.UpdateChannel);
    public void BindLegacyHosts(IReadOnlyDictionary<int,string> hosts)=>State(s=>{
        foreach(var connection in s.Connections.Where(c=>c.BoundHost==null&&c.ProtectedPassword.Length>0))
            connection.BoundHost=hosts.GetValueOrDefault(connection.DeviceId,"");
        return true;
    },true);

    public static RouterSettings Target(string host,NetworkConnection? connection,RouterSettings legacy)
    {
        if(connection==null)return new(){Host=host,Port=legacy.Port,User=legacy.User,CertificateSha256=legacy.CertificateSha256,
            ProtectedPassword=StoredCredential.Reuse(legacy.ProtectedPassword,StoredCredential.SameHost(host,legacy.Host))};
        return new(){Host=host,Port=connection.Port,User=connection.User,CertificateSha256=connection.CertificateSha256,
            ProtectedPassword=StoredCredential.Reuse(connection.ProtectedPassword,StoredCredential.SameHost(host,connection.BoundHost))};
    }

    public void Save(NetworkConnectionSave request,string host)
    {
        if(request.UpdateChannel is not("stable" or "long-term"))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Канал обновлений: stable или long-term"));
        if(request.User==null||request.Password==null||request.CertificateSha256==null||request.DictionaryId==null||request.Interfaces==null||request.ExpectedHash==null)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пустые настройки подключения"));
        if(request.Port is <1 or >65535||request.User.Length>200||request.Password.Length>10000||request.Interfaces.Count>100||request.Interfaces.Any(p=>p.Value==null||p.Value.Length>100||p.Value.Any(v=>v==null||v.Length>200)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверные настройки подключения"));
        var dictionary=Dictionaries().SingleOrDefault(d=>d.Id==request.DictionaryId)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Словарь не найден"));
        if(request.Interfaces.Keys.Any(key=>dictionary.Rules.All(r=>r.Id!=key)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестное правило словаря"));
        if(request.Rules!=null)NetworkDictionaries.ValidateRules(request.Rules);
        var pin=RouterOsClient.NormalizePin(request.CertificateSha256);
        State(state=>{var config=state.Connections.SingleOrDefault(c=>c.DeviceId==request.DeviceId);var expected=config??new(){DeviceId=request.DeviceId};if(RemoteData.Hash(expected)!=request.ExpectedHash)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Подключение изменилось. Обновите карточку."));
            config??=new(){DeviceId=request.DeviceId};
            if(request.Password.Length==0)StoredCredential.Reuse(config.ProtectedPassword,StoredCredential.SameEndpoint(host,request.Port,request.User,config.BoundHost,config.Port,config.User)&&pin==config.CertificateSha256);
            config.BoundHost=host;config.RulePolicyVersion=1;config.UpdateChannel=request.UpdateChannel;config.Port=request.Port;config.User=request.User;config.CertificateSha256=pin;if(config.DictionaryId!=request.DictionaryId)config.Rules=null;if(request.Rules!=null)config.Rules=request.Rules;config.DictionaryId=request.DictionaryId;config.Interfaces=request.Interfaces;if(request.Password.Length>0)config.ProtectedPassword=LocalSecret.Protect(request.Password);if(!state.Connections.Contains(config))state.Connections.Add(config);
            var history=state.Histories.SingleOrDefault(h=>h.DeviceId==request.DeviceId);if(history!=null){
                if(config.Rules!=null)foreach(var problem in history.Problems.Where(p=>p.Open)){
                    var rule=config.Rules.SingleOrDefault(r=>r.Id==problem.RuleId);if(rule!=null){if(rule.Severity>problem.Severity)problem.OpenQueued=false;problem.Severity=rule.Severity;}
                }
                history.Snapshot=null;history.CheckedAt=null;history.Error="";history.Configured=config.User.Length>0;}
            return true;},true);
    }
}
