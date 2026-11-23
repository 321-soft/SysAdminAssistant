using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.AgentLinux;

// Read-only: the command and its arguments are fixed; server policy cannot execute commands.
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class LinuxJournalCollector(string directory)
{
    private sealed class JournalState { public string Cursor {get;set;}=""; public bool Initialized {get;set;} public AgentEventBatch? Pending {get;set;} public string PendingCursor {get;set;}=""; }
    private readonly string path=Path.Combine(directory,"linux-journal.json");
    private JournalState Load()=>File.Exists(path)?JsonSerializer.Deserialize<JournalState>(File.ReadAllBytes(path),AgentWire.Json)??throw new InvalidDataException():new();
    private void Save(JournalState s){var temp=path+".tmp";try{using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);JsonSerializer.Serialize(f,s,AgentWire.Json);f.Flush(true);}File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
    public void Ack(string id){var s=Load();if(s.Pending?.Id!=id)return;if(s.PendingCursor.Length>0)s.Cursor=s.PendingCursor;s.Pending=null;s.PendingCursor="";Save(s);}
    private static async Task<string?> Line(StreamReader reader,CancellationToken token){var text=new StringBuilder();var one=new char[1];while(await reader.ReadAsync(one.AsMemory(),token)>0){if(one[0]=='\n')return text.ToString();if(text.Length>=262144)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Строка журнала слишком длинная"));text.Append(one[0]);}return text.Length==0?null:text.ToString();}
    private static string Value(JsonElement e,string key){if(!e.TryGetProperty(key,out var v))return "";if(v.ValueKind==JsonValueKind.String)return v.GetString()??"";if(v.ValueKind==JsonValueKind.Array){var bytes=new List<byte>();foreach(var b in v.EnumerateArray()){if(!b.TryGetByte(out var value)||bytes.Count>=65536)return "";bytes.Add(value);}return Encoding.UTF8.GetString(bytes.ToArray());}return "";}
    public static AgentJournalEvent? Parse(string line,out string cursor)
    {
        using var doc=JsonDocument.Parse(line);var e=doc.RootElement;cursor=Value(e,"__CURSOR");if(cursor.Length is <1 or >512||cursor.Any(char.IsControl))throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Некорректный курсор журнала"));
        if(!long.TryParse(Value(e,"__REALTIME_TIMESTAMP"),out var us)||us<0)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Некорректное время журнала"));
        DateTime at;try{at=DateTimeOffset.FromUnixTimeMilliseconds(us/1000).UtcDateTime;}catch(ArgumentOutOfRangeException){throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Некорректное время журнала"));}
        var msg=Value(e,"MESSAGE");var unit=Value(e,"_SYSTEMD_UNIT");var process=Value(e,"SYSLOG_IDENTIFIER");int id=0;string source="";
        if(msg.Contains("Out of memory",StringComparison.OrdinalIgnoreCase)||msg.Contains("Killed process",StringComparison.OrdinalIgnoreCase)&&msg.Contains("oom",StringComparison.OrdinalIgnoreCase))id=1001;
        else if(msg.Contains("I/O error",StringComparison.OrdinalIgnoreCase)||msg.Contains("EXT4-fs error",StringComparison.OrdinalIgnoreCase)||msg.Contains("XFS",StringComparison.OrdinalIgnoreCase)&&msg.Contains("corruption",StringComparison.OrdinalIgnoreCase))id=1002;
        else if(process.StartsWith("sshd",StringComparison.OrdinalIgnoreCase)&&msg.Contains("Failed password",StringComparison.OrdinalIgnoreCase)){var m=Regex.Match(msg,@"\bfrom\s+([0-9a-fA-F:.]+)\b",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));if(m.Success&&System.Net.IPAddress.TryParse(m.Groups[1].Value,out var ip)){id=1003;source=ip.ToString();}}
        else if((process=="systemd"||unit=="init.scope")&&msg.Contains("Failed with result",StringComparison.OrdinalIgnoreCase)){var m=Regex.Match(msg,@"(?<unit>[A-Za-z0-9_.@:-]+\.service):\s+Failed with result",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));if(m.Success){id=1004;source=m.Groups["unit"].Value;}}
        else if(msg.Contains("Kernel panic",StringComparison.OrdinalIgnoreCase))id=1005;
        if(id==0)return null;var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cursor)));msg=new string(msg.Where(c=>!char.IsControl(c)||c is '\r' or '\n' or '\t').Take(1000).ToArray());
        return new(key,"Linux","journald",id,0,at,msg,source,id is 1001 or 1002 or 1005?2:3);
    }
    public async Task<AgentEventBatch> ReadAsync(CancellationToken token)
    {
        var s=Load();if(s.Pending!=null)return s.Pending;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var args=new List<string>{"--system","--no-pager","--no-hostname"};
        if(!s.Initialized)args.AddRange(["--lines=0","--show-cursor"]);
        else args.AddRange(["--output=json","--output-fields=MESSAGE,_SYSTEMD_UNIT,SYSLOG_IDENTIFIER,__CURSOR,__REALTIME_TIMESTAMP","--no-tail","--after-cursor",s.Cursor]);
        var exe=File.Exists("/usr/bin/journalctl")?"/usr/bin/journalctl":"/bin/journalctl";var pi=new ProcessStartInfo(exe){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};pi.Environment["LC_ALL"]="C";foreach(var a in args)pi.ArgumentList.Add(a);
        string next="";var events=new List<AgentJournalEvent>();string health="OK",detail="Журнал доступен";
        try{
            using var p=Process.Start(pi)??throw new InvalidOperationException();var errors=p.StandardError.ReadToEndAsync(timeout.Token);int count=0;bool capped=false;
            try{while(await Line(p.StandardOutput,timeout.Token) is string line){
                if(!s.Initialized){if(line.StartsWith("-- cursor: ",StringComparison.Ordinal))next=line[11..].Trim();continue;}
                var item=Parse(line,out var cursor);next=cursor;if(item!=null)events.Add(item);count++;if(count>=100||events.Count>=32){capped=true;p.Kill(true);break;}
            }await p.WaitForExitAsync(timeout.Token);var err=await errors;if(!capped&&(p.ExitCode!=0||!string.IsNullOrWhiteSpace(err)))throw new IOException("journalctl: "+err[..Math.Min(120,err.Length)]);
            }finally{if(!p.HasExited)p.Kill(true);}
            if(!s.Initialized){if(next.Length==0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Журнал не предоставил начальный курсор"));s.Cursor=next;s.Initialized=true;next="";}
        }catch(Exception e)when(e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException or InvalidDataException or TimeoutException or OperationCanceledException){if(token.IsCancellationRequested)throw;events.Clear();next="";health="Unavailable";detail="Не удалось прочитать journald; проверьте доступ службы к журналу";}
        s.PendingCursor=next;s.Pending=new AgentEventBatch(Guid.NewGuid().ToString("N"),events.ToArray(),[new("Linux",health,detail)]);Save(s);return s.Pending;
    }
}
