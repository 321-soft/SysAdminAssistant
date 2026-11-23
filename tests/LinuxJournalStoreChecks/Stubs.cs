using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public class NetworkNotification {public string Id {get;set;}=Guid.NewGuid().ToString();public string EntityKey {get;set;}="";public string Topic {get;set;}="";public string Source {get;set;}="";public string Title {get;set;}="";public Severity Severity {get;set;} public DateTime CreatedAt {get;set;} public DateTime? HandledAt {get;set;}public string Result {get;set;}="";public string Transition {get;set;}="";}
public class NetworkState {public List<NetworkNotification> Notifications {get;set;}=[];}
public class NetworkStore {public static T Locked<T>(string path,Func<T> f)=>f();public T State<T>(Func<NetworkState,T> f,bool save=false)=>f(new());}
public static class UpdateNotifications {public static bool IsAvailability(string s)=>false;public static void Suppress(NetworkState s){}}
