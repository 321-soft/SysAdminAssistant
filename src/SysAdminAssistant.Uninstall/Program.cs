using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
namespace SysAdminAssistant.Uninstall;
internal static class Program
{
    [STAThread]private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try{
            // Execute from an administrator-only temporary copy so installed files can be removed.
            if(!args.Contains("--relocated")){
                var root=Path.Combine(Path.GetTempPath(),"SAA-Uninstall-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
                foreach(var sid in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                new DirectoryInfo(root).SetAccessControl(acl);
                var name=Path.GetFileName(Environment.ProcessPath!);
#if SAA_SINGLE_FILE
                File.Copy(Environment.ProcessPath!,Path.Combine(root,name));
#else
                Copy(AppContext.BaseDirectory,root);
#endif
                var start=new ProcessStartInfo(Path.Combine(root,name)){UseShellExecute=true,WorkingDirectory=root};
                foreach(var arg in args)start.ArgumentList.Add(arg);start.ArgumentList.Add("--relocated");
                start.ArgumentList.Add("--source-dir");start.ArgumentList.Add(AppContext.BaseDirectory);Process.Start(start);return;
            }
            var language=Setup.SetupLanguage.Choose();if(language==null)return;
            if(Path.GetFileName(Environment.ProcessPath!).EndsWith(".Setup.exe",StringComparison.OrdinalIgnoreCase)||args.Contains("--install-workstation")||Path.GetFileName(Environment.ProcessPath!).Equals("WorkstationSetupLauncher.exe",StringComparison.OrdinalIgnoreCase)||Core.ProductSetupBundle.HasPayload(Environment.ProcessPath!))Application.Run(new WorkstationSetupWindow(args,language=="en"));
            else Application.Run(new RemovalWindow(args,language=="en"));
        }catch(Exception e){MessageBox.Show(e.Message,"SysAdmin Assistant",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    private static void Copy(string source,string target)
    {
        foreach(var item in Directory.EnumerateFileSystemEntries(source)){
            if((File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0)throw new IOException("A filesystem link was found in the removal tools.");
            var dest=Path.Combine(target,Path.GetFileName(item));
            if(Directory.Exists(item)){Directory.CreateDirectory(dest);Copy(item,dest);}else File.Copy(item,dest);
        }
    }
}
