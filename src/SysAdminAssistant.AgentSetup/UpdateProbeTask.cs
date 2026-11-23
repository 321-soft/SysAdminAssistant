using System.IO;
using System.Security.Principal;
using System.Runtime.InteropServices;
namespace SysAdminAssistant.AgentSetup;
internal static class UpdateProbeTask
{
 private const string Name="SysAdminAssistantAgentUpdateRead",Source="SysAdminAssistant.UpdateProbe/v1",Acl="D:P(A;;GA;;;SY)(A;;GA;;;BA)";
 private static readonly string Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant Agent");
 private static string Binary=>Path.Combine(Target,"SysAdminAssistant.UpdateProbe.exe");
 private static dynamic Connect(){dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;service.Connect();return service;}
 private static dynamic? Existing(dynamic folder){try{return folder.GetTask(Name);}catch(Exception e)when(SysAdminAssistant.Core.InstallerTaskErrors.Missing(e)){return null;}}
 private static bool SystemUser(string user){if(user=="S-1-5-18")return true;try{return ((SecurityIdentifier)new NTAccount(user).Translate(typeof(SecurityIdentifier))).Value=="S-1-5-18";}catch(IdentityNotMappedException){return false;}}
 private static void Own(dynamic task){dynamic d=task.Definition;if((string)d.RegistrationInfo.Source!=Source||(int)d.Actions.Count!=1||(int)d.Actions[1].Type!=0||!string.Equals((string)d.Actions[1].Path,Binary,StringComparison.OrdinalIgnoreCase)||!string.IsNullOrEmpty((string?)d.Actions[1].Arguments)||!SystemUser((string)d.Principal.UserId))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Имя задачи обновлений занято другой конфигурацией"));}
 public static string? Pause(){dynamic service=Connect();dynamic folder=service.GetFolder(@"\");dynamic? task=Existing(folder);if(task==null)return null;Own(task);string xml=task.Xml;bool enabled=task.Enabled;try{task.Enabled=false;if((int)task.State==4)task.Stop(0);var end=DateTime.UtcNow.AddSeconds(20);while((int)task.GetInstances(0).Count>0){if(DateTime.UtcNow>end)throw new TimeoutException(global::SysAdminAssistant.Core.ProductText.Get("Не завершилось чтение обновлений"));Thread.Sleep(100);}return xml;}catch{task.Enabled=enabled;throw;}}
 public static void Install(){if(!File.Exists(Binary))throw new FileNotFoundException(global::SysAdminAssistant.Core.ProductText.Get("Нет локального сборщика обновлений"),Binary);dynamic service=Connect();dynamic folder=service.GetFolder(@"\");dynamic? existing=Existing(folder);if(existing!=null)Own(existing);dynamic d=service.NewTask(0);d.RegistrationInfo.Source=Source;d.RegistrationInfo.Description="Только чтение локального кэша Windows Update. Без сетевого поиска, загрузки, установки и входящих команд.";d.Principal.UserId="S-1-5-18";d.Principal.LogonType=5;d.Principal.RunLevel=1;
 d.Settings.AllowDemandStart=true;d.Settings.Enabled=true;d.Settings.StartWhenAvailable=true;d.Settings.DisallowStartIfOnBatteries=false;d.Settings.StopIfGoingOnBatteries=false;d.Settings.ExecutionTimeLimit="PT3M";d.Settings.MultipleInstances=2;
 dynamic trigger=d.Triggers.Create(1);trigger.StartBoundary=DateTime.Now.AddMinutes(15).ToString("s");trigger.Repetition.Interval="PT15M";trigger.Enabled=true;dynamic boot=d.Triggers.Create(8);boot.Delay="PT30S";boot.Enabled=true;
 dynamic action=d.Actions.Create(0);action.Path=Binary;action.Arguments="";action.WorkingDirectory=Target;
 dynamic task=folder.RegisterTaskDefinition(Name,d,6,"SYSTEM",null,5,Acl);task.Run(null);
 }
 public static void Restore(string? xml){dynamic service=Connect();dynamic folder=service.GetFolder(@"\");dynamic? task=Existing(folder);if(task!=null){Own(task);task.Enabled=false;if((int)task.State==4)task.Stop(0);}if(xml==null){if(task!=null)folder.DeleteTask(Name,0);}else folder.RegisterTask(Name,xml,6,"SYSTEM",null,5,Acl);}
 public static void Remove(){_=Pause();Restore(null);}
}
