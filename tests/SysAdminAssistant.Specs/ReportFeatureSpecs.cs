using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

internal static class ReportFeatureSpecs
{
    public static async Task Run(string folder, Action<string, bool> check)
    {
        var sender = InternetAddressList.Parse("Reporter <backup@EXAMPLE.test>");
        var admission = new MailSettings { Purpose = MailboxPurpose.Reports };
        check("Empty report domain list blocks mail", !MailAdmission.Allows(admission, sender, "Success"));
        admission.AllowedDomains.Add("example.test");
        check("Report domain match ignores case and display name", MailAdmission.Allows(admission, sender, "Success"));
        check("DMARC cannot enter through report mailbox", !MailAdmission.Allows(admission, sender, "DMARC report"));
        check("Subdomain requires explicit permission", !MailAdmission.Allows(admission, InternetAddressList.Parse("backup@sub.example.test"), "Success"));
        check("Suffix lookalike domain rejected", !MailAdmission.Allows(admission, InternetAddressList.Parse("backup@example.test.attacker.test"), "Success"));
        check("Display name cannot grant permission", !MailAdmission.Allows(admission, InternetAddressList.Parse("\"backup@example.test\" <evil@attacker.test>"), "Success"));
        check("Multiple From addresses rejected", !MailAdmission.Allows(admission, InternetAddressList.Parse("backup@example.test, evil@attacker.test"), "Success"));
        admission.Purpose = MailboxPurpose.Dmarc;
        check("DMARC domain permission is insufficient", !MailAdmission.Allows(admission, sender, "DMARC report"));
        admission.TrustedDmarcSenders.Add("backup@example.test");
        check("Approved DMARC sender with mixed-case subject accepted", MailAdmission.Allows(admission, sender, "[dMaRc] Aggregate report"));
        check("Approved sender without DMARC subject rejected", !MailAdmission.Allows(admission, sender, "Success"));
        check("DMARC must be a word", !MailAdmission.Allows(admission, sender, "notdmarc"));
        check("DMARC approval is for exact mailbox", !MailAdmission.Allows(admission, InternetAddressList.Parse("other@example.test"), "DMARC"));
        check("Domain list normalized", MailAdmission.ParseList("EXAMPLE.test; other.test\nexample.test", false).SequenceEqual(new[] { "example.test", "other.test" }));
        var markerMessage = new MimeMessage { Body = new TextPart("plain") { Text = DmarcParser.Marker + "<feedback/>" } };
        check("Reports cannot inject DMARC routing marker", !MailPayloadReader.Read(markerMessage, "a", "k").Single().Body.StartsWith(DmarcParser.Marker, StringComparison.Ordinal));
        var legacy = new AppSettings { Mail = new MailSettings { Host = "imap.example.test", User = "reports", Enabled = true, ProtectedPassword = "existing-cipher" } };
        legacy.NormalizeMailboxes(); legacy.NormalizeMailboxes();
        check("Legacy IMAP migration keeps one account and cipher", legacy.Mailboxes.Count == 1 && legacy.Mailboxes[0].ProtectedPassword == "existing-cipher" && legacy.Mail.Host == "");
        legacy.Mailboxes.Clear(); legacy.NormalizeMailboxes(); check("Deleted legacy mailbox cannot return", legacy.Mailboxes.Count == 0);
        check("Retention defaults to unlimited", new AppSettings().EventRetentionDays == 0);
        var custom = new CustomDiagnostic { Provider = "Veeam", Title = "Own rule", Phrase = "Vendor code XYZ", Meaning = "Known issue", Steps = "Read log\nCheck host" };
        check("Custom article analysis", custom.Validate() == null && DiagnosticGuide.Analyze("Veeam", "Warning Vendor code XYZ", [custom]).Findings.Single().Entry.IsCustom);
        check("Custom article provider isolation", DiagnosticGuide.Analyze("QNAP", "Warning Vendor code XYZ", [custom]).Findings.Length == 0);
        custom.Provider = "Любой"; check("Generic custom article matches an unknown source", DiagnosticGuide.Analyze("Неизвестно", "Warning Vendor code XYZ", [custom]).Findings.Length == 1); custom.Provider = "Veeam";
        custom.Enabled = false; check("Disabled article ignored", DiagnosticGuide.Analyze("Veeam", "Warning Vendor code XYZ", [custom]).Findings.Length == 0);
        var xml = Xml("one", "fail", "pass", "pass"); var parsed = DmarcParser.Parse(xml);
        check("DMARC either aligned mechanism passes", parsed.Total == 8 && parsed.Passed == 8 && parsed.Failed == 0);
        var foreign = DmarcParser.Parse(Xml("two", "fail", "fail", "pass"));
        check("Raw foreign SPF pass does not imply DMARC pass", foreign.Failed == 8 && foreign.Format().Contains("Возможна подделка"));
        check("DMARC raw permerror translated", DmarcParser.Parse(Xml("perm", "fail", "fail", "permerror")).Format().Contains("постоянная ошибка проверки"));
        check("Namespace-aware RFC9990 reports", DmarcParser.Parse(xml.Replace("<feedback>", "<feedback xmlns=\"urn:ietf:params:xml:ns:dmarc-2.0\">")).Total == 8);
        bool dtd = false; try { DmarcParser.Parse("<!DOCTYPE feedback [<!ENTITY a SYSTEM 'file:///never-read'>]>" + xml); } catch (System.Xml.XmlException) { dtd = true; } check("DMARC DTD rejected", dtd);
        bool deep = false; try { DmarcParser.Parse(string.Concat(Enumerable.Repeat("<x>", 70)) + string.Concat(Enumerable.Repeat("</x>", 70))); } catch (InvalidDataException) { deep = true; } check("DMARC excessive XML depth rejected", deep);
        bool invalidCount = false; try { DmarcParser.Parse(xml.Replace("<count>8</count>", "<count>-8</count>")); } catch (InvalidDataException) { invalidCount = true; } check("DMARC negative count rejected", invalidCount);
        foreach (var compression in new[] { "xml", "gz", "zip" })
        {
            using var data = new MemoryStream();
            if (compression == "gz") { using (var gzip = new GZipStream(data, CompressionMode.Compress, true)) gzip.Write(Encoding.UTF8.GetBytes(xml)); }
            else if (compression == "zip") { using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true)) { using var entry = zip.CreateEntry("report.xml").Open(); entry.Write(Encoding.UTF8.GetBytes(xml)); } }
            else data.Write(Encoding.UTF8.GetBytes(xml));
            data.Position = 0;
            var mime = new MimeMessage { Subject = "DMARC", Body = new MimePart("application", "octet-stream") { FileName = "report." + compression, Content = new MimeContent(data) } };
            var payload = MailPayloadReader.Read(mime, "account", "uid", true);
            check("DMARC attachment " + compression, payload.Count == 1 && DmarcParser.Parse(payload[0].Body[DmarcParser.Marker.Length..]).Total == 8);
        }
        using (var broken = new MemoryStream([1, 2, 3]))
        {
            var mime = new MimeMessage { Body = new MimePart("application", "gzip") { FileName = "bad.gz", Content = new MimeContent(broken) } };
            check("Reports never inspect malformed attachments by default", !MailPayloadReader.ReadSafe(mime, "a", "k").Single().Body.StartsWith(DmarcParser.ErrorMarker));
            check("Bad compressed report remains reviewable", MailPayloadReader.ReadSafe(mime, "a", "k", true).Single().Body.StartsWith(DmarcParser.ErrorMarker));
        }
        foreach (var name in new[] { "../report.xml", "report.xml" })
        {
            using var data = new MemoryStream();
            using (var unsafeZip = new ZipArchive(data, ZipArchiveMode.Create, true))
            {
                using var stream = unsafeZip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(name.StartsWith("..") ? xml : new string('x', 1_000_000)));
            }
            data.Position = 0;
            var mime = new MimeMessage { Body = new MimePart("application", "zip") { FileName = "report.zip", Content = new MimeContent(data) } };
            check("Unsafe ZIP rejected: " + name, MailPayloadReader.ReadSafe(mime, "a", "k", true).Single().Body.StartsWith(DmarcParser.ErrorMarker));
        }
        using (var data = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE feedback [<!ENTITY a SYSTEM 'file:///never-read'>]>" + xml)))
        {
            var mime = new MimeMessage { Body = new MimePart("application", "xml") { FileName = "report.xml", Content = new MimeContent(data) } };
            check("Attachment external XML entity rejected safely", MailPayloadReader.ReadSafe(mime, "a", "k", true).Single().Body.StartsWith(DmarcParser.ErrorMarker));
        }
        var factory = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "report-features.db") }); await factory.InitializeAsync();
        var store = new ReportStore(factory);
        var mail = new MailReport("first-box", "uid", "", "DMARC", DmarcParser.Marker + xml, null);
        check("DMARC normalized without device binding", await store.IngestAsync(mail));
        check("DMARC across-mailbox deduplication", !await store.IngestAsync(mail with { Account = "second-box", Key = "other-uid" }));
        await using (var db = factory.Create()) check("DMARC normalized readable record", (await db.Reports.SingleAsync()).Provider == "DMARC" && !await db.Pending.AnyAsync());
        var multiple = new[] { mail with { Key = "part-a", Body = DmarcParser.Marker + Xml("three", "pass", "fail", "pass") }, mail with { Key = "part-b", Body = DmarcParser.Marker + Xml("four", "fail", "fail", "fail") } };
        check("Multiple DMARC attachments committed together", await store.IngestMessageAsync(multiple, "first-box", "batch") == 2 && await store.IngestMessageAsync(multiple, "first-box", "batch") == 0);
        var now = new DateTime(2026, 10, 9, 12, 0, 0);
        await using (var db = factory.Create())
        {
            foreach (var report in await db.Reports.ToArrayAsync()) report.ReceivedAt = now.AddDays(-91);
            foreach (var incident in await db.Incidents.ToArrayAsync()) incident.CreatedAt = now.AddDays(-91);
            db.Devices.Add(new Device { Name = "Keep inventory" }); await db.SaveChangesAsync();
        }
        var preview = await EventRetention.PreviewAsync(factory, 90, now); check("Retention previews old records", preview.Reports == 3);
        check("Unlimited retention deletes nothing", (await EventRetention.ApplyAsync(factory, 0, now)).Reports == 0);
        check("Retention actually removes reports", (await EventRetention.ApplyAsync(factory, 90, now)).Reports == 3);
        check("Deleted DMARC cannot return from another mailbox", !await store.IngestAsync(mail with { Account = "third-box", Key = "third-uid" }));
        await using (var db = factory.Create()) check("Retention clears incidents and preserves devices/hash receipts", !await db.Incidents.AnyAsync() && !await db.Reports.AnyAsync() && await db.RetiredKeys.AnyAsync() && await db.Devices.CountAsync() == 1);
        var pending = new MailReport("pending", "uid", "", "[Success] Veeam", "Status: Success", null); await store.IngestAsync(pending);
        await using (var db = factory.Create()) { var record = await db.Reports.SingleAsync(); record.ReceivedAt = now.AddDays(-31); await db.SaveChangesAsync(); }
        check("30-day retention deletes pending body", (await EventRetention.ApplyAsync(factory, 30, now)).Reports == 1);
        await using (var db = factory.Create()) check("Pending raw message cascades away", !await db.Pending.AnyAsync());
        var archive = Path.Combine(folder, "retention-export.json"); await new DatabaseTransfer(factory).ExportAsync(archive);
        var copy = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "retention-copy.db") }); await copy.InitializeAsync(); await new DatabaseTransfer(copy).ImportAsync(archive);
        check("Portable archive preserves deletion receipts", !await new ReportStore(copy).IngestAsync(pending));
        var migration = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "upgrade-v3.db") }); await migration.InitializeAsync();
        await using (var db = migration.Create()) { (await db.Schema.SingleAsync()).Version = 3; db.Devices.Add(new Device { Name = "V3 kept" }); await db.SaveChangesAsync(); await db.Database.ExecuteSqlRawAsync("DROP TABLE sa2_RetiredMailKey"); }
        await migration.InitializeAsync(); await migration.InitializeAsync();
        await using (var db = migration.Create()) check("Schema 3-to-6 upgrade preserves existing data", (await db.Schema.SingleAsync()).Version == 6 && (await db.Devices.SingleAsync()).Name == "V3 kept" && !await db.RetiredKeys.AnyAsync());
        var v4 = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "upgrade-v4.db") }); await v4.InitializeAsync();
        await using (var db = v4.Create())
        {
            (await db.Schema.SingleAsync()).Version = 4;
            (await db.Profiles.SingleAsync(x => x.Provider == "Любой")).Critical = "keep custom words";
            await db.SaveChangesAsync();
            // Fixed test-only column names, never external input.
#pragma warning disable EF1003
            foreach (var column in new[] { "SenderTerms", "SubjectTerms", "BodyTerms", "MatchAllSourceFields", "ResultScope" }) await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_KeywordProfile DROP COLUMN " + column);
#pragma warning restore EF1003
        }
        await v4.InitializeAsync(); await v4.InitializeAsync();
        await using (var db = v4.Create())
        {
            var profile = await db.Profiles.SingleAsync(x => x.Provider == "Любой");
            check("V4 source migration sets schema version", (await db.Schema.SingleAsync()).Version == 6);
            check("V4 source migration preserves custom dictionary", profile.Critical == "keep custom words");
            check("V4 source migration initializes all-fields match", profile.MatchAllSourceFields);
            check("V4 source migration initializes empty source filters", profile.SenderTerms == "" && profile.SubjectTerms == "" && profile.BodyTerms == "");
            check("V4 source migration initializes subject result scope; actual: " + profile.ResultScope, profile.ResultScope == SearchScope.Subject);
            db.Profiles.Add(new KeywordProfile { Provider = "ExampleBackup", SenderTerms = "reports@example.invalid", SuccessResults = "done" });
            await db.SaveChangesAsync();
        }
        await using (var db = v4.Create())
        {
            (await db.Profiles.SingleAsync(x => x.Provider == "Любой")).ResultScope = SearchScope.Both;
            (await db.Profiles.SingleAsync(x => x.Provider == "ExampleBackup")).ResultScope = SearchScope.Body;
            await db.SaveChangesAsync();
        }
        await v4.InitializeAsync(); await v4.InitializeAsync();
        await using (var db = v4.Create())
        {
            check("Repeated initialization preserves explicit Both scope", (await db.Profiles.SingleAsync(x => x.Provider == "Любой")).ResultScope == SearchScope.Both);
            check("Repeated initialization preserves explicit Body scope", (await db.Profiles.SingleAsync(x => x.Provider == "ExampleBackup")).ResultScope == SearchScope.Body);
        }
        var sourceArchive = Path.Combine(folder, "source-export.json"); await new DatabaseTransfer(v4).ExportAsync(sourceArchive);
        var sourceCopy = new DatabaseFactory(new AppSettings { SqlitePath = Path.Combine(folder, "source-copy.db") }); await sourceCopy.InitializeAsync(); await new DatabaseTransfer(sourceCopy).ImportAsync(sourceArchive);
        await using (var db = sourceCopy.Create()) check("Portable custom source preserves rules and dictionary", (await db.Profiles.SingleAsync(x => x.Provider == "ExampleBackup")).SenderTerms == "reports@example.invalid" && (await db.Profiles.SingleAsync(x => x.Provider == "ExampleBackup")).SuccessResults == "done");
        var ordinary=new DatabaseFactory(new AppSettings {SqlitePath=Path.Combine(folder,"ordinary-reports.db")});await ordinary.InitializeAsync();
        var ordinaryStore=new ReportStore(ordinary);
        await ordinaryStore.IngestAsync(new("ordinary","uid-1","backup@example.test","Daily custom notice","Unrecognized payload",null));
        await ordinaryStore.IngestAsync(new("ordinary","uid-2","backup@example.test","[Success] Veeam","Status: Success",null));
        await using(var db=ordinary.Create()){
            var received=await db.Reports.OrderBy(r=>r.Id).ToArrayAsync();
            check("Unrecognized mail remains queued and recognized Veeam mail needs no job",received.Length==2 && received[0].IsPending && !received[1].IsPending && received[1].JobId==null && await db.Pending.CountAsync()==1 && !await db.Jobs.AnyAsync());
        }
    }
    private static string Xml(string id, string spf, string dkim, string rawSpf) =>
        $"<feedback><report_metadata><org_name>Example Receiver</org_name><report_id>{id}</report_id><date_range><begin>1791504000</begin><end>1791590400</end></date_range></report_metadata><policy_published><domain>example.test</domain><p>none</p></policy_published><record><row><source_ip>192.0.2.10</source_ip><count>8</count><policy_evaluated><disposition>none</disposition><dkim>{dkim}</dkim><spf>{spf}</spf></policy_evaluated></row><identifiers><header_from>example.test</header_from></identifiers><auth_results><spf><domain>foreign.test</domain><result>{rawSpf}</result></spf></auth_results></record></feedback>";
}
