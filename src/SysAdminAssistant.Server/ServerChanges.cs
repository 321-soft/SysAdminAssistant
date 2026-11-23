using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;

public static class ServerChanges
{
    private static readonly Dictionary<string, string[]> Fields = new()
    {
        ["devices"] = ["IsKscServer", "ConnectionsJson", "Name", "Type", "Host", "Site", "Role", "Enabled", "EquipmentFamily", "MaintenanceUntil"],
        ["services"] = ["Name", "Type", "Address", "Notes", "DeviceId"],
        ["payments"] = ["Title", "DueDate", "Repeat", "AnchorDay", "Paid", "Notes", "AmountKopecks"],
        ["jobs"] = ["Title", "Host", "Instance", "DeviceId", "Days", "DeadlineMinutes", "GraceMinutes", "Active", "MonitoringSince", "CompletionTimes", "BackupSystem", "EarlyMinutes"],
        ["rules"] = ["MailCategory", "UpdateProduct", "Provider", "SenderPhrase", "SubjectPhrase", "BodyPhrase", "MatchAll", "TargetKind", "TargetId", "ResultOverride", "Priority", "Active", "BodyMatchAll", "SubjectMatchAll"],
        ["profiles"] = ["AdditionalEventRulesJson","EventRulesJson","UpdateSubjectTerms", "UpdateBodyTerms", "UpdateProduct", "UpdateMatchAll", "Provider", "SenderTerms", "SubjectTerms", "BodyTerms", "MatchAllSourceFields", "ResultScope", "Critical", "Warning", "Info", "Ignore", "Scope", "SuccessResults", "WarningResults", "FailedResults"],
        ["incidents"] = ["Closed"], ["credentials"] = ["Name", "DeviceId", "Ciphertext"], ["vault"] = ["Salt", "Proof"]
    };
    public static async Task<RemoteChangeResult[]> ApplyAsync(DatabaseFactory factory, RemoteChange[] changes, CancellationToken token, UserView? actor = null)
    {
        if (changes.Any(x => x.Resource is "credentials" or "vault")) throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Vault доступен только через проверяемые операции владельца"));
        if (changes.Length is < 1 or > 100) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пакет изменений: 1–100 записей"));
        await using var db = factory.Create(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var results = new List<RemoteChangeResult>();
        foreach (var change in changes)
        {
            if(actor!=null && change.Resource is not ("incidents" or "devices")) AccessRules.Require(actor,AccessRules.ResourceModule(change.Resource),2);
            if (!Fields.TryGetValue(change.Resource, out var fields) || change.State is not ("Added" or "Modified" or "Deleted")) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Ресурс или операция недоступны"));
            var type = RemoteData.Types[change.Resource];
            var input = (Entity)(change.Value.Deserialize(type, RemoteData.Json) ?? throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пустая запись")));
            if (input.Id != change.Id || input.Id < 0) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный идентификатор"));
            if (type.GetProperties().Any(p => fields.Contains(p.Name) && p.PropertyType == typeof(string) && (p.GetValue(input) is not string s || s.Length > (p.Name is nameof(KeywordProfile.EventRulesJson) or nameof(KeywordProfile.AdditionalEventRulesJson) ? 65_536 : 10_000)))) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пустое или слишком длинное текстовое поле"));
            if(actor!=null && input is Device proposed)AccessRules.Require(actor,NetworkDevices.Module(proposed.Type),2);
            Entity current;
            if (change.State == "Added")
            {
                if (change.Id != 0 || change.Resource == "incidents") throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Создание недоступно"));
                current = (Entity)Activator.CreateInstance(type)!; db.Add(current);
            }
            else
            {
                current = (Entity)(await db.FindAsync(type, new object[] { change.Id }, token) ?? throw new ConflictException(global::SysAdminAssistant.Core.ProductText.Get("Запись уже удалена")));
                if(actor!=null && current is Device original)AccessRules.Require(actor,NetworkDevices.Module(original.Type),2);
                if (change.ExpectedHash != RemoteData.EntityStamp(current)) throw new ConflictException(global::SysAdminAssistant.Core.ProductText.Get("Запись изменилась. Обновите данные и повторите действие."));
                if (change.Resource == "vault") throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Мастер-параметры Vault не перезаписываются"));
            }
            if(actor!=null && current is Incident incident) {
                var isDmarc=incident.ReportId!=null && await db.Reports.AnyAsync(x=>x.Id==incident.ReportId && x.Provider=="DMARC",token);
                AccessRules.Require(actor,isDmarc?"dmarc":"reports",2);
            }
            if (change.State == "Deleted")
            {
                if (change.Resource is not ("rules" or "services" or "devices" or "jobs" or "payments")) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Удаление этого ресурса недоступно"));
                await CatalogDeletion.EnsureAllowedAsync(db,(Entity)current,token);
                db.Remove(current);
            }
            else
            {
                if (current is Device device && device.Host != ((Device)input).Host) { device.Status = "UNKNOWN"; device.LastSeen = null; device.LastError = ""; }
                if (current is ServiceEntry service && (service.Address != ((ServiceEntry)input).Address || service.Type != ((ServiceEntry)input).Type)) service.Status = "UNKNOWN";
                if (change.State == "Modified" && current is KeywordProfile old && old.Provider == "Любой" && ((KeywordProfile)input).Provider != "Любой") throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Общий профиль нельзя переименовать"));
                if(current is ServiceEntry beforeService && input is ServiceEntry nextService && beforeService.DeviceId != nextService.DeviceId && nextService.DeviceId!=null && actor!=null) AccessRules.Require(actor,"devices");
                foreach (var field in fields) {
                    if(field==nameof(KeywordProfile.AdditionalEventRulesJson)&&!change.Value.EnumerateObject().Any(x=>x.Name.Equals(field,StringComparison.OrdinalIgnoreCase)))continue;
                    var p = type.GetProperty(field)!; p.SetValue(current, p.GetValue(input));
                }
                if(current is ServiceEntry linkedService && linkedService.DeviceId != null) {
                    if(!await db.Devices.AnyAsync(d=>d.Id==linkedService.DeviceId,token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Связанное устройство отсутствует"));
                }
                await ValidateAsync(db, current, token);
            }
            await db.SaveChangesAsync(token);
            results.Add(new(change.Resource, change.State, change.Id, JsonSerializer.SerializeToElement(current, type, RemoteData.Json)));
        }
        await tx.CommitAsync(token); return results.ToArray();
    }
    private static async Task ValidateAsync(AssistantDb db, Entity value, CancellationToken token)
    {
        Validator.ValidateObject(value, new ValidationContext(value), true);
        switch (value)
        {
            case Device d:
                _=DeviceConnections.Parse(d.ConnectionsJson);
                if(string.IsNullOrWhiteSpace(d.Name))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите имя устройства"));
                break;
            case ServiceEntry s when string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.Address) || s.Type is not ("HTTP" or "TCP"): throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверная проверка сервиса"));
            case Payment payment when string.IsNullOrWhiteSpace(payment.Title) || !Enum.IsDefined(payment.Repeat) || payment.AnchorDay is < 0 or > 31: throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный счёт"));
            case SqlBackupJob j:
                _=BackupSchedule.Deadlines(j);
                if(j.BackupSystem==null || j.BackupSystem.Length>100 || j.BackupSystem is "Любой" or "Неизвестно" or "DMARC" || j.BackupSystem.Length>0 && !ReportAnalyzer.Providers.Contains(j.BackupSystem) && !await db.Profiles.AnyAsync(p=>p.Provider==j.BackupSystem,token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выберите систему бэкапа из словаря или автоопределение"));
                if (string.IsNullOrWhiteSpace(j.Title) || string.IsNullOrWhiteSpace(j.Host) || j.Active && j.Days == 0 || j.Days is < 0 or > 127 || j.DeadlineMinutes is < 0 or > 1439 || j.GraceMinutes is < 0 or > 1440 || j.DeviceId != null && !await db.Devices.AnyAsync(x => x.Id == j.DeviceId, token)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверное расписание или устройство SQL-бэкапа"));
                break;
            case BindingRule r:
                if(r.MailCategory is not("Report" or "Update")||r.UpdateProduct.Length>100||r.MailCategory=="Update"&&(r.TargetKind!=TargetKind.Device||r.ResultOverride!=null||string.IsNullOrWhiteSpace(r.UpdateProduct)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Правило обновления требует устройство и название продукта, без результата бэкапа"));
                if(RuleMatcher.BodyLines(r.SubjectPhrase).Length>32||RuleMatcher.BodyLines(r.BodyPhrase).Length>32)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Не более 32 строк тела"));
                if (!Enum.IsDefined(r.TargetKind) || r.ResultOverride != null && !Enum.IsDefined(r.ResultOverride.Value) || string.IsNullOrWhiteSpace(r.SenderPhrase + r.SubjectPhrase + r.BodyPhrase)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверное правило"));
                var exists = r.TargetKind == TargetKind.Device ? await db.Devices.AnyAsync(x => x.Id == r.TargetId, token) : await db.Jobs.AnyAsync(x => x.Id == r.TargetId, token);
                if (!exists) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Цель правила отсутствует"));
                break;
            case KeywordProfile p:
                MailSources.ValidateProfile(p);
                if(p.UpdateProduct.Length>100||MailUpdates.Terms(p.UpdateSubjectTerms).Length>32||MailUpdates.Terms(p.UpdateBodyTerms).Length>32)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Словарь обновлений: продукт до 100 символов, до 32 фраз на поле"));
                if (string.IsNullOrWhiteSpace(p.Provider) || !Enum.IsDefined(p.Scope) || !Enum.IsDefined(p.ResultScope) || await db.Profiles.AnyAsync(x => x.Id != p.Id && x.Provider.ToLower() == p.Provider.ToLower(), token)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный или повторяющийся источник"));
                var error = KeywordDefaults.Validate(p.Critical, p.Warning, p.Info, p.Ignore, p.SuccessResults, p.WarningResults, p.FailedResults, p.SenderTerms, p.SubjectTerms, p.BodyTerms);
                if (error != null) throw new ArgumentException(error);
                break;
            case CredentialEntry c:
                if(c.ServiceId is int serviceId&&!await db.Services.AnyAsync(x=>x.Id==serviceId,token))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сервис записи Vault отсутствует"));
                if (string.IsNullOrWhiteSpace(c.Name) || Convert.FromBase64String(c.Ciphertext).Length < 28 || c.DeviceId != null && !await db.Devices.AnyAsync(x => x.Id == c.DeviceId, token)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверная запись Vault"));
                break;
            case VaultMetadata v:
                if (await db.Vault.AnyAsync(token) || Convert.FromBase64String(v.Salt).Length != 32 || Convert.FromBase64String(v.Proof).Length < 28) throw new ConflictException(global::SysAdminAssistant.Core.ProductText.Get("Vault уже создан или параметры неверны"));
                break;
        }
    }
}
