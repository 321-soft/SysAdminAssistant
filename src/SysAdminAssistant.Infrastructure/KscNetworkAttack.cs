using System.Net;
using System.Text.RegularExpressions;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Infrastructure;

internal static class KscNetworkAttack
{
    private static readonly Regex Ip = new(@"(?<![\w.:])(?:\d{1,3}\.){3}\d{1,3}(?![\w.])|(?<![\w.:])[0-9a-f]*:[0-9a-f:]+(?![\w.:])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static KscEvent Enrich(KscEvent row, string title, string description, string threat)
    {
        var text = $"{title} {description} {threat}".ToLowerInvariant();
        bool brute = text.Contains("brute force") || text.Contains("brute-force") || text.Contains("bruteforce") || text.Contains("брутфорс") ||
            text.Contains("перебор парол") || text.Contains("подбор парол") || text.Contains("грубой силы");
        bool rdp = brute && (text.Contains("rdp") || text.Contains("remote desktop") || text.Contains("удаленного рабочего стола") || text.Contains("удалённого рабочего стола"));
        var ipSource = $"{title} {description}";
        var evidence = ipSource.ToLowerInvariant();
        // Do not choose between multiple endpoints or interpret arbitrary event parameters as IP addresses.
        var sourceLabel = Regex.Match(ipSource,
            @"\b(?:IpAddress|source (?:ip(?: address)?|address)|attacker(?: IP)?|IP[- ]адрес источника|адрес атакующего узла)\s*[:=]\s*([0-9a-f:.]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var addresses = Ip.Matches(ipSource).Select(m => IPAddress.TryParse(m.Value, out var address) ? address.ToString() : null)
            .OfType<string>().Distinct(StringComparer.Ordinal).Take(2).ToArray();
        var ip = sourceLabel.Success && IPAddress.TryParse(sourceLabel.Groups[1].Value, out var sourceAddress)
            ? sourceAddress.ToString()
            : addresses.Length == 1 ? addresses[0] : null;
        bool blocked = ip != null &&
            !new[]{"not blocked","not be blocked","block failed","failed to block","could not block","unblocked","не заблокирован","не удалось","разблокирован","блокировка снята","will be blocked","будет заблокирован"}.Any(evidence.Contains) &&
            (rdp || text.Contains("network attack") || text.Contains("сетев") || text.Contains("ip-адрес") || text.Contains("ip address") || text.Contains("untrusted host")) &&
            (evidence.Contains("blocked") || evidence.Contains("заблокирован") || evidence.Contains("added to the list of untrusted") || evidence.Contains("добавлен в список недоверенных"));
        return row with { RdpBruteForce = rdp, AttackerIp = ip, IpBlocked = blocked };
    }
}
