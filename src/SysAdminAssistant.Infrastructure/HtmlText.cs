using System.Net;
using System.Text.RegularExpressions;
namespace SysAdminAssistant.Infrastructure;
public static class HtmlText
{
    public static string Convert(string html)
    {
        var timeout = TimeSpan.FromSeconds(2);
        html = Regex.Replace(html, @"<(script|style)\b[^>]*>.*?</\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, timeout);
        // Report tables encode a label/value pair as two adjacent cells.
        // Keep each row together instead of splitting the label from its value.
        html = Regex.Replace(html, @"<tr\b[^>]*>(.*?)</tr\s*>", row =>
        {
            var cells = Regex.Matches(row.Groups[1].Value, @"<t[dh]\b[^>]*>(.*?)</t[dh]\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, timeout)
                .Select(cell => Regex.Replace(Regex.Replace(cell.Groups[1].Value, @"<[^>]*>", " ", RegexOptions.Singleline, timeout), @"\s+", " ", RegexOptions.None, timeout).Trim()).ToArray();
            if (cells.Length == 0) return row.Value;
            if (cells.Length == 2 && cells[0].Length > 0 && cells[1].Length > 0)
                return "\n" + cells[0].TrimEnd(':', '：') + ": " + cells[1] + "\n";
            return "\n" + string.Join(" | ", cells.Where(x => x.Length > 0)) + "\n";
        }, RegexOptions.IgnoreCase | RegexOptions.Singleline, timeout);
        html = Regex.Replace(html, @"</?(?:br|p|div|tr|h[1-6]|li)\b[^>]*>", "\n", RegexOptions.IgnoreCase, timeout);
        html = Regex.Replace(html, @"</t[dh]\s*>", "\n", RegexOptions.IgnoreCase, timeout);
        html = Regex.Replace(html, @"<[^>]*>", "", RegexOptions.Singleline, timeout);
        var text = WebUtility.HtmlDecode(html).Replace('\u00a0', ' ');
        return string.Join("\n", text.Replace("\r", "").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
    }
}
