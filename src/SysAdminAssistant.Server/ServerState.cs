using System.Collections;
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;

public sealed class ServerState
{
    public CertificateLifecycle Certificates {get;set;}=null!;public string ServerUrl {get;set;}="";
    public AgentEventStore AgentEvents {get;}
    public AgentEventStore LinuxAgentEvents {get;}
    public AgentEventStore EventStore(int id)=>Agents.Get(id)?.Report?.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)==true?LinuxAgentEvents:AgentEvents;
    public AgentStore Agents {get;}
    public WorkstationStore Workstations {get;}
    public NetworkMonitor Routers {get;private set;}=null!;
    public readonly SemaphoreSlim Gate = new(1);
    public readonly string DirectoryPath;
    public AppSettings Settings { get; private set; }
    private long revision = DateTime.UtcNow.Ticks;
    public long Revision => Interlocked.Read(ref revision);
    public MailboxPollInfo[] MailChecks {get;set;}=[];
    public ServerHeartbeatStatus HeartbeatStatus {get;set;}=new(false,null,null,"Внешний контроль не настроен");
    public string TelegramError { get; set; } = "";
    public string[] MonitorErrors { get; set; } = [];
    public DateTime? LastMonitorUtc { get; set; }
    public readonly SemaphoreSlim Wake = new(0, 1);
    public readonly SemaphoreSlim RouterWake = new(0,1);
    public readonly SemaphoreSlim AvailabilityWake = new(0,1);
    public ServerState(string directory,string agentPin="")
    {
        DirectoryPath = directory; Workstations=new(directory); AgentEvents=new(directory); LinuxAgentEvents=new(directory,true); Agents=new(directory,agentPin); Routers=new(new NetworkStore(directory)); Settings = SettingsFile.Load(Path.Combine(directory, "settings.json")); Settings.ExternalWorker = false;AppTime.Configure(Settings.TimeZoneId);
    }
    public DatabaseFactory Factory => new(Settings);
    public void Touch() => Interlocked.Increment(ref revision);
    public void RequestPoll() { foreach(var signal in new[]{Wake,RouterWake,AvailabilityWake})if(signal.CurrentCount==0)try{signal.Release();}catch(SemaphoreFullException){} }
    public async Task InitializeAsync(CancellationToken token)
    {
        // Keep schema 7 installations current without importing legacy databases.
        await using var db = Factory.Create();
        if (db.Database.IsSqlServer() && !await db.Database.CanConnectAsync(token)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("SQL-база недоступна. Проверьте адрес SQL Server, учётные данные и созданную установщиком базу"));
        await db.Database.EnsureCreatedAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sp_getapplock @Resource=N'SysAdminAssistant.Schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @r < 0 THROW 51000, 'Schema upgrade lock failed', 1;", token);
        var schema = await db.Schema.SingleOrDefaultAsync(token);
        if (schema != null && schema.Version != 7) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нужна чистая база схемы 7; миграция сервером не выполняется"));
        if (schema == null) db.Schema.Add(new SchemaInfo { Version = 7 });
        await InventorySchema.EnsureMonitoringLinkAsync(db, token);
        await InventorySchema.EnsureModelFieldsAsync(db, token);
        await InvoiceSchema.EnsureAsync(db,token);
        await MailArchiveSchema.EnsureAsync(db,token);
        await BindingSchema.EnsureAsync(db,token);
        await SourceSchema.UpgradeAsync(db,token);
        await PaymentHistory.EnsureAsync(db,token);
        await TimeStorage.EnsureAsync(db,TimeZoneInfo.Local,token);
        var existing = await db.Profiles.Select(x => x.Provider).ToArrayAsync(token);
        foreach (var provider in ReportAnalyzer.Providers.Where(x => !existing.Contains(x))) db.Profiles.Add(KeywordDefaults.For(provider));
        await db.SaveChangesAsync(token); await MailUpdateStore.SeedAsync(db,token); await tx.CommitAsync(token);
        var routerHosts=await db.Devices.AsNoTracking().Where(d=>d.Type=="MikroTik").ToDictionaryAsync(d=>d.Id,d=>d.Host,token);
        Routers.Store.BindLegacyHosts(routerHosts);
    }
    public async Task<RemoteSnapshot> SnapshotAsync(CancellationToken token, UserView? actor = null,bool inventoryUnlocked=false)
    {
        if(inventoryUnlocked)await InventoryHardwareSync.SyncAsync(this,token);
        var capturedRevision = Revision;
        await using var db = Factory.Create(); db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var rows = new Dictionary<string, JsonElement[]>();
        foreach (var (resource, type) in RemoteData.Types)
        {
            if(!inventoryUnlocked && AccessRules.ResourceModule(resource)=="inventory"){rows[resource]=[];continue;}
            if (resource is "credentials" or "vault" || actor != null && !actor.Administrator && resource is not ("reports" or "pending" or "incidents" or "devices") && AccessRules.Level(actor,AccessRules.ResourceModule(resource))<1) { rows[resource] = []; continue; }
            var property = typeof(AssistantDb).GetProperties().Single(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>) && p.PropertyType.GenericTypeArguments[0] == type);
            var values = ((IEnumerable)property.GetValue(db)!).Cast<Entity>();
            if(actor!=null && !actor.Administrator && resource=="devices")values=values.Cast<Device>().Where(d=>AccessRules.Level(actor,NetworkDevices.Module(d.Type))>=1);
            if(actor!=null && !actor.Administrator && resource=="reports") values=values.Cast<ReportEvent>().Where(x=>AccessRules.Level(actor,x.Provider=="DMARC"?"dmarc":"reports")>=1);
            if(resource=="pending") {
                var allowDmarc=actor==null||AccessRules.Level(actor,"dmarc")>=1;
                var allowReports=actor==null||AccessRules.Level(actor,"reports")>=1;
                values=await (from pending in db.Pending
                    join report in db.Reports on pending.EventId equals report.Id
                    where report.Provider=="DMARC"?allowDmarc:allowReports
                    select new PendingReport{Id=pending.Id,EventId=pending.EventId,Subject=pending.Subject,Sender=pending.Sender,MessageDate=pending.MessageDate})
                    .ToArrayAsync(token);
            }
            if(actor!=null && !actor.Administrator && resource=="incidents") {
                var dmarcIds=await db.Reports.Where(x=>x.Provider=="DMARC").Select(x=>x.Id).ToArrayAsync(token);
                values=values.Cast<Incident>().Where(x=>x.ReportId is int reportId?AccessRules.Level(actor,dmarcIds.Contains(reportId)?"dmarc":"reports")>=1:AccessRules.Level(actor,"reports")>=1);
            }
            rows[resource] = values.Select(x => JsonSerializer.SerializeToElement(x, type, RemoteData.Json)).ToArray();
        }
        await tx.CommitAsync(token);
        var errors = actor==null || actor.Administrator ? MonitorErrors : MailChecks.Where(x=>AccessRules.Level(actor,"settings")>=1 || AccessRules.Level(actor,x.Purpose==MailboxPurpose.Dmarc?"dmarc":"reports")>=1).Where(x=>x.Error.Length>0).Select(x=>x.Name+": "+x.Error).ToArray();
        if(Certificates!=null&&Certificates.Error.Length>0)errors=errors.Append(Certificates.Error).ToArray();
        if(TelegramError.Length>0 && (actor==null || AccessRules.Level(actor,"settings")>=1))errors=errors.Append(TelegramError).ToArray();
        Dictionary<int,string>? agentVersions=null;
        if(actor==null||AccessRules.Level(actor,"devices")>=1){
            var visibleIds=rows["devices"].Select(row=>row.GetProperty(nameof(Device.Id)).GetInt32()).ToHashSet();
            agentVersions=Agents.ReportedVersions().Where(pair=>visibleIds.Contains(pair.Key)).ToDictionary();
        }
        return new(capturedRevision, AppTime.Now, PublicFor(actor), rows, errors, RemoteData.Hash(Settings),null,agentVersions);
    }
    private AppSettings PublicFor(UserView? actor)
    {
        if (actor == null || AccessRules.Level(actor,"settings")>=1) return RemoteData.PublicSettings(Settings);
        var visible=new AppSettings {TimeZoneId=Settings.TimeZoneId,ExternalWorker=true,MonitoringEnabled=Settings.MonitoringEnabled,PollSeconds=Settings.PollSeconds,NotificationLanguage=Settings.NotificationLanguage,ProductUpdates=new ProductUpdateSources{Primary=Settings.ProductUpdates.Primary,Mirror=Settings.ProductUpdates.Mirror}};
        if(AccessRules.Level(actor,"payments")>=1)visible.Company=new CompanySettings {WeekendMask=Settings.Company.WeekendMask,PaymentSoonDays=Settings.Company.PaymentSoonDays};
        if(AccessRules.Level(actor,"routers")>=1)visible.Router=new RouterSettings {Host=Settings.Router.Host,Port=Settings.Router.Port};
        return RemoteData.PublicSettings(visible);
    }
    public RemoteSettingsResult SaveSettings(RemoteSettingsChange request)
    {
        if (request.ExpectedHash != RemoteData.Hash(Settings)) throw new ConflictException(global::SysAdminAssistant.Core.ProductText.Get("Настройки изменены другим клиентом. Обновите данные."));
        var next = request.Value;
        _=AppTime.Resolve(next.TimeZoneId);
        if(next.NotificationLanguage is not ("ru" or "en"))throw new ArgumentException("Unsupported notification language.");
        if(next.ProductUpdates==null)throw new ArgumentException("Update sources missing.");
        next.ProductUpdates.Mirror??="";_=ProductReleases.Https(next.ProductUpdates.Primary);if(!string.IsNullOrEmpty(next.ProductUpdates.Mirror))_=ProductReleases.Https(next.ProductUpdates.Mirror);
        if (next.Mailboxes == null || next.Mailboxes.Count > 100 || next.CustomDiagnostics == null || next.CustomDiagnostics.Count > 1000 || next.ServerHeartbeat==null || next.Telegram == null || next.Smtp == null || next.Router == null || next.Company == null || next.Inventory == null) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректные настройки"));
        if(next.Company.PaymentSoonDays is <1 or >90)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Порог близкого платежа: 1–90 дней"));
        DailySummarySettings.Validate(next.DailySummary); PaymentCalendar.Validate(next.Company.WeekendMask); EventRetention.ValidateDays(next.EventRetentionDays);
        ConnectivityProbes.Validate(next);
        if (next.PollSeconds is < 15 or > 3600) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Период мониторинга: 15–3600 секунд"));
        if (next.Mailboxes.Select(x => x.Id).Distinct().Count() != next.Mailboxes.Count) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Повторяются идентификаторы ящиков"));
        string Secret(string key, string old) => request.Secrets.TryGetValue(key, out var value) ? LocalSecret.Protect(value.Length <= 10000 ? value : throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Слишком длинный секрет"))) : old;
        foreach (var m in next.Mailboxes)
        {
            if (!Enum.IsDefined(m.Purpose) || m.Id.Length is < 1 or > 100 || m.Port is < 1 or > 65535 || m.AllowedDomains == null || m.TrustedDmarcSenders == null) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректный ящик IMAP"));
            if (m.Enabled && (string.IsNullOrWhiteSpace(m.Host) || string.IsNullOrWhiteSpace(m.User) || string.IsNullOrWhiteSpace(m.Folder))) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Заполните сервер, логин и папку IMAP"));
            if (m.AllowedDomains.Count > 1000 || m.TrustedDmarcSenders.Count > 1000) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Слишком много отправителей"));
            m.AllowedDomains = m.AllowedDomains.Select(MailAdmission.NormalizeDomain).Distinct().ToList();
            m.TrustedDmarcSenders = m.TrustedDmarcSenders.Select(MailAdmission.NormalizeAddress).Distinct().ToList();
            if (m.Enabled) MailAuthentication.Validate(m);
            var mailKey="mail/"+m.Id;
            m.ProtectedPassword = request.Secrets.ContainsKey(mailKey)?Secret(mailKey,""):StoredCredential.MailPassword(m,Settings.Mailboxes.SingleOrDefault(x=>x.Id==m.Id));
            if (m.Enabled && m.ProtectedPassword.Length == 0) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите пароль включённого IMAP"));
        }
        if (next.Smtp.Port is < 1 or > 65535 || next.Router.Port is < 1 or > 65535) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректный порт SMTP/RouterOS"));
        next.Router.CertificateSha256=RouterOsClient.NormalizePin(next.Router.CertificateSha256);
        foreach (var article in next.CustomDiagnostics) { var error = article.Validate(); if (error != null) throw new ArgumentException(error); }
        var smtp=Settings.Smtp;var router=Settings.Router;
        next.Smtp.ProtectedPassword=request.Secrets.ContainsKey("smtp")?Secret("smtp",""):StoredCredential.Reuse(smtp.ProtectedPassword,StoredCredential.SameEndpoint(next.Smtp.Host,next.Smtp.Port,next.Smtp.User,smtp.Host,smtp.Port,smtp.User));
        next.Router.ProtectedPassword=request.Secrets.ContainsKey("router")?Secret("router",""):StoredCredential.Reuse(router.ProtectedPassword,StoredCredential.SameEndpoint(next.Router.Host,next.Router.Port,next.Router.User,router.Host,router.Port,router.User)&&next.Router.CertificateSha256==RouterOsClient.NormalizePin(router.CertificateSha256));
        next.Telegram.ProtectedToken = Secret("telegram", Settings.Telegram.ProtectedToken);
        next.Telegram.Validate();
        next.ServerHeartbeat.ProtectedSecret=Secret("server-heartbeat",Settings.ServerHeartbeat.ProtectedSecret);
        if(next.ServerHeartbeat.Enabled){ServerHeartbeatSettings.ValidateUrl(next.ServerHeartbeat.EndpointUrl);ServerHeartbeatSettings.ValidateIdentity(next.ServerHeartbeat.ServerId);ServerHeartbeatSettings.ValidateSecret(LocalSecret.Unprotect(next.ServerHeartbeat.ProtectedSecret));}
        // Storage and execution location cannot be changed by a client, including forged fields.
        next.DatabaseProvider = Settings.DatabaseProvider; next.SqlitePath = Settings.SqlitePath; next.SqlConnectionString = Settings.SqlConnectionString; next.ProtectedSqlPassword = Settings.ProtectedSqlPassword; next.ExternalWorker = false; next.Mail = new();
        SettingsFile.Save(next, Path.Combine(DirectoryPath, "settings.json"));
        if(next.ServerHeartbeat.EndpointUrl!=Settings.ServerHeartbeat.EndpointUrl||next.ServerHeartbeat.ServerId!=Settings.ServerHeartbeat.ServerId||next.ServerHeartbeat.Enabled!=Settings.ServerHeartbeat.Enabled||next.ServerHeartbeat.ProtectedSecret!=Settings.ServerHeartbeat.ProtectedSecret)HeartbeatStatus=new(next.ServerHeartbeat.Enabled,null,null,"Настройка изменена; ожидается следующая проверка (до минуты). Telegram проверьте командой test в PHP-наблюдателе.");
        Settings = next;AppTime.Configure(next.TimeZoneId); Touch(); RequestPoll();
        return new(RemoteData.PublicSettings(next), RemoteData.Hash(next));
    }
}
public sealed class ConflictException(string message) : Exception(message);

