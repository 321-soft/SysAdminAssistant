using SysAdminAssistant.Core;
internal static class UninstallPolicySpecs
{
    public static void Run(Action<string,bool> check)
    {
        bool Fails(Action action){try{action();return false;}catch(Exception e)when(e is IOException or ArgumentException){return true;}}
        check("Uninstaller refuses every SQL Server system database",new[]{"master","MODEL","msdb","TempDB"}.All(n=>Fails(()=>UninstallPolicy.DatabaseName(n))));
        check("Uninstaller rejects empty and control-character database names",new[]{""," ","name\n",new string('x',129)}.All(n=>Fails(()=>UninstallPolicy.DatabaseName(n))));
        check("Uninstaller quotes a legal database name without SQL injection",UninstallPolicy.DatabaseName("SAA];DROP DATABASE other;--")=="[SAA]];DROP DATABASE other;--]");
        check("Uninstaller refuses the filesystem root",Fails(()=>UninstallPolicy.PathForRemoval(Path.GetPathRoot(Path.GetTempPath())!,[])));
        var temp=Directory.CreateTempSubdirectory("saa-removal-policy-");
        try{
            var owned=Directory.CreateDirectory(Path.Combine(temp.FullName,"owned"));File.WriteAllText(Path.Combine(owned.FullName,"keep.db"),"data");
            check("Uninstaller refuses protected parent and relative paths",Fails(()=>UninstallPolicy.PathForRemoval(temp.FullName,[temp.FullName]))&&Fails(()=>UninstallPolicy.PathForRemoval("relative",[])));
            UninstallPolicy.CheckTree(owned.FullName,[temp.FullName]);
            check("Uninstaller safety inspection never deletes retained data",File.ReadAllText(Path.Combine(owned.FullName,"keep.db"))=="data");
            if(!OperatingSystem.IsWindows()){
                var outside=Directory.CreateDirectory(Path.Combine(temp.FullName,"outside"));var link=Path.Combine(owned.FullName,"link");Directory.CreateSymbolicLink(link,outside.FullName);
                check("Uninstaller refuses directory links inside a removal tree",Fails(()=>UninstallPolicy.CheckTree(owned.FullName,[temp.FullName])));
                check("Uninstaller refuses paths through linked parents",Fails(()=>UninstallPolicy.PathForRemoval(Path.Combine(link,"child"),[temp.FullName])));
                Directory.Delete(link);
            }
        }finally{temp.Delete(true);}
        check("License includes paid services and prohibits software resale",LicenseAgreement.Text("EN").Contains("Paid professional services are permitted")&&LicenseAgreement.Text("EN").Contains("sell, resell, rent, lease")&&LicenseAgreement.Text("RU").Contains("SOURCE LICENSE"));
        using(ProductText.Scope("en"))check("New setup and immediate language-switch labels translate to English",ProductText.Get("Удалить сервер…")=="Uninstall server…"&&ProductText.Get("Язык интерфейса применяется сразу. Язык серверных уведомлений настраивается отдельно.").StartsWith("The interface language changes immediately."));
        using(ProductText.Scope("ru"))check("Server management language can translate English labels back",ProductText.Get("Uninstall server…")=="Удалить сервер…");
    }
}
