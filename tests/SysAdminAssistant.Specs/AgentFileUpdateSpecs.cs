using SysAdminAssistant.AgentSetup;

internal static class AgentFileUpdateSpecs
{
    public static void Run(string folder,Action<string,bool> check)
    {
        var target=Directory.CreateDirectory(Path.Combine(folder,"agent-file-update")).FullName;
        var agent=Path.Combine(target,"agent.dll");
        var obsolete=Path.Combine(target,"obsolete.dll");
        var added=Path.Combine(target,"new.dll");
        File.WriteAllBytes(agent,[1]);File.WriteAllBytes(obsolete,[2]);
        var files=new Dictionary<string,byte[]>{{"agent.dll",[3]},{"new.dll",[4]}};

        var blocked=new AgentFileUpdate(target,Path.Combine(target,"blocked"),_=>{},TimeSpan.Zero);
        bool rejected=false;
        using(var file=new FileStream(obsolete,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            try{blocked.Prepare(files,["agent.dll","obsolete.dll"]);}catch(IOException){rejected=true;}
        check("Agent update refuses a locked file before modifying installed binaries",rejected&&!blocked.HasChanges&&File.ReadAllBytes(agent).SequenceEqual(new byte[]{1})&&!File.Exists(added));

        var partial=new AgentFileUpdate(target,Path.Combine(target,"partial"),_=>{},TimeSpan.Zero);
        partial.Prepare(files,["agent.dll","obsolete.dll"]);
        int calls=0;bool failed=false;
        try{partial.Apply(files,()=>{if(++calls==2)throw new InvalidOperationException();});}catch(InvalidOperationException){failed=true;}
        using(var untouched=new FileStream(obsolete,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            partial.Rollback(()=>{});
        check("Agent rollback restores only files that changed",failed&&!partial.HasChanges&&File.ReadAllBytes(agent).SequenceEqual(new byte[]{1})&&!File.Exists(added));

        var full=new AgentFileUpdate(target,Path.Combine(target,"full"),_=>{},TimeSpan.Zero);
        full.Prepare(files,["agent.dll","obsolete.dll"]);full.Apply(files,()=>{});
        check("Agent update replaces and removes staged binaries",File.ReadAllBytes(agent).SequenceEqual(new byte[]{3})&&File.Exists(added)&&!File.Exists(obsolete));
        full.Rollback(()=>{});
        check("Agent rollback restores removed files and removes newly added files",!full.HasChanges&&File.ReadAllBytes(agent).SequenceEqual(new byte[]{1})&&File.ReadAllBytes(obsolete).SequenceEqual(new byte[]{2})&&!File.Exists(added));
    }
}
