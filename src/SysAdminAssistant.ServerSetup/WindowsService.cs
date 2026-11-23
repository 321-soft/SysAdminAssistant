using System.ComponentModel;
using System.Runtime.InteropServices;
namespace SysAdminAssistant.ServerSetup;
internal sealed class WindowsService : IDisposable
{
    public const string Name = "SysAdminAssistantServer";
    private IntPtr manager;
    private IntPtr service;
    [StructLayout(LayoutKind.Sequential)] private struct Status
    {
        public uint Type, State, Controls, Win32Exit, ServiceExit, Checkpoint, WaitHint;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateService(IntPtr manager, string name, string display, uint access, uint type, uint start, uint error,
        string binary, string? group, IntPtr tag, string? dependencies, string user, string password);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool StartService(IntPtr service, uint count, IntPtr args);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatus(IntPtr service, out Status status);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool ControlService(IntPtr service, uint control, out Status status);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DeleteService(IntPtr service);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
    private static Win32Exception Error() => new(Marshal.GetLastWin32Error());
    public WindowsService()
    {
        manager = OpenSCManager(null, null, 3);
        if (manager == IntPtr.Zero) throw Error();
    }
    public bool Exists()
    {
        var existing = OpenService(manager, Name, 4);
        if (existing != IntPtr.Zero) { CloseServiceHandle(existing); return true; }
        var error = Marshal.GetLastWin32Error();
        if (error != 1060) throw new Win32Exception(error);
        return false;
    }
    public void OpenInstalled()
    {
        service=OpenService(manager,Name,0x34); // QUERY_STATUS, START, STOP; account/config remain unchanged.
        if(service==IntPtr.Zero)throw Error();
    }
    public bool IsRunning() { if(!QueryServiceStatus(service,out var status))throw Error();return status.State==4; }
    public void StopInstalled()
    {
        if(!QueryServiceStatus(service,out var status))throw Error();
        if(status.State==1)return;
        if(status.State!=3 && !ControlService(service,1,out _) && Marshal.GetLastWin32Error()!=1062)throw Error();
        var until=DateTime.UtcNow.AddSeconds(45);
        while(DateTime.UtcNow<until) {
            if(!QueryServiceStatus(service,out status))throw Error();
            if(status.State==1)return;Thread.Sleep(200);
        }
        throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Служба не остановилась; файлы не заменены."));
    }
    public void RemoveInstalled()
    {
        service = OpenService(manager, Name, 0x10024); // DELETE, STOP, QUERY_STATUS
        if (service == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1060) return;
            if (error == 1072) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Служба уже помечена для удаления. Закройте services.msc и перезапустите Windows при необходимости."));
            throw new Win32Exception(error);
        }
        Rollback();
        CloseServiceHandle(service); service = IntPtr.Zero;
    }
    public void Create(string exe, string data, string account, string password)
    {
        var binary = "\"" + exe + "\" --data-dir \"" + data.TrimEnd('\\') + "\"";
        service = CreateService(manager, Name, "SysAdmin Assistant Server", 0xf01ff, 0x10, 2, 1, binary, null, IntPtr.Zero, null, account, password);
        if (service == IntPtr.Zero) throw Error();
    }
    public void Start() { if (!StartService(service, 0, IntPtr.Zero)) throw Error(); }
    public bool Running()
    {
        if (!QueryServiceStatus(service, out var status)) throw Error();
        if (status.State == 1) throw new InvalidOperationException($"Служба остановилась (код Windows {status.Win32Exit}). Проверьте журнал Windows и доступ к базе.");
        return status.State == 4;
    }
    public void Rollback()
    {
        if (service == IntPtr.Zero) return;
        if (!QueryServiceStatus(service, out var status)) throw Error();
        if (status.State != 1)
        {
            if (!ControlService(service, 1, out _) && Marshal.GetLastWin32Error() != 1062) throw Error();
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (!QueryServiceStatus(service, out status)) throw Error();
                if (status.State == 1) break;
                Thread.Sleep(200);
            }
            if (status.State != 1) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Служба не остановилась; требуется ручная проверка."));
        }
        if (!DeleteService(service)) throw Error();
    }
    public void Dispose()
    {
        if (service != IntPtr.Zero) { CloseServiceHandle(service); service = IntPtr.Zero; }
        if (manager != IntPtr.Zero) { CloseServiceHandle(manager); manager = IntPtr.Zero; }
    }
}
internal sealed class Firewall : IDisposable
{
    public const string RuleName = "SysAdminAssistantServer-LAN";
    private readonly object instance = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true)!)!;
    public bool Exists()
    {
        dynamic policy = instance;
        foreach (dynamic rule in policy.Rules)
        {
            try { if ((string)rule.Name == RuleName) return true; }
            finally { Marshal.FinalReleaseComObject((object)rule); }
        }
        return false;
    }
    public void Add(string exe, string ip, int port, string[] networks)
    {
        dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true)!)!;
        try
        {
            rule.Name = RuleName; rule.Description = "SysAdmin Assistant — LAN HTTPS";
            rule.ApplicationName = exe; rule.Protocol = 6; rule.LocalPorts = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            rule.LocalAddresses = ip; rule.RemoteAddresses = string.Join(",", networks);
            rule.Direction = 1; rule.Action = 1; rule.Profiles = 3; rule.Enabled = true;
            ((dynamic)instance).Rules.Add(rule);
        }
        finally { Marshal.FinalReleaseComObject((object)rule); }
    }
    public void Remove() => ((dynamic)instance).Rules.Remove(RuleName);
    public void Dispose() => Marshal.FinalReleaseComObject(instance);
}
