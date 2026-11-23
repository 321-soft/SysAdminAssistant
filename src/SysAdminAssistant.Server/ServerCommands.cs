using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;
internal sealed record RemoteIdRevision(int Id,int Revision);
public static class ServerCommands
{
    public static async Task<object> ExecuteAsync(ServerState state, string command, JsonElement payload, CancellationToken token, UserView? actor=null)
    {
        T Read<T>() where T : class => payload.Deserialize<T>(RemoteData.Json) ?? throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пустой запрос"));
        var factory = state.Factory;
        switch (command)
        {
            case "daily-summary-preview":
                if(actor==null||!actor.Administrator)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Полная сводка доступна администратору"));
                await DailySummaryCollector.ObserveAsync(state,token);var digest=new DailySummaryStore(state.DirectoryPath);return new {Text=digest.Preview(state.Settings.DailySummary,state.Settings.TimeZoneId,DateTime.UtcNow),Status=digest.Status()};
            case "workstations-list": return StationUsbNames.Annotate(state.Workstations.List(false,DateTime.UtcNow));
            case "workstations-denied": return state.Workstations.List(true,DateTime.UtcNow);
            case "workstations-config": return state.Workstations.Config();
            case "workstations-enrollment": return state.Workstations.IssueEnrollment();
            case "workstations-autoupdate":
                if(actor is not { Administrator:true })throw new UnauthorizedAccessException("Нужны права администратора.");
                return state.Workstations.SetAutoUpdate(Read<StationUpdatePolicy>());
            case "workstations-save": return state.Workstations.Save(Read<StationConfig>());
            case "workstations-approve": case "workstations-deny": case "workstations-delete":
                return state.Workstations.Decide(payload.Deserialize<Guid>(RemoteData.Json),command["workstations-".Length..]);
            case "agent-trust-stage":
            case "agent-trust-export":
            case "agent-trust-reset":
                if(actor==null||!actor.Administrator)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Доверие HTTPS изменяет только главный администратор"));
                if(command=="agent-trust-stage")return state.Agents.StageTrust(payload.Deserialize<AgentTrustStage>(AgentWire.Json)!);
                return command=="agent-trust-reset"?state.Agents.ResetTrust():state.Agents.Trust();
            case "agent-journal-history":
                var journalDevice=payload.GetProperty("DeviceId").GetInt32();if(actor!=null)AccessRules.Require(actor,"devices");await using(var journalDb=factory.Create())if(!await journalDb.Devices.AnyAsync(d=>d.Id==journalDevice,token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Устройство отсутствует"));return state.EventStore(journalDevice).History(journalDevice,payload.TryGetProperty("BeforeUtc",out var journalBefore)&&journalBefore.ValueKind==JsonValueKind.String?journalBefore.GetDateTime():null,payload.TryGetProperty("BeforeKey",out var journalKey)&&journalKey.ValueKind==JsonValueKind.String?journalKey.GetString():null,token);
            case "agent-metric-history":
                var historyDevice=payload.GetProperty("DeviceId").GetInt32();if(actor!=null)AccessRules.Require(actor,"devices");await using(var historyDb=factory.Create())if(!await historyDb.Devices.AnyAsync(d=>d.Id==historyDevice,token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Устройство отсутствует"));return AgentMetricHistory.Read(state.DirectoryPath,historyDevice,payload.TryGetProperty("Before",out var before)&&before.ValueKind==JsonValueKind.Number?before.GetInt64():null);
            case "agent-events":
            case "agent-events-save":
            case "agent-event-resolve":
                var eventDevice=payload.GetProperty("DeviceId").GetInt32();
                if(actor!=null)AccessRules.Require(actor,"devices",command=="agent-events"?1:2);
                await using(var eventDb=factory.Create())if(!await eventDb.Devices.AnyAsync(d=>d.Id==eventDevice&&d.Type!="MikroTik"&&d.Type!="Коммутатор",token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Устройство отсутствует"));
                var eventStore=state.EventStore(eventDevice);
                if(command=="agent-events")return eventStore.View(eventDevice);
                if(command=="agent-events-save"){var save=Read<AgentEventsSave>();bool linux=ReferenceEquals(eventStore,state.LinuxAgentEvents);if(save.Policy.Rules.Any(r=>linux?r.Channel!="Linux":r.Channel=="Linux"))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Правило не соответствует системе агента"));return eventStore.Save(save);}
                var resolveEvent=Read<AgentEventResolve>();eventStore.Resolve(eventDevice,resolveEvent.Id);
                state.Routers.Store.State(s=>{foreach(var n in s.Notifications.Where(n=>n.EntityKey=="journal:"+eventDevice+":"+resolveEvent.Id&&n.HandledAt==null)){n.HandledAt=DateTime.UtcNow;n.Result="Решено";}return true;},true);
                await AgentMonitoring.RefreshHealth(state,eventDevice,token);return true;
            case "agent-linux-package":
            case "agent-recover":
            case "agent-view":
            case "agent-approve":
            case "agent-revoke":
            case "agent-policy":
            case "agent-package":
                var agentDevice=payload.GetProperty("DeviceId").GetInt32();
                await using(var agentDb=factory.Create()){
                    var device=await agentDb.Devices.AsNoTracking().SingleAsync(d=>d.Id==agentDevice,token);
                    if(NetworkDevices.IsNetwork(device.Type))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Windows-агент нельзя привязать к сетевому устройству"));
                    if(actor!=null)AccessRules.Require(actor,"devices",command=="agent-view"?1:2);
                    if(command=="agent-view")return new AgentViewResult(state.Agents.Get(agentDevice));
                    if(command=="agent-linux-package")return LinuxInstallBundles.Create(state,agentDevice,device.Name);
                    if(command=="agent-recover"){state.Agents.AllowRecovery(agentDevice);return true;}
                    if(command=="agent-package")return state.Agents.Package(payload.Deserialize<AgentPackageRequest>(AgentWire.Json)!,device.Name);
                    if(command=="agent-policy")return state.Agents.Save(payload.Deserialize<AgentPolicySave>(AgentWire.Json)!);
                    if(command=="agent-approve"){state.Agents.Approve(agentDevice);await agentDb.Devices.Where(d=>d.Id==agentDevice&&d.AgentLastSeen==null).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.AgentHealth,"MISSING").SetProperty(d=>d.AgentSummary,"Ожидание первого отчёта агента"),token);}
                    else {state.Agents.Revoke(agentDevice);AgentMonitoring.Cancel(state,agentDevice);await agentDb.Devices.Where(d=>d.Id==agentDevice).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.AgentHealth,"").SetProperty(d=>d.AgentLastSeen,(DateTime?)null).SetProperty(d=>d.AgentSummary,""),token);}
                    return true;
                }
            case "router-targets":
                await using(var db=factory.Create())return await db.Devices.AsNoTracking().Where(x=>x.Enabled && x.Type=="MikroTik").OrderBy(x=>x.Name).Select(x=>new RouterTarget(x.Id,x.Name,x.Host)).ToArrayAsync(token);
            case "mail-bind-preview":
            case "mail-bind":
                var binding=Read<MailBindRequest>();
                if(actor!=null){MailBinding.RequireAccess(actor,binding);if(binding.TargetKind==TargetKind.Device){await using var bindingDb=factory.Create();var device=await bindingDb.Devices.AsNoTracking().SingleAsync(d=>d.Id==binding.TargetId,token);AccessRules.Require(actor,NetworkDevices.Module(device.Type));}}
                if(command=="mail-bind-preview")return await MailBinding.PreviewAsync(factory,binding,token);
                var bindingResult=await MailBinding.ApplyAsync(factory,binding,token,state.Settings.CustomDiagnostics);state.Touch();return bindingResult;
            case "device-updates":
            case "device-update-installed":
                var updateDevice=payload.GetProperty("DeviceId").GetInt32();
                await using(var updateDb=factory.Create()){
                    var updateTarget=await updateDb.Devices.AsNoTracking().SingleAsync(d=>d.Id==updateDevice,token);
                    if(actor!=null)AccessRules.Require(actor,NetworkDevices.Module(updateTarget.Type),command=="device-updates"?1:2);
                    if(command=="device-update-installed"){
                        var confirm=Read<MailUpdateConfirm>();await MailUpdateStore.ConfirmAsync(updateDb,confirm,actor?.Login??"administrator",token);
                        state.Routers.Store.State(s=>{foreach(var n in s.Notifications.Where(n=>n.EntityKey=="mail-updates:"+updateDevice+":"+confirm.Key&&n.HandledAt==null)){n.HandledAt=DateTime.UtcNow;n.Result="Отмечено установленным";}return true;},true);
                    }
                    return await MailUpdateStore.ViewAsync(updateDb,updateDevice,token);
                }
            case "device-problems":
                var problemDeviceId=payload.GetProperty("DeviceId").GetInt32();if(actor!=null)AccessRules.Require(actor,"devices");await using(var problemDb=factory.Create()){
                    if(!await problemDb.Devices.AnyAsync(d=>d.Id==problemDeviceId&&d.Type!="MikroTik"&&d.Type!="Коммутатор",token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Устройство не найдено"));
                    var mailProblems=actor==null||AccessRules.Level(actor,"reports")>=1?await DeviceProblems.OpenAsync(problemDb,problemDeviceId,token):Array.Empty<DeviceProblem>();
                    var problemAgent=state.Agents.Get(problemDeviceId);return UnifiedDeviceProblems.Merge(mailProblems,problemAgent,problemAgent==null?null:state.EventStore(problemDeviceId).View(problemDeviceId),problemAgent==null?[]:state.EventStore(problemDeviceId).Findings(problemDeviceId),DateTime.UtcNow).OrderByDescending(p=>p.Severity).ToArray();
                }
            case "device-resolve":
                if(actor!=null){AccessRules.Require(actor,"reports",2);AccessRules.Require(actor,"devices");}
                await DeviceProblems.ResolveAsync(factory,Read<ResolveDeviceProblem>(),token);state.Touch();return true;
            case "device-reanalyze":
                if(actor!=null){AccessRules.Require(actor,"reports",2);AccessRules.Require(actor,"devices",2);}
                var deviceReanalyzed=await MailBinding.ReanalyzeDeviceAsync(factory,payload.GetProperty("DeviceId").GetInt32(),token,state.Settings.CustomDiagnostics);state.Touch();return deviceReanalyzed;
            case "job-reanalyze":
                if(actor!=null){AccessRules.Require(actor,"reports",2);AccessRules.Require(actor,"jobs",2);}
                var reanalyzed=await MailBinding.ReanalyzeJobAsync(factory,payload.GetProperty("JobId").GetInt32(),token,state.Settings.CustomDiagnostics);state.Touch();return reanalyzed;
            case "mail-view":
                var view=Read<MailViewRequest>();
                await using(var db=factory.Create()) {
                    var report=await db.Reports.AsNoTracking().SingleAsync(x=>x.Id==view.Id,token);
                    if(actor!=null)AccessRules.Require(actor,report.Provider=="DMARC"?"dmarc":"reports");
                }
                return await MailArchive.ReadAsync(factory,state.Settings,view.Id,token);
            case "mail-receipt-repair":
                var requested=Read<MailReceiptRepair>();
                var repairMailbox=state.Settings.Mailboxes.Single(x=>x.Id==requested.MailboxId);
                try {return await MailArchive.RepairAsync(factory,repairMailbox,token);} finally {state.Touch();}
            case "mail-poll": state.RequestPoll(); return true;
            case "mail-status": return state.MailChecks.Where(x=>actor==null || AccessRules.Level(actor,"settings")>=1 || AccessRules.Level(actor,x.Purpose==MailboxPurpose.Dmarc?"dmarc":"reports")>=1).ToArray();
            case "mail-test":
                var test=Read<MailConnectionTest>();var m=test.Mailbox;
                if(m.Host.Length>1000||m.User.Length>1000||m.Folder.Length>1000||test.Password.Length>10000)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Слишком длинные параметры IMAP"));
                m.ProtectedPassword=test.Password.Length>0?LocalSecret.Protect(test.Password):StoredCredential.MailPassword(m,state.Settings.Mailboxes.SingleOrDefault(x=>x.Id==m.Id));
                if(m.ProtectedPassword.Length==0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите пароль для проверки"));
                return await MailDiagnostics.TestAsync(m,token);
            case "hardware-history":
            {
                var query=Read<HardwareHistoryQuery>();
                await HardwareHistoryWorker.CaptureAsync(state,token);
                if(query.Kind=="station"){
                    if(actor!=null)AccessRules.Require(actor,"workstations");
                    if(!Guid.TryParse(query.Id,out var historyStationId)||!state.Workstations.List(false,DateTime.UtcNow).Any(s=>s.Id==historyStationId))throw new ArgumentException("Рабочая станция не найдена");
                    return new HardwareHistoryStore(state.DirectoryPath).History(HardwareHistoryStore.Station(historyStationId)).Reverse().ToArray();
                }
                if(query.Kind!="device"||!int.TryParse(query.Id,out var deviceId))throw new ArgumentException("Неизвестный объект истории");
                await using var db=factory.Create();var device=await db.Devices.AsNoTracking().SingleAsync(d=>d.Id==deviceId,token);
                if(actor!=null)AccessRules.Require(actor,NetworkDevices.Module(device.Type));
                return new HardwareHistoryStore(state.DirectoryPath).History(HardwareHistoryStore.Device(deviceId)).Reverse().ToArray();
            }
            case "inventory-hardware-history":
            {
                var query=Read<RemoteIdRevision>();
                await using var db=factory.Create();
                if(!await db.Assets.AnyAsync(a=>a.Id==query.Id,token))throw new ArgumentException("Имущество не найдено");
                await HardwareHistoryWorker.CaptureAsync(state,token);
                return new HardwareHistoryStore(state.DirectoryPath).AssetHistory(query.Id);
            }
            case "inventory-photo-read": return InventoryPhotos.Read(state.DirectoryPath,Read<InventoryModelKey>());
            case "inventory-photo-write": return InventoryPhotos.Save(state.DirectoryPath,Read<InventoryPhotoWrite>());
            case "inventory-act": return await new InventoryService(factory).ConductAsync(Read<InventoryRequest>(), token);
            case "inventory-unlink":
                var unlink=Read<RemoteIdRevision>();
                await using(var linkDb=factory.Create()){
                    var oldLink=await linkDb.Assets.AsNoTracking().SingleAsync(a=>a.Id==unlink.Id,token);
                    var history=new HardwareHistoryStore(state.DirectoryPath);
                    history.Link(oldLink.Id,oldLink.WorkstationId is Guid station?HardwareHistoryStore.Station(station):oldLink.DeviceId is int device?HardwareHistoryStore.Device(device):null);
                    await new InventoryService(factory).UnlinkAssetAsync(unlink.Id,unlink.Revision,token);
                    history.Link(oldLink.Id,null);
                }
                return true;
            case "inventory-delete":
                var deletion=Read<RemoteIdRevision>();
                await new InventoryService(factory).DeleteAssetAsync(deletion.Id,deletion.Revision,token);return true;
            case "inventory-asset":
            {
                var asset = Read<InventoryAsset>();
                await using var inventoryDb = factory.Create();
                var old = asset.Id == 0 ? null : await inventoryDb.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == asset.Id, token);
                if (old?.DeviceId != asset.DeviceId && actor != null) AccessRules.Require(actor,"devices");
                if (old?.WorkstationId != asset.WorkstationId)
                {
                    if (actor != null) AccessRules.Require(actor,"workstations");
                    if (asset.WorkstationId is Guid station && !state.Workstations.List(false,DateTime.UtcNow).Any(x => x.Id == station && x.Status == "Approved"))
                        throw new ArgumentException(ProductText.Get("Можно связать только подтверждённую рабочую станцию"));
                }
                var history=new HardwareHistoryStore(state.DirectoryPath);
                if(old!=null)history.Link(old.Id,old.WorkstationId is Guid priorStation?HardwareHistoryStore.Station(priorStation):old.DeviceId is int priorDevice?HardwareHistoryStore.Device(priorDevice):null);
                await new InventoryService(factory).SaveAssetAsync(asset, token);
                history.Link(asset.Id,asset.WorkstationId is Guid nextStation?HardwareHistoryStore.Station(nextStation):asset.DeviceId is int nextDevice?HardwareHistoryStore.Device(nextDevice):null);
                if(old!=null)history.Manual(asset.Id,InventoryHistoryDescription(old),InventoryHistoryDescription(asset),actor?.Login??"Пользователь",DateTime.UtcNow);
                return true;
            }
            case "inventory-employee": await new InventoryService(factory).SaveEmployeeAsync(Read<Employee>(), token); return true;
            case "inventory-repair": var repair = Read<RemoteRepair>(); await new InventoryService(factory).SetRepairAsync(repair.Id, repair.Revision, repair.Repair, token); return true;
            case "mail-recheck":
                var recheck=await new ReportStore(factory,state.Settings.CustomDiagnostics).RecheckMailAsync(Read<MailViewRequest>().Id,token);
                if(recheck.Applied)state.Touch();
                return recheck;
            case "mail-reprocess-selected":
                var selection = Read<MailReprocessSelection>();
                if(selection.ReportIds is not {Length:>0 and <=100} || selection.ReportIds.Any(id=>id<=0))throw new ArgumentException("Выберите от 1 до 100 писем.");
                return await new ReportStore(factory,state.Settings.CustomDiagnostics).ReprocessAsync(token,false,true,selection.ReportIds);
            case "reprocess": return await new ReportStore(factory, state.Settings.CustomDiagnostics).ReprocessAsync(token,actor==null||AccessRules.Level(actor,"dmarc")>=2,actor==null||AccessRules.Level(actor,"reports")>=2);
            case "mail":
                var mail = Read<RemoteMail>();
                if (mail.Payloads.Count is < 1 or > 32 || mail.Account.Length > 1000 || mail.Key.Length > 1000 || mail.Payloads.Any(x => x.Body.Length > DmarcParser.MaxXmlBytes + 100 || x.Subject.Length > 10000 || x.Sender.Length > 10000 || x.Account != mail.Account)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный импорт отчёта"));
                if(actor!=null)foreach(var item in mail.Payloads)AccessRules.Require(actor,item.Body.StartsWith(DmarcParser.Marker,StringComparison.Ordinal)||item.Body.StartsWith(DmarcParser.ErrorMarker,StringComparison.Ordinal)?"dmarc":"reports",2);
                return await new ReportStore(factory, state.Settings.CustomDiagnostics).IngestMessageAsync(mail.Payloads, mail.Account, mail.Key, token);
            case "retention-preview": var preview = Read<RemoteRetention>(); return await EventRetention.PreviewAsync(factory, preview.Days, AppTime.Now, token);
            case "retention-apply":
                var retention = Read<RemoteRetention>();
                if (!retention.Confirm || retention.ExpectedHash != RemoteData.Hash(state.Settings)) throw new ConflictException(global::SysAdminAssistant.Core.ProductText.Get("Очистка не подтверждена либо настройки изменены"));
                return await EventRetention.ApplyAsync(factory, retention.Days, AppTime.Now, token);
            case "server-heartbeat-status": return state.HeartbeatStatus;
            case "server-heartbeat-package":
                if(actor==null||!actor.Administrator)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Архив с ключами может получить только главный администратор"));
                var deployment=Read<WatchdogDeployment>();
                var heartbeatKey=state.Settings.ServerHeartbeat.ProtectedSecret.Length>0?LocalSecret.Unprotect(state.Settings.ServerHeartbeat.ProtectedSecret):Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                return new WatchdogPackageResult(WatchdogPackage.Create(deployment,heartbeatKey,LocalSecret.Unprotect(state.Settings.Telegram.ProtectedToken),state.Settings.Telegram.ChatId,state.Settings.NotificationLanguage),heartbeatKey);
            case "telegram-pin-status": return new TelegramProblemPins(state.DirectoryPath).Status();
            case "telegram-test":
                using(SysAdminAssistant.Core.ProductText.Scope(state.Settings.NotificationLanguage))await TelegramSender.SendAsync(state.Settings.Telegram, "SysAdmin Assistant · тест уведомлений\nСвязь с Telegram работает.", token); return true;
            case "sendmail":
                var message = Read<RemoteMessage>();
                if (message.To.Length > 1000 || message.Subject.Length > 1000 || message.Body.Length > 10000) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большое SMTP-сообщение"));
                await new MonitoringService(factory, state.Settings).SendMailAsync(message.To, message.Subject, message.Body, token); return true;
            case "dmarc-senders":
                var id = payload.GetProperty("MailboxId").GetString();
                var mailbox = state.Settings.Mailboxes.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Ящик отсутствует"));
                return await MailAdmission.DiscoverAsync(mailbox, token);
        }
        if(command=="payment-history"){
            await using var paymentDb=factory.Create();var paymentId=payload.GetProperty("PaymentId").GetInt32();
            return await paymentDb.PaymentReceipts.AsNoTracking().Where(r=>r.PaymentId==paymentId).OrderByDescending(r=>r.PaidAt).ThenByDescending(r=>r.Id).ToArrayAsync(token);
        }
        if(command is "payment-pay" or "payment-cancel"){
            await PaymentHistory.ApplyAsync(factory,Read<PaymentOperation>(),actor?.Login??"Администратор",command=="payment-cancel",token);state.RequestPoll();return true;
        }
        if(command.StartsWith("router-",StringComparison.Ordinal)) {
            if(command is "router-audit-view" or "router-audit-refresh" or "router-audit-ignore" or "router-audit-policy"){
                var deviceId=payload.GetProperty("DeviceId").GetInt32();await using var auditDb=factory.Create();var device=await auditDb.Devices.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==deviceId&&d.Type=="MikroTik",token)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выберите зарегистрированный MikroTik"));
                if(command=="router-audit-ignore"){if(actor!=null)AccessRules.Require(actor,"routers",2);RouterAuditStore.Ignore(state.Routers.Store,Read<RouterAuditIgnore>(),actor?.Login??"Администратор");}
                if(command=="router-audit-policy"){if(actor!=null)AccessRules.Require(actor,"routers",2);RouterAuditStore.Policy(state.Routers.Store,Read<RouterAuditPolicy>());}
                if(command=="router-audit-refresh"){
                    await RouterAuditRefresh.RunAsync(state.Routers,factory,deviceId,token);
                }
                return RouterAuditStore.View(state.Routers.Store,deviceId);
            }
            if(command=="router-dictionaries")return state.Routers.Store.Dictionaries();
            if(command is "router-connection" or "router-connection-save" or "router-events" or "router-problem-resolve") {
                int deviceId=payload.GetProperty("DeviceId").GetInt32();
                await using var targetDb=factory.Create();var networkDevice=await targetDb.Devices.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==deviceId&&(d.Type=="MikroTik"||d.Type=="Коммутатор"),token);if(networkDevice==null)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сетевое устройство не найдено"));
                if(command=="router-problem-resolve"){
                    if(actor!=null)AccessRules.Require(actor,"routers",2);var problemId=payload.GetProperty("ProblemId").GetString();var profiles=state.Routers.Store.Dictionaries();
                    return state.Routers.Store.State(s=>{
                        var history=s.Histories.SingleOrDefault(h=>h.DeviceId==deviceId)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("История не найдена"));
                        var problem=history.Problems.SingleOrDefault(p=>p.Id==problemId)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Проблема не найдена"));
                        if(problem.Kind!="Log")throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Восстановление интерфейса подтверждается опросом"));
                        if(problem.Open)NetworkLogRules.Close(problem,DateTime.UtcNow,"Отмечено решённым пользователем");
                        var c=s.Connections.SingleOrDefault(c=>c.DeviceId==deviceId);var selected=profiles.SingleOrDefault(d=>d.Id==c?.DictionaryId)??NetworkDictionaries.Generic();var rules=NetworkLogRules.Effective(selected,c?.Interfaces??new Dictionary<string,string[]>(),c?.Rules);
                        NetworkNotifications.Capture(s,history,"устройство #"+deviceId,rules,DateTime.UtcNow);return true;
                    },true);
                }
                if(command=="router-events")return state.Routers.Store.State(s=>s.Histories.SingleOrDefault(h=>h.DeviceId==deviceId)??new NetworkHistory{DeviceId=deviceId});
                if(command=="router-connection")return state.Routers.Store.Connection(deviceId);
                if(actor!=null)AccessRules.Require(actor,"routers",2);state.Routers.Store.Save(Read<NetworkConnectionSave>(),networkDevice.Host);state.RequestPoll();return true;
            }
            if(command=="router-monitor-list")return await state.Routers.ListAsync(factory,token);
            if(command=="router-monitor-refresh") {await state.Routers.CollectAsync(factory,state.Settings.Router,token,availabilityDelaySeconds:state.Settings.AvailabilityAlertMinutes*60);return await state.Routers.ListAsync(factory,token);}
            if(command is not ("router-snapshot" or "router-firewall-list"))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("MikroTik доступен только для мониторинга"));
            var host=payload.GetProperty("Host").GetString()??"";
            await using var db=factory.Create();
            if(string.IsNullOrWhiteSpace(host)||host!=state.Settings.Router.Host&&!await db.Devices.AnyAsync(d=>d.Host==host&&d.Enabled&&d.Type=="MikroTik",token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Устройство RouterOS не зарегистрировано"));
            var target=await db.Devices.AsNoTracking().FirstOrDefaultAsync(d=>d.Host==host&&d.Enabled&&d.Type=="MikroTik",token)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Роутер не зарегистрирован"));
            var connection=state.Routers.Store.State(s=>s.Connections.SingleOrDefault(c=>c.DeviceId==target.Id));
            var config=NetworkStore.Target(host,connection,state.Settings.Router);
            return command=="router-snapshot"?(object)await state.Routers.Sessions.FullSnapshotAsync(target.Id,config,token):await state.Routers.Sessions.FirewallAsync(target.Id,config,token);
        }
        throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестная серверная операция"));
    }
    private static string InventoryHistoryDescription(InventoryAsset a)=>string.Join("\n",
        $"Инвентарный номер: {a.InventoryNumber}",$"Модель: {a.Manufacturer} {a.Model}",$"SN: {a.SerialNumber}",
        $"Процессор: {a.Cpu}",$"ОЗУ: {a.RamGb} ГБ",$"Накопители: {a.DiskGb} ГБ",$"Видеокарта: {a.Gpu}",$"Расположение: {a.Location}");

}
