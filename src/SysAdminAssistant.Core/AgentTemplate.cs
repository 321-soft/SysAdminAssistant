namespace SysAdminAssistant.Core;
/// <summary>The operational template contract. Documentation is not an installation dependency.</summary>
public static class AgentTemplate
{
    public static IReadOnlyList<string> RequiredScripts {get;}=Array.AsReadOnly(new[]{"INSTALL_AGENT.cmd","INSTALL_AGENT.ps1","VerifyAgentWindows.ps1"});
    public static void Validate(string root)
    {
        foreach(var name in RequiredScripts.Concat(new[]{"AgentSetupLauncher.exe","Agent/SysAdminAssistant.Agent.exe","Setup/SysAdminAssistant.AgentSetup.exe"}))
            if(!File.Exists(Path.Combine(root,name)))throw new FileNotFoundException(ProductText.Get("Неполный шаблон установщика: ")+name,Path.Combine(root,name));
    }
}
