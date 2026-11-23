using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

var passed = new List<string>();
void Check(string name, bool result) { if (!result) throw new Exception("FAIL: " + name); passed.Add(name); Console.WriteLine("PASS: " + name); }
LicenseAgreementSpecs.Run(Check);
StationReleaseSpecs.Run(Check);
StationPeripheralSpecs.Run(Check);
InventoryHardwareSpecs.Run(Check);
HardwareHistorySpecs.Run(Check);
NativeWmiSpecs.Run(Check);
TableSortingSpecs.Run(Check);
WorkstationInstallerBundleSpecs.Run(Check);
UninstallPolicySpecs.Run(Check);
InventoryModelPhotoSpecs.Run(Check);
MailNumericSpecs.Run(Check);
ProductReleaseSpecs.Run(Check);
await ProductLanguageSpecs.Run(Check);
LinuxMonitoringSpecs.Run(Check);
SystemdTransitionSpecs.Run(Check);
SocketFloppySpecs.Run(Check);
NetworkNameSpecs.Run(Check);
AgentGaugeSpecs.Run(Check);
AgentAdviceSpecs.Run(Check);
AgentTemplateSpecs.Run(Check);
await KscCursorSpecs.Run(Check);
await KscReaderSpecs.Run(Check);
await KscLedgerSpecs.Run(Check);
LinuxServiceProfileSpecs.Run(Check);
await InvoiceSpecs.Run(Check);
SessionLeaseSpecs.Run(Check);
LoginAttemptsSpecs.Run(Check);
AgentServiceImageSpecs.Run(Check);
ExcelExchangeSpecs.Run(Check);
await TelegramSpecs.RunAsync(Check);
DashboardSpecs.Run(Check);
RepeatedScheduleSpecs.Run(Check);
var telegram = new TelegramSettings { Enabled = true, ChatId = "-123456", ProtectedToken = "fixture" };
Check("Telegram defaults to critical only", telegram.Accepts(Severity.Critical) && !telegram.Accepts(Severity.Warning) && !telegram.Accepts(Severity.Info) && !telegram.Accepts(Severity.Unknown));
telegram.Critical = false; telegram.Warning = true; telegram.Info = true;
Check("Telegram levels selected independently", !telegram.Accepts(Severity.Critical) && telegram.Accepts(Severity.Warning) && telegram.Accepts(Severity.Info));
telegram.Validate();
var notification = TelegramSender.Format(new Incident { Id = 7, Severity = Severity.Warning, Source = "ICMP", Title = "Device offline", Details = "SECRET-MAIL-BODY" });
Check("Telegram summary omits incident details", notification.Contains("ICMP") && notification.Contains("#7") && !notification.Contains("SECRET-MAIL-BODY"));
var maskedTelegram = RemoteData.PublicSettings(new AppSettings { Telegram = telegram });
Check("Telegram token masked from clients", maskedTelegram.Telegram.ProtectedToken == "REMOTE");
bool invalidTelegramRejected = false;
try { new TelegramSettings { Enabled = true, ChatId = "https://example.com", ProtectedToken = "fixture" }.Validate(); } catch (ArgumentException) { invalidTelegramRejected = true; }
Check("Invalid Telegram destination rejected", invalidTelegramRejected);
var authSettings = new MailSettings { TrustedAuthservId = "mx.example.net", AuthHeadersSanitized = true };
const string authGood = "mx.example.net; spf=pass smtp.mailfrom=reports@example.com; dkim=pass header.d=example.com";
Check("SPF and DKIM both pass with exact From alignment", MailAuthentication.Allows(authSettings, [authGood], "reports@example.com"));
Check("Untrusted authserv-id denied", !MailAuthentication.Allows(authSettings, [authGood.Replace("mx.example.net", "evil.example")], "reports@example.com"));
Check("Missing authentication denied", !MailAuthentication.Allows(authSettings, [], "reports@example.com"));
Check("DKIM failure and SPF-only denied", !MailAuthentication.Allows(authSettings, [authGood.Replace("dkim=pass", "dkim=fail")], "reports@example.com"));
Check("SPF failure denied despite DKIM pass", !MailAuthentication.Allows(authSettings, [authGood.Replace("spf=pass", "spf=fail")], "reports@example.com"));
Check("Unaligned DKIM denied", !MailAuthentication.Allows(authSettings, [authGood.Replace("header.d=example.com", "header.d=evil.example")], "reports@example.com"));
Check("Unaligned SPF denied", !MailAuthentication.Allows(authSettings, [authGood.Replace("reports@example.com", "reports@evil.example")], "reports@example.com"));
Check("Duplicate trusted headers denied", !MailAuthentication.Allows(authSettings, [authGood, authGood], "reports@example.com"));
Check("Comment cannot inject authentication", !MailAuthentication.Allows(authSettings, ["mx.example.net; spf=pass smtp.mailfrom=reports@example.com (dkim=pass header.d=example.com)"], "reports@example.com"));
Check("Quoted reason cannot supply DKIM domain", !MailAuthentication.Allows(authSettings, ["mx.example.net; spf=pass smtp.mailfrom=reports@example.com; dkim=pass reason=\"header.d=example.com\""], "reports@example.com"));
Check("Boundary confirmation required", !MailAuthentication.Allows(new MailSettings { TrustedAuthservId = "mx.example.net" }, [authGood], "reports@example.com"));
var nineCharacterHash = ServerPassword.Create("123456789");
Check("Nine-character server password accepted", ServerPassword.Verify("123456789", nineCharacterHash.Salt, nineCharacterHash.Hash));
bool eightCharacterRejected = false;
try { ServerPassword.Create("12345678"); } catch (ArgumentException) { eightCharacterRejected = true; }
Check("Eight-character server password rejected", eightCharacterRejected && !ServerPassword.Verify("12345678", nineCharacterHash.Salt, nineCharacterHash.Hash));
Check("SQL database identifier escapes closing bracket", SqlStorage.QuoteDatabase("reports]2026") == "[reports]]2026]");
bool systemDatabaseRejected = false;
try { SqlStorage.QuoteDatabase("master"); } catch (ArgumentException) { systemDatabaseRejected = true; }
Check("Installer rejects system database name", systemDatabaseRejected);
Check("Diagnostic catalogue official sources and unique IDs", DiagnosticGuide.Entries.Count == 33 && DiagnosticGuide.Entries.Select(x => x.Id).Distinct().Count() == 33 && DiagnosticGuide.Entries.All(x => DiagnosticGuide.OfficialSource(x.Source)));
var configWarnings = "Warning Unable to backup appliance configuration: configuration backup encryption is disabled\nWarning Skipping server certificate backup because encryption is disabled\nWarning Skipping credentials backup because encryption is disabled";
var configAdvice = DiagnosticGuide.Analyze("Veeam", configWarnings);
Check("Three config encryption warnings form one diagnosis", configAdvice.Findings.Length == 1 && configAdvice.Findings[0].Evidence.Length == 3 && configAdvice.Findings[0].Entry.Id == "veeam.config.encryption");
Check("Diagnosis retains original evidence", configAdvice.Format(true).Contains("Skipping credentials") && configAdvice.Format(true).Contains("Encrypt configuration backup"));
Check("No cross-product code matching", DiagnosticGuide.Analyze("QNAP", "Error code: 23").Findings.Length == 0);
Check("ABB code boundary", DiagnosticGuide.Analyze("Active Backup for Business", "Error code: 230").Findings.Length == 0 && DiagnosticGuide.Analyze("Active Backup for Business", "Error code: 23").Findings.Single().Entry.Id == "abb.disk.23");
Check("Numbers and job labels are not diagnostic evidence", DiagnosticGuide.Analyze("Active Backup for Business", "Items: 23\nJob name: Error code: 23").Findings.Length == 0);
Check("Old resolved warning is not current diagnosis", DiagnosticGuide.Analyze("Veeam", "Resolved: configuration backup encryption is disabled\n> configuration backup encryption is disabled").Findings.Length == 0);
Check("Case and whitespace normalization", DiagnosticGuide.Analyze("veeam", "Warning CONFIGURATION  BACKUP encryption IS disabled").Findings.Length == 1);
Check("Multiple independent causes retained", DiagnosticGuide.Analyze("Veeam", configWarnings + "\nInitial copy did not complete").Findings.Length == 2);
Check("Unknown warning has honest fallback", DiagnosticGuide.Analyze("Veeam", "Warning: unknown vendor failure").Format(true).Contains("не распознана"));
Check("Successful unknown message has no invented diagnosis", DiagnosticGuide.Analyze("Veeam", "Status: Success").Format(false) == "");
Check("HBS exact phrase", DiagnosticGuide.Analyze("QNAP HBS", "Warning Not all extended attributes are copied").Findings.Single().Entry.Id == "hbs.attributes");
Check("QNAP pool threshold distinct from low space", DiagnosticGuide.Analyze("QNAP", "Warning (Threshold Reached)").Findings.Single().Entry.Id == "qnap.pool.threshold");
Check("Long messages report analysis limit", DiagnosticGuide.Analyze("Veeam", new string('x', 500001)).Limited);
Check("Official source rejects fake host and non-HTTPS", !DiagnosticGuide.OfficialSource("https://www.qnap.com.evil.example/a") && !DiagnosticGuide.OfficialSource("http://www.qnap.com/a") && !DiagnosticGuide.OfficialSource("https://user@www.qnap.com/a"));
using (var fixtures = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("DiagnosticFixtures.json")!)
    foreach (var fixture in System.Text.Json.JsonSerializer.Deserialize<DiagnosticFixture[]>(fixtures)!)
        Check("Diagnostic fixture: " + fixture.Name, DiagnosticGuide.Analyze(fixture.Provider, fixture.Body).Findings.Select(x => x.Entry.Id).Order().SequenceEqual(fixture.Expected.Order()));
