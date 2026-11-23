namespace SysAdminAssistant.Core;
public static class HardwareIdentity
{
    public static string Serial(string? value)
    {
        var text=(value??"").Trim();
        var key=new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if(key is "" or "defaultstring" or "tobefilledbyoem" or "systemserialnumber" or "serialnumber" or "none" or "unknown" or "notapplicable" or "na" or "notavailable"||key.All(c=>c=='0')||key.Length>=8&&key.All(c=>c=='f'))return "";
        return text;
    }
}
