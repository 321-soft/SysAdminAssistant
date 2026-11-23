namespace SysAdminAssistant.ServerSetup;

internal static class ServerEventLog
{
    // Runs under the elevated installer; service accounts need no log-management permissions.
    public static async Task EnsureAsync()
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        const string script = "$ErrorActionPreference='Stop'; $source='SysAdminAssistant.Server'; if ([System.Diagnostics.EventLog]::SourceExists($source)) { if ([System.Diagnostics.EventLog]::LogNameFromSourceName($source,'.') -ne 'Application') { throw 'The server event source belongs to another log.' } } else { New-EventLog -LogName Application -Source $source }";
        await SetupEngine.RunAsync(powershell, ["-NoProfile", "-NonInteractive", "-Command", script]);
    }
}
