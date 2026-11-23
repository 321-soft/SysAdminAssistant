using SysAdminAssistant.Core;
internal static class WorkstationInstallerBundleSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var root=Directory.CreateTempSubdirectory("saa-workstation-setup-");
        try{
            var launcher=Path.Combine(root.FullName,"Uninstall.exe");var msi=Path.Combine(root.FullName,"original.msi");var setup=Path.Combine(root.FullName,"Station.Setup.exe");
            File.WriteAllBytes(launcher,[0x4d,0x5a,1,2]);File.WriteAllText(msi,"fixture MSI with enrollment data");
            check("Unbundled removal tool is not mistaken for a workstation installer",!ProductSetupBundle.HasPayload(launcher));
            WorkstationInstallerBundle.Build(launcher,msi,setup);File.Delete(msi);File.Delete(launcher);
            var extracted=Path.Combine(root.FullName,"extracted");
            ProductSetupBundle.Extract(setup,extracted,(_,_)=>{});
            check("A workstation Setup contains its MSI without sibling files",ProductSetupBundle.HasPayload(setup)&&File.ReadAllText(Path.Combine(extracted,WorkstationInstallerBundle.PackageName))=="fixture MSI with enrollment data");
            check("Workstation setup extracts exactly the embedded package",Directory.GetFiles(extracted).Length==1);
        }finally{root.Delete(true);}
    }
}