Check("Unknown issue excerpt survives", DiagnosticGuide.IssueEvidence("Status: Warning\nWarning Unrecognized code ZZ42").Contains("ZZ42"));
Check("Issue excerpt ignores metadata and quotes", DiagnosticGuide.IssueEvidence("Job name: error test\n> Warning old") == "");
var profiles = new[] { new KeywordProfile() };
Analysis Parse(string subject, string body) => ReportAnalyzer.Analyze(new("a", "key", "sender", subject, body, new DateTime(2026, 10, 6, 3, 0, 0)), profiles);
Check("Case insensitive result word in plain subject", Parse("SUCCESS — scheduled report", "SQLBackupAndFTP").Result == BackupResult.Success && Parse("success — scheduled report", "SQLBackupAndFTP").Result == BackupResult.Success);
Check("No special job phrase needed", Parse("Success on job \"Example task\"", "SQLBackupAndFTP").Result == BackupResult.Success);
Check("Result word boundary", Parse("unsuccessful report", "SQLBackupAndFTP").Result != BackupResult.Success);
var customProfile = new KeywordProfile { Provider = "ExampleBackup", SenderTerms = "reports@example.invalid", SuccessResults = "done", WarningResults = "partial", FailedResults = "broken" };
Analysis CustomParse(string sender, string subject, string body = "") => ReportAnalyzer.Analyze(new("a", "k", sender, subject, body, DateTime.Now), [new KeywordProfile(), customProfile]);
Check("Custom source and independent dictionary", CustomParse("reports@example.invalid", "DONE").Provider == "ExampleBackup" && CustomParse("reports@example.invalid", "DONE").Result == BackupResult.Success);
Check("Unmatched custom source not selected", CustomParse("other@example.invalid", "done").Provider == "Неизвестно");
Check("Custom failure", CustomParse("reports@example.invalid", "broken").Result == BackupResult.Failed);
Check("Conflicting sources remain unknown", ReportAnalyzer.DetectProvider("done", "", "reports@example.invalid", [customProfile, new KeywordProfile { Provider = "AnotherBackup", SenderTerms = "reports@example.invalid" }]) == "Неизвестно");
customProfile.SubjectTerms = "daily";
Check("All source fields required", CustomParse("reports@example.invalid", "done").Provider == "Неизвестно" && CustomParse("reports@example.invalid", "daily done").Provider == "ExampleBackup");
customProfile.SubjectTerms = ""; customProfile.ResultScope = SearchScope.Body;
Check("Result scope is editable", CustomParse("reports@example.invalid", "done", "no status").Result == BackupResult.Unknown && CustomParse("reports@example.invalid", "report", "done").Result == BackupResult.Success);
Check("Device type from metadata", DeviceTypes.Detect("Example", "Windows Server 2022") == "Сервер" && DeviceTypes.Detect("Example", "Windows 11") == "ПК" && DeviceTypes.Detect("Example", "QNAP NAS") == "QNAP NAS" && DeviceTypes.Detect("Example", "") == "Другое");
var abbProfiles = new[] { KeywordDefaults.For("Любой"), KeywordDefaults.For("Active Backup for Business") };
Analysis AbbParse(string status) => ReportAnalyzer.Analyze(new("a", "k", "", "Active Backup for Business — задача " + status, "Задача резервного копирования ExampleJob " + status + ".\nВремя окончания: 09.10.2026 03:08\nПереданный размер: 1.9 GB\nИз ExampleNAS", DateTime.Now), abbProfiles);
Check("Russian ABB partial", AbbParse("выполнена частично").Result == BackupResult.Warning && AbbParse("выполнена частично").Task == "ExampleJob" && AbbParse("выполнена частично").Host == "ExampleNAS");
Check("Russian ABB success", AbbParse("выполнена").Result == BackupResult.Success);
Check("Russian ABB failure", AbbParse("не выполнена").Result == BackupResult.Failed);
var htmlFields = HtmlText.Convert("<table><tr><td>Job:</td><td>ExampleJob</td></tr><tr><td>Server:</td><td>ExampleServer</td></tr><tr><td>Backup archive size:</td><td>1.9 GB</td></tr></table>");
var htmlAnalysis = Parse("Success", "SQLBackupAndFTP\n" + htmlFields);
Check("HTML cells preserve report metadata", htmlAnalysis.Task == "ExampleJob" && htmlAnalysis.Host == "ExampleServer" && htmlAnalysis.Size == "1.9 GB");
var veeamWarning = ReportAnalyzer.Analyze(new("a", "k", "", "[Warning] Backup Configuration Job", "Veeam\nConfiguration Backup for ExampleServer\n09.10.2026 10:00:25 Warning Unable to backup appliance configuration: configuration backup encryption is disabled\nEnd time\n10:00:25\nDuration\n0:00:13", new DateTime(2026, 10, 9, 10, 0, 30)), [KeywordDefaults.For("Любой"), KeywordDefaults.For("Veeam")]);
Check("Explicit Veeam warning line does not become critical", veeamWarning.Result == BackupResult.Warning && veeamWarning.Severity == Severity.Warning && veeamWarning.Host == "ExampleServer" && veeamWarning.Duration == "0:00:13");
var sqlTime = Parse("Success", "SQLBackupAndFTP\nJob:\nExampleJob\nServer:\nExampleServer\nDate:\n06.10.2026 1:00:14\n1:26:27 : Job \"ExampleJob\" finished.");
Check("SQL completed time and duration extracted", sqlTime.OccurredAt == new DateTime(2026, 10, 6, 1, 26, 27) && sqlTime.Duration == "0.00:26:13");
Check("SUCCESS and zero errors", Parse("[Success] Veeam", "Job name: Daily\nStatus: Success\nErrors: 0").Severity == Severity.Info);
Check("SUCCESS + critical stays critical", Parse("[Success] Veeam", "Status: Success\nDisk degraded").Result == BackupResult.Success && Parse("[Success] Veeam", "Status: Success\nDisk degraded").Severity == Severity.Critical);
Check("Word boundary ignores terror", Parse("[Success] Veeam", "Status: Success\nterror").Severity == Severity.Info);
Check("Russian prefix", Parse("DSM", "Произошла ошибка диска").Severity == Severity.Critical);
Check("NAS message is not backup success", Parse("[Success] DSM", "Status: Success").Result == BackupResult.Event);
Check("Unknown provider is conservative", Parse("Backup", "success").Result == BackupResult.Unknown);
foreach (var p in ReportAnalyzer.Providers.Where(x => x != "Любой")) Check("Detect " + p, Parse(p, "").Provider == p);
Check("Ignore phrases", Parse("[Success] Veeam", "Status: Success\nNo errors\nFailed tasks: 0").Severity == Severity.Info);
Check("Ignore variable colon spacing", Parse("[Success] Veeam", "Status: Success\nFailed tasks :0\nError count:0").Severity == Severity.Info);
Check("Conflicting multiple results cannot hide failure", Parse("[Success] Veeam", "Status: Success\nTask status: Failed").Result == BackupResult.Failed);
Check("Positive failed machines override success", Parse("[Success] Veeam", "Status: Success\nFailed machines: 2 machines").Result == BackupResult.Failed);
Check("Zero warnings and failures neutral", Parse("[Success] Veeam", "Status: Success\nFailed jobs: 0\nWarnings: 0\nFailures: 0").Severity == Severity.Info);
Check("Whitespace and equals labels", Parse("Veeam", "Job status = Success\nHost : PC-01").Result == BackupResult.Success && Parse("Veeam", "Host : PC-01").Host == "PC-01");
Check("Warning beats success", Parse("[Success] Veeam", "Status: Success\nWarnings: 3").Result == BackupResult.Warning);
Check("Zero skipped is not warning", ReportAnalyzer.Analyze(new("a", "k", "", "[Success] Veeam", "Status: Success\nSkipped files: 0", DateTime.Now), [KeywordDefaults.For("Любой")]).Severity == Severity.Info);
Check("Newline dictionaries", ReportAnalyzer.Analyze(new("a", "k", "", "DSM", "disk offline", DateTime.Now), [new KeywordProfile { Critical = "broken\ndisk offline" }]).Severity == Severity.Critical);
Check("Multiple evidence retained", Parse("DSM", "critical error").Evidence.Contains("critical") && Parse("DSM", "critical error").Evidence.Contains("error"));
Check("Reject unsafe wildcard", KeywordDefaults.Validate("*") != null && KeywordDefaults.Validate("err*or") != null && KeywordDefaults.Validate("ошиб*") == null);
foreach (var provider in ReportAnalyzer.Providers) Check("Populated preset " + provider, !string.IsNullOrWhiteSpace(KeywordDefaults.For(provider).Critical));
var subjectScope = new[] { new KeywordProfile { Scope = SearchScope.Subject } };
Check("Subject-only scope", ReportAnalyzer.Analyze(new("a", "k", "", "[Success] Veeam", "Error: broken", DateTime.Now), subjectScope).Severity == Severity.Info);
var mail = new MailReport("a", "k", "same@example.com", "[Success] SQLBackupAndFTP", "Server: SQL-01", DateTime.Now);
var rules = new[] { new BindingRule { BodyPhrase = "SQL-01", TargetId = 1, Priority = 10 }, new BindingRule { BodyPhrase = "SQL-01", TargetId = 2, Priority = 10 } };
Check("No implicit binding", RuleMatcher.Match(mail, "SQLBackupAndFTP", [], out _) == null);
Check("Conflicting target warning", RuleMatcher.Match(mail, "SQLBackupAndFTP", rules, out var issue) == null && issue.Contains("Конфликт"));
rules[1].Priority = 20;
Check("Priority and literal phrase", RuleMatcher.Match(mail, "SQLBackupAndFTP", rules, out _)?.TargetId == 1);
var weekendMask = PaymentCalendar.DefaultWeekendMask;
var firstDayInvoice = new Payment { DueDate = new DateTime(2026, 8, 1), Repeat = RepeatPeriod.Monthly, AnchorDay = 1 };
Check("Invoice Saturday moves to Monday", PaymentCalendar.EffectiveDueDate(firstDayInvoice.DueDate, weekendMask) == new DateTime(2026, 8, 3));
Check("Invoice reminder muted Saturday and Sunday", !PaymentCalendar.NeedsReminder(firstDayInvoice, new DateTime(2026, 8, 1), weekendMask) && !PaymentCalendar.NeedsReminder(firstDayInvoice, new DateTime(2026, 8, 2), weekendMask));
Check("Invoice reminder on effective due day", PaymentCalendar.NeedsReminder(firstDayInvoice, new DateTime(2026, 8, 3), weekendMask));
firstDayInvoice.MarkPaid();
Check("Monthly invoice keeps original first day", firstDayInvoice.DueDate == new DateTime(2026, 9, 1) && firstDayInvoice.AnchorDay == 1);
Check("Working date stays unchanged", PaymentCalendar.EffectiveDueDate(new DateTime(2026, 9, 1), weekendMask) == new DateTime(2026, 9, 1));
Check("Custom company weekend", PaymentCalendar.EffectiveDueDate(new DateTime(2026, 8, 7), (1 << 4) | (1 << 5)) == new DateTime(2026, 8, 9));
Check("No weekends permitted", PaymentCalendar.EffectiveDueDate(new DateTime(2026, 8, 1), 0) == new DateTime(2026, 8, 1));
Check("Invoice moves across month boundary", PaymentCalendar.EffectiveDueDate(new DateTime(2026, 1, 31), weekendMask) == new DateTime(2026, 2, 2));
Check("Paid invoice has no reminder", !PaymentCalendar.NeedsReminder(new Payment { DueDate = new DateTime(2026, 8, 1), Paid = true }, new DateTime(2026, 8, 3), weekendMask));
Check("Old unpaid invoice muted on weekend", !PaymentCalendar.NeedsReminder(new Payment { DueDate = new DateTime(2026, 7, 1) }, new DateTime(2026, 8, 1), weekendMask));
bool allWeekendRejected = false; try { PaymentCalendar.Validate(127); } catch (ArgumentOutOfRangeException) { allWeekendRejected = true; }
Check("All seven days off rejected", allWeekendRejected);
Check("Existing settings get Saturday Sunday default", System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!.Company.WeekendMask == weekendMask);
var payment = new Payment { DueDate = new(2026, 1, 31), Repeat = RepeatPeriod.Monthly, AnchorDay = 31 }; payment.MarkPaid();
Check("Monthly clamps February", payment.DueDate == new DateTime(2026, 2, 28)); payment.MarkPaid(); Check("Monthly restores anchor", payment.DueDate == new DateTime(2026, 3, 31));
Check("Leap annual", Recurrence.Next(new(2024, 2, 29), RepeatPeriod.Yearly, 29) == new DateTime(2025, 2, 28));
Check("Money parser accepts comma dot and grouping", InventoryMoney.Parse("67 375,00") == 6737500 && InventoryMoney.Parse("67375.00") == 6737500);
Check("Missing money differs from zero", InventoryMoney.Parse("") == null && InventoryMoney.Parse("0") == 0 && InventoryMoney.Print(null) == "—");
bool badMoney = false; try { InventoryMoney.Parse("12,345"); } catch (InvalidOperationException) { badMoney = true; }
Check("Money does not silently round fractions", badMoney);
var job = new SqlBackupJob { Id = 1, Title = "SQL", Days = 1 | 2 | 64, DeadlineMinutes = 180, GraceMinutes = 60, MonitoringSince = new(2026, 10, 1) };
Check("Offday does not clear missed Tuesday", BackupSchedule.Evaluate(job, [], new(2026, 10, 7, 12, 0, 0)).State == LampState.Red);
Check("Tuesday overdue", BackupSchedule.Evaluate(job, [], new(2026, 10, 6, 4, 1, 0)).Status == "Нет отчёта");
Check("Before grace", BackupSchedule.Evaluate(job, [], new(2026, 10, 6, 3, 30, 0)).Status == "Ожидается");
var success = new ReportEvent { JobId = 1, Result = BackupResult.Success, Severity = Severity.Info, OccurredAt = new(2026, 10, 6, 2, 59, 0), IsPending = false };
Check("Offday needs no extra report after Tuesday success", BackupSchedule.Evaluate(job, [success], new(2026, 10, 7, 12, 0, 0)).State == LampState.Green);
Check("Early completion is already green", BackupSchedule.Evaluate(job, [success], new(2026, 10, 6, 2, 59, 30)).State == LampState.Green);
Check("Today's report satisfies Tuesday", BackupSchedule.Evaluate(job, [success], new(2026, 10, 6, 5, 0, 0)).State == LampState.Green);
// A report already bound to a job must be resolved; IsPending defaults to true.
var receiptOnly = new ReportEvent { JobId = 1, Result = BackupResult.Success, Severity = Severity.Info, OccurredAt = null, ReceivedAt = new(2026, 10, 6, 2, 59, 0), IsPending = false };
Check("Undated report uses receipt time in window", BackupSchedule.Evaluate(job, [receiptOnly], new(2026, 10, 6, 4, 1, 0)).State == LampState.Green);
receiptOnly.IsPending = true;
Check("Pending undated report cannot satisfy scheduled job", BackupSchedule.Evaluate(job, [receiptOnly], new(2026, 10, 6, 4, 1, 0)).State == LampState.Red);
receiptOnly.IsPending = false; receiptOnly.ReceivedAt = new(2026, 10, 5, 2, 59, 0);
Check("Undated report outside current window cannot satisfy job", BackupSchedule.Evaluate(job, [receiptOnly], new(2026, 10, 6, 4, 1, 0)).State == LampState.Red);
receiptOnly.ReceivedAt = new(2026, 10, 6, 4, 2, 0);
Check("Future receipt cannot satisfy job yet", BackupSchedule.Evaluate(job, [receiptOnly], new(2026, 10, 6, 4, 1, 0)).State == LampState.Red);
success.OccurredAt = new(2026, 10, 5, 3, 0, 0);
Check("Yesterday cannot satisfy today", BackupSchedule.Evaluate(job, [success], new(2026, 10, 6, 5, 0, 0)).State == LampState.Red);
Check("Old Monday cannot hide missed Tuesday on Wednesday", BackupSchedule.Evaluate(job, [success], new(2026, 10, 7, 12, 0, 0)).State == LampState.Red);
var criticalBackup = new ReportEvent { Result = BackupResult.Success, Severity = Severity.Critical };
Check("Success with critical has explanatory lamp", BackupSchedule.FromLatest("Task", criticalBackup).State == LampState.Red && BackupSchedule.FromLatest("Task", criticalBackup).Status.Contains("критическое"));
job.MonitoringSince = new(2026, 10, 6, 5, 0, 0);
Check("Activation after deadline", BackupSchedule.Evaluate(job, [], new(2026, 10, 6, 5, 1, 0)).State != LampState.Red);
job.MonitoringSince = new(2026, 10, 1); job.DeadlineMinutes = 23 * 60 + 30; job.GraceMinutes = 120;
Check("Overnight grace no premature warning", BackupSchedule.Evaluate(job, [], new(2026, 10, 7, 0, 30, 0)).State != LampState.Red);
Check("Overnight grace detects missed", BackupSchedule.Evaluate(job, [], new(2026, 10, 7, 1, 31, 0)).State == LampState.Red);
Check("Html retains table labels", HtmlText.Convert("<table><tr><td>Status</td><td>Success</td></tr></table>").Contains("Status: Success"));
Check("HTML existing colon is not doubled", HtmlText.Convert("<table><tr><td>Status:</td><td><b>Success</b></td></tr></table>") == "Status: Success");
Check("HTML header cells and attributes retain pair", HtmlText.Convert("<TABLE><TR><TH class='label'>Status</TH><TD>Warning</TD></TR></TABLE>") == "Status: Warning");
Check("HTML separate rows stay separate", HtmlText.Convert("<tr><td>Job</td><td>Daily</td></tr><tr><td>Server</td><td>SQL01</td></tr>") == "Job: Daily\nServer: SQL01");
Check("HTML executable content stays excluded", HtmlText.Convert("<script>alert('never')</script><style>.x{}</style><tr><td>Status</td><td>Success</td></tr>") == "Status: Success");
Check("Router length encoding boundaries", RouterOsClient.EncodeLength(127).SequenceEqual(new byte[] { 127 }) && RouterOsClient.EncodeLength(128).SequenceEqual(new byte[] { 0x80, 0x80 }) && RouterOsClient.EncodeLength(0x4000).SequenceEqual(new byte[] { 0xC0, 0x40, 0 }));
var folder = Path.Combine(Path.GetTempPath(), "SysAdminSpecs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
try
{
    await ReportFeatureSpecs.Run(folder, Check);
    await MailArchiveSpecs.Run(folder, Check);
    await BindingSpecs.Run(folder, Check);
    await ProviderBindingSpecs.Run(folder,Check);
    var gridTiles = new[]{new WorkspaceTile("first",1,2),new WorkspaceTile("second",2,1),new WorkspaceTile("third",1,1)};
    var compactLayout=WorkspaceLayout.Pack(gridTiles,2);
    Check("Workspace respects configured order rather than filling earlier gaps",compactLayout.SequenceEqual(new[]{new WorkspacePlacement(0,0,1,2),new WorkspacePlacement(2,0,2,1),new WorkspacePlacement(3,0,1,1)}));
    var wideLayout=WorkspaceLayout.Pack(gridTiles,4);
    Check("A wider workspace fits more tiles in the first row",wideLayout.All(p=>p.Row==0)&&wideLayout[2].Column==3);
    Check("A narrow workspace reduces spans while preserving order",WorkspaceLayout.Pack(gridTiles,1).SequenceEqual(new[]{new WorkspacePlacement(0,0,1,2),new WorkspacePlacement(2,0,1,1),new WorkspacePlacement(3,0,1,1)}));
    Check("Workspace accepts the two large sizes and rejects unspecified shapes",
        WorkspaceLayout.Fits("overview-devices",2,4)&&WorkspaceLayout.Fits("overview-devices",4,2)&&
        !WorkspaceLayout.Fits("overview-devices",2,1)&&!WorkspaceLayout.Fits("devices-down",4,4));
    var largeTiles=new[]{new WorkspaceTile("first",2,4),new WorkspaceTile("second",4,2),new WorkspaceTile("third",4,2)};
    Check("Large cards retain height and fit narrow windows",WorkspaceLayout.Pack(largeTiles,1).SequenceEqual(
        new[]{new WorkspacePlacement(0,0,1,4),new WorkspacePlacement(4,0,1,2),new WorkspacePlacement(6,0,1,2)}));
    var equalRows=WorkspaceLayout.Pack(Enumerable.Range(0,4).Select(i=>new WorkspaceTile(i.ToString(),2,2)).ToArray(),5);
    Check("Fill width uses four tracks per pair in a five-track workspace",
        WorkspaceLayout.Bands(equalRows).SequenceEqual(new[]{(0,2,4),(2,4,4)}));
    Check("A tall card keeps the rows it crosses in the same band",
        WorkspaceLayout.Bands(WorkspaceLayout.Pack(largeTiles,6)).SequenceEqual(new[]{(0,3,6)}));
    Check("Existing workspace JSON leaves full width and height disabled",
        System.Text.Json.JsonSerializer.Deserialize<WorkspacePage>("""{"Page":0,"Tiles":null}""") is {FillRows:false,FillHeight:false});
    Check("Every menu page has a workspace default",Enumerable.Range(0,13).All(i=>WorkspaceLayout.Defaults(i).Length>0)&&WorkspaceLayout.Defaults(0).Select(t=>t.Id).SequenceEqual(new[]{"overview-devices","overview-backups","overview-payments","overview-network"}));
    await MailSourcesSpecs.Run(folder,Check);
    await DeviceProblemSpecs.Run(folder,Check);
    await BackupIndicatorSpecs.Run(folder,Check);
    await MailSubjectSpecs.Run(folder,Check);
    NetworkDictionarySpecs.Run(folder,Check);
    await NetworkSecuritySpecs.Run(folder,Check);
    await AgentSpecs.Run(folder,Check);
    AgentLifecycleSpecs.Run(folder,Check);
    WindowsEventSpecs.Run(folder,Check);
    await UpdateFixSpecs.Run(folder,Check);
    await MailUpdateSpecs.Run(folder,Check);
    AgentReleaseSpecs.Run(folder,Check);
    RouterAuditSpecs.Run(folder,Check);
    await RecoverySpecs.Run(folder,Check);
    LargeRouterSpecs.Run(Check);
    await Revision125Specs.Run(folder,Check);
    Revision127Specs.Run(Check);
    await Revision128Specs.Run(folder,Check);
    await Revision129Specs.Run(folder,Check);
    await Revision130Specs.Run(folder,Check);
    Revision131Specs.Run(Check);
    Revision132Specs.Run(Check);
    Revision140Specs.Run(Check);
    Revision141Specs.Run(Check);
    Revision150Specs.Run(Check);
    Revision170Specs.Run(Check);
    await Revision180Specs.Run(Check);
    Revision190Specs.Run(folder,Check);
    await Revision200Specs.Run(folder,Check);
    await TelegramPinSpecs.Run(folder,Check);
    AgentBundleSpecs.Run(folder,Check);
    await ServerHeartbeatSpecs.Run(Check);
    WatchdogPackageSpecs.Run(Check);
    AgentServiceProfileSpecs.Run(Check);
    await AvailabilitySpecs.Run(folder,Check);
    await CatalogDeletionSpecs.Run(folder,Check);
    await ServiceVaultSpecs.Run(folder,Check);
await NotificationLifecycleSpecs.Run(folder,Check);
await DeviceConnectionSpecs.Run(folder,Check);
await KscDeviceRoleSpecs.Run(folder,Check);
    await PaymentHistorySpecs.Run(folder,Check);
    await RouterSessionSpecs.Run(folder,Check);
AgentCrashSpecs.Run(folder,Check);
AgentFileUpdateSpecs.Run(folder,Check);
await Security0427Specs.Run(folder,Check);
await RouterReadSpecs.Run(folder,Check);
    SymmetricWindowSpecs.Run(Check);
    var settings = new AppSettings { SqlitePath = Path.Combine(folder, "specs.db") }; var factory = new DatabaseFactory(settings); await factory.InitializeAsync();
    await using (var db = factory.Create()) { db.Devices.Add(new Device { Name = "NAS1", Host = "nas1" }); await db.SaveChangesAsync(); }
    var store = new ReportStore(factory);
    var report = new MailReport("box1", "uid1", "sender", "[Success] Daily job", "Job name: Daily\nHost: NAS1\nStatus: Success", DateTime.Now);
    Check("Intake new", await store.IngestAsync(report)); Check("Intake deduplicates", !await store.IngestAsync(report));
    await using (var db = factory.Create())
    {
        Check("First mail is pending", await db.Pending.CountAsync() == 1 && await db.Incidents.CountAsync() == 0);
        db.Rules.Add(new BindingRule { BodyPhrase = "Host: NAS1", TargetId = 1 }); await db.SaveChangesAsync();
    }
    Check("Replay past reports", await store.ReprocessAsync() == 1);
    Check("Replay idempotent", await store.ReprocessAsync() == 0);
    await using (var db = factory.Create())
    {
        Check("Recognized mail deletes raw text", await db.Pending.CountAsync() == 0);
        Check("Structured archive", await db.Reports.CountAsync() == 1 && (await db.Reports.SingleAsync()).DeviceId == 1);
        Check("Incident once", await db.Incidents.CountAsync() == 1);
    }
    Check("Same UID other account is separate", await store.IngestAsync(report with { Account = "box2" }));
    using var vault = new VaultService(factory); await vault.UnlockAsync("correct-password-123"); var cipher = vault.Seal(new("admin", "secret", "notes"));
    Check("No plaintext ciphertext", !cipher.Contains("secret")); Check("Vault roundtrip", vault.Open(cipher).Password == "secret");
    var bytes = Convert.FromBase64String(cipher); bytes[^1] ^= 1; bool tamper = false; try { _ = vault.Open(Convert.ToBase64String(bytes)); } catch (CryptographicException) { tamper = true; }
    Check("Vault authenticated tamper rejection", tamper); vault.Lock(); Check("Vault locks", !vault.IsUnlocked);
    bool wrong = false; try { await vault.UnlockAsync("wrong-password-1234"); } catch (CryptographicException) { wrong = true; } Check("Wrong master rejected", wrong);
    await vault.UnlockAsync("correct-password-123"); Check("Reunlock existing proof", vault.Open(cipher).Login == "admin");

    await using (var db = factory.Create()) { db.Credentials.Add(new CredentialEntry { Name = "NAS admin", DeviceId = 1, Ciphertext = cipher }); await db.SaveChangesAsync(); }
    var archivePath = Path.Combine(folder, "portable.json"); await new DatabaseTransfer(factory).ExportAsync(archivePath);
    var second = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "second.db") }); await second.InitializeAsync(); await new DatabaseTransfer(second).ImportAsync(archivePath);
    await using (var db = second.Create())
    {
        Check("Portable reports preserved", await db.Reports.CountAsync() == 2);
        Check("Portable encrypted credentials", (await db.Credentials.SingleAsync()).Ciphertext == cipher && (await db.Credentials.SingleAsync()).DeviceId == (await db.Devices.SingleAsync()).Id);
        Check("Portable links remapped", (await db.Reports.FirstAsync()).DeviceId == (await db.Devices.FirstAsync()).Id);
        Check("Portable no plaintext secrets", !File.ReadAllText(archivePath).Contains("secret"));
        Check("Device mail does not create an implicit backup job", !await db.Jobs.AnyAsync() && (await BackupQueries.LampsAsync(db, DateTime.Now)).Count == 0);
    }
    using var secondVault = new VaultService(second); await secondVault.UnlockAsync("correct-password-123"); Check("Portable vault proof preserved", secondVault.Open(cipher).Password == "secret");
    var legacyPath = Path.Combine(folder, "legacy.db");
    await using (var old = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = legacyPath, Pooling = false }.ToString()))
    {
        await old.OpenAsync(); await using var cmd = old.CreateCommand();
        cmd.CommandText = "CREATE TABLE devices(id INTEGER,name TEXT,host TEXT,site TEXT,type TEXT,role TEXT,status TEXT,enabled INTEGER,last_seen TEXT,last_error TEXT);" +
            "INSERT INTO devices VALUES(10,'Old NAS','oldnas','Office','Synology NAS','','ONLINE',1,'2026-10-06 03:00:00','');" +
            "CREATE TABLE sa_report_events(id INTEGER,device_id INTEGER,job_id INTEGER,provider TEXT,status TEXT,category TEXT,task TEXT,host TEXT,occurred_at TEXT,received_at TEXT,duration TEXT,size_text TEXT,description TEXT,issue TEXT,rule_id INTEGER);" +
            "INSERT INTO sa_report_events VALUES(20,10,0,'Veeam','SUCCESS','BACKUP','Daily','oldnas','2026-10-06 03:00:00','2026-10-06 03:00:00','','','success','',0);" +
            "CREATE TABLE sa_report_intake(account TEXT,message_key TEXT,event_id INTEGER); INSERT INTO sa_report_intake VALUES('server/user/INBOX','<legacy@example.com>',20);";
        await cmd.ExecuteNonQueryAsync();
    }
    var imported = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "imported.db") }); await imported.InitializeAsync();
    var oldHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(legacyPath)); await new LegacyImporter(imported).ImportAsync(legacyPath);
    // No pooled source handle may survive import, including rejected repeat imports.
    using (var released = File.Open(legacyPath, FileMode.Open, FileAccess.Read, FileShare.None))
        Check("Legacy source released after import", released.Length > 0);
    bool repeatLegacyRejected = false;
    try { await new LegacyImporter(imported).ImportAsync(legacyPath); }
    catch (InvalidOperationException) { repeatLegacyRejected = true; }
    Check("Legacy repeat import rejected", repeatLegacyRejected);
    using (var released = File.Open(legacyPath, FileMode.Open, FileAccess.Read, FileShare.None))
        Check("Legacy source released after rejected import", released.Length > 0);
    Check("Legacy source unchanged", oldHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(legacyPath))));
    await using (var db = imported.Create()) Check("Legacy relational remap", (await db.Reports.SingleAsync()).DeviceId == (await db.Devices.SingleAsync()).Id);
    var oldMail = new MailReport("server/user/INBOX", "100:200", "", "[Success] Veeam", "Status: Success", DateTime.Now, "legacy@example.com");
    Check("Legacy Message-ID bridges first UID", !await new ReportStore(imported).IngestAsync(oldMail));
    await using (var db = imported.Create()) Check("Legacy duplicate not archived twice", await db.Reports.CountAsync() == 1);

    await using (var db = factory.Create())
    {
        db.Jobs.Add(new SqlBackupJob { Title = "SQL Daily", Host = "SQL-01", DeviceId = 1 }); await db.SaveChangesAsync();
        db.Rules.Add(new BindingRule { Provider = "SQLBackupAndFTP", BodyPhrase = "Host: SQL-01", TargetKind = TargetKind.SqlJob, TargetId = 1 }); await db.SaveChangesAsync();
    }
    Check("SQL intake normalized", await store.IngestAsync(new("box1", "sqluid", "", "[Success] SQLBackupAndFTP", "Host: SQL-01\nStatus: Success", DateTime.Now)));
    await using (var db = factory.Create())
    {
        var e = await db.Reports.SingleAsync(x => x.Provider == "SQLBackupAndFTP");
        Check("SQL job also in device history", e.JobId == 1 && e.DeviceId == 1 && !e.IsPending);
    }
    await using(var db=factory.Create()) {
        db.Rules.Add(new BindingRule {Provider="Veeam",SubjectPhrase="Portable non-SQL schedule",TargetKind=TargetKind.SqlJob,TargetId=1});
        await db.SaveChangesAsync();
    }
    Check("Non-SQL scheduled report accepted",await store.IngestAsync(new("box1","veeam-schedule","","[Success] Veeam Portable non-SQL schedule","Status: Success",null)));
    await using(var db=factory.Create())Check("Non-SQL schedule keeps job and device links",(await db.Reports.SingleAsync(x=>x.Provider=="Veeam" && x.JobId==1)).DeviceId==1 && !await db.Reports.AnyAsync(x=>x.JobId==1 && x.IsPending));
    var corrupt = new PortableArchive { Devices = [new Device { Id = 1, Name = "Should rollback" }], Pending = [new PendingReport { Id = 1, EventId = 999 }] };
    var corruptPath = Path.Combine(folder, "corrupt.json"); File.WriteAllText(corruptPath, System.Text.Json.JsonSerializer.Serialize(corrupt));
    var rollbackFactory = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "rollback.db") }); await rollbackFactory.InitializeAsync();
    bool rejected = false; try { await new DatabaseTransfer(rollbackFactory).ImportAsync(corruptPath); } catch (InvalidDataException) { rejected = true; }
    await using (var db = rollbackFactory.Create()) Check("Corrupt transfer rolls back", rejected && !await db.Devices.AnyAsync());
    var inventory = new InventoryService(factory);
    var person = new Employee { FullName = "Иванов Иван", PersonnelNumber = "T-01", Department = "ИТ" }; await inventory.SaveEmployeeAsync(person);
    var laptop = new InventoryAsset { InventoryNumber = "pc-001", Category = "Ноутбук", SerialNumber = "SN001", ValueKopecks = 6737500, WindowsName = "WIN-01", Cpu = "AMD Ryzen 5 5600", RamGb = 16, DiskGb = 512, DeviceId = 1 }; await inventory.SaveAssetAsync(laptop);
    var monitor = new InventoryAsset { InventoryNumber = "mon-001", Category = "Монитор", SerialNumber = "SN002" }; await inventory.SaveAssetAsync(monitor);
    Check("Inventory number normalized", laptop.InventoryNumber == "PC-001");
    InventoryRequest Request(InventoryOperation op, int[] ids) => new(op, ids, op == InventoryOperation.WriteOff ? null : person.Id, new DateTime(2026, 10, 8), "Компания", "Петров П.П.", "Москва", "Распоряжение", "Сидоров С.С.", RepresentativePosition: "Специалист ИТ");
    var issueAct = await inventory.ConductAsync(Request(InventoryOperation.Issue, [laptop.Id, monitor.Id]));
    await using (var db = factory.Create()) Check("Batch issue assigns both assets", await db.Assets.CountAsync(a => a.EmployeeId == person.Id && a.State == AssetState.Assigned) == 2);
    bool duplicateIssue = false; try { await inventory.ConductAsync(Request(InventoryOperation.Issue, [laptop.Id])); } catch (InvalidOperationException) { duplicateIssue = true; }
    Check("Double issue rejected", duplicateIssue);
    bool illegalWriteOff = false; try { await inventory.ConductAsync(Request(InventoryOperation.WriteOff, [laptop.Id])); } catch (InvalidOperationException) { illegalWriteOff = true; }
    Check("Assigned asset cannot be written off", illegalWriteOff);
    await using (var db = factory.Create()) person = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == person.Id);
    person.Active = false; bool refusedArchive = false; try { await inventory.SaveEmployeeAsync(person); } catch (InvalidOperationException) { refusedArchive = true; }
    Check("Employee cannot leave with equipment", refusedArchive); person.Active = true;
    await using (var db = factory.Create()) laptop = await db.Assets.AsNoTracking().SingleAsync(a => a.Id == laptop.Id);
    var revision = laptop.Revision; laptop.Cpu = "Intel Core i5-12400"; laptop.ValueKopecks = 9999900; await inventory.SaveAssetAsync(laptop);
    var stale = new InventoryAsset { Id = laptop.Id, InventoryNumber = "PC-001", Revision = revision }; bool conflict = false;
    try { await inventory.SaveAssetAsync(stale); } catch (InvalidOperationException) { conflict = true; }
    Check("Stale inventory edit rejected", conflict);
    await using (var db = factory.Create()) Check("Issued act preserves CPU snapshot", (await db.ActLines.SingleAsync(l => l.ActId == issueAct && l.AssetId == laptop.Id)).Specifications.Contains("AMD Ryzen 5 5600"));
    await using (var db = factory.Create()) Check("Act preserves original price and representative position", (await db.ActLines.SingleAsync(l => l.ActId == issueAct && l.AssetId == laptop.Id)).ValueKopecks == 6737500 && (await db.Acts.SingleAsync(a => a.Id == issueAct)).RepresentativePosition == "Специалист ИТ");
    bool outdatedAct = false;
    try { await inventory.ConductAsync(Request(InventoryOperation.Return, [laptop.Id]) with { ExpectedRevisions = new Dictionary<int, int> { [laptop.Id] = revision } }); } catch (InvalidOperationException) { outdatedAct = true; }
    Check("Act cannot use stale reviewed hardware", outdatedAct);
    bool earlierReturn = false; try { await inventory.ConductAsync(Request(InventoryOperation.Return, [laptop.Id]) with { Date = new DateTime(2026, 10, 7) }); } catch (InvalidOperationException) { earlierReturn = true; }
    Check("Return cannot predate issue", earlierReturn);
    var dismissalAct = await inventory.ConductAsync(Request(InventoryOperation.Return, [laptop.Id, monitor.Id]) with { DismissalDate = new DateTime(2026, 10, 9) });
    await using (var db = factory.Create()) Check("Dismissal date is saved separately from act date", (await db.Acts.SingleAsync(a => a.Id == dismissalAct)).DismissalDate == new DateTime(2026, 10, 9) && (await db.Acts.SingleAsync(a => a.Id == dismissalAct)).Date == new DateTime(2026, 10, 8));
    await inventory.ConductAsync(Request(InventoryOperation.WriteOff, [laptop.Id]));
    bool atomicReturn = false;
    try { await inventory.ConductAsync(Request(InventoryOperation.Return, [laptop.Id, monitor.Id]) with { DismissalDate = new DateTime(2026, 10, 9) }); }
    catch (InvalidOperationException) { atomicReturn = true; }
    await using (var db = factory.Create()) Check("Invalid batch has no partial writes", atomicReturn && await db.Acts.CountAsync() == 3 && (await db.Assets.SingleAsync(a => a.Id == monitor.Id)).State == AssetState.Stock);
    await using (var db = factory.Create()) person = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == person.Id);
    person.Active = false; await inventory.SaveEmployeeAsync(person);
    Check("Employee archive after full return", !person.Active);
    await using(var db=factory.Create()) {
        db.Services.Add(new ServiceEntry {Name="Linked HTTPS",Address="https://nas1",DeviceId=1});
        db.Payments.Add(new Payment {Title="Fixture hosting",AmountKopecks=123456,DueDate=DateTime.Today.AddDays(3)});
        await db.SaveChangesAsync();
    }
    var inventoryArchive = Path.Combine(folder, "inventory.json"); await new DatabaseTransfer(factory).ExportAsync(inventoryArchive);
    var inventoryCopy = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "inventory-copy.db") }); await inventoryCopy.InitializeAsync(); await new DatabaseTransfer(inventoryCopy).ImportAsync(inventoryArchive);
    await using (var db = inventoryCopy.Create())
    {
        var copiedDevice=await db.Devices.SingleAsync(d=>d.Name=="NAS1");
        Check("Transfer preserves service device link",(await db.Services.SingleAsync(x=>x.Name=="Linked HTTPS")).DeviceId==copiedDevice.Id);
        Check("Transfer preserves payment amount",(await db.Payments.SingleAsync(x=>x.Title=="Fixture hosting")).AmountKopecks==123456);
        var copiedActs = await db.Acts.AsNoTracking().ToArrayAsync();
        var copiedLines = await db.ActLines.AsNoTracking().ToArrayAsync();
        var copiedAssets = await db.Assets.AsNoTracking().ToArrayAsync();
        Check($"Inventory transfer act count (actual {copiedActs.Length})", copiedActs.Length == 3);
        Check($"Inventory transfer line count (actual {copiedLines.Length})", copiedLines.Length == 5);
        Check($"Inventory transfer asset count (actual {copiedAssets.Length})", copiedAssets.Length == 2);
        Check("Inventory transfer preserves every act and asset link", copiedLines.All(line =>
            copiedActs.Any(act => act.Id == line.ActId) &&
            copiedAssets.Any(asset => asset.Id == line.AssetId && asset.InventoryNumber == line.InventoryNumber)));
        var copiedPerson = await db.Employees.SingleAsync(e => e.PersonnelNumber == "T-01");
        Check("Inventory transfer preserves archived employee and act links", !copiedPerson.Active &&
            copiedActs.Where(act => act.Operation != InventoryOperation.WriteOff).All(act => act.EmployeeId == copiedPerson.Id));
        var copiedLaptop = copiedAssets.Single(asset => asset.InventoryNumber == "PC-001");
        var copiedIssue = copiedActs.Single(act => act.Operation == InventoryOperation.Issue);
        var copiedReturn = copiedActs.Single(act => act.Operation == InventoryOperation.Return);
        var issuedLaptop = copiedLines.Single(line => line.ActId == copiedIssue.Id && line.AssetId == copiedLaptop.Id);
        var returnedLaptop = copiedLines.Single(line => line.ActId == copiedReturn.Id && line.AssetId == copiedLaptop.Id);
        Check("Inventory transfer preserves original CPU snapshot", issuedLaptop.Specifications.Contains("AMD Ryzen 5 5600"));
        Check("Inventory transfer preserves later CPU snapshot", returnedLaptop.Specifications.Contains("Intel Core i5-12400"));
        Check("Portable act preserves price snapshot", issuedLaptop.ValueKopecks == 6737500 && returnedLaptop.ValueKopecks == 9999900);
        Check("Inventory transfer preserves final asset state", copiedLaptop.State == AssetState.WrittenOff &&
            copiedLaptop.EmployeeId == null && copiedAssets.Single(asset => asset.InventoryNumber == "MON-001").State == AssetState.Stock);
    }
    var upgrade = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "upgrade.db") }); await upgrade.InitializeAsync();
    await using (var db = upgrade.Create())
    {
        db.Devices.Add(new Device { Name = "Keep me" }); var common = await db.Profiles.SingleAsync(p => p.Provider == "Любой"); common.Critical = "мой словарь";
        (await db.Schema.SingleAsync()).Version = 1; await db.SaveChangesAsync();
        // Fixed test-only table names, never external input.
#pragma warning disable EF1003
        foreach (var table in new[] { "InventoryActLine", "InventoryAct", "InventoryAsset", "Employee", "RetiredMailKey" }) await db.Database.ExecuteSqlRawAsync("DROP TABLE sa2_" + table);
#pragma warning restore EF1003
    }
    await upgrade.InitializeAsync(); await upgrade.InitializeAsync();
    await using (var db = upgrade.Create()) Check("V1 additive upgrade keeps data and custom words", (await db.Schema.SingleAsync()).Version == 6 && (await db.Devices.SingleAsync()).Name == "Keep me" && (await db.Profiles.SingleAsync(p => p.Provider == "Любой")).Critical == "мой словарь" && !await db.Assets.AnyAsync());
    var upgradeV2 = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "upgrade-v2.db") }); await upgradeV2.InitializeAsync();
    await using (var db = upgradeV2.Create())
    {
        db.Assets.Add(new InventoryAsset { InventoryNumber = "V2-KEPT", Category = "Монитор" }); (await db.Schema.SingleAsync()).Version = 2; await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE sa2_RetiredMailKey");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_InventoryAsset DROP COLUMN ValueKopecks");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_InventoryActLine DROP COLUMN ValueKopecks");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_InventoryAct DROP COLUMN RepresentativePosition");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_InventoryAct DROP COLUMN DismissalDate");
    }
    await upgradeV2.InitializeAsync(); await upgradeV2.InitializeAsync();
    await using (var db = upgradeV2.Create()) Check("V2 additive template upgrade preserves asset and unknown price", (await db.Schema.SingleAsync()).Version == 6 && (await db.Assets.SingleAsync()).InventoryNumber == "V2-KEPT" && (await db.Assets.SingleAsync()).ValueKopecks == null);
    await using (var db = upgradeV2.Create())
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_InventoryAsset DROP COLUMN WorkstationId");
    await upgradeV2.InitializeAsync();
    var stationLink = Guid.NewGuid();
    InventoryAsset linkedAsset;
    await using (var db = upgradeV2.Create()) linkedAsset = await db.Assets.AsNoTracking().SingleAsync();
    linkedAsset.WorkstationId = stationLink;
    var linkedInventory = new InventoryService(upgradeV2);
    await linkedInventory.SaveAssetAsync(linkedAsset);
    bool duplicateStationLink = false;
    try { await linkedInventory.SaveAssetAsync(new InventoryAsset { InventoryNumber = "V2-DUP", WorkstationId = stationLink }); }
    catch (InvalidOperationException) { duplicateStationLink = true; }
    await upgradeV2.InitializeAsync();
    await using (var db = upgradeV2.Create())
        Check("Inventory monitoring migration preserves assets and blocks duplicate active station links",
            duplicateStationLink && (await db.Assets.SingleAsync()).WorkstationId == stationLink &&
            (await db.Assets.SingleAsync()).InventoryNumber == "V2-KEPT");
    Check("SQL Server template migration columns", InventorySchema.ActTemplateStatements(true).Count == 4 && InventorySchema.ActTemplateStatements(true).Any(x => x.Contains("datetime2")));
    var invalidLink = new PortableArchive { Devices = [new Device { Id = 1, Name = "Rollback" }], Jobs = [new SqlBackupJob { Id = 1, DeviceId = 999 }] };
    var invalidLinkPath = Path.Combine(folder, "invalid-link.json"); File.WriteAllText(invalidLinkPath, System.Text.Json.JsonSerializer.Serialize(invalidLink));
    var modelFactory = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "model-migration.db") });
    await modelFactory.InitializeAsync();
    await using (var modelDb = modelFactory.Create())
    {
        modelDb.Assets.Add(new InventoryAsset { InventoryNumber = "PRESERVE", Notes = "Existing asset" });
        await modelDb.SaveChangesAsync();
        await modelDb.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_InventoryAsset DROP COLUMN Manufacturer;");
        await modelDb.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_InventoryAsset DROP COLUMN Model;");
    }
    await modelFactory.InitializeAsync();
    await modelFactory.InitializeAsync();
    await using (var modelDb = modelFactory.Create())
    {
        var asset = await modelDb.Assets.SingleAsync();
        Check("Inventory model migration preserves existing asset and initializes missing fields",
            asset.InventoryNumber == "PRESERVE" && asset.Notes == "Existing asset" && asset.Manufacturer == "" && asset.Model == "");
        asset.Manufacturer = "Example"; asset.Model = "Desk-01";
        await modelDb.SaveChangesAsync();
    }
    await modelFactory.InitializeAsync();
    await using (var modelDb = modelFactory.Create())
        Check("Repeated inventory migration preserves model edits",
            await modelDb.Assets.AnyAsync(a => a.Manufacturer == "Example" && a.Model == "Desk-01"));

    var linkFactory = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "invalid-link.db") }); await linkFactory.InitializeAsync();
    bool badLink = false; try { await new DatabaseTransfer(linkFactory).ImportAsync(invalidLinkPath); } catch (InvalidDataException) { badLink = true; }
    await using (var db = linkFactory.Create()) Check("Missing optional reference is rejected atomically", badLink && !await db.Devices.AnyAsync());
    var invalidEnum = new PortableArchive { Payments = [new Payment { Id = 1, Repeat = (RepeatPeriod)99 }] };
    File.WriteAllText(invalidLinkPath, System.Text.Json.JsonSerializer.Serialize(invalidEnum)); bool badEnum = false;
    try { await new DatabaseTransfer(linkFactory).ImportAsync(invalidLinkPath); } catch (InvalidDataException) { badEnum = true; }
    Check("Unknown archive enum rejected", badEnum);
    var diagnosticMail = report with { Key = "diagnostic-uid", Subject = "[Warning] Veeam", Body = "Host: NAS1\nStatus: Warning\n" + configWarnings };
    Check("Diagnostic mail intake", await store.IngestAsync(diagnosticMail));
    await using (var db = factory.Create())
    {
        var diagnosed = await db.Reports.SingleAsync(x => x.Description.Contains("Encrypt configuration backup"));
        Check("Advice archived before raw body deletion", !diagnosed.IsPending && !await db.Pending.AnyAsync(x => x.EventId == diagnosed.Id) && diagnosed.Description.Contains("Skipping credentials") && diagnosed.Result == BackupResult.Warning);
    }
    await using (var db = factory.Create()) { var nas = await db.Devices.SingleAsync(d => d.Id == 1); nas.Enabled = false; await db.SaveChangesAsync(); var lamps=await BackupQueries.LampsAsync(db,DateTime.Now);var jobs=await db.Jobs.AsNoTracking().ToArrayAsync();Check("Archived device creates no implicit backup but explicit jobs remain",lamps.All(l=>l.JobId!=null)&&lamps.Count==jobs.Sum(j=>BackupSchedule.Deadlines(j).Length)); }
    var parallelMail = report with { Key = "parallel-uid" };
    var intakeBefore = 0; await using (var db = factory.Create()) intakeBefore = await db.Reports.CountAsync();
    var intakeResults = await Task.WhenAll(Task.Run(() => store.IngestAsync(parallelMail)), Task.Run(() => store.IngestAsync(parallelMail)));
    await using (var db = factory.Create()) Check("Concurrent intake stores exactly once", intakeResults.Count(x => x) == 1 && await db.Reports.CountAsync() == intakeBefore + 1);
    bool sqlPasswordRejected = false;
    try { using var forbidden = new DatabaseFactory(new AppSettings { DatabaseProvider = DatabaseProvider.SqlServer, SqlConnectionString = "Server=.;Database=test;User ID=test;Password=test-only" }).Create(); } catch (InvalidOperationException) { sqlPasswordRejected = true; }
    Check("Plaintext SQL password rejected outside UI", sqlPasswordRejected);
    var sqlFactory = new DatabaseFactory(new AppSettings { DatabaseProvider = DatabaseProvider.SqlServer }); await using var sqlDb = sqlFactory.Create();
    var sqlSchema = sqlDb.Database.GenerateCreateScript(); Check("SQL Server inventory upgrade DDL", InventorySchema.Statements(sqlDb).Count(x => x.StartsWith("CREATE TABLE")) == 4);
    Check("SQL Server schema generation", sqlSchema.Contains("sa2_ReportEvent") && sqlSchema.Contains("IDENTITY"));
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
Console.WriteLine($"PASS {passed.Count} specifications");

sealed record DiagnosticFixture(string Name, string Provider, string Body, string[] Expected);
