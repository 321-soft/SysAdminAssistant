using System.IO;
using System.Diagnostics.Eventing.Reader;
using System.Security.AccessControl;
using System.Security.Principal;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.AgentSetup;
internal static class JournalPermissions
{
 public static void Apply(string service,string data,bool remove=false){var sid=(SecurityIdentifier)new NTAccount("NT SERVICE",service).Translate(typeof(SecurityIdentifier));var failures=new List<string>();foreach(var name in AgentEvents.Channels){try{using var log=new EventLogConfiguration(name);var acl=new RawSecurityDescriptor(log.SecurityDescriptor);var dacl=acl.DiscretionaryAcl??throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нет DACL журнала"));for(int i=dacl.Count-1;i>=0;i--)if(dacl[i] is CommonAce a&&a.SecurityIdentifier==sid&&a.AceQualifier==AceQualifier.AccessAllowed&&a.AccessMask==1)dacl.RemoveAce(i);if(!remove)dacl.InsertAce(dacl.Count,new CommonAce(AceFlags.None,AceQualifier.AccessAllowed,1,sid,false,null));log.SecurityDescriptor=acl.GetSddlForm(AccessControlSections.All);log.SaveChanges();}catch(EventLogNotFoundException){}catch(Exception e)when(e is UnauthorizedAccessException or EventLogException or InvalidOperationException){failures.Add(name+": "+e.Message);}}if(!remove){Directory.CreateDirectory(data);File.WriteAllLines(Path.Combine(data,"journal-access.txt"),failures.Count==0?["Права чтения журналов назначены SID службы. Политики домена могут их переопределить."]:failures);}}
}
