using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SysAdminAssistant.Core;

public sealed record DmarcRow(string Ip, long Count, string Spf, string Dkim, string Disposition, string HeaderFrom, string AuthDetails, string Reasons)
{
    public bool Passed => Spf == "pass" || Dkim == "pass";
}
public sealed record DmarcSummary(string Domain, string Reporter, string ReportId, DateTime BeginUtc, DateTime EndUtc, string Policy, DmarcRow[] Rows)
{
    public long Total => Rows.Aggregate(0L, (n, x) => checked(n + x.Count));
    public long Failed => Rows.Where(x => !x.Passed).Aggregate(0L, (n, x) => checked(n + x.Count));
    public long Passed => Total - Failed;
    public string Identity => string.Join("\0", "DMARC", Domain.ToLowerInvariant(), Reporter.Trim().ToLowerInvariant(), ReportId, BeginUtc.Ticks.ToString(CultureInfo.InvariantCulture), EndUtc.Ticks.ToString(CultureInfo.InvariantCulture));
    public string Format()
    {
        var text = new StringBuilder();
        text.AppendLine($"DMARC · {Domain}\nСоставитель отчёта: {Reporter}\nID: {ReportId}\nПериод UTC: {BeginUtc:dd.MM.yyyy HH:mm} — {EndUtc:dd.MM.yyyy HH:mm}\nПолитика домена: {Policy}\nВсего писем: {Total}; прошли DMARC: {Passed}; не прошли: {Failed}.");
        text.AppendLine(Failed > 0 ? "Есть сообщения без согласованного SPF или DKIM. Возможна подделка адреса, пересылка либо ошибка настройки законного отправителя. Сначала сверяй IP с почтовыми сервисами домена." : "Для указанных потоков получатель сообщил об успешном DMARC. Это не оценка содержимого писем и не гарантия отсутствия спама.");
        if (Policy == "none") text.AppendLine("Политика none запрашивает наблюдение без требования блокировки по DMARC. После проверки законных отправителей можно планировать quarantine/reject; программа DNS не меняет.");
        text.AppendLine("Статусы ниже учитывают совпадение доменов SPF/DKIM с From. Достаточно одного согласованного успешного механизма. Успешная подпись чужого домена сама по себе не даёт DMARC pass.");
        foreach (var row in Rows)
        {
            text.AppendLine($"\nIP: {row.Ip} · писем: {row.Count} · From: {row.HeaderFrom}\nDMARC: {(row.Passed ? "пройден" : "не пройден")}; SPF: {Aligned(row.Spf)}; DKIM: {Aligned(row.Dkim)}; действие: {Disposition(row.Disposition)}.");
            if (row.AuthDetails.Length > 0) text.AppendLine("Исходные проверки: " + row.AuthDetails);
            if (row.Reasons.Length > 0) text.AppendLine("Исключения получателя: " + row.Reasons);
            if (!row.Passed) text.AppendLine("Проверка: если IP принадлежит вашему отправителю — проверьте SPF, подпись DKIM и домен From. Для пересылки проверьте сохранение DKIM и причины исключений. Не добавляйте неизвестный IP в SPF только ради зелёного статуса.");
        }
        text.AppendLine("\nЭто агрегированная статистика получателя, а не письма пользователей. Подлинность отправителя XML отдельно не установлена. Сведения из отчёта нельзя считать доказательством атаки без проверки источника.");
        text.AppendLine("Официальная статья: https://www.rfc-editor.org/rfc/rfc9990.html");
        return text.ToString();
    }
    private static string Aligned(string value) => value == "pass" ? "пройден и согласован с From" : "не пройден или не согласован с From";
    public static string Disposition(string value) => value switch { "reject" => "отклонение по отчёту", "quarantine" => "карантин по отчёту", "pass" => "проверка пройдена", _ => "без действия DMARC (не означает доставку)" };
}
public static class DmarcParser
{
    public const string ErrorMarker = "SysAdminAssistant.DMARC.ERROR\n";
    public const string Marker = "SysAdminAssistant.DMARC.XML\n";
    public const int MaxXmlBytes = 4_000_000;
    public static DmarcSummary Parse(string xml)
    {
        if (xml.Length > MaxXmlBytes) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: XML слишком большой"));
        var limits = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXmlBytes, MaxCharactersFromEntities = 0 };
        using (var scanInput = new StringReader(xml))
        using (var scan = XmlReader.Create(scanInput, limits))
        {
            var nodes = 0;
            while (scan.Read())
                if (scan.Depth > 64 || ++nodes > 150000) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: превышена глубина или сложность XML"));
        }
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, limits);
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: отсутствует XML"));
        if (root.Name.LocalName != "feedback" || root.Name.NamespaceName is not ("" or "urn:ietf:params:xml:ns:dmarc" or "urn:ietf:params:xml:ns:dmarc-2.0" or "http://dmarc.org/dmarc-xml/0.1")) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: неизвестный формат XML"));
        var ns = root.Name.Namespace;
        XElement Child(XElement parent, string name)
        {
            var children = parent.Elements(ns + name).Take(2).ToArray();
            if (children.Length != 1) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: отсутствует или повторяется ") + name);
            return children[0];
        }
        string Text(XElement parent, string name) { var s = Child(parent, name).Value.Trim(); if (s.Length == 0 || s.Length > 2000) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: неверное поле ") + name); return s; }
        string Token(XElement parent, string name, params string[] values) { var s = Text(parent, name).ToLowerInvariant(); if (!values.Contains(s)) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: неизвестное значение ") + name); return s; }
        DateTime Epoch(XElement parent, string name) { if (!long.TryParse(Text(parent, name), NumberStyles.None, CultureInfo.InvariantCulture, out var n)) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: неверный период")); try { return DateTimeOffset.FromUnixTimeSeconds(n).UtcDateTime; } catch (ArgumentOutOfRangeException) { throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: неверный период")); } }
        var metadata = Child(root, "report_metadata"); var range = Child(metadata, "date_range");
        var begin = Epoch(range, "begin"); var end = Epoch(range, "end"); if (end < begin) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: конец периода раньше начала"));
        var policy = Child(root, "policy_published"); var domain = Text(policy, "domain");
        if (Uri.CheckHostName(domain) != UriHostNameType.Dns) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: неверный домен"));
        var rows = new List<DmarcRow>();
        foreach (var record in root.Elements(ns + "record"))
        {
            if (rows.Count == 10000) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: слишком много строк"));
            var row = Child(record, "row"); var evaluated = Child(row, "policy_evaluated"); var ip = Text(row, "source_ip");
            if (!IPAddress.TryParse(ip, out _)) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: неверный IP"));
            if (!long.TryParse(Text(row, "count"), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0 || count > 1_000_000_000_000) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: неверное количество писем"));
            var auth = record.Element(ns + "auth_results");
            var authText = auth == null ? "" : string.Join("; ", auth.Elements().Where(x => x.Name.Namespace == ns && x.Name.LocalName is "spf" or "dkim").Take(20).Select(x => x.Name.LocalName + " " + (x.Element(ns + "domain")?.Value ?? "") + ": " + AuthResult(x.Element(ns + "result")?.Value ?? "") + (x.Element(ns + "selector") is XElement selector ? " (selector " + selector.Value + ")" : "")));
            var reasons = string.Join("; ", evaluated.Elements(ns + "reason").Take(20).Select(x => Reason(x.Element(ns + "type")?.Value ?? "other") + (x.Element(ns + "comment") is XElement comment ? ": " + comment.Value[..Math.Min(500, comment.Value.Length)] : "")));
            rows.Add(new(ip, count, Token(evaluated, "spf", "pass", "fail"), Token(evaluated, "dkim", "pass", "fail"), Token(evaluated, "disposition", "none", "quarantine", "reject", "pass"), Text(Child(record, "identifiers"), "header_from"), authText, reasons));
        }
        if (rows.Count == 0) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC: отсутствуют строки статистики"));
        var report = new DmarcSummary(domain, Text(metadata, "org_name"), Text(metadata, "report_id"), begin, end, Token(policy, "p", "none", "quarantine", "reject"), rows.ToArray());
        _ = report.Total; return report;
    }
    private static string AuthResult(string value) => value.ToLowerInvariant() switch
    {
        "pass" => "успешно (pass)", "fail" => "не пройдено (fail)", "softfail" => "мягкий отказ SPF (softfail)",
        "neutral" => "домен не дал положительного или отрицательного ответа (neutral)", "none" => "результат отсутствует (none)",
        "temperror" => "временная ошибка проверки, например DNS; проверьте доступность и повторяемость (temperror)",
        "permerror" => "постоянная ошибка проверки; проверьте корректность записей/параметров, для SPF также лимиты DNS-запросов (permerror)",
        "policy" => "результат определён политикой получателя (policy)", _ => "неизвестный результат: " + value
    };
    private static string Reason(string value) => value switch { "forwarded" => "пересылка", "trusted_forwarder" => "доверенный пересыльщик", "mailing_list" => "список рассылки", "local_policy" => "локальное правило получателя", "sampled_out" => "исключено выборкой", _ => "другое: " + value };
}
