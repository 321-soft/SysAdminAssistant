using SysAdminAssistant.Core;
public static class AgentTemplateSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var folder=Path.Combine(Path.GetTempPath(),"SAA-template-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            foreach(var file in AgentTemplate.RequiredScripts.Concat(new[]{"AgentSetupLauncher.exe","Agent/SysAdminAssistant.Agent.exe","Setup/SysAdminAssistant.AgentSetup.exe"})){var path=Path.Combine(folder,file);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"fixture");}
            AgentTemplate.Validate(folder);check("Published operational agent template needs no historical Markdown files",!Directory.EnumerateFiles(folder,"*.md").Any());
            File.Delete(Path.Combine(folder,"INSTALL_AGENT.ps1"));bool rejected=false;try{AgentTemplate.Validate(folder);}catch(FileNotFoundException e){rejected=e.FileName==Path.Combine(folder,"INSTALL_AGENT.ps1");}check("Incomplete agent template identifies the missing operational file",rejected);
        }finally{Directory.Delete(folder,true);}
    }
}
