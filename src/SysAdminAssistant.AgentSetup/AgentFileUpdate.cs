using System.Diagnostics;
using System.IO;

namespace SysAdminAssistant.AgentSetup;

internal sealed class AgentFileUpdate(string target,string workspace,Action<string> safe,TimeSpan lockTimeout)
{
    private readonly List<string> changed=[];
    private readonly HashSet<string> original=new(StringComparer.OrdinalIgnoreCase);
    private string Staged(string name)=>Path.Combine(workspace,"new",name);
    private string Backup(string name)=>Path.Combine(workspace,"previous",name);
    public bool HasChanges=>changed.Count!=0;

    private void RetryLocked(Action action,Stopwatch timer)
    {
        while(true)
        {
            try {action();return;}
            catch(IOException e) when((e.HResult & 0xffff) is 32 or 33 or 1224 && timer.Elapsed<lockTimeout)
            {Thread.Sleep(200);}
        }
    }

    public void Prepare(IReadOnlyDictionary<string,byte[]> files,IReadOnlyCollection<string> installed)
    {
        safe(workspace);
        Directory.CreateDirectory(Path.Combine(workspace,"new"));
        Directory.CreateDirectory(Path.Combine(workspace,"previous"));
        foreach(var file in files)File.WriteAllBytes(Staged(file.Key),file.Value);
        var timer=Stopwatch.StartNew();
        foreach(var name in installed)
        {
            var path=Path.Combine(target,name);safe(path);
            // Open without sharing before changing any live file, including DLLs and the apphost.
            RetryLocked(()=>{using var stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);},timer);
            File.Copy(path,Backup(name));
            original.Add(name);
        }
    }

    public void Apply(IReadOnlyDictionary<string,byte[]> files,Action requireStopped)
    {
        var timer=Stopwatch.StartNew();
        var incoming=files.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var name in files.Keys)
        {
            var path=Path.Combine(target,name);safe(path);
            RetryLocked(()=>{requireStopped();File.Move(Staged(name),path,true);},timer);
            changed.Add(name);
        }
        foreach(var name in original.Where(name=>!incoming.Contains(name)))
        {
            var path=Path.Combine(target,name);safe(path);
            RetryLocked(()=>{requireStopped();File.Delete(path);},timer);
            changed.Add(name);
        }
    }

    public void Rollback(Action requireStopped)
    {
        var timer=Stopwatch.StartNew();
        for(int i=changed.Count-1;i>=0;i--)
        {
            var name=changed[i];var path=Path.Combine(target,name);safe(path);
            if(original.Contains(name))
            {
                var restore=Path.Combine(workspace,"restore.tmp");
                File.Copy(Backup(name),restore,true);
                RetryLocked(()=>{requireStopped();File.Move(restore,path,true);},timer);
            }
            else RetryLocked(()=>{requireStopped();File.Delete(path);},timer);
            changed.RemoveAt(i);
        }
    }
}