public sealed class ServerMonitor(ServerState state, ILogger<ServerMonitor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(state.Settings, RemoteData.Json), RemoteData.Json)!;
                using var timeScope=AppTime.Scope(settings.TimeZoneId);
                var factory = new DatabaseFactory(settings); var monitor = new MonitoringService(factory, settings);
                using var mailBudget=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);mailBudget.CancelAfter(TimeSpan.FromMinutes(2));
                monitor.MailProgress += rows => {state.MailChecks=rows;state.Touch();};
                async Task PollReports()
                {
                    try { await monitor.PollMailAsync(mailBudget.Token); }
                    finally { state.MailChecks=monitor.MailChecks.Select(x=>x.Copy()).ToArray();state.MonitorErrors=monitor.MailErrors;state.Touch(); }
                }
                try {await PollReports();}
                finally {if(!stoppingToken.IsCancellationRequested)await ApplicationNotifications.CaptureAsync(factory,state.Routers.Store,settings,AppTime.Now,stoppingToken,monitor.MailErrors);}
                await EventRetention.ApplyAsync(factory, settings.EventRetentionDays, AppTime.Now, stoppingToken);
                state.MonitorErrors = monitor.MailErrors; state.LastMonitorUtc = DateTime.UtcNow; state.Touch();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { state.MonitorErrors = ["Мониторинг: " + e.Message]; state.Touch(); log.LogWarning("Ошибка фоновой проверки: {Kind}", e.GetType().Name); }
            try { await state.Wake.WaitAsync(TimeSpan.FromSeconds(Math.Clamp(state.Settings.PollSeconds, 15, 3600)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
