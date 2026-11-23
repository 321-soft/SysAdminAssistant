using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
using SysAdminAssistant.Server;

if(args.Contains("--session-storage")){await SessionStorageSpecs.Run((name,ok)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});return;}
if(args.Contains("--excel-storage")){await ExcelStorageSpecs.Run((name,ok)=>{if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);});return;}
if(args.Contains("--mail-source-storage")){await MailSourceStorageSpecs.Run(Check);return;}
if(args.Contains("--workspace-storage")){WorkspaceStorageSpecs.Run(Check);return;}
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Сетевые интеграционные проверки выполняются на Windows");
void Check(string name, bool result) { if (!result) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
async Task<bool> Rejected(Func<Task> action, string? contains = null)
{
    try { await action(); return false; } catch (Exception e) { return contains == null || e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase); }
}
await SessionStorageSpecs.Run(Check);
await ExcelStorageSpecs.Run(Check);
await MailSourceStorageSpecs.Run(Check);
WorkspaceStorageSpecs.Run(Check);
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
if (!File.Exists(Path.Combine(root, "SysAdminAssistant.sln"))) throw new InvalidOperationException("Запускай NetworkSpecs из собранного решения");
var directory = Path.Combine(Path.GetTempPath(), "SysAdminAssistant-NetworkSpecs-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory); Process? server = null;
try
{
    var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start(); var port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
    const string password = "Fixture-Server-Pass-2026";
    var sqlSettings = new AppSettings();
    SqlStorage.Configure(sqlSettings, "sql.example.invalid", "InstallerFixture", "fixture-login", "Fixture;SQL=Secret");
    Check("SQL password encrypted outside connection string", !sqlSettings.SqlConnectionString.Contains("Fixture;SQL=Secret") && sqlSettings.ProtectedSqlPassword != "Fixture;SQL=Secret");
    var sqlResolved = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(SqlStorage.Connection(sqlSettings));
    Check("SQL authentication resolves protected secret", !sqlResolved.IntegratedSecurity && sqlResolved.Password == "Fixture;SQL=Secret" && !sqlResolved.PersistSecurityInfo);
    Check("SQL certificate validation enabled by default", !sqlResolved.TrustServerCertificate);
    LocalSecret.MachineScope = true;
    SqlStorage.Configure(sqlSettings, "sql.example.invalid", "InstallerFixture", "fixture-login", "Fixture;SQL=Secret", true);
    LocalSecret.MachineScope = false;
    var machineResolved = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(SqlStorage.Connection(sqlSettings));
    Check("Machine SQL secret resolves independently of current protect scope", sqlSettings.ProtectedSqlPassword.StartsWith("machine:") && machineResolved.Password == "Fixture;SQL=Secret");
    Check("Explicit SQL certificate trust survives configuration", machineResolved.TrustServerCertificate);
    var sqlPublic = RemoteData.PublicSettings(sqlSettings);
    Check("SQL secret never included in client snapshot", sqlPublic.ProtectedSqlPassword == "" && sqlPublic.SqlConnectionString == "");
    var hash = ServerPassword.Create(password);
    Check("Server password validates without plaintext storage", ServerPassword.Verify(password, hash.Salt, hash.Hash) && hash.Hash != password);
    Check("Wrong password rejected", !ServerPassword.Verify("Fixture-Wrong-Password", hash.Salt, hash.Hash));
    var secondHash = ServerPassword.Create(password); Check("Equal passwords receive distinct salts", hash.Salt != secondHash.Salt);
    var originalDevice = new Device { Id = 1, Name = "Fixture" }; var originalStamp = RemoteData.EntityStamp(originalDevice);
    originalDevice.Status = "ONLINE"; originalDevice.LastSeen = DateTime.Now;
    Check("Monitor status updates do not conflict with metadata edits", originalStamp == RemoteData.EntityStamp(originalDevice));
    originalDevice.Role = "Changed"; Check("Metadata change invalidates version", originalStamp != RemoteData.EntityStamp(originalDevice));
    var lan = new LanNetwork("192.168.1.0/24");
    Check("LAN matches boundary addresses", lan.Contains(IPAddress.Parse("192.168.1.1")) && lan.Contains(IPAddress.Parse("192.168.1.254")));
    Check("Other subnet and public peer blocked", !lan.Contains(IPAddress.Parse("192.168.2.1")) && !lan.Contains(IPAddress.Parse("203.0.113.1")));
    Check("Mapped IPv4 peer supported", lan.Contains(IPAddress.Parse("::ffff:192.168.1.2")));
    Check("Global subnet cannot be configured", await Rejected(() => { _ = new LanNetwork("0.0.0.0/0"); return Task.CompletedTask; }));
    using var rsa = RSA.Create(2048); var request = new CertificateRequest("CN=SysAdmin Network Specs", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(san.Build());
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddDays(180));
    const string certificatePassword = "Fixture-Pfx-Pass-2026";
    File.WriteAllBytes(Path.Combine(directory, "server.pfx"), certificate.Export(X509ContentType.Pfx, certificatePassword));
    LocalSecret.MachineScope = true;
    var config = new ServerConfiguration { InstallFormat = 6, MachineSecrets = true, OwnerSid = WindowsIdentity.GetCurrent().User!.Value, BindIp = "127.0.0.1", Port = port, Networks = ["192.168.1.0/24"], User = "admin", Salt = hash.Salt, PasswordHash = hash.Hash, ProtectedCertificatePassword = LocalSecret.Protect(certificatePassword) };
    UserDirectory.Bootstrap(directory, config);
    LocalSecret.MachineScope = false;
    config.Save(directory); SettingsFile.Save(new AppSettings { SqlitePath = Path.Combine(directory, "server.db"), MonitoringEnabled = false }, Path.Combine(directory, "settings.json"));
    var sessions = new ServerSessions(new UserDirectory(directory)); var issued = sessions.Login(new RemoteLogin("admin", password), "127.0.0.1")!;
    var context = new DefaultHttpContext(); context.Connection.RemoteIpAddress = IPAddress.Loopback; context.Request.Headers.Authorization = "Bearer " + issued.Token;
    Check("Issued session accepted", sessions.Valid(context)); context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.2"); Check("Token bound to socket peer", !sessions.Valid(context));
    context.Connection.RemoteIpAddress = IPAddress.Loopback; sessions.Logout(context); Check("Logged out session revoked", !sessions.Valid(context));
    var processInfo = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    foreach (var arg in new[] { "run", "--project", "src/SysAdminAssistant.Server/SysAdminAssistant.Server.csproj", "-c", "Release", "--no-build", "--no-launch-profile", "--", "--data-dir", directory }) processInfo.ArgumentList.Add(arg);
    server = Process.Start(processInfo) ?? throw new Exception("Сервер не запущен");
    var output = new List<string>(); var outputGate = new object();
    server.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outputGate) output.Add(e.Data); };
    server.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (outputGate) output.Add(e.Data); };
    server.BeginOutputReadLine(); server.BeginErrorReadLine();
    var started = false;
    for (var i = 0; i < 150 && !server.HasExited; i++)
    {
        try { using var socket = new TcpClient(); await socket.ConnectAsync(IPAddress.Loopback, port); started = true; break; } catch (SocketException) { await Task.Delay(200); }
    }
    if (!started) { lock (outputGate) throw new Exception("Сервер не открыл порт: " + string.Join("\n", output.TakeLast(12))); }
    Check("Client cannot connect to a public IP", await Rejected(() => { using var blocked = new RemoteRepository("https://203.0.113.1:7443/", ""); return Task.CompletedTask; }));
    var url = $"https://127.0.0.1:{port}/"; var pin = Convert.ToHexString(SHA256.HashData(certificate.RawData));
    using (var served = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(directory, "server.pfx"), certificatePassword, X509KeyStorageFlags.EphemeralKeySet))
        Check("Ordinary integration certificate is outside renewal window and retains pin", served.GetCertHashString(HashAlgorithmName.SHA256) == pin);
    using var anonymous = new RemoteRepository(url, pin);
    Check("Anonymous state read rejected", await Rejected(async () => { await anonymous.GetAsync<RemoteSnapshot>("api/state"); }, "Сеанс сервера завершён"));
    Check("Wrong login rejected", await Rejected(() => anonymous.LoginAsync("admin", "Fixture-Wrong-Password"), "Неверный логин или пароль сервера"));
    using var wrongPin = new RemoteRepository(url, new string('0', 64));
    Check("Different certificate rejected", await Rejected(() => wrongPin.LoginAsync("admin", password)));
    using var first = new RemoteRepository(url, pin); await first.LoginAsync("admin", password);
    var catalog=await first.GetAsync<MonitoringDictionaryCatalog>("api/dictionaries/catalog");
    Check("Matching server exposes the dictionary catalog over HTTPS",catalog.Network.Any(x=>x.Id=="generic-network"));
    _=await first.GetAsync<KscSectionView>("api/ksc/status");
    Check("Firewall read rejects an unregistered router", await Rejected(() => first.CommandAsync<Dictionary<string, string>[]>("router-firewall-list", new { Host = "192.168.254.254" }), "400"));
    Check("Firewall change rejects an unregistered router", await Rejected(() => first.CommandAsync<bool>("router-firewall-set", new { Host = "192.168.254.254", Id = "*1", Disabled = true }), "400"));
    Check("Server storage strings absent from snapshot", first.Settings.SqlitePath == "" && first.Settings.SqlConnectionString == "");
    var device = new Device { Name = "Fixture Device", Host = "127.0.0.1", Enabled = false };
    await using (var db = first.Create()) { db.Devices.Add(device); await db.SaveChangesAsync(); }
    Check("Server assigns new ID and client replica keeps it", device.Id > 0);
    using var second = new RemoteRepository(url, pin); await second.LoginAsync("admin", password);
    await using (var db = second.Create()) Check("Second client receives first client's record", await db.Devices.AnyAsync(x => x.Id == device.Id));
    await using (var db = first.Create()) { var row = await db.Devices.SingleAsync(); row.Role = "Changed by client one"; await db.SaveChangesAsync(); }
    await using (var db = second.Create()) { var row = await db.Devices.SingleAsync(); row.Name = "Stale overwrite"; Check("Stale writes rejected with HTTP 409", await Rejected(async () => { await db.SaveChangesAsync(); }, "409")); }
    await second.RefreshAsync(); await using (var db = second.Create()) Check("Conflict does not overwrite latest record", (await db.Devices.SingleAsync()).Name == "Fixture Device");
    Check("Inventory starts locked even for administrator",!await first.GetAsync<bool>("api/personal/inventory/status"));
    Check("Inventory commands require confirmation",await Rejected(()=>first.CommandAsync<bool>("inventory-employee",new Employee{FullName="Denied"}),"403"));
    await first.PostAsync<RemoteLogin,bool>("api/personal/inventory/unlock",new("admin",password));first.InventoryOpen=true;await first.RefreshAsync();
    await second.PostAsync<RemoteLogin,bool>("api/personal/inventory/unlock",new("admin",password));second.InventoryOpen=true;await second.RefreshAsync();
    var inventory = new InventoryService(new DatabaseFactory(first.Settings, first));
    await inventory.SaveEmployeeAsync(new Employee { FullName = "Fixture Employee", PersonnelNumber = "FIXTURE-001" });
    await inventory.SaveAssetAsync(new InventoryAsset { InventoryNumber = "FIXTURE-ASSET", DeviceId = device.Id, State = AssetState.Assigned });
    InventoryAsset asset; Employee employee;
    await using (var db = first.Create()) { asset = await db.Assets.SingleAsync(); employee = await db.Employees.SingleAsync(); }
    Check("Server prevents forged initial ownership", asset.State == AssetState.Stock && asset.EmployeeId == null);
    await second.RefreshAsync();
    var actRequest = new InventoryRequest(InventoryOperation.Issue, [asset.Id], employee.Id, DateTime.Today, "Fixture Org", "Fixture Representative", "Fixture City", "Fixture reason", ExpectedRevisions: new Dictionary<int, int> { [asset.Id] = asset.Revision });
    var actId = await inventory.ConductAsync(actRequest); Check("Inventory act performed on server", actId > 0);
    Check("Concurrent stale issue rejected", await Rejected(async () => { await new InventoryService(new DatabaseFactory(second.Settings, second)).ConductAsync(actRequest); }));
    await using (var db = first.Create()) Check("Act lines returned for client printing", await db.ActLines.AnyAsync(x => x.ActId == actId));
    var proposed = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(first.Settings, RemoteData.Json), RemoteData.Json)!;
    proposed.Smtp.ProtectedPassword = LocalSecret.Protect("Fixture-Smtp-Secret");
    var savedSettings = await first.SaveSettingsAsync(proposed); first.AcceptSettings(savedSettings);
    Check("Server masks integration secret", savedSettings.Smtp.ProtectedPassword == "REMOTE");
    Check("Stale settings write rejected", await Rejected(async () => { await second.SaveSettingsAsync(second.Settings); }, "409"));
    await first.RefreshAsync();
    var forbidden = new RemoteChange("reports", "Added", 0, "", System.Text.Json.JsonSerializer.SerializeToElement(new ReportEvent()));
    Check("Direct report insertion rejected", await Rejected(async () => { await first.PostAsync<RemoteChange[], RemoteChangeResult[]>("api/changes", [forbidden]); }, "400"));
    Check("Revision wait reports changes", await first.GetAsync<long>("api/wait?after=0") >= first.Revision);
    // Full server authorization paths, not just hidden client buttons.
    var alice = await first.PostAsync<UserCreate, UserView>("api/users/create", new("alice", "Alice-Fixture-Password",new(){["devices"]=1,["reports"]=1,["vault"]=2}));
    var bob = await first.PostAsync<UserCreate, UserView>("api/users/create", new("bob", "Bob-Fixture-Password",new(){["vault"]=2}));
    using var aliceClient = new RemoteRepository(url, pin); await aliceClient.LoginAsync("alice", "Alice-Fixture-Password");
    using var bobClient = new RemoteRepository(url, pin); await bobClient.LoginAsync("bob", "Bob-Fixture-Password");
    Check("Client update sources are visible without settings permission",aliceClient.Settings.ProductUpdates.Primary==first.Settings.ProductUpdates.Primary&&aliceClient.Settings.ProductUpdates.Mirror==first.Settings.ProductUpdates.Mirror);
    Check("User gets own role without private account fields", !aliceClient.CurrentUser.Administrator && aliceClient.CurrentUser.Id == alice.Id);
    Check("User cannot enumerate accounts", await Rejected(() => aliceClient.GetAsync<UserView[]>("api/users"), "403"));
    Check("User cannot authorize a product server update",await Rejected(()=>aliceClient.PostAsync<object,bool>("api/product-updates/server",new{}),"403"));
    Check("User cannot access product maintenance status",await Rejected(()=>aliceClient.GetAsync<object>("api/product-updates/status"),"403"));
    await first.PostAsync<UserCreate,UserView>("api/users/create",new("settings-editor","Settings-Fixture-Password",new(){["settings"]=2}));
    using var settingsEditor=new RemoteRepository(url,pin);await settingsEditor.LoginAsync("settings-editor","Settings-Fixture-Password");
    var protectedSettings=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settingsEditor.Settings,RemoteData.Json),RemoteData.Json)!;
    protectedSettings.ProductUpdates.Primary="https://updates.example.invalid/release.json";
    Check("Settings write role cannot replace update publisher sources",await Rejected(()=>settingsEditor.SaveSettingsAsync(protectedSettings),"403"));
    protectedSettings.ProductUpdates.Primary=settingsEditor.Settings.ProductUpdates.Primary;protectedSettings.NotificationLanguage="en";
    Check("Settings write role cannot change server notification language",await Rejected(()=>settingsEditor.SaveSettingsAsync(protectedSettings),"403"));
    Check("User cannot edit server settings", await Rejected(() => aliceClient.SaveSettingsAsync(aliceClient.Settings), "403"));
    Check("User cannot invoke RouterOS commands", await Rejected(() => aliceClient.CommandAsync<bool>("router-reboot",new { Host = "127.0.0.1" }), "403"));
    Check("User cannot forge generic database mutations", await Rejected(() => aliceClient.PostAsync<RemoteChange[],RemoteChangeResult[]>("api/changes",[new("devices","Deleted",device.Id,"",System.Text.Json.JsonSerializer.SerializeToElement(device))]),"403"));
    Check("No-infrastructure permission hides all shared rows", bobClient.Snapshot.Rows.All(x => x.Value.Length == 0));
    var saveVault = new VaultSave(0,0,"Alice secret",null,new("login","TopSecret-Alice-123","private note"));
    Check("Vault rejects direct write without unlock", await Rejected(() => aliceClient.PostAsync<VaultSave,bool>("api/personal/vault/save",saveVault),"403"));
    Check("Wrong Vault reauthentication fails", !await aliceClient.PostAsync<RemoteLogin,bool>("api/personal/vault/unlock",new("alice","Wrong-Fixture-Password")));
    Check("Vault reauthentication succeeds", await aliceClient.PostAsync<RemoteLogin,bool>("api/personal/vault/unlock",new("alice","Alice-Fixture-Password")));
    await aliceClient.PostAsync<VaultSave,bool>("api/personal/vault/save",saveVault);
    var aliceRows=await aliceClient.GetAsync<VaultItem[]>("api/personal/vault"); var aliceEntry=aliceRows.Single();
    Check("Server assigns authenticated Vault owner", aliceEntry.OwnerId==alice.Id);
    Check("Other user cannot see Vault metadata", (await bobClient.GetAsync<VaultItem[]>("api/personal/vault")).Length==0);
    await bobClient.PostAsync<RemoteLogin,bool>("api/personal/vault/unlock",new("bob","Bob-Fixture-Password"));
    Check("Other user cannot read guessed entry ID",await Rejected(()=>bobClient.PostAsync<VaultTarget,CredentialPayload>("api/personal/vault/read",new(aliceEntry.Id,aliceEntry.Revision)),"403"));
    Check("Other user cannot replace entry payload",await Rejected(()=>bobClient.PostAsync<VaultSave,bool>("api/personal/vault/save",saveVault with { Id=aliceEntry.Id,Revision=aliceEntry.Revision }),"403"));
    Check("Other user cannot delete guessed entry ID",await Rejected(()=>bobClient.PostAsync<VaultTarget,bool>("api/personal/vault/delete",new(aliceEntry.Id,aliceEntry.Revision)),"403"));
    await first.PostAsync<RemoteLogin,bool>("api/personal/vault/unlock",new("admin",password));
    Check("Administrator sees all Vault owners",(await first.GetAsync<VaultItem[]>("api/personal/vault")).Single().OwnerId==alice.Id);
    var adminPayload=await first.PostAsync<VaultTarget,CredentialPayload>("api/personal/vault/read",new(aliceEntry.Id,aliceEntry.Revision));
    Check("Administrator decrypts another user's entry",adminPayload.Password=="TopSecret-Alice-123");
    await aliceClient.RefreshAsync();
    Check("Vault ciphertext and keys never enter snapshot",aliceClient.Snapshot.Rows["credentials"].Length==0&&aliceClient.Snapshot.Rows["vault"].Length==0);
    // Access to a section is enforced on snapshots, commands and mutations independently.
    var clerk=await first.PostAsync<UserCreate,UserView>("api/users/create",new("clerk","Clerk-Fixture-Password",new(){["payments"]=2}));
    using var clerkClient=new RemoteRepository(url,pin);await clerkClient.LoginAsync("clerk","Clerk-Fixture-Password");
    Check("Bills-only user gets no device rows",clerkClient.Snapshot.Rows["devices"].Length==0);
    var bill=new Payment {Title="Scoped bill",DueDate=DateTime.Today};
    await using(var db=clerkClient.Create()){db.Payments.Add(bill);await db.SaveChangesAsync();}
    Check("Bills write permission saves on server",bill.Id>0);
    Check("No password section blocks Vault endpoint",await Rejected(()=>clerkClient.GetAsync<VaultItem[]>("api/personal/vault"),"403"));
    Check("No password section blocks Vault unlock",await Rejected(()=>clerkClient.PostAsync<RemoteLogin,bool>("api/personal/vault/unlock",new("clerk","Clerk-Fixture-Password")),"403"));
    Check("No reports permission blocks mail diagnostics",await Rejected(()=>clerkClient.CommandAsync<MailboxPollInfo[]>("mail-status",new{}),"403"));
    Check("Scoped writer cannot grant itself roles",await Rejected(()=>clerkClient.PostAsync<UserUpdate,UserView>("api/users/update",new(clerk.Id,true,clerk.Revision,"",AccessRules.Modules.ToDictionary(x=>x,x=>2))),"403"));
    var readClerk=await first.PostAsync<UserUpdate,UserView>("api/users/update",new(clerk.Id,true,clerk.Revision,"",new(){["payments"]=1}));
    Check("Permission changes revoke current session",await Rejected(()=>clerkClient.GetAsync<UserView>("api/me")));
    using var readClient=new RemoteRepository(url,pin);await readClient.LoginAsync("clerk","Clerk-Fixture-Password");
    Check("Read permission receives bills",readClient.Snapshot.Rows["payments"].Length>0);
    Check("Read permission cannot write bills",await Rejected(()=>readClient.PostAsync<RemoteChange[],RemoteChangeResult[]>("api/changes",[new("payments","Added",0,"",System.Text.Json.JsonSerializer.SerializeToElement(new Payment {Title="Denied",DueDate=DateTime.Today}))]),"403"));
    // Existing password entries keep ownership and device binding; a read-only user can still view own entry.
    await bobClient.PostAsync<VaultSave,bool>("api/personal/vault/save",new(0,0,"Bob secret",null,new("bob","Bob-Entry-Secret","")));
    var bobEntry=(await bobClient.GetAsync<VaultItem[]>("api/personal/vault")).Single();
    await first.PostAsync<UserUpdate,UserView>("api/users/update",new(bob.Id,true,bob.Revision,"",new(){["vault"]=1}));
    using var bobReader=new RemoteRepository(url,pin);await bobReader.LoginAsync("bob","Bob-Fixture-Password");
    await bobReader.PostAsync<RemoteLogin,bool>("api/personal/vault/unlock",new("bob","Bob-Fixture-Password"));
    Check("Vault read role decrypts own existing entry",(await bobReader.PostAsync<VaultTarget,CredentialPayload>("api/personal/vault/read",new(bobEntry.Id,bobEntry.Revision))).Password=="Bob-Entry-Secret");
    Check("Vault read role cannot replace own entry",await Rejected(()=>bobReader.PostAsync<VaultSave,bool>("api/personal/vault/save",new(bobEntry.Id,bobEntry.Revision,"Denied",null,new("","",""))),"403"));
    Check("Vault read role cannot delete own entry",await Rejected(()=>bobReader.PostAsync<VaultTarget,bool>("api/personal/vault/delete",new(bobEntry.Id,bobEntry.Revision)),"403"));
    await first.PostAsync<VaultSave,bool>("api/personal/vault/save",new(aliceEntry.Id,aliceEntry.Revision,aliceEntry.Name,device.Id,adminPayload));
    aliceEntry=(await aliceClient.GetAsync<VaultItem[]>("api/personal/vault")).Single();
    Check("Existing Vault entry binds to device without duplicate",aliceEntry.DeviceId==device.Id && (await aliceClient.GetAsync<VaultItem[]>("api/personal/vault")).Length==1);
    var vaultService=new ServiceEntry{Name="Password service fixture",Type="TCP",Address="127.0.0.1:1"};await using(var serviceDb=first.Create()){serviceDb.Services.Add(vaultService);await serviceDb.SaveChangesAsync();}
    Check("Vault write alone cannot assign a hidden service",await Rejected(()=>aliceClient.PostAsync<VaultSave,bool>("api/personal/vault/save",new(aliceEntry.Id,aliceEntry.Revision,aliceEntry.Name,device.Id,adminPayload,vaultService.Id)),"403"));
    await first.PostAsync<VaultSave,bool>("api/personal/vault/save",new(aliceEntry.Id,aliceEntry.Revision,aliceEntry.Name,device.Id,adminPayload,vaultService.Id));aliceEntry=(await aliceClient.GetAsync<VaultItem[]>("api/personal/vault")).Single();Check("Password simultaneously links device and service without duplication",aliceEntry.ServiceId==vaultService.Id&&aliceEntry.DeviceId==device.Id);Check("Service link denial uses current unlocked reader",(await bobReader.GetAsync<UserView>("api/me")).Id==bob.Id&&(await bobReader.PostAsync<VaultTarget,CredentialPayload>("api/personal/vault/read",new(bobEntry.Id,bobEntry.Revision))).Password=="Bob-Entry-Secret");Check("Service link never bypasses other owner vault restriction",await Rejected(()=>bobReader.PostAsync<VaultTarget,CredentialPayload>("api/personal/vault/read",new(aliceEntry.Id,aliceEntry.Revision)),"403"));
    var changedAlice=await first.PostAsync<UserUpdate,UserView>("api/users/update",new(alice.Id,true,alice.Revision,"Alice-Reset-Password",alice.Permissions));
    Check("Password reset revokes previous session",await Rejected(()=>aliceClient.GetAsync<UserView>("api/me")));
    using var newAlice=new RemoteRepository(url,pin);await newAlice.LoginAsync("alice","Alice-Reset-Password");
    await newAlice.PostAsync<RemoteLogin,bool>("api/personal/vault/unlock",new("alice","Alice-Reset-Password"));
    Check("Password reset retains owner decryption key",(await newAlice.PostAsync<VaultTarget,CredentialPayload>("api/personal/vault/read",new(aliceEntry.Id,aliceEntry.Revision))).Password==adminPayload.Password);
    await first.PostAsync<UserUpdate,UserView>("api/users/update",new(changedAlice.Id,false,changedAlice.Revision,"",changedAlice.Permissions));
    Check("Blocking revokes active session",await Rejected(()=>newAlice.GetAsync<UserView>("api/me")));
    Check("Blocked user cannot log in",await Rejected(()=>newAlice.LoginAsync("alice","Alice-Reset-Password")));
    Check("Administrator keeps access to blocked user's entries",(await first.PostAsync<VaultTarget,CredentialPayload>("api/personal/vault/read",new(aliceEntry.Id,aliceEntry.Revision))).Password==adminPayload.Password);
    // Polling must work independently of the ICMP/HTTP/TCP switch. Use a closed local port, no real mailbox.
    await first.RefreshAsync();
    var mailConfig=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(first.Settings,RemoteData.Json),RemoteData.Json)!;
    mailConfig.MonitoringEnabled=false;
    var closedProbe=new TcpListener(IPAddress.Loopback,0);closedProbe.Start();var closedPort=((IPEndPoint)closedProbe.LocalEndpoint).Port;closedProbe.Stop();
    mailConfig.Mailboxes.Add(new MailSettings {Id="poll-fixture",Name="Fixture mailbox",Enabled=true,Host="127.0.0.1",Port=closedPort,User="fixture",Folder="INBOX",UnseenOnly=false,RequireSpfDkim=false,AllowedDomains=["example.test"],ProtectedPassword=LocalSecret.Protect("Fixture-Imap-Secret")});
    first.AcceptSettings(await first.SaveSettingsAsync(mailConfig));
    await first.CommandAsync<bool>("mail-poll",new{});
    MailboxPollInfo[] mailChecks=[];
    for(var i=0;i<60;i++) {
        mailChecks=await first.CommandAsync<MailboxPollInfo[]>("mail-status",new{});
        if(mailChecks.Any(x=>x.Id=="poll-fixture" && x.FinishedUtc!=null))break;
        await Task.Delay(200);
    }
    Check("IMAP executes with network monitoring disabled",mailChecks.Any(x=>x.Id=="poll-fixture" && x.FinishedUtc!=null && x.Error.Length>0));
    Check("Mailbox failure exposes stage without password",mailChecks.Any(x=>x.Error.Contains("TLS-соединение")) && !System.Text.Json.JsonSerializer.Serialize(mailChecks).Contains("Fixture-Imap-Secret"));
    await first.PostAsync<UserCreate,UserView>("api/users/create",new("mail-reader","Mail-Reader-Password",new(){["reports"]=1}));
    using var mailReader=new RemoteRepository(url,pin);await mailReader.LoginAsync("mail-reader","Mail-Reader-Password");
    Check("Reports reader can inspect mailbox diagnostics",(await mailReader.CommandAsync<MailboxPollInfo[]>("mail-status",new{})).Any(x=>x.Id=="poll-fixture"));
    Check("Reports reader cannot change mailbox settings",await Rejected(()=>mailReader.CommandAsync<string>("mail-test",new MailConnectionTest(mailConfig.Mailboxes.Last(),"")),"403"));
    var audit=await first.GetAsync<AuditView[]>("api/audit");
    Check("Foreign administrator read audited",audit.Any(x=>x.Action=="vault.read"&&x.ActorLogin=="admin"&&x.OwnerId==alice.Id));
    Check("Passwords absent from audit",!System.Text.Json.JsonSerializer.Serialize(audit).Contains("TopSecret-Alice-123"));
    var publicSnapshot=await first.GetAsync<RemoteSnapshot>("api/state");
    Check("Ordinary snapshot omits unlocked personnel and acts",new[]{"employees","assets","acts","actLines"}.All(k=>publicSnapshot.Rows[k].Length==0));
    var lockedExport=await first.GetAsync<ExcelFile>("api/excel/devices/export");
    Check("Excel device export has expected fixture",ExcelExchange.Read(lockedExport.File,"devices").Any(x=>x.Values[0]==device.Id.ToString()));
    var employeeSheet=ExcelExchange.Write("employees",new[]{new[]{"","","Excel employee","XLSX-1","","","","Да"}});
    var employeePreview=await first.PostAsync<ExcelRequest,ExcelPreview>("api/excel/employees/preview",new(employeeSheet));
    Check("Excel preview validates without saving",employeePreview.Valid);
    await first.RefreshAsync();await using(var xdb=first.Create())Check("Excel preview did not insert",!await xdb.Employees.AnyAsync(x=>x.PersonnelNumber=="XLSX-1"));
    var badBatch=ExcelExchange.Write("employees",new[]{new[]{"","","Excel employee","XLSX-1","","","","Да"},new[]{"","","","XLSX-2","","","","Да"}});
    Check("Excel invalid batch rejected",await Rejected(()=>first.PostAsync<ExcelRequest,ExcelPreview>("api/excel/employees/import",new(badBatch)),"400"));
    await first.RefreshAsync();await using(var xdb=first.Create())Check("Excel batch rollback preserves all rows",!await xdb.Employees.AnyAsync(x=>x.PersonnelNumber=="XLSX-1"));
    await first.PostAsync<ExcelRequest,ExcelPreview>("api/excel/employees/import",new(employeeSheet));
    await first.RefreshAsync();await using(var xdb=first.Create())Check("Excel batch commits once",await xdb.Employees.CountAsync(x=>x.PersonnelNumber=="XLSX-1")==1);
    Check("Excel import respects read only Vault role",await Rejected(()=>bobReader.PostAsync<ExcelRequest,ExcelPreview>("api/excel/vault/import",new(ExcelExchange.Write("vault",[]))),"403"));
    var ownerExport=await bobReader.GetAsync<ExcelFile>("api/excel/vault/export");
    Check("Excel Vault export respects owner scope",ExcelExchange.Read(ownerExport.File,"vault").All(x=>x.Values[0]==bobEntry.Id.ToString()));
    await first.PostAsync<object,bool>("api/personal/vault/lock",new{});
    Check("Excel locked Vault export rejected",await Rejected(()=>first.GetAsync<ExcelFile>("api/excel/vault/export"),"403"));
    Check("Explicit lock enforced on server",await Rejected(()=>first.PostAsync<VaultTarget,CredentialPayload>("api/personal/vault/read",new(aliceEntry.Id,aliceEntry.Revision)),"403"));
    await CompatibilitySpecs.Run(directory,Check);
    Console.WriteLine("NetworkSpecs complete: HTTPS, auth, certificate pin, two clients, conflicts, inventory and secret masking.");
}
finally
{
    if (server != null) { if (!server.HasExited) server.Kill(true); await server.WaitForExitAsync(); server.Dispose(); }
    Directory.Delete(directory, true);
}
