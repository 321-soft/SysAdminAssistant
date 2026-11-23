using System.Data;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Infrastructure;

public sealed class PortableArchive
{
    public int Format { get; set; } = 5;
    public List<MailSource> MailSources {get;set;}=[];
    public List<RetiredMailKey> RetiredKeys { get; set; } = [];
    public List<Employee> Employees { get; set; } = [];
    public List<InventoryAsset> Assets { get; set; } = [];
    public List<InventoryAct> Acts { get; set; } = [];
    public List<InventoryActLine> ActLines { get; set; } = [];
    public List<Device> Devices { get; set; } = [];
    public List<ServiceEntry> Services { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
    public List<SqlBackupJob> Jobs { get; set; } = [];
    public List<BindingRule> Rules { get; set; } = [];
    public List<KeywordProfile> Profiles { get; set; } = [];
    public List<ReportEvent> Reports { get; set; } = [];
    public List<PendingReport> Pending { get; set; } = [];
    public List<IntakeKey> Intake { get; set; } = [];
    public List<Incident> Incidents { get; set; } = [];
    public List<CredentialEntry> Credentials { get; set; } = [];
    public List<VaultMetadata> Vault { get; set; } = [];
    public List<LegacyImport> Imports { get; set; } = [];
    public List<LegacyMessageKey> LegacyKeys { get; set; } = [];
}
public sealed class DatabaseTransfer(DatabaseFactory factory)
{
    public async Task ExportAsync(string path, CancellationToken token = default)
    {
        await using var db = factory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var archive = new PortableArchive
        {
            MailSources = await db.MailSources.AsNoTracking().ToListAsync(token),
            Employees = await db.Employees.AsNoTracking().ToListAsync(token), Assets = await db.Assets.AsNoTracking().ToListAsync(token),
            Acts = await db.Acts.AsNoTracking().ToListAsync(token), ActLines = await db.ActLines.AsNoTracking().ToListAsync(token),
            Devices = await db.Devices.AsNoTracking().ToListAsync(token), Services = await db.Services.AsNoTracking().ToListAsync(token),
            Payments = await db.Payments.AsNoTracking().ToListAsync(token), Jobs = await db.Jobs.AsNoTracking().ToListAsync(token),
            Rules = await db.Rules.AsNoTracking().ToListAsync(token), Profiles = await db.Profiles.AsNoTracking().ToListAsync(token),
            Reports = await db.Reports.AsNoTracking().ToListAsync(token), Pending = await db.Pending.AsNoTracking().ToListAsync(token),
            RetiredKeys = await db.RetiredKeys.AsNoTracking().ToListAsync(token), Intake = await db.Intake.AsNoTracking().ToListAsync(token), Incidents = await db.Incidents.AsNoTracking().ToListAsync(token),
            Credentials = await db.Credentials.AsNoTracking().ToListAsync(token), Vault = await db.Vault.AsNoTracking().ToListAsync(token),
            Imports = await db.LegacyImports.AsNoTracking().ToListAsync(token), LegacyKeys = await db.LegacyMessageKeys.AsNoTracking().ToListAsync(token)
        };
        await transaction.CommitAsync(token);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, archive, new JsonSerializerOptions { WriteIndented = true }, token);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public async Task ImportAsync(string path, CancellationToken token = default)
    {
        await using var stream = File.OpenRead(path);
        var data = await JsonSerializer.DeserializeAsync<PortableArchive>(stream, cancellationToken: token) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Пустой архив"));
        ValidateArchive(data);
        foreach(var profile in data.Profiles)MailSources.ValidateProfile(profile);
        if (data.Format is not (1 or 2 or 3 or 4 or 5)) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неподдерживаемая версия архива"));
        await using var db = factory.Create(); await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        if (await db.RetiredKeys.AnyAsync(token) || await db.Assets.AnyAsync(token) || await db.Employees.AnyAsync(token) || await db.Acts.AnyAsync(token) || await db.Devices.AnyAsync(token) || await db.Services.AnyAsync(token) || await db.Payments.AnyAsync(token) || await db.Jobs.AnyAsync(token) || await db.Rules.AnyAsync(token) || await db.Reports.AnyAsync(token) || await db.Credentials.AnyAsync(token) || await db.Vault.AnyAsync(token) || await db.Incidents.AnyAsync(token) || await db.Intake.AnyAsync(token) || await db.LegacyImports.AnyAsync(token))
            throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Для переноса выберите пустую базу. Существующие данные не удаляются."));
        var devices = await AddAsync(data.Devices);
        var employees = await AddAsync(data.Employees);
        int? EmployeeMap(int? old) => old is null ? null : Map(employees, old) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Сотрудник в архиве отсутствует"));
        var assets = await AddAsync(data.Assets, a => { a.EmployeeId = EmployeeMap(a.EmployeeId); a.DeviceId = Link(devices, a.DeviceId); if ((a.State == AssetState.Assigned) != (a.EmployeeId != null)) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неверное состояние выдачи имущества")); });
        var acts = await AddAsync(data.Acts, a => a.EmployeeId = EmployeeMap(a.EmployeeId));
        await AddAsync(data.ActLines, l => { l.ActId = Map(acts, l.ActId) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Акт отсутствует")); l.AssetId = Map(assets, l.AssetId) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Имущество акта отсутствует")); });
        var services=await AddAsync(data.Services, s => s.DeviceId = Link(devices, s.DeviceId)); await AddAsync(data.Payments);
        var jobs = await AddAsync(data.Jobs, j => j.DeviceId = Link(devices, j.DeviceId));
        var rules = await AddAsync(data.Rules, r =>
        {
            var target = r.TargetId == 0 && !r.Active ? null : Link(r.TargetKind == TargetKind.Device ? devices : jobs, r.TargetId);
            r.TargetId = target ?? 0;
        });
        await db.Profiles.ExecuteDeleteAsync(token); await AddAsync(data.Profiles);
        var profileNames = await db.Profiles.Select(p => p.Provider).ToArrayAsync(token);
        foreach (var provider in ReportAnalyzer.Providers.Where(p => !profileNames.Contains(p))) db.Profiles.Add(KeywordDefaults.For(provider));
        await db.SaveChangesAsync(token);
        var reports = await AddAsync(data.Reports, e => { e.DeviceId = Link(devices, e.DeviceId); e.JobId = Link(jobs, e.JobId); e.RuleId = Link(rules, e.RuleId); });
        await AddAsync(data.MailSources, m=>m.EventId=Map(reports,m.EventId)??throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Исходное письмо без отчёта")));
        await AddAsync(data.Pending, p => p.EventId = Map(reports, p.EventId) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Ссылка нераспознанного отчёта отсутствует")));
        await AddAsync(data.Intake, i => i.EventId = Map(reports, i.EventId) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Ключ сообщения без отчёта")));
        await AddAsync(data.Incidents, i => i.ReportId = Link(reports, i.ReportId));
        await AddAsync(data.Vault);
        await AddAsync(data.Credentials, c => {c.DeviceId = Link(devices, c.DeviceId);c.ServiceId=Link(services,c.ServiceId);});
        await AddAsync(data.RetiredKeys);
        await AddAsync(data.Imports);
        await AddAsync(data.LegacyKeys, k => k.EventId = Map(reports, k.EventId) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Старый ключ без отчёта")));
        await transaction.CommitAsync(token);
        async Task<Dictionary<int, int>> AddAsync<T>(IEnumerable<T> values, Action<T>? remap = null) where T : Entity
        {
            var map = new Dictionary<int, int>();
            foreach (var value in values)
            {
                var old = value.Id; if (old < 1 || map.ContainsKey(old)) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Повторяющийся или неверный идентификатор в архиве"));
                remap?.Invoke(value); value.Id = 0; db.Add(value); await db.SaveChangesAsync(token); map.Add(old, value.Id);
            }
            return map;
        }
    }
    private static int? Link(Dictionary<int, int> map, int? old) => old == null ? null : Map(map, old) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Архив содержит ссылку на отсутствующую запись"));
    private static void ValidateArchive(PortableArchive data)
    {
        foreach (var property in typeof(PortableArchive).GetProperties().Where(p => p.PropertyType.IsGenericType))
        {
            if (property.GetValue(data) is not System.Collections.IEnumerable rows) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("В архиве отсутствует список ") + property.Name);
            foreach (var row in rows)
            {
                if (row is not Entity entity) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Пустая запись в архиве"));
                try { Validator.ValidateObject(entity, new ValidationContext(entity), true); }
                catch (ValidationException e) { throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неверные поля записи ") + property.Name, e); }
                foreach (var field in row.GetType().GetProperties())
                {
                    var type = Nullable.GetUnderlyingType(field.PropertyType) ?? field.PropertyType; var value = field.GetValue(row);
                    if (type.IsEnum && value != null && !Enum.IsDefined(type, value)) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестное значение ") + field.Name);
                }
            }
        }
        foreach(var device in data.Devices)_=DeviceConnections.Parse(device.ConnectionsJson);
        foreach(var job in data.Jobs)_=BackupSchedule.Deadlines(job);
        if(data.Rules.Any(r=>RuleMatcher.BodyLines(r.SubjectPhrase).Length>32||RuleMatcher.BodyLines(r.BodyPhrase).Length>32))throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Более 32 условий тела"));
        if (data.Acts.Any(a => a.DismissalDate != null && a.Operation != InventoryOperation.Return)) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Дата увольнения указана не в акте возврата"));
        if (data.Vault.Count > 1 || data.Credentials.Count > 0 && data.Vault.Count != 1) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Архив содержит несовместимые настройки Vault"));
    }
    private static int? Map(Dictionary<int, int> map, int? old) => old != null && map.TryGetValue(old.Value, out var value) ? value : null;
}
