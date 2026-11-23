using System.Net;
namespace SysAdminAssistant.Core;
public sealed class LanNetwork
{
    private readonly byte[] network; private readonly int prefix;
    public LanNetwork(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || !int.TryParse(parts[1], out prefix)) throw new FormatException(global::SysAdminAssistant.Core.ProductText.Get("Подсеть: IP/префикс"));
        if (ip.IsIPv4MappedToIPv6) throw new FormatException(global::SysAdminAssistant.Core.ProductText.Get("В CIDR используйте обычный IPv4-адрес"));
        network = ip.GetAddressBytes();
        if (prefix < 0 || prefix > network.Length * 8 || !IsPrivate(ip) || (network.Length == 4 && prefix < (network[0] == 10 ? 8 : network[0] == 172 ? 12 : 16)) || (network.Length == 16 && prefix < 7)) throw new FormatException(global::SysAdminAssistant.Core.ProductText.Get("Укажите частную локальную подсеть с корректным префиксом"));
        for (int bit = prefix; bit < network.Length * 8; bit++) network[bit / 8] &= (byte)~(1 << (7 - bit % 8));
    }
    public bool Contains(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4(); var bytes = ip.GetAddressBytes();
        if (bytes.Length != network.Length) return false;
        for (int bit = 0; bit < prefix; bit++) if ((bytes[bit / 8] & (1 << (7 - bit % 8))) != (network[bit / 8] & (1 << (7 - bit % 8)))) return false;
        return true;
    }
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4(); var b = ip.GetAddressBytes();
        return b.Length == 4 ? b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 : (b[0] & 0xfe) == 0xfc;
    }
}
