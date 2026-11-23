namespace SysAdminAssistant.Core;

public static class DeviceTypes
{
    public static string Detect(string name, string role)
    {
        var text = (name + " " + role).ToLowerInvariant();
        if (text.Contains("mikrotik") || text.Contains("routeros")) return "MikroTik";
        if (text.Contains("synology") || text.Contains("diskstation")) return "Synology NAS";
        if (text.Contains("qnap")) return "QNAP NAS";
        if (text.Contains("windows server") || text.Contains("сервер") || text.Contains("server")) return "Сервер";
        if (text.Contains("windows") || text.Contains("ноутбук") || text.Contains("компьютер")) return "ПК";
        if (text.Contains("принтер") || text.Contains("printer")) return "Принтер";
        if (text.Contains("коммутатор") || text.Contains("switch")) return "Коммутатор";
        if (text.Contains("ибп") || text.Contains("ups")) return "ИБП";
        return "Другое";
    }
}
