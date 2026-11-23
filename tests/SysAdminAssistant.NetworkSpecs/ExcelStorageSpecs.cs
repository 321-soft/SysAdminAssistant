using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
using SysAdminAssistant.Server;
static class ExcelStorageSpecs
{
    public static async Task Run(Action<string,bool> check)
    {
        var folder=Path.Combine(Path.GetTempPath(),"SaaExcelSpecs-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            SettingsFile.Save(new AppSettings{SqlitePath=Path.Combine(folder,"excel.db"),MonitoringEnabled=false},Path.Combine(folder,"settings.json"));
            var account=new UserDirectory.Account{Id="fixture",Login="fixture",Administrator=true,Permissions=new()};
            File.WriteAllText(Path.Combine(folder,"users.json"),JsonSerializer.Serialize(new[]{account}));var actor=account.View();
            var state=new ServerState(folder);await state.InitializeAsync(default);var ops=new OrganizationVault(state,new UserDirectory(folder));
            ExcelRequest Sheet(string kind,bool update,params string[][] rows)=>new(ExcelExchange.Write(kind,rows),update);
            Task<ExcelPreview> Import(string kind,ExcelRequest req,bool commit)=>ops.ImportExcel(actor,kind,req,commit,default);
            async Task<bool> Rejected(Func<Task> action){try{await action();return false;}catch(Exception e)when(e is ArgumentException or UnauthorizedAccessException){return true;}}
            var add=Sheet("devices",false,["","","Device","Сервер","host","Office","Role","Да"]);
            check("Excel storage preview valid",(await Import("devices",add,false)).Valid);
            await using(var db=state.Factory.Create())check("Excel storage preview has no writes",await db.Devices.CountAsync()==0);
            var invalid=Sheet("devices",false,["","","Device","Сервер","host","Office","Role","Да"],["","","","Сервер","host","Office","Role","Да"]);
            check("Excel storage invalid batch rejected",await Rejected(()=>Import("devices",invalid,true)));
            await using(var db=state.Factory.Create())check("Excel storage invalid batch rolled back",await db.Devices.CountAsync()==0);
            await Import("devices",add,true);Device device;
            await using(var db=state.Factory.Create()){device=await db.Devices.SingleAsync();device.Status="ONLINE";device.LastError="retained";await db.SaveChangesAsync();}
            var export=await ops.ExportExcel(actor,"devices",default);var row=ExcelExchange.Read(export.File,"devices").Single().Values;row[2]="Renamed";
            var duplicateRow=(string[])row.Clone();duplicateRow[0]="0"+row[0];check("Excel storage canonical duplicate ID rejected",!(await Import("devices",Sheet("devices",true,row,duplicateRow),false)).Valid);
            var update=Sheet("devices",true,row);check("Excel storage existing ID needs explicit update",!(await Import("devices",Sheet("devices",false,row),false)).Valid);
            await Import("devices",update,true);
            await using(var db=state.Factory.Create()){var d=await db.Devices.SingleAsync();check("Excel storage update keeps ID and status",d.Id==device.Id&&d.Name=="Renamed"&&d.Status=="ONLINE"&&d.LastError=="retained");}
            check("Excel storage stale device version rejected",!(await Import("devices",update,false)).Valid);
            var network=Sheet("devices",false,["","","Router","MikroTik","host","","","Да"]);check("Excel storage separate network section enforced",!(await Import("devices",network,false)).Valid);
            var employee=Sheet("employees",false,["","","Employee","00123","IT","Engineer","","Да"]);await Import("employees",employee,true);
            var duplicate=Sheet("employees",false,["","","Other","00123","","","","Да"]);check("Excel storage personnel uniqueness enforced",!(await Import("employees",duplicate,false)).Valid);
            var ee=await ops.ExportExcel(actor,"employees",default);var er=ExcelExchange.Read(ee.File,"employees").Single().Values;check("Excel storage personnel leading zeros retained",er[3]=="00123");
            er[4]="New department";await Import("employees",Sheet("employees",true,er),true);check("Excel storage stale employee revision rejected",!(await Import("employees",Sheet("employees",true,er),false)).Valid);
            await using(var db=state.Factory.Create()){var e=await db.Employees.SingleAsync();db.Assets.Add(new InventoryAsset{InventoryNumber="FIXTURE",EmployeeId=e.Id,State=AssetState.Assigned});await db.SaveChangesAsync();}
            ee=await ops.ExportExcel(actor,"employees",default);er=ExcelExchange.Read(ee.File,"employees").Single().Values;er[7]="Нет";check("Excel storage cannot archive assigned employee",!(await Import("employees",Sheet("employees",true,er),false)).Valid);
            var vault=Sheet("vault",false,["","","Credential","user","secret","notes",device.Id.ToString()]);check("Excel storage Vault preview validates device link",(await Import("vault",vault,false)).Valid);
            await using(var db=state.Factory.Create())check("Excel storage Vault preview stores no plaintext",await db.Credentials.CountAsync()==0);
            var badLink=Sheet("vault",false,["","","Credential","user","secret","notes","2147483647"]);var badPreview=await Import("vault",badLink,false);check("Excel storage Vault nonexistent link rejected",!badPreview.Valid);check("Excel storage preview omits sensitive names and secrets",!JsonSerializer.Serialize(badPreview).Contains("secret")&&!JsonSerializer.Serialize(badPreview).Contains("Credential"));
            var reader=new UserView("reader","reader",false,true,1,new(){["devices"]=1});check("Excel storage permission enforced independently of UI",await Rejected(()=>ops.ImportExcel(reader,"devices",add,true,default)));
            var employee2=Sheet("employees",false,["","","A","SAME","","","","Да"],["","","B","same","","","","Да"]);check("Excel storage duplicate personnel within file rejected",!(await Import("employees",employee2,false)).Valid);
            Console.WriteLine("PASS Excel storage transaction checks; no Windows DPAPI/TLS/GUI executed");
        }finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
    }
}
