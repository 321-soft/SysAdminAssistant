using System.Text;
using System.Text.Json;
namespace SysAdminAssistant.ServerSetup;
internal sealed record ServerShellOptions(bool StartMenu=true,bool Desktop=true);
internal static class ServerShell
{
    private static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant Maintenance");
    public static string Manager=>Path.Combine(Root,"versions",typeof(ServerShell).Assembly.GetName().Version!.ToString(),"SysAdminAssistant.ServerSetup.exe");
    public static ServerShellOptions Load()=>File.Exists(Path.Combine(Root,"shell.json"))?JsonSerializer.Deserialize<ServerShellOptions>(File.ReadAllText(Path.Combine(Root,"shell.json")))??new():new();
    public static async Task ApplyAsync(ServerShellOptions? choices=null)
    {
        var path=Path.Combine(Root,"shell.json");choices??=Load();
        if((File.GetAttributes(Root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Management directory contains a filesystem link.");
        static string B(string text)=>Convert.ToBase64String(Encoding.Unicode.GetBytes(text));
        var script="$exe=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('"+B(Manager)+"'));$shell=New-Object -ComObject WScript.Shell;";
        foreach(var item in new[]{("CommonPrograms",choices.StartMenu),("CommonDesktopDirectory",choices.Desktop)}){
            script+="$link=Join-Path ([Environment]::GetFolderPath('"+item.Item1+"')) 'SysAdmin Assistant Server.lnk';";
            script+=item.Item2?"$s=$shell.CreateShortcut($link);$s.TargetPath=$exe;$s.Arguments='--manage';$s.WorkingDirectory=[IO.Path]::GetDirectoryName($exe);$s.IconLocation=$exe+',0';$s.Save();":"if(Test-Path -LiteralPath $link){Remove-Item -LiteralPath $link -Force};";
        }
        await SetupEngine.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),["-NoProfile","-NonInteractive","-EncodedCommand",B(script)]);
        var installed=await UpdateEngine.DetectAsync();
        var removal=Path.Combine(installed.Target,"Uninstall.exe");
        if(!File.Exists(removal))throw new FileNotFoundException("Uninstaller package is missing.",removal);
        using(var key=Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SysAdminAssistantServer")){
            key.SetValue("DisplayName","SysAdmin Assistant Server");key.SetValue("Publisher","321.su");key.SetValue("DisplayVersion",typeof(ServerShell).Assembly.GetName().Version!.ToString(3));
            key.SetValue("UninstallString","\""+removal+"\" --component server");key.SetValue("DisplayIcon",Manager+",0");key.SetValue("URLInfoAbout","https://assistant.321.su");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
        }
        var unlink="$shell=New-Object -ComObject WScript.Shell;$link=Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'SysAdmin Assistant Server — Uninstall.lnk';$s=$shell.CreateShortcut($link);$s.TargetPath=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('"+B(removal)+"'));$s.Arguments='--component server';$s.WorkingDirectory=[IO.Path]::GetDirectoryName($s.TargetPath);$s.Save();";
        await SetupEngine.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),["-NoProfile","-NonInteractive","-EncodedCommand",B(unlink)]);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temporary,JsonSerializer.Serialize(choices));File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
