using System.Text.Json;
using SysAdminAssistant.AgentLinux;
using SysAdminAssistant.Core;
void Check(string s,bool ok){if(!ok)throw new Exception(s);Console.WriteLine("PASS " +s);}
string Entry(string text,string process="kernel")=>JsonSerializer.Serialize(new Dictionary<string,object>{{"__CURSOR","s=test;i=4"},{"__REALTIME_TIMESTAMP",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()*1000+""},{"SYSLOG_IDENTIFIER",process},{"MESSAGE",text}});
var e=LinuxJournalCollector.Parse(Entry("Failed password for root from 192.168.1.20 port 4000","sshd"),out var cursor);
Check("SSH source",e?.Id==1003&&e.Source=="192.168.1.20"&&cursor=="s=test;i=4");
Check("no guessed source",LinuxJournalCollector.Parse(Entry("Failed password for root","sshd"),out _)==null);
Check("OOM",LinuxJournalCollector.Parse(Entry("Out of memory: Killed process"),out _)?.Id==1001);
Check("service failure",LinuxJournalCollector.Parse(Entry("nginx.service: Failed with result 'exit-code'.","systemd"),out _)?.Source=="nginx.service");
Check("normal log ignored",LinuxJournalCollector.Parse(Entry("Server started","systemd"),out _)==null);
var p=new AgentEventPolicy{Rules=AgentEvents.StandardLinux()};AgentEvents.Validate(p);
Check("Linux rules distinct",p.Rules.Count==5&&p.Rules.Any(r=>r.Id=="linux-login"&&r.Threshold==5));
var folder=Path.Combine(Path.GetTempPath(),"saa-journal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
try{var batch=new AgentEventBatch("batch1",[],[new("Linux","OK","ok")]);File.WriteAllText(Path.Combine(folder,"linux-journal.json"),JsonSerializer.Serialize(new {Cursor="old",Initialized=true,Pending=batch,PendingCursor="next"},AgentWire.Json));var reader=new LinuxJournalCollector(folder);reader.Ack("wrong");Check("wrong acknowledgement retained",File.ReadAllText(Path.Combine(folder,"linux-journal.json")).Contains("batch1"));reader.Ack("batch1");var json=File.ReadAllText(Path.Combine(folder,"linux-journal.json"));Check("matching acknowledgement advances cursor",json.Contains("next")&&!json.Contains("batch1"));}finally{Directory.Delete(folder,true);}
