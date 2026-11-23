namespace SysAdminAssistant.Core;
/// <summary>Fail-closed validation shared by the graphical remover and its tests.</summary>
public static class UninstallPolicy
{
    public static string DatabaseName(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>128||name.Any(char.IsControl)||new[]{"master","model","msdb","tempdb"}.Contains(name,StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Protected or invalid database name.");
        return "["+name.Replace("]","]]")+"]";
    }
    public static string PathForRemoval(string path,IEnumerable<string> protectedPaths)
    {
        if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)||path.StartsWith(@"\\"))throw new IOException("An absolute local path is required.");
        var absolute=Path.GetFullPath(path);
        var root=Path.GetPathRoot(absolute);
        if(string.Equals(absolute,root,StringComparison.OrdinalIgnoreCase))throw new IOException("A filesystem root cannot be removed.");
        var full=absolute.TrimEnd(Path.DirectorySeparatorChar);
        if(protectedPaths.Append(Path.GetPathRoot(full)!).Where(p=>!string.IsNullOrWhiteSpace(p)).Any(p=>string.Equals(full,Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)))throw new IOException("Protected folder: "+full);
        var current=full;
        while(!string.IsNullOrEmpty(current)){
            try{if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("A filesystem link is not allowed: "+current);}
            catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
            current=Path.GetDirectoryName(current);
        }
        return full;
    }
    public static void CheckTree(string path,IEnumerable<string> protectedPaths)
    {
        var roots=protectedPaths.ToArray();var full=PathForRemoval(path,roots);
        if(!Directory.Exists(full))return;
        var pending=new Stack<string>();pending.Push(full);
        while(pending.Count>0)foreach(var entry in Directory.EnumerateFileSystemEntries(pending.Pop())){
            var attributes=File.GetAttributes(entry);
            if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("A filesystem link is not allowed: "+entry);
            if((attributes&FileAttributes.Directory)!=0)pending.Push(entry);
        }
    }
}
