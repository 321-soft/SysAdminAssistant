using SysAdminAssistant.Core;using SysAdminAssistant.AgentLinux;
if(!OperatingSystem.IsLinux())throw new Exception("Linux required");
void Check(string name,bool ok){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
foreach(var distro in new[]{"Debian","Ubuntu"}){
 var now=DateTime.UtcNow;var units=LinuxSystemd.Parse("Id=job.service\nType=oneshot\nActiveState=inactive\nSubState=dead\nLoadState=loaded\nResult=success\nExecMainCode=1\nExecMainStatus=0\nExecMainExitTimestampMonotonic=9000000\n\nId=job.timer\nActiveState=active\nSubState=waiting\nLoadState=loaded\nTriggers=job.service\nLastTriggerUSecMonotonic=8000000\nNextElapseUSecMonotonic=20000000\n",now,10000000);
 Check(distro+" systemd fixture preserves successful oneshot exit",units[0].Type=="oneshot"&&units[0].ExitStatus==0&&units[0].Active=="inactive"&&units[0].LastRunUtc==now.AddSeconds(-1));Check(distro+" timer fixture preserves dependency and due time",units[1].Trigger=="job.service"&&units[1].NextRunUtc==now.AddSeconds(10));
 var p=new AgentPolicy{StandardServices=false,ExcludedServices=["sysadminassistant-agent-update.service","sysadminassistant-agent-update.timer"],Services=[new("job.timer",MaxIntervalMinutes:5)]};var report=new AgentReport{Os="Linux "+distro,UptimeSeconds=3600,CapturedUtc=now,Linux=new(){Units=units}};Check(distro+" successful inactive oneshot never requires Running",LinuxPolicy.Systemd(report,p).Length==0);
}
var mount=LinuxVolumes.ParseMount(@"42 20 253:0 / /srv/my\040data rw,relatime - ext4 /dev/mapper/vg-data rw");Check("mount parser distinguishes escaped mount filesystem and LVM source",mount?.Path=="/srv/my data"&&mount.Fs=="ext4"&&mount.Source=="/dev/mapper/vg-data"&&mount.Device=="253:0");
var bind=LinuxVolumes.ParseMount(@"43 20 253:0 /folder /bind rw - ext4 /dev/mapper/vg-data rw");Check("bind mount keeps its filesystem root instead of inventing new disk",bind?.Root=="/folder"&&bind.Device==mount!.Device);
var volumes=LinuxVolumes.Read();Check("live statvfs collector returns bounded valid volumes",volumes.Length<=64&&volumes.All(v=>v.TotalBytes>=0&&v.AvailableBytes<=v.TotalBytes));LinuxPolicy.ValidateTelemetry(new(){Volumes=volumes});Console.WriteLine("PASS live Linux volume collection validated");

var clock=new DateTime(2026,10,29,12,0,0,DateTimeKind.Utc);var timed=LinuxSystemd.Parse("Id=calendar.timer\nActiveState=active\nSubState=waiting\nLastTriggerUSecMonotonic=9s\nNextElapseUSecRealtime=Thu 2026-10-29 13:00:00 UTC\n",clock,10000000).Single();Check("systemctl formatted monotonic and calendar UTC times are understood",timed.LastRunUtc==clock.AddSeconds(-1)&&timed.NextRunUtc==clock.AddHours(1));

var socketUnits=LinuxSystemd.Parse("Id=ssh.service\nType=notify\nActiveState=inactive\nSubState=dead\nLoadState=loaded\nResult=success\nTriggeredBy=ssh.socket other.socket\n\nId=ssh.socket\nLoadState=loaded\nActiveState=active\nSubState=listening\nTriggers=ssh.service\n",DateTime.UtcNow,10000000);
Check("systemctl parser carries declared socket relationship",socketUnits[0].Trigger=="ssh.socket other.socket"&&socketUnits[1].Type=="socket");
Check("Parsed socket telemetry proves inactive SSH healthy",LinuxPolicy.SocketActivated(socketUnits[0],new AgentReport{Linux=new(){Units=socketUnits}}));
