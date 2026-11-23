using System.Text.Json;
using SysAdminAssistant.Agent;

internal static class AgentCrashSpecs
{
    public static void Run(string folder,Action<string,bool> check)
    {
        var directory=Directory.CreateDirectory(Path.Combine(folder,"agent-crash")).FullName;
        Exception error;
        try { throw new IOException("SECRET_TEST_VALUE",new FormatException("SECRET_TEST_VALUE")); }
        catch(IOException caught) { error=caught; }

        CrashLog.Record(directory,"bootstrap",error);
        var path=Path.Combine(directory,"crash.json");
        var raw=File.ReadAllText(path);
        using var parsed=JsonDocument.Parse(raw);
        var causes=parsed.RootElement.GetProperty("Causes");
        check("Agent crash records stage, cause chain and throwing method without exception text",
            parsed.RootElement.GetProperty("Stage").GetString()=="bootstrap"&&causes.GetArrayLength()==2&&
            causes[0].GetProperty("HResult").GetString()=="0x80131620"&&raw.Contains("AgentCrashSpecs.Run")&&!raw.Contains("SECRET_TEST_VALUE"));
        CrashLog.Record(directory,"registration",error);
        CrashLog.Record(directory,"report",error);
        check("Agent crash log retains only latest and previous diagnostic",
            Directory.GetFiles(directory).Length==2&&File.ReadAllText(path).Contains("report")&&
            File.ReadAllText(Path.Combine(directory,"crash.previous.json")).Contains("registration"));
    }
}
