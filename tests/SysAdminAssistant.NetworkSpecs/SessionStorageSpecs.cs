using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
using SysAdminAssistant.Server;
static class SessionStorageSpecs
{
    public static async Task Run(Action<string,bool> check)
    {
        var dir=Path.Combine(Path.GetTempPath(),"SaaSessionSpecs-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try{
            const string password="Fixture-Session-Password";var hash=ServerPassword.Create(password);
            var admin=new UserDirectory.Account{Id="admin-fixture",Login="admin",Administrator=true,Salt=hash.Salt,Hash=hash.Hash};
            var normal=new UserDirectory.Account{Id="normal-fixture",Login="reader",Salt=hash.Salt,Hash=hash.Hash,Permissions=new(){["devices"]=1}};
            File.WriteAllText(Path.Combine(dir,"users.json"),JsonSerializer.Serialize(new[]{admin,normal}));var users=new UserDirectory(dir);var clock=new Clock();var sessions=new ServerSessions(users,clock);
            DefaultHttpContext Context(RemoteToken login){var c=new DefaultHttpContext();c.Connection.RemoteIpAddress=IPAddress.Loopback;c.Request.Headers.Authorization="Bearer "+login.Token;return c;}
            var session=sessions.Login(new("admin",password,true),"127.0.0.1")!;var c=Context(session);
            check("Server session has one hour initial lease",session.ExpiresUtc==clock.GetUtcNow().UtcDateTime.AddHours(1));
            for(var i=0;i<20;i++){clock.Advance(TimeSpan.FromMinutes(30));if(!sessions.Valid(c))throw new Exception("Unexpected live expiry");}
            check("Server session active for ten hours remains valid",sessions.Valid(c));
            check("Server general session does not unlock secrets",!sessions.VaultUnlocked(c)&&!sessions.InventoryUnlocked(c));
            check("Server private inventory accepts own password",sessions.UnlockInventory(c,password));check("Server inventory does not unlock Vault",!sessions.VaultUnlocked(c));
            check("Server Vault accepts own password",sessions.UnlockVault(c,password));clock.Advance(TimeSpan.FromMinutes(4));check("Server private scopes live before five minutes",sessions.InventoryUnlocked(c)&&sessions.VaultUnlocked(c));clock.Advance(TimeSpan.FromMinutes(1));check("Server heartbeats do not prolong private scopes",!sessions.InventoryUnlocked(c)&&!sessions.VaultUnlocked(c)&&sessions.Valid(c));
            check("Server wrong private password cannot unlock",!sessions.UnlockInventory(c,"wrong"));
            var p=sessions.Login(new("admin",password,true),"127.0.0.1")!;var peer=Context(p);clock.Advance(TimeSpan.FromMinutes(59));peer.Connection.RemoteIpAddress=IPAddress.Parse("192.168.1.2");check("Server wrong peer rejected",!sessions.Valid(peer));clock.Advance(TimeSpan.FromMinutes(1));peer.Connection.RemoteIpAddress=IPAddress.Loopback;check("Server wrong peer cannot extend lease",!sessions.Valid(peer));
            var outage=sessions.Login(new("admin",password,true),"127.0.0.1")!;var oc=Context(outage);clock.Advance(TimeSpan.FromHours(1));check("Server offline one hour revokes token",!sessions.Valid(oc)&&!sessions.UnlockVault(oc,password));
            var ordinary=sessions.Login(new("admin",password),"127.0.0.1")!;var old=Context(ordinary);clock.Advance(TimeSpan.FromHours(7));check("Server ordinary eight hour session survives seven",sessions.Valid(old));clock.Advance(TimeSpan.FromHours(1));check("Server ordinary activity cannot extend eight hours",!sessions.Valid(old));
            var revoked=sessions.Login(new("reader",password,true),"127.0.0.1")!;var rc=Context(revoked);users.Update(new(normal.Id,false,normal.Revision,"",normal.Permissions));check("Server disabled user immediately loses remembered session",!sessions.Valid(rc));
            var logged=sessions.Login(new("admin",password,true),"127.0.0.1")!;var lc=Context(logged);sessions.Logout(lc);check("Server explicit logout revokes remembered token",!sessions.Valid(lc));
            var concurrent=sessions.Login(new("admin",password,true),"127.0.0.1")!;var cc=Context(concurrent);await Task.WhenAll(Enumerable.Range(0,20).Select(_=>Task.Run(()=>sessions.Valid(cc))));check("Server concurrent heartbeats preserve authentication",sessions.UnlockInventory(cc,password)&&sessions.InventoryUnlocked(cc));sessions.LockInventory(cc);check("Server inventory lock independent of general session",!sessions.InventoryUnlocked(cc)&&sessions.Valid(cc));
            SettingsFile.Save(new AppSettings{SqlitePath=Path.Combine(dir,"state.db")},Path.Combine(dir,"settings.json"));var state=new ServerState(dir);await state.InitializeAsync(default);await using(var db=state.Factory.Create()){db.Employees.Add(new Employee{FullName="Private fixture"});await db.SaveChangesAsync();}
            var publicView=await state.SnapshotAsync(default,admin.View());var privateView=await state.SnapshotAsync(default,admin.View(),true);check("Server default snapshot omits private inventory",publicView.Rows["employees"].Length==0);check("Server explicitly unlocked snapshot includes inventory",privateView.Rows["employees"].Length==1);
            Console.WriteLine("PASS session storage checks; no Windows DPAPI/TLS/GUI executed");
        }finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    }
    private sealed class Clock:TimeProvider{private DateTimeOffset now=new(2026,10,26,0,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>now;public void Advance(TimeSpan t)=>now+=t;}
}
