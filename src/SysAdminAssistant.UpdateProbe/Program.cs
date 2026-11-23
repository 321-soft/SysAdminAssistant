using System.Security.Principal;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Agent;
namespace SysAdminAssistant.UpdateProbe;
internal static class Program
{
 private static void Safe(string path){for(var p=new DirectoryInfo(path);p!=null;p=p.Parent)if(p.Exists&&(p.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Ссылки запрещены"));}
 public static int Main(string[] args){if(!OperatingSystem.IsWindows()||args.Length!=0)return 1;try{using var identity=WindowsIdentity.GetCurrent();if(identity.User?.Value!="S-1-5-18")return 2;
 var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant Agent");if(!Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(root,StringComparison.OrdinalIgnoreCase))return 3;Safe(root);var dir=Path.Combine(root,"UpdateCache");Safe(dir);Directory.CreateDirectory(dir);Safe(dir);
 var path=Path.Combine(dir,"updates.json");if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return 4;
 var value=new WindowsUpdateSnapshot(DateTime.UtcNow,WindowsUpdateQuery.Read());var temp=Path.Combine(dir,Guid.NewGuid().ToString("N")+".tmp");try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){JsonSerializer.Serialize(f,value,AgentWire.Json);f.Flush(true);}File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}return 0;
 }catch{return 5;}}
}
