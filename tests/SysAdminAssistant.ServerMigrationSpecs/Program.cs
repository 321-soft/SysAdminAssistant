using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Server;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
using System.Security.Cryptography;
using System.Text.Json;
var folder=Path.Combine(Path.GetTempPath(),"saa-server-migrate-"+Guid.NewGuid()); Directory.CreateDirectory(folder);
try{
 var state=new ServerState(folder);
 state.Settings.SqlitePath=Path.Combine(folder,"db.sqlite");
 await using(var db=state.Factory.Create()){
  await db.Database.EnsureCreatedAsync();
  db.Schema.Add(new SchemaInfo{Version=7});
  db.Assets.Add(new InventoryAsset{InventoryNumber="KEEP",Notes="Preserved"});
  await db.SaveChangesAsync();
  foreach(var column in new[]{"WorkstationId","Manufacturer","Model","HardwareJson"}){
   var sql="ALTER TABLE sa2_InventoryAsset DROP COLUMN "+column;
   await db.Database.ExecuteSqlRawAsync(sql);
  }
 }
 await state.InitializeAsync(default);
 await state.InitializeAsync(default);
 await state.SnapshotAsync(default,inventoryUnlocked:true);
 await using(var db=state.Factory.Create()){
  var asset=await db.Assets.SingleAsync();
  if(asset.InventoryNumber!="KEEP"||asset.Notes!="Preserved"||asset.WorkstationId!=null||asset.Manufacturer!=""||asset.Model!="")throw new Exception("Asset migration failed");
 }
 var now=DateTime.UtcNow;
 var stationId=Guid.NewGuid();
 using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
 StationRequest Request(string? payload=null,string enrollment=""){
  var req=new StationRequest(stationId,Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),enrollment,"PC-LIVE",
   new DateTimeOffset(now).ToUnixTimeSeconds(),Guid.NewGuid().ToString("N"),payload,"");
  return req with{Signature=Convert.ToBase64String(key.SignData(StationWire.Bytes(req),HashAlgorithmName.SHA256))};
 }
 state.Workstations.Report(Request(enrollment:state.Workstations.IssueEnrollment()),now);
 state.Workstations.Decide(stationId,"approve");
 var sample=new StationSample("Windows 11",1,null,[],null,null,null,null,"0.46.8","SN-01",Hardware:new StationHardware("CPU-ACTUAL",32L*1073741824,1000000000000,"VENDOR","MODEL",InstalledMemoryBytes:32L*1073741824));
 state.Workstations.Report(Request(JsonSerializer.Serialize(sample,AgentWire.Json)),now);
 await using(var db=state.Factory.Create()){
  var asset=await db.Assets.SingleAsync();asset.WorkstationId=stationId;asset.Cpu="WRONG";await db.SaveChangesAsync();
 }
 await state.SnapshotAsync(default,inventoryUnlocked:true);
 int id,revision;
 await using(var db=state.Factory.Create()){
  var asset=await db.Assets.SingleAsync();
  if(asset.Cpu!="CPU-ACTUAL"||asset.RamGb!=32||asset.Model!="MODEL"||asset.Notes!="Preserved")throw new Exception("Live inventory update failed");
  id=asset.Id;revision=asset.Revision;
 }
 await new InventoryService(state.Factory).UnlinkAssetAsync(id,revision);
 state.Workstations.Report(Request(JsonSerializer.Serialize(sample with{Hardware=sample.Hardware! with{Cpu="CPU-NEW"}},AgentWire.Json)),now);
 await state.SnapshotAsync(default,inventoryUnlocked:true);
 await using(var db=state.Factory.Create()){
  var asset=await db.Assets.SingleAsync();
  if(asset.Cpu!="CPU-ACTUAL")throw new Exception("Detached asset was modified");
  revision=asset.Revision;
 }
 await new InventoryService(state.Factory).DeleteAssetAsync(id,revision);
 await using(var db=state.Factory.Create())if(await db.Assets.AnyAsync())throw new Exception("Asset deletion failed");
 if(state.Workstations.List(false,now).Length!=1)throw new Exception("Inventory delete removed station");
 Console.WriteLine("PASS: Actual server snapshots replace incorrect hardware, preserve accounting fields, stop synchronization after unlink, and delete unused asset without removing station.");
 Console.WriteLine("PASS: Actual ServerState startup repairs existing schema 7; unlocked inventory snapshot succeeds; existing assets preserved; repeat startup succeeds.");
}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
