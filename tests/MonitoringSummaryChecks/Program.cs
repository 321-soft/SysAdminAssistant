using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
using SysAdminAssistant.Server;
var root=Path.Combine(Path.GetTempPath(),"saa-summary-integration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
void Check(string name,bool result){if(!result)throw new Exception(name);Console.WriteLine("PASS: "+name);}
try{
 SettingsFile.Save(new AppSettings{SqlitePath=Path.Combine(root,"db.sqlite"),MonitoringEnabled=false},Path.Combine(root,"settings.json"));var state=new ServerState(root);await state.Factory.InitializeAsync();var now=AppTime.Now;int slot=(int)now.TimeOfDay.TotalMinutes;int jid,did;
 await using(var db=state.Factory.Create()){
  var d=new Device{Name="Hyper-V north",Host="192.168.50.3"};db.Devices.Add(d);await db.SaveChangesAsync();did=d.Id;
  var j=new SqlBackupJob{Title="Night backup",Host=d.Host,DeviceId=did,DeadlineMinutes=slot,GraceMinutes=60,MonitoringSince=now.AddDays(-1),Days=127,Active=true};db.Jobs.Add(j);await db.SaveChangesAsync();jid=j.Id;
  for(int i=20;i>0;i--){var r=new ReportEvent{DeviceId=did,JobId=jid,Provider="Veeam",Task="Backup failed",Description="Path is too long",Result=BackupResult.Failed,Severity=Severity.Critical,IsPending=false,ScheduledSlotMinutes=slot,ReceivedAt=now.AddSeconds(-i*3),OccurredAt=now.AddSeconds(-i*3)};db.Reports.Add(r);await db.SaveChangesAsync();db.Incidents.Add(new(){ReportId=r.Id,CreatedAt=r.ReceivedAt,Title=r.Task,Source="Veeam",Severity=Severity.Critical});}await db.SaveChangesAsync();
 }
 var problems=await DailySummaryCollector.CollectAsync(state,default);var backups=problems.Where(p=>p.Category=="backup").ToArray();Check("Actual pin collector exposes one incident for twenty backup reports",backups.Length==1&&backups[0].Title.Contains("повторов: 20"));Check("Actual pin collector labels backup with friendly host",backups[0].Source.Contains("Hyper-V north"));Check("Pinned rendering includes one grouped backup",TelegramProblemPins.Render(backups).Single().Contains("Всего: 1"));
 await using(var db=state.Factory.Create()){db.Reports.Add(new ReportEvent{DeviceId=did,JobId=jid,Provider="Veeam",Task="Backup succeeded",Result=BackupResult.Success,Severity=Severity.Info,IsPending=false,ScheduledSlotMinutes=slot,ReceivedAt=now,OccurredAt=now});await db.SaveChangesAsync();}
 problems=await DailySummaryCollector.CollectAsync(state,default);Check("Actual pin collector drops recovered backup",!problems.Any(p=>p.Category=="backup"));await using(var db=state.Factory.Create()){Check("Pin reconciliation also closes client-visible incidents",await db.Incidents.CountAsync(i=>!i.Closed)==0);Check("Pin reconciliation retains full report and incident history",await db.Reports.CountAsync()==21&&await db.Incidents.CountAsync()==20);}
}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
