using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Infrastructure;

public sealed class AssistantDb(DbContextOptions<AssistantDb> options) : DbContext(options)
{
    public RemoteRepository? Remote { get; init; }
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (Remote != null) await Remote.PushAsync(this, cancellationToken).ConfigureAwait(false);
        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
    public override int SaveChanges()
    {
        if (Remote != null) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Сетевая запись требует SaveChangesAsync"));
        return base.SaveChanges();
    }
    public DbSet<MailSource> MailSources => Set<MailSource>();
    public DbSet<SchemaInfo> Schema => Set<SchemaInfo>();
    public DbSet<ConnectivityRecord> Connectivity => Set<ConnectivityRecord>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<ServiceEntry> Services => Set<ServiceEntry>();
    public DbSet<PaymentReceipt> PaymentReceipts=>Set<PaymentReceipt>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<SqlBackupJob> Jobs => Set<SqlBackupJob>();
    public DbSet<BindingRule> Rules => Set<BindingRule>();
    public DbSet<KeywordProfile> Profiles => Set<KeywordProfile>();
    public DbSet<ReportEvent> Reports => Set<ReportEvent>();
    public DbSet<PendingReport> Pending => Set<PendingReport>();
    public DbSet<IntakeKey> Intake => Set<IntakeKey>();
    public DbSet<RetiredMailKey> RetiredKeys => Set<RetiredMailKey>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<CredentialEntry> Credentials => Set<CredentialEntry>();
    public DbSet<VaultMetadata> Vault => Set<VaultMetadata>();
    public DbSet<LegacyMessageKey> LegacyMessageKeys => Set<LegacyMessageKey>();
    public DbSet<LegacyImport> LegacyImports => Set<LegacyImport>();
    public DbSet<PollLease> Leases => Set<PollLease>();
    public DbSet<InvoiceRecord> InvoiceRecords=>Set<InvoiceRecord>();
    public DbSet<InvoiceAttachment> InvoiceAttachments=>Set<InvoiceAttachment>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<InventoryAsset> Assets => Set<InventoryAsset>();
    public DbSet<InventoryAct> Acts => Set<InventoryAct>();
    public DbSet<InventoryActLine> ActLines => Set<InventoryActLine>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        foreach (var t in b.Model.GetEntityTypes()) t.SetTableName("sa2_" + t.ClrType.Name);
        TimeStorage.Configure(b);
        b.Entity<InvoiceRecord>().Property(x=>x.Key).HasMaxLength(64);b.Entity<InvoiceRecord>().HasIndex(x=>x.Key).IsUnique();
        b.Entity<PaymentReceipt>().Property(r=>r.RequestKey).HasMaxLength(100);
        b.Entity<PaymentReceipt>().HasIndex(r=>r.RequestKey).IsUnique();
        b.Entity<MailSource>().HasIndex(x=>x.EventId).IsUnique();
        b.Entity<CredentialEntry>().HasIndex(e => e.OwnerId);
        b.Entity<Employee>().HasIndex(e => e.PersonnelNumber).IsUnique().HasFilter("[PersonnelNumber] <> ''");
        b.Entity<InventoryAsset>().ToTable("sa2_InventoryAsset", t =>
        {
            t.HasCheckConstraint("CK_InventoryAsset_Owner", "([State] = 1 AND [EmployeeId] IS NOT NULL) OR ([State] <> 1 AND [EmployeeId] IS NULL)");
            t.HasCheckConstraint("CK_InventoryAsset_Values", "[State] IN (0,1,2,3) AND [RamGb] >= 0 AND [DiskGb] >= 0");
        });
        b.Entity<Employee>().Property(e => e.Revision).IsConcurrencyToken();
        b.Entity<InventoryAsset>().Property(e => e.Revision).IsConcurrencyToken();
        b.Entity<InventoryAsset>().HasIndex(e => e.InventoryNumber).IsUnique();
        b.Entity<InventoryAct>().HasIndex(e => e.Number).IsUnique();
        b.Entity<InventoryAsset>().HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<InventoryAsset>().HasOne<Device>().WithMany().HasForeignKey(e => e.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<InventoryAct>().HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<InventoryActLine>().HasOne<InventoryAct>().WithMany().HasForeignKey(e => e.ActId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<InventoryActLine>().HasOne<InventoryAsset>().WithMany().HasForeignKey(e => e.AssetId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<IntakeKey>().HasIndex(x => x.Digest).IsUnique();
        b.Entity<RetiredMailKey>().HasIndex(x => x.Digest).IsUnique();
        b.Entity<PendingReport>().HasIndex(x => x.EventId).IsUnique();
        b.Entity<KeywordProfile>().HasIndex(x => x.Provider).IsUnique();
        b.Entity<LegacyMessageKey>().HasIndex(x => x.Digest).IsUnique();
        b.Entity<LegacyImport>().HasIndex(x => x.Digest).IsUnique();
        b.Entity<PollLease>().HasIndex(x => x.Name).IsUnique();
        b.Entity<ReportEvent>().HasIndex(x => new { x.JobId, x.OccurredAt });
        b.Entity<ReportEvent>().HasIndex(x => new { x.DeviceId, x.OccurredAt });
        b.Entity<Incident>().HasIndex(x => x.ReportId).IsUnique().HasFilter("[ReportId] IS NOT NULL");
        b.Entity<PendingReport>().HasOne<ReportEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<IntakeKey>().HasOne<ReportEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class DatabaseFactory(AppSettings settings, RemoteRepository? remote = null)
{
    public RemoteRepository? Remote => remote;
    public AssistantDb Create()
    {
        if (remote != null) return remote.Create();
        if (!Enum.IsDefined(settings.DatabaseProvider)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестный тип хранилища"));
        var options = new DbContextOptionsBuilder<AssistantDb>();
        if (settings.DatabaseProvider == DatabaseProvider.SqlServer)
        {
            options.UseSqlServer(SqlStorage.Connection(settings));
        }
        else
        {
            var path = Path.GetFullPath(settings.SqlitePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, DefaultTimeout = 15 };
            options.UseSqlite(connection.ToString());
        }
        return new(options.Options);
    }
    public async Task InitializeAsync(CancellationToken token = default)
    {
        if (remote != null) { await remote.RefreshAsync(token); return; }
        await using var db = Create();
        await db.Database.EnsureCreatedAsync(token);
        await MailArchiveSchema.EnsureAsync(db,token);
        await BindingSchema.EnsureAsync(db,token);
        await PaymentHistory.EnsureAsync(db,token);
        // A legacy database must be imported into a separate new store, never used in place.
        try { _ = await db.Profiles.CountAsync(token); _ = await db.Schema.CountAsync(token); }
        catch (Exception e) { throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Это не база C#-версии или её схема несовместима. Выберите новую базу и импортируйте старую через настройки."), e); }
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sp_getapplock @Resource=N'SysAdminAssistant.Schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @r < 0 THROW 51000, 'Schema upgrade lock failed', 1;", token);
        var schema = await db.Schema.SingleOrDefaultAsync(token);
        if (schema is not null && schema.Version is not (1 or 2 or 3 or 4 or 5 or 6)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Неподдерживаемая версия схемы базы"));
        var sourceUpgrade = schema != null && schema.Version < 5;
        var upgrading = schema?.Version == 1;
        if (upgrading)
        {
            await InventorySchema.UpgradeAsync(db, token);
            schema!.Version = 3;
        }
        if (schema?.Version == 2)
        {
            await InventorySchema.UpgradeActTemplatesAsync(db, token); schema.Version = 3;
        }
        if (schema != null && schema.Version < 4) { await EventRetention.UpgradeAsync(db, token); schema.Version = 4; }
        if (schema != null) { await SourceSchema.UpgradeAsync(db, token); schema.Version = 6; }
        if (schema is null) db.Schema.Add(new SchemaInfo { Version = 6 });
        await InventorySchema.EnsureMonitoringLinkAsync(db, token);
        await InventorySchema.EnsureModelFieldsAsync(db, token);
        var profiles = await db.Profiles.ToArrayAsync(token);
        var common = profiles.FirstOrDefault(p => p.Provider == "Любой");
        var original = new KeywordProfile();
        bool OriginalLevels(KeywordProfile p) => p.Critical == original.Critical && p.Warning == original.Warning && p.Info == original.Info;
        bool OriginalOptions(KeywordProfile p) => p.Ignore == original.Ignore && p.Scope == original.Scope && p.SuccessResults == original.SuccessResults && p.WarningResults == original.WarningResults && p.FailedResults == original.FailedResults;
        // Upgrade only untouched v0.1 defaults. User dictionaries and custom inheritance stay intact.
        if (upgrading && common != null && OriginalLevels(common) && OriginalOptions(common))
        {
            foreach (var profile in profiles.Where(p => OriginalOptions(p) && (OriginalLevels(p) || string.IsNullOrWhiteSpace(p.Critical + p.Warning + p.Info))))
            {
                var defaults = KeywordDefaults.For(profile.Provider); defaults.Id = profile.Id;
                db.Entry(profile).CurrentValues.SetValues(defaults);
            }
        }
        if (sourceUpgrade)
        {
            var abb = profiles.FirstOrDefault(x => x.Provider == "Active Backup for Business");
            if (abb != null)
            {
                var defaults = KeywordDefaults.For(abb.Provider);
                if (abb.Critical == defaults.Critical.Replace(";не выполнена", "")) abb.Critical = defaults.Critical;
                if (abb.Warning == defaults.Warning.Replace(";выполнена частично", "")) abb.Warning = defaults.Warning;
                if (abb.Info == defaults.Info.Replace(";выполнена", "")) abb.Info = defaults.Info;
                if (abb.SuccessResults == defaults.SuccessResults.Replace(";выполнена", "")) abb.SuccessResults = defaults.SuccessResults;
                if (abb.WarningResults == defaults.WarningResults.Replace(";выполнена частично", "")) abb.WarningResults = defaults.WarningResults;
                if (abb.FailedResults == defaults.FailedResults.Replace(";не выполнена", "")) abb.FailedResults = defaults.FailedResults;
            }
        }
        var existing = profiles.Select(p => p.Provider).ToArray();
        foreach (var provider in ReportAnalyzer.Providers.Where(x => !existing.Contains(x))) db.Profiles.Add(KeywordDefaults.For(provider));
        await db.SaveChangesAsync(token); await MailUpdateStore.SeedAsync(db,token); await transaction.CommitAsync(token);
    }
}
