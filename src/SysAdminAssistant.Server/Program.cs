using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
using SysAdminAssistant.Server;

if(args.Length==3&&args[0] is "--invoice-extract" or "--invoice-convert"){Environment.ExitCode=await InvoiceExtraction.ExtractAsync(args[1],args[2],args[0]=="--invoice-convert");return;}
var directory = ServerConfiguration.DefaultDirectory;
if (Console.IsInputRedirected) Console.InputEncoding = new System.Text.UTF8Encoding(false);
if (Console.IsOutputRedirected) Console.OutputEncoding = new System.Text.UTF8Encoding(false);
var index = Array.IndexOf(args, "--data-dir");
if (index >= 0) { if (index + 1 >= args.Length) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите каталог после --data-dir")); directory = Path.GetFullPath(args[index + 1]); }
try
{
    if (args.Contains("--setup"))
    {
        var serviceIndex = Array.IndexOf(args, "--service-sid");
        if (serviceIndex >= 0)
        {
            var principal = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
            if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Настройка учётной записи службы требует прав администратора"));
            if (serviceIndex + 2 >= args.Length) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите SID и имя учётной записи службы"));
            _ = new System.Security.Principal.SecurityIdentifier(args[serviceIndex + 1]);
            ServerConfiguration.Setup(directory, args[serviceIndex + 1], args[serviceIndex + 2]);
        }
        else ServerConfiguration.Setup(directory);
        return;
    }
    if (!File.Exists(Path.Combine(directory, "server.json"))) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Сначала запустите SysAdminAssistant.Server.exe --setup"));
    var config = ServerConfiguration.Load(directory);
    LocalSecret.MachineScope = config.MachineSecrets;
    var maintenance = args.Contains("--local-admin-status") || args.Contains("--local-admin-backup") || args.Contains("--info") || args.Contains("--reset-password") || args.Contains("--import-network-dictionaries");
    var admin = OperatingSystem.IsWindows() && new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    if (!OperatingSystem.IsWindows() || System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value != config.OwnerSid && !(config.MachineSecrets && maintenance && admin)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Запускай сервер под учётной записью, указанной в конфигурации службы"));
    if(args.Contains("--local-admin-status")||args.Contains("--local-admin-backup")){await LocalAdministration.RunAsync(directory,config,args,admin);return;}
    if(args.Contains("--import-network-dictionaries")){var importIndex=Array.IndexOf(args,"--import-network-dictionaries");if(importIndex+1>=args.Length)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите файл словарей"));var count=new NetworkStore(directory).Import(File.ReadAllText(args[importIndex+1]));Console.WriteLine($"Импортировано словарей: {count}. Устройства и подключения не изменены.");return;}
    if (args.Contains("--reset-password"))
    {
        var password = ServerConfiguration.ReadPassword("Новый пароль администратора: ");
        if (password != ServerConfiguration.ReadPassword("Повторите пароль: ")) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пароли не совпадают"));
        new UserDirectory(directory).ResetAdministrator(password);
        Console.WriteLine("Пароль заменён. Перезапустите сервер, чтобы отозвать старые сеансы."); return;
    }
    using var cert = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(directory, "server.pfx"), LocalSecret.Unprotect(config.ProtectedCertificatePassword), X509KeyStorageFlags.MachineKeySet);
    if (!cert.HasPrivateKey) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("HTTPS-сертификат не содержит закрытый ключ. Проверьте server.pfx"));
    var state=new ServerState(directory,cert.GetCertHashString(HashAlgorithmName.SHA256));
    TelegramSender.ServerDelayMinutes=()=>state.Settings.Telegram.DelayMinutes;
    using var certificates=new CertificateLifecycle(directory,LocalSecret.Unprotect(config.ProtectedCertificatePassword),config.BindIp,state.Agents);state.Certificates=certificates;state.ServerUrl=config.Url;certificates.Maintain(DateTime.UtcNow);
    Console.WriteLine("SysAdmin Assistant Server · " + config.Url);
    Console.WriteLine("SHA-256: " + certificates.Current.GetCertHashString(HashAlgorithmName.SHA256));
    if (args.Contains("--info")) return;
    // No environment/appsettings URL override may introduce a public or plaintext listener.
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
    builder.Configuration.Sources.Clear();
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = 16_000_000;
        options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
        options.Listen(IPAddress.Parse(config.BindIp), config.Port, listener => listener.UseHttps(https=>https.ServerCertificateSelector=(_,_)=>certificates.Current));
    });
    builder.Services.AddWindowsService(options => options.ServiceName = "SysAdminAssistantServer");
    builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.PropertyNamingPolicy = null; o.SerializerOptions.PropertyNameCaseInsensitive = true; });
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = 429;
        o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });
    builder.Services.AddHostedService<DatabaseStartup>();
    var users = new UserDirectory(directory); var organizationVault = new OrganizationVault(state, users);
    builder.Services.AddSingleton(state);builder.Services.AddSingleton<KscMonitor>();builder.Services.AddHostedService(sp=>sp.GetRequiredService<KscMonitor>()); builder.Services.AddSingleton(new ServerSessions(users)); builder.Services.AddHostedService<InvoiceWorker>();builder.Services.AddHostedService<ServerHeartbeatWorker>();builder.Services.AddHostedService<ServerMonitor>(); builder.Services.AddHostedService<RouterMonitoringWorker>(); builder.Services.AddHostedService<AvailabilityMonitoringWorker>(); builder.Services.AddHostedService<RouterConfigurationWorker>(); builder.Services.AddHostedService<TelegramWorker>(); builder.Services.AddHostedService<TelegramPinsWorker>(); builder.Services.AddHostedService<AgentWatchdog>();builder.Services.AddHostedService<WorkstationNotifications>();builder.Services.AddHostedService<HardwareHistoryWorker>();builder.Services.AddHostedService<CertificateWorker>();
    var app = builder.Build(); var networkAccess = new ServerNetworkAccess(directory,config.Networks);
    app.MapGet("/api/server-networks",(HttpContext context)=>networkAccess.View(context.Connection.RemoteIpAddress!));
    app.MapPost("/api/server-networks",(ServerNetworksSave request,HttpContext context)=>networkAccess.Save(request,context.Connection.RemoteIpAddress!));
    app.Use(async (context, next) =>
    {
        using var timeScope=AppTime.Scope(state.Settings.TimeZoneId);
        context.Response.Headers.CacheControl = "no-store"; context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        var ip = context.Connection.RemoteIpAddress;
        if (ip == null || !IPAddress.IsLoopback(ip) && !networkAccess.Allows(ip)) { context.Response.StatusCode = 403; return; }
        // X-Forwarded-For is deliberately ignored: the socket peer is the client.
        var agentPath=context.Request.Path is var requestPath && (requestPath=="/agent/workstation-report"||requestPath=="/agent/workstation-update"||requestPath=="/agent/linux-install"||requestPath=="/agent/linux-update-manifest"||requestPath=="/agent/linux-update-file"||requestPath=="/agent/enroll"||requestPath=="/agent/report"||requestPath=="/agent/report-v2"||requestPath=="/agent/report-v3"||requestPath=="/agent/update-manifest"||requestPath=="/agent/update-file");
        if (!agentPath && context.Request.Path != "/api/login" && context.Request.Path != "/api/server-trust" && !context.RequestServices.GetRequiredService<ServerSessions>().Valid(context)) { context.Response.StatusCode = 401; return; }
        try
        {
            if (!agentPath && context.Request.Path != "/api/login" && context.Request.Path != "/api/server-trust")
            {
                var actor = context.RequestServices.GetRequiredService<ServerSessions>().Actor(context)!;
                if (actor == null) { context.Response.StatusCode=401; return; }
                context.Items["Actor"] = actor;
                var path = (context.Request.Path.Value ?? "").TrimEnd('/').ToLowerInvariant();
                bool personal = path is "/api/me" or "/api/state" or "/api/wait" or "/api/logout" or "/api/personal/password" or "/api/server-certificate" or "/api/personal/workspace" or "/api/personal/navigation-health";
                if (path.StartsWith("/api/personal/vault",StringComparison.Ordinal)) { AccessRules.Require(actor,"vault",path.EndsWith("/save")||path.EndsWith("/delete")?2:1); personal=true; }
                else if(path.StartsWith("/api/excel/",StringComparison.Ordinal)) {
                    var parts=path.Split('/');if(parts.Length!=5)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестная операция Excel"));
                    AccessRules.Require(actor,ExcelExchange.Module(parts[3]),parts[4] is "preview" or "import"?2:1);personal=true;
                    if(parts[3]=="vault"&&parts[4]!="template"&&!context.RequestServices.GetRequiredService<ServerSessions>().VaultUnlocked(context))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Vault заблокирован: подтвердите пароль учётной записи"));
                }
                else if(path=="/api/device-update-status"){AccessRules.Require(actor,"devices");personal=true;}
                else if(path.StartsWith("/api/invoices",StringComparison.Ordinal)){personal=true;
                    if(path=="/api/invoices/settings"&&context.Request.Method!="GET"||path=="/api/invoices/poll")AccessRules.Require(actor,"payments",2);
                    if(path is "/api/invoices/template-test" or "/api/invoices/reprocess")AccessRules.Require(actor,"payments",path.EndsWith("/reprocess")?2:1);
                    var configuration=path is "/api/invoices/settings" or "/api/invoices/history" or "/api/invoices/test" or "/api/invoices/poll" or "/api/invoices/template-test" or "/api/invoices/reprocess";
                    AccessRules.Require(actor,configuration?"settings":"payments",configuration?2:context.Request.Method=="GET"?1:2);
                }
                else if(path.StartsWith("/api/personal/inventory/",StringComparison.Ordinal)){AccessRules.Require(actor,"inventory");personal=true;}
                else if(path=="/api/settings") AccessRules.Require(actor,"settings",2);
                else if(path=="/api/changes") { /* Resource checks run inside the transaction. */ }
                else if(path=="/api/poll") { if(!actor.Administrator && AccessRules.Level(actor,"reports")<1 && AccessRules.Level(actor,"dmarc")<1 && AccessRules.Level(actor,"overview")<1)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Нет права запуска проверок")); }
                else if(path.StartsWith("/api/command/",StringComparison.Ordinal))
                {
                    AccessRules.Command(actor,path[13..]);
                    if(path is "/api/command/inventory-photo-read" or "/api/command/inventory-photo-write" &&
                       context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is {IsReadOnly:false} photoLimit)
                        photoLimit.MaxRequestBodySize=path.EndsWith("-write",StringComparison.Ordinal)?1_000_000:2048;
                }
                else if(!personal && !actor.Administrator) throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Операция требует главного администратора"));
                if (path is "/api/personal/vault/read" or "/api/personal/vault/save" or "/api/personal/vault/delete")
                    if (!context.RequestServices.GetRequiredService<ServerSessions>().VaultUnlocked(context)) throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Vault заблокирован: подтвердите пароль учётной записи"));
                if((path.StartsWith("/api/command/inventory-",StringComparison.Ordinal)||path.StartsWith("/api/excel/employees/",StringComparison.Ordinal)&&!path.EndsWith("/template",StringComparison.Ordinal))&&!context.RequestServices.GetRequiredService<ServerSessions>().InventoryUnlocked(context))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Подтвердите пароль для доступа к сотрудникам и актам"));
                if(path.StartsWith("/api/invoices",StringComparison.Ordinal)&&context.Request.Method!="GET")organizationVault.Audit(actor,"invoice.request:"+path);
                if (actor.Administrator && context.Request.Method != "GET" && !personal) organizationVault.Audit(actor,"admin.request:"+path);
            }
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception e) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = e is UnauthorizedAccessException ? 403 : e is KscConnectionException ? 502 : e is BadHttpRequestException bad ? bad.StatusCode : e is InvoiceConflictException or ConflictException or DbUpdateConcurrencyException ? 409 : e is ArgumentException or InvalidDataException or InvalidOperationException or FormatException or System.ComponentModel.DataAnnotations.ValidationException or JsonException ? 400 : 500;
            if (context.Response.StatusCode == 500)
                app.Logger.LogError(e, "Server request failed: {Method} {Path}", context.Request.Method, context.Request.Path.Value);
            await context.Response.WriteAsJsonAsync(new { Error = context.Response.StatusCode == 500 ? "Серверная операция не выполнена" : e.Message }, context.RequestAborted);
        }
    });
    app.UseRateLimiter();
    app.MapGet("/api/server-trust",()=>certificates.Trust(config.Url));
    ProductUpdateEndpoints.Map(app,state);
    app.MapGet("/api/server-certificate",(HttpContext c)=>certificates.Status(((UserView)c.Items["Actor"]!).Administrator));
    app.MapPost("/api/login", (RemoteLogin login, HttpContext context, ServerSessions sessions) =>
    {
        var token = sessions.Login(login, context.Connection.RemoteIpAddress!.ToString());
        return token == null ? Results.Unauthorized() : Results.Json(token, RemoteData.Json);
    }).RequireRateLimiting("login");
    app.MapPost("/api/logout", (HttpContext context, ServerSessions sessions) => { sessions.Logout(context); return Results.Ok(true); });
    app.MapGet("/api/wait", async (long after, CancellationToken token) =>
    {
        var until = DateTime.UtcNow.AddSeconds(25);
        while (state.Revision == after && DateTime.UtcNow < until) await Task.Delay(500, token);
        return Results.Ok(state.Revision);
    });
    app.MapGet("/api/personal/navigation-health", async (HttpContext context, KscMonitor ksc, CancellationToken token) =>
        Results.Json(await NavigationHealth.ReadAsync(state,ksc,(UserView)context.Items["Actor"]!,token),RemoteData.Json));
    app.MapGet("/api/state", async (HttpContext context, CancellationToken token) =>
    {
        await state.Gate.WaitAsync(token); try { return Results.Json(await state.SnapshotAsync(token, (UserView)context.Items["Actor"]!,context.Request.Query["inventory"]=="true"&&context.RequestServices.GetRequiredService<ServerSessions>().InventoryUnlocked(context)), RemoteData.Json); } finally { state.Gate.Release(); }
    });
    app.MapPost("/api/settings", async (RemoteSettingsChange request, HttpContext context, CancellationToken token) =>
    {
        await state.Gate.WaitAsync(token); try { var actor=(UserView)context.Items["Actor"]!;var next=request.Value;if(!actor.Administrator && next!=null && (next.NotificationLanguage!=state.Settings.NotificationLanguage || next.ProductUpdates?.Primary!=state.Settings.ProductUpdates.Primary || next.ProductUpdates?.Mirror!=state.Settings.ProductUpdates.Mirror))throw new UnauthorizedAccessException("Only the principal administrator can change update sources or server notification language.");return Results.Json(state.SaveSettings(request), RemoteData.Json); } finally { state.Gate.Release(); }
    });
    app.MapPost("/api/changes", async (RemoteChange[] changes, HttpContext context, CancellationToken token) =>
    {
        await state.Gate.WaitAsync(token); try { var result = await ServerChanges.ApplyAsync(state.Factory, changes, token, (UserView)context.Items["Actor"]!); state.Touch(); return Results.Json(result, RemoteData.Json); } finally { state.Gate.Release(); }
    });
    app.MapPost("/api/poll", () => { state.RequestPoll(); return Results.Ok(true); });
    var telegramTest=new SemaphoreSlim(1);
    app.MapPost("/api/command/{command}", async (string command, JsonElement payload, HttpContext context, CancellationToken token) =>
    {
        if(command.Equals("telegram-test",StringComparison.OrdinalIgnoreCase)){
            if(!telegramTest.Wait(0))throw new InvalidOperationException("Тест Telegram уже ожидает отправки");
            _=SendTelegramTestAsync();
            return Results.Ok(true);
        }
        await state.Gate.WaitAsync(token); try { var result = await ServerCommands.ExecuteAsync(state, command.ToLowerInvariant(), payload, token, (UserView)context.Items["Actor"]!); if(command.ToLowerInvariant() is not ("hardware-history" or "inventory-hardware-history" or "inventory-photo-read" or "workstations-list" or "workstations-config" or "workstations-denied" or "device-problems" or "router-audit-view" or "device-updates" or "agent-events" or "agent-metric-history" or "agent-journal-history" or "agent-view" or "payment-history" or "router-targets" or "router-snapshot" or "router-firewall-list" or "router-monitor-list" or "router-events" or "router-connection" or "router-dictionaries" or "telegram-pin-status" or "server-heartbeat-status" or "server-heartbeat-package" or "mail-status" or "mail-test" or "retention-preview" or "dmarc-senders"))state.Touch(); return Results.Json(result, RemoteData.Json); } finally { state.Gate.Release(); }
    });
    async Task SendTelegramTestAsync()
    {
        try{
            using var language=ProductText.Scope(state.Settings.NotificationLanguage);
            await TelegramSender.SendAsync(state.Settings.Telegram,"SysAdmin Assistant · тест уведомлений",app.Lifetime.ApplicationStopping);
        }catch(OperationCanceledException) when(app.Lifetime.ApplicationStopping.IsCancellationRequested){}
        catch(Exception e){app.Logger.LogWarning("Тест Telegram не отправлен: {Type}",e.GetType().Name);}
        finally{telegramTest.Release();}
    }
    app.MapGet("/api/workstations/installer",(HttpContext context)=>{
        if(context.Items["Actor"] is not UserView {Administrator:true})throw new UnauthorizedAccessException("Установщик доступен главному администратору.");
        const string name="SysAdminAssistant_Workstation_x64.msi";
        var path=Path.Combine(AppContext.BaseDirectory,"AgentTemplate","Workstation",name);
        return File.Exists(path)?Results.File(path,"application/x-msi",name)
            :Results.NotFound(new {Error="MSI отсутствует на сервере. Обновите сервер из полного комплекта публикации."});
    });
    app.MapPost("/agent/workstation-update",async(HttpContext c,CancellationToken token)=>
        await StationUpdateDelivery.GetAsync(state,c,token));
    app.MapPost("/agent/workstation-report",async(HttpContext c,CancellationToken token)=>{
        var reply=state.Workstations.Report(await AgentEndpoints.ReadAsync<StationRequest>(c,token),DateTime.UtcNow);
        c.Response.Headers[StationPeripherals.CapabilityHeader]="1";
        return Results.Json(reply,AgentWire.Json);
    });
    app.MapGet("/agent/linux-install",(HttpContext context)=>Results.File(LinuxInstallBundles.Take(context.Request.Headers["X-SAA-Install"].ToString()),"application/octet-stream"));
    app.MapPost("/agent/enroll", async (HttpContext c,CancellationToken token)=>{
        var request=await AgentEndpoints.ReadAsync<AgentEnrollment>(c,token);
        return Results.Json(state.Agents.Enroll(request),AgentWire.Json);
    }).RequireRateLimiting("login");
    app.MapPost("/agent/report", async (HttpContext c,CancellationToken token)=>{
        var request=await AgentEndpoints.ReadAsync<AgentEnvelope>(c,token);
        await state.Gate.WaitAsync(token);try{return Results.Json(await AgentMonitoring.ReportAsync(state,request,token),AgentWire.Json);}catch(AgentSequenceException e){return Results.Json(new AgentSequenceNotice(e.NextSequence),AgentWire.Json,statusCode:409);}finally{state.Gate.Release();}
    });
    app.MapPost("/agent/report-v2", async (HttpContext c,CancellationToken token)=>{
        var request=await AgentEndpoints.ReadAsync<AgentEnvelope>(c,token);
        await state.Gate.WaitAsync(token);try{var reply=await AgentMonitoring.ReportAsync(state,request,token);return Results.Json(new AgentReplyV2(reply.Policy,reply.Certificate,state.Agents.Trust()),AgentWire.Json);}catch(AgentSequenceException e){return Results.Json(new AgentSequenceNotice(e.NextSequence),AgentWire.Json,statusCode:409);}finally{state.Gate.Release();}
    });
    app.MapPost("/agent/report-v3", async (HttpContext c,CancellationToken token)=>{
        var request=await AgentEndpoints.ReadAsync<AgentEnvelope>(c,token);
        await state.Gate.WaitAsync(token);try{var reply=await AgentMonitoring.ReportAsync(state,request,token);return Results.Json(new AgentReplyV3(reply.Policy,reply.Certificate,state.Agents.Trust(),state.EventStore(state.Agents.DeviceId(request.AgentId)).View(state.Agents.DeviceId(request.AgentId)).Policy,state.Agents.Get(state.Agents.DeviceId(request.AgentId))?.Report?.EventBatch?.Id??""),AgentWire.Json);}catch(AgentSequenceException e){return Results.Json(new AgentSequenceNotice(e.NextSequence),AgentWire.Json,statusCode:409);}finally{state.Gate.Release();}
    });
    app.MapPost("/agent/linux-update-manifest",async(HttpContext c,CancellationToken token)=>Results.Json(state.Agents.LinuxRelease(await AgentEndpoints.ReadAsync<AgentReleaseRequest>(c,token)),AgentWire.Json));
    var linuxReleaseDelivery=new LinuxReleaseDelivery(state.Agents);
    app.MapPost("/agent/linux-update-file",(HttpContext c,CancellationToken token)=>linuxReleaseDelivery.SendAsync(c,token));
    app.MapPost("/agent/update-manifest",async(HttpContext c,CancellationToken token)=>Results.Json(state.Agents.Release(await AgentEndpoints.ReadAsync<AgentReleaseRequest>(c,token)),AgentWire.Json));
    app.MapPost("/agent/update-file",async(HttpContext c,CancellationToken token)=>Results.Bytes(state.Agents.ReleaseFile(await AgentEndpoints.ReadAsync<AgentReleaseDownload>(c,token)),"application/octet-stream"));
    UserView Actor(HttpContext context) => (UserView)context.Items["Actor"]!;
    app.MapPost("/api/ksc/findings",async(KscIssueResolution request,KscMonitor worker,CancellationToken token)=>Results.Ok(await worker.Findings(request,token)));
    app.MapPost("/api/ksc/resolve",(KscIssueResolution r,KscMonitor worker)=>Results.Ok(worker.ResolveIssue(r.Key)));
    var dictionaryStore=new NetworkStore(directory);
    KscRuleView CurrentKscRules()=>dictionaryStore.State(net=>new KscRuleView(AgentWire.Hash(JsonSerializer.Serialize(net.Ksc.Rules)),net.Ksc.Rules.ToArray()));
    app.MapGet("/api/dictionaries/catalog",()=>Results.Ok(new MonitoringDictionaryCatalog(dictionaryStore.Dictionaries(),CurrentKscRules())));
    app.MapGet("/api/dictionaries/export",(string? product)=>product switch{
      null or "network"=>Results.Ok(dictionaryStore.ExportPackage()),
      "KSC"=>Results.Ok(KscRules.Export(CurrentKscRules().Rules)),
      _=>Results.BadRequest()
    });
    DictionaryChanges KscDictionary(DictionaryImport request,bool commit){
      var rules=KscRules.Parse(request.Json);
      return dictionaryStore.State(net=>{
        var existing=net.Ksc.Rules;
        var revision=AgentWire.Hash(JsonSerializer.Serialize(existing));
        if(commit&&revision!=request.ExpectedRevision)throw new InvalidOperationException("Правила изменились — повторите просмотр.");
        var next=KscRules.Merge(existing,rules);
        var changes=rules.Select(r=>(existing.FirstOrDefault(x=>x.Id==r.Id) is {} prior?prior==r?"= ":"Δ ":"+ ")+r.Title+" ["+r.Id+"] · "+r.OpenEvent+" → "+r.RecoveryEvent+" · "+(r.Enabled?"включено":"отключено")+" · Telegram: "+(r.Notify?"да":"нет")+" · "+r.Level).ToArray();
        if(commit)net.Ksc.Rules=next.ToList();
        return new DictionaryChanges(revision,changes,changes.Count(d=>!d.StartsWith("= ")));
      },commit);
    }
    DictionaryChanges ChangeDictionary(DictionaryImport request,bool commit)
      =>MonitoringDictionaryFormat.IsKaspersky(request.Json)?KscDictionary(request,commit):dictionaryStore.ApplyPackage(request,commit);
    app.MapGet("/api/ksc/status",(KscMonitor worker)=>Results.Ok(worker.View()));
    app.MapPost("/api/ksc/activate",(KscActivation request,KscMonitor worker)=>Results.Ok(worker.Activate(request)));
    app.MapGet("/api/ksc/connection",(KscMonitor worker)=>Results.Ok(worker.Connection()));
    app.MapPost("/api/ksc/enable",async(KscConnection target,KscMonitor worker,CancellationToken t)=>Results.Ok(await worker.Enable(target,t)));
    app.MapPost("/api/ksc/telegram",(KscTelegramOptions options,KscMonitor worker)=>Results.Ok(worker.SaveTelegram(options)));
    app.MapPost("/api/ksc/probe",async(KscConnection target,KscMonitor worker,CancellationToken token)=>Results.Ok(await worker.Probe(target,token)));
    app.MapPost("/api/dictionaries/preview",(DictionaryImport request)=>Results.Ok(ChangeDictionary(request,false)));
    app.MapPost("/api/dictionaries/import",(DictionaryImport request,HttpContext c)=>{
      var result=ChangeDictionary(request,true);
      if(result.Changed>0){organizationVault.Audit(Actor(c),"monitoring-dictionaries.import");state.Touch();}
      return Results.Ok(result);
    });
    app.MapGet("/api/me", (HttpContext c) => Actor(c));
    app.MapGet("/api/users", () => users.List());
    app.MapGet("/api/audit", () => organizationVault.AuditRows());
    app.MapPost("/api/users/create", (UserCreate request, HttpContext c) => { organizationVault.Audit(Actor(c),"user.create.request"); var result=users.Create(request); state.Touch(); return result; });
    app.MapPost("/api/users/update", (UserUpdate request, HttpContext c) => { organizationVault.Audit(Actor(c),"user.update.request",request.Id); var result=users.Update(request); state.Touch(); return result; });
    var workspaces=new WorkspaceStore(directory);
    app.MapGet("/api/personal/workspace",(HttpContext c)=>Results.Ok(workspaces.Load(Actor(c))));
    app.MapPost("/api/personal/workspace",(WorkspacePage layout,HttpContext c)=>Results.Ok(workspaces.Save(Actor(c),layout)));
    app.MapPost("/api/personal/password", (PasswordChange request, HttpContext c) => { organizationVault.Audit(Actor(c),"password.change.request",Actor(c).Id); users.ChangePassword(Actor(c),request); return true; }).RequireRateLimiting("login");
    app.MapPost("/api/personal/vault/unlock", (RemoteLogin request, HttpContext c, ServerSessions sessions) => { var ok=sessions.UnlockVault(c,request.Password); organizationVault.Audit(Actor(c),ok ? "vault.unlock" : "vault.unlock.denied"); return ok; }).RequireRateLimiting("login");
    app.MapPost("/api/personal/vault/lock", (HttpContext c, ServerSessions sessions) => { sessions.LockVault(c); return true; });
    app.MapGet("/api/personal/inventory/status",(HttpContext c,ServerSessions sessions)=>sessions.InventoryUnlocked(c));
    app.MapPost("/api/personal/inventory/unlock",(RemoteLogin request,HttpContext c,ServerSessions sessions)=>{var ok=sessions.UnlockInventory(c,request.Password);organizationVault.Audit(Actor(c),ok?"inventory.unlock":"inventory.unlock.denied");return ok;}).RequireRateLimiting("login");
    app.MapPost("/api/personal/inventory/lock",(HttpContext c,ServerSessions sessions)=>{sessions.LockInventory(c);return true;});
    app.MapGet("/api/personal/vault", async (HttpContext c, CancellationToken token) => {
        await state.Gate.WaitAsync(token); try { return await organizationVault.List(Actor(c),token); } finally { state.Gate.Release(); }
    });
    app.MapPost("/api/personal/vault/read", async (VaultTarget request, HttpContext c, CancellationToken token) => {
        await state.Gate.WaitAsync(token); try { return await organizationVault.Read(Actor(c),request,token); } finally { state.Gate.Release(); }
    });
    app.MapPost("/api/personal/vault/save", async (VaultSave request, HttpContext c, CancellationToken token) => {
        await state.Gate.WaitAsync(token); try { return await organizationVault.Save(Actor(c),request,token); } finally { state.Gate.Release(); }
    });
    app.MapPost("/api/personal/vault/delete", async (VaultTarget request, HttpContext c, CancellationToken token) => {
        await state.Gate.WaitAsync(token); try { return await organizationVault.Delete(Actor(c),request,token); } finally { state.Gate.Release(); }
    });
    app.MapGet("/api/excel/{kind}/template",(string kind)=>new ExcelFile(ExcelExchange.Template(kind),"SAA-template-"+kind+".xlsx"));
    app.MapGet("/api/excel/{kind}/export",async(string kind,HttpContext c,CancellationToken token)=>{
        await state.Gate.WaitAsync(token);try{return await organizationVault.ExportExcel(Actor(c),kind,token);}finally{state.Gate.Release();}
    });
    app.MapPost("/api/excel/{kind}/preview",async(string kind,ExcelRequest request,HttpContext c,CancellationToken token)=>{
        await state.Gate.WaitAsync(token);try{return await organizationVault.ImportExcel(Actor(c),kind,request,false,token);}finally{state.Gate.Release();}
    });
    app.MapPost("/api/excel/{kind}/import",async(string kind,ExcelRequest request,HttpContext c,CancellationToken token)=>{
        await state.Gate.WaitAsync(token);try{return await organizationVault.ImportExcel(Actor(c),kind,request,true,token);}finally{state.Gate.Release();}
    });
    app.MapGet("/api/device-update-status",async(CancellationToken token)=>{await using var db=state.Factory.Create();var rows=await db.Devices.AsNoTracking().Where(d=>d.Type!="MikroTik"&&d.Type!="Коммутатор").Select(d=>new DeviceUpdateStamp(d.Id,d.OsUpdatesJson,d.MailUpdatesJson,null)).ToArrayAsync(token);var versions=state.Agents.ReportedVersions();return rows.Select(row=>row with{AgentVersion=versions.GetValueOrDefault(row.DeviceId,"")}).ToArray();});
    InvoiceApi.Map(app,state);
    await app.RunAsync();
}
catch (Exception e) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
