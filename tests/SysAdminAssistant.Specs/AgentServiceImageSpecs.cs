using SysAdminAssistant.AgentSetup;

internal static class AgentServiceImageSpecs
{
    public static void Run(Action<string,bool> check)
    {
        const string path=@"C:\Program Files\SysAdmin Assistant Agent\SysAdminAssistant.Agent.exe";
        var command="\""+path+"\"";
        check("Windows maintenance accepts the exact quoted SCM image",AgentServiceImage.Matches(" "+command.ToLowerInvariant()+" ",path));
        check("Windows maintenance rejects ambiguous and modified SCM commands",
            !AgentServiceImage.Matches(path,path)&&!AgentServiceImage.Matches(command+" --option",path)
            &&!AgentServiceImage.Matches("\"C:\\Other.exe\"",path)&&!AgentServiceImage.Matches(null,path));
    }
}
