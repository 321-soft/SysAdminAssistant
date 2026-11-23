using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace SysAdminAssistant.Core;

public enum BackupResult { Unknown, Success, Warning, Failed, Event }
public enum Severity { Unknown, Info, Warning, Critical }
public enum TargetKind { Device, SqlJob }
public enum SearchScope { Both, Subject, Body }
public enum RepeatPeriod { None, Weekly, Monthly, Yearly }
public enum LampState { Gray, Green, Amber, Red }
public enum DatabaseProvider { SQLite, SqlServer }

public abstract class Entity { public int Id { get; set; } }
public sealed class Device : Entity
{
    public bool IsKscServer {get;set;}
    [MaxLength(4000)] public string ConnectionsJson {get;set;}="";
    public DateTime? AgentLastSeen {get;set;}
    public string MailUpdatesJson {get;set;}="";
    public string OsUpdatesJson {get;set;}="";
    public string AgentHealth {get;set;}="";
    public string AgentSummary {get;set;}="";
    [MaxLength(200)] public string Name { get; set; } = "";
    public string Site { get; set; } = "";
    public string Type { get; set; } = "Сервер";
    public string Host { get; set; } = "";
    public string Role { get; set; } = "";
    [MaxLength(100)] public string MailSystem {get;set;}="";
    public string EquipmentFamily { get; set; } = "generic";
    public string Status { get; set; } = "UNKNOWN";
    public bool Enabled { get; set; } = true;
    public DateTime? CheckedAt { get; set; }
    public DateTime? StatusSince { get; set; }
    public DateTime? MaintenanceUntil { get; set; }
    public DateTime? LastSeen { get; set; }
    public string LastError { get; set; } = "";
    public override string ToString() => $"{Name} · {Host}";
}
public sealed class SchemaInfo : Entity { public int Version { get; set; } = 6; }
public sealed class ConnectivityRecord : Entity { public string Lan {get;set;}="DISABLED"; public string ProbeResultsJson {get;set;}="[]"; public DateTime CheckedAt { get; set; } public string Ping { get; set; } = "UNKNOWN"; public string Dns { get; set; } = "UNKNOWN"; }
public sealed class ServiceEntry : Entity
{
    public int? DeviceId { get; set; }
    public DateTime? CheckedAt { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "HTTP";
    public string Address { get; set; } = "";
    public string Status { get; set; } = "UNKNOWN";
    public string Notes { get; set; } = "";
}
public sealed class Payment : Entity
{
    [Range(typeof(long), "0", "99999999999999")] public long? AmountKopecks { get; set; }
    public string Title { get; set; } = "";
    public DateTime DueDate { get; set; } = AppTime.Today;
    public RepeatPeriod Repeat { get; set; }
    public int AnchorDay { get; set; }
    public bool Paid { get; set; }
    public string Notes { get; set; } = "";
    public void MarkPaid()
    {
        if (Repeat == RepeatPeriod.None) { Paid = true; return; }
        if (AnchorDay == 0) AnchorDay = DueDate.Day;
        DueDate = Recurrence.Next(DueDate, Repeat, AnchorDay);
        Paid = false;
    }
}
public sealed class SqlBackupJob : Entity
{
    [MaxLength(100)] public string BackupSystem {get;set;}="";
    [Range(0,1440)] public int? EarlyMinutes {get;set;}
    public string CompletionTimes {get;set;}="";
    public string Title { get; set; } = "";
    public string Host { get; set; } = "";
    public string Instance { get; set; } = "";
    public int? DeviceId { get; set; }
    public int Days { get; set; } = 127;
    public int DeadlineMinutes { get; set; } = 180;
    public int GraceMinutes { get; set; } = 60;
    public bool Active { get; set; } = true;
    public DateTime MonitoringSince { get; set; } = AppTime.Now;
    public override string ToString() => $"{Title} · {Host}";
}
public sealed class BindingRule : Entity
{
    public string MailCategory {get;set;}="Report";
    public string UpdateProduct {get;set;}="";
    public bool SubjectMatchAll {get;set;}=true;
    public bool BodyMatchAll {get;set;}=true;
    public string Provider { get; set; } = "Любой";
    public string SenderPhrase { get; set; } = "";
    public string SubjectPhrase { get; set; } = "";
    public string BodyPhrase { get; set; } = "";
    public bool MatchAll { get; set; } = true;
    public TargetKind TargetKind { get; set; }
    public int TargetId { get; set; }
    public BackupResult? ResultOverride { get; set; }
    public int Priority { get; set; } = 100;
    public bool Active { get; set; } = true;
}
public sealed class KeywordProfile : Entity
{
    public string AdditionalEventRulesJson {get;set;}="";
    public string EventRulesJson {get;set;}="";
    public string UpdateSubjectTerms {get;set;}="";
    public string UpdateBodyTerms {get;set;}="";
    public string UpdateProduct {get;set;}="";
    public bool UpdateMatchAll {get;set;}
    public int UpdateDictionaryVersion {get;set;}
    public string SenderTerms { get; set; } = "";
    public string SubjectTerms { get; set; } = "";
    public string BodyTerms { get; set; } = "";
    public bool MatchAllSourceFields { get; set; } = true;
    public SearchScope ResultScope { get; set; } = SearchScope.Subject;
    [MaxLength(100)] public string Provider { get; set; } = "Любой";
    public string Critical { get; set; } = "critical;fatal;failed;failure;error;crashed;degraded;ошиб*;сбой;авария";
    public string Warning { get; set; } = "warning;warn;low capacity;partially completed;предупрежд*";
    public string Info { get; set; } = "success;successful;completed;info;успешно;завершено";
    public string Ignore { get; set; } = "no errors;no warnings;errors:0;errors=0;error count:0;failed:0;failed=0;failed tasks:0;failed tasks=0;ошибок:0;предупреждений:0;0 errors;0 warnings;без ошибок;нет ошибок;no failures";
    public SearchScope Scope { get; set; }
    public string SuccessResults { get; set; } = "success;successful;completed;complete;успешно;завершено";
    public string WarningResults { get; set; } = "warning;partially complete;partially completed;предупреждение";
    public string FailedResults { get; set; } = "failed;failure;error;critical;ошибка;сбой";
}
public sealed class ReportEvent : Entity
{
    public string MailCategory {get;set;}="Report";
    public string CategoryLabel=>MailCategory=="Update"?"Обновления":"Отчёт / событие";
    public string UpdateProduct {get;set;}="";
    public string UpdateVersion {get;set;}="";
    public string UpdateKey {get;set;}="";
    public bool BindingLocked {get;set;}
    [Range(0,1439)] public int? ScheduledSlotMinutes {get;set;}
    public int? DeviceId { get; set; }
    public int? JobId { get; set; }
    public int? RuleId { get; set; }
    public string Provider { get; set; } = "Неизвестно";
    public BackupResult Result { get; set; }
    public Severity Severity { get; set; }
    public string TargetName { get; set; } = "";
    public string Task { get; set; } = "";
    public string Host { get; set; } = "";
    public DateTime? OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; } = AppTime.Now;
    public string Duration { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Description { get; set; } = "";
    public string Issue { get; set; } = "";
    public bool IsPending { get; set; } = true;
}
public sealed class PendingReport : Entity
{
    public int EventId { get; set; }
    public string Sender { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime? MessageDate { get; set; }
}
public sealed class RetiredMailKey : Entity { [MaxLength(64)] public string Digest { get; set; } = ""; }
public sealed class IntakeKey : Entity
{
    [MaxLength(64)] public string Digest { get; set; } = "";
    public int EventId { get; set; }
}
public sealed class LegacyMessageKey : Entity { [MaxLength(64)] public string Digest { get; set; } = ""; public int EventId { get; set; } }
public sealed class Incident : Entity
{
    public int? ReportId { get; set; }
    public DateTime CreatedAt { get; set; } = AppTime.Now;
    public Severity Severity { get; set; }
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public bool Closed { get; set; }
}
public sealed class CredentialEntry : Entity
{
    public string OwnerId { get; set; } = "";
    public string OwnerKey { get; set; } = "";
    public string AdministratorKey { get; set; } = "";
    public int Revision { get; set; } = 1;
    public string Name { get; set; } = "";
    public int? DeviceId { get; set; }
    [Range(1,int.MaxValue)] public int? ServiceId { get; set; }
    public string Ciphertext { get; set; } = "";
}
public sealed class VaultMetadata : Entity { public string Salt { get; set; } = ""; public string Proof { get; set; } = ""; }
public sealed class LegacyImport : Entity { [MaxLength(64)] public string Digest { get; set; } = ""; public DateTime ImportedAt { get; set; } = AppTime.Now; }
public sealed class PollLease : Entity { [MaxLength(64)] public string Name { get; set; } = ""; [MaxLength(64)] public string Owner { get; set; } = ""; public DateTime UntilUtc { get; set; } }
public sealed record MailReport(string Account, string Key, string Sender, string Subject, string Body, DateTime? MessageDate, string MessageId = "", DateTime? ImapReceivedAt = null);
public sealed record Analysis(string Provider, BackupResult Result, Severity Severity, string Task, string Host,
    DateTime? OccurredAt, string Duration, string Size, string Evidence, string Details = "") { public bool MatchedAdditionalRule {get;init;} }
public sealed record BackupLamp(string Name, LampState State, string Status, DateTime? LastReport, int? DeviceId = null, int? JobId = null);
public sealed record CredentialPayload(string Login, string Password, string Notes);
public static class Display
{
    public static string Level(Severity level)=>ProductText.Get(level switch {Severity.Critical=>"Критично",Severity.Warning=>"Предупреждение",Severity.Info=>"Информация",_=>"Уровень не распознан"});
    public static string Date(DateTime? date) => date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "—";
    public static string Stamp(DateTime? date) => date?.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) ?? "—";
    public static string Result(BackupResult result) => ProductText.Get(result switch { BackupResult.Success => "Успешно", BackupResult.Failed => "Ошибка", BackupResult.Warning => "Предупреждение", BackupResult.Event => "Событие", _ => "Результат не распознан" });
}
public static class Recurrence
{
    public static DateTime Next(DateTime date, RepeatPeriod repeat, int anchorDay) => repeat switch
    {
        RepeatPeriod.Weekly => date.AddDays(7),
        RepeatPeriod.Monthly => Anchored(date.AddMonths(1), anchorDay),
        RepeatPeriod.Yearly => Anchored(date.AddYears(1), anchorDay),
        _ => date
    };
    private static DateTime Anchored(DateTime month, int day) => new(month.Year, month.Month, Math.Min(Math.Clamp(day, 1, 31), DateTime.DaysInMonth(month.Year, month.Month)));
}

public sealed class PaymentReceipt : Entity
{
    public int PaymentId {get;set;}public string Title {get;set;}="";public DateTime ScheduledDate {get;set;}public DateTime PaidAt {get;set;}
    public long? AmountKopecks {get;set;}public string PaidBy {get;set;}="";public string RequestKey {get;set;}="";
    public bool Cancelled {get;set;}public DateTime? CancelledAt {get;set;}public string CancelledBy {get;set;}="";
}
