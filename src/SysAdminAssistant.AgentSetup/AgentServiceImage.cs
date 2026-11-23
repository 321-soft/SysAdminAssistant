namespace SysAdminAssistant.AgentSetup;

internal static class AgentServiceImage
{
    public static bool Matches(string? command,string executable)
    {
        var image=command?.Trim(' ','\t');
        if(image==null)return false;
        // CreateService registers exactly this quoted command. A bare name is safe
        // only when it has no whitespace; never parse arguments or fall back to PATH.
        if(string.Equals(image,"\""+executable+"\"",StringComparison.OrdinalIgnoreCase))return true;
        return !executable.Any(char.IsWhiteSpace)&&string.Equals(image,executable,StringComparison.OrdinalIgnoreCase);
    }
}
