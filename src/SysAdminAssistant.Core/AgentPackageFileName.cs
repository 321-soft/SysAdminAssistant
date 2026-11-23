namespace SysAdminAssistant.Core;

public static class AgentPackageFileName
{
    public static string Create(string deviceName, Version version, bool upgrade)
    {
        const string invalid = "<>:\"/\\|?*";
        var name = new string(deviceName.Select(c => char.IsControl(c) ||
            char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ||
            invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (name.Length > 80)
            name = name[..(char.IsHighSurrogate(name[79]) ? 79 : 80)].TrimEnd(' ', '.');
        if (name.Length == 0) name = "Device";
        var stem = name.Split('.', 2)[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
            stem.StartsWith("LPT", StringComparison.Ordinal)) && "0123456789¹²³".Contains(stem[3]))
            name = "_" + name;
        var release = version.Revision == 0 ? version.ToString(3) : version.ToString();
        return $"{name}_v{release}_{(upgrade ? "Update" : "Setup")}.exe";
    }
}
