using System.IO.Compression;
using System.Text;
using MimeKit;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Infrastructure;

public static class MailPayloadReader
{
    public const int MaxMessageBytes = 16_000_000;
    public const string ErrorMarker = "SysAdminAssistant.MAIL.ERROR\n";
    public static bool IsDmarcPart(string name, string type) => name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || type is "text/xml" or "application/xml" or "application/gzip" or "application/zip";
    public static IReadOnlyList<MailReport> ReadSafe(MimeMessage message, string account, string key, bool readDmarc = false)
    {
        try { return Read(message, account, key, readDmarc); }
        catch (Exception e) when (e is InvalidDataException or IOException or FormatException or System.Xml.XmlException)
        {
            return [new(account, key, message.From.ToString(), message.Subject ?? "", (readDmarc ? DmarcParser.ErrorMarker : ErrorMarker) + e.Message, message.Date == default ? null : AppTime.FromOffset(message.Date))];
        }
    }
    public static IReadOnlyList<MailReport> Read(MimeMessage message, string account, string key, bool readDmarc = false)
    {
        var date = message.Date == default ? (DateTime?)null : AppTime.FromOffset(message.Date);
        var result = new List<MailReport>(); int parts = 0, decodedBytes = 0;
        foreach (var part in readDmarc ? message.BodyParts.OfType<MimePart>() : Enumerable.Empty<MimePart>())
        {
            var name = part.FileName ?? ""; var type = part.ContentType.MimeType;
            if (!IsDmarcPart(name, type)) continue;
            if (++parts > 32) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком много XML/архивов в одном письме"));
            using var decoded = new LimitedMemoryStream(DmarcParser.MaxXmlBytes);
            if (part.Content == null) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Вложение не содержит данных"));
            part.Content.DecodeTo(decoded); decodedBytes = checked(decodedBytes + (int)decoded.Length);
            if (decodedBytes > 8_000_000) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой набор вложений"));
            decoded.Position = 0;
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || type == "application/zip")
            {
                if (decoded.Length < 4 || decoded.ReadByte() != 0x50 || decoded.ReadByte() != 0x4b || decoded.ReadByte() != 0x03 || decoded.ReadByte() != 0x04)
                    throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("ZIP: содержимое не соответствует формату"));
                decoded.Position = 0;
                using var archive = new ZipArchive(decoded, ZipArchiveMode.Read, true);
                if (archive.Entries.Count > 32) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком много файлов в ZIP"));
                foreach (var entry in archive.Entries.Where(x => x.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                {
                    // Never extract paths, recurse into archives or open attachments with the OS.
                    if (entry.FullName.Contains('\\') || entry.FullName.StartsWith('/') || entry.FullName.Split('/').Any(x => x is ".." or ".") || entry.FullName.Contains(':'))
                        throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("ZIP: недопустимое имя файла"));
                    if (entry.Length > DmarcParser.MaxXmlBytes || entry.Length > 200 * Math.Max(1L, entry.CompressedLength))
                        throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("ZIP: превышен размер или коэффициент распаковки"));
                    using var stream = entry.Open(); Add(stream);
                }
            }
            else if (name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || type == "application/gzip")
            {
                if (decoded.Length < 3 || decoded.ReadByte() != 0x1f || decoded.ReadByte() != 0x8b || decoded.ReadByte() != 8)
                    throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("GZIP: содержимое не соответствует формату"));
                decoded.Position = 0;
                using var gzip = new GZipStream(decoded, CompressionMode.Decompress, true); Add(gzip, Math.Min(DmarcParser.MaxXmlBytes, checked((int)Math.Min(int.MaxValue, decoded.Length * 200))));
            }
            else Add(decoded);
        }
        if (result.Count == 0)
        {
            var body = message.TextBody ?? HtmlText.Convert(message.HtmlBody ?? "");
            if (!readDmarc && (body.StartsWith(DmarcParser.Marker, StringComparison.Ordinal) || body.StartsWith(DmarcParser.ErrorMarker, StringComparison.Ordinal) || body.StartsWith(ErrorMarker, StringComparison.Ordinal))) body = "\u200b" + body;
            if (readDmarc && (body.TrimStart().StartsWith("<?xml", StringComparison.Ordinal) || body.TrimStart().StartsWith("<feedback", StringComparison.Ordinal))) body = DmarcParser.Marker + body;
            result.Add(new(account, key, message.From.ToString(), message.Subject ?? "", body[..Math.Min(body.StartsWith(DmarcParser.Marker, StringComparison.Ordinal) ? DmarcParser.MaxXmlBytes + DmarcParser.Marker.Length : 1_000_000, body.Length)], date, message.MessageId ?? ""));
        }
        return result;
        void Add(Stream stream, int limit = DmarcParser.MaxXmlBytes)
        {
            using var xml = new LimitedMemoryStream(limit); stream.CopyTo(xml);
            decodedBytes = checked(decodedBytes + (int)xml.Length);
            if (decodedBytes > 16_000_000 || result.Count >= 32) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Превышен общий лимит XML в письме"));
            xml.Position = 0;
            using var reader = new StreamReader(xml, Encoding.UTF8, true); var text = reader.ReadToEnd();
            // Only report-shaped XML is routed to DMARC. Invalid feedback remains reviewable.
            if (!text.Contains("feedback", StringComparison.Ordinal)) return;
            _ = DmarcParser.Parse(text);
            result.Add(new(account, key + ":xml:" + result.Count, message.From.ToString(), message.Subject ?? "", DmarcParser.Marker + text, date, message.MessageId ?? ""));
        }
    }
    private sealed class LimitedMemoryStream(int limit) : MemoryStream
    {
        private void Check(int count) { if (Position + count > limit) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Вложение превышает допустимый размер")); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
    }
}
