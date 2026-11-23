using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace SysAdminAssistant.AgentSetup;

internal static partial class Program
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint Type,State,Accepted,Win32Exit,ServiceExit,CheckPoint,WaitHint,ProcessId,Flags;
    }

    [DllImport("advapi32.dll",SetLastError=true)]
    private static extern bool QueryServiceStatusEx(SafeHandle service,int level,out ServiceStatusProcess info,int size,out int needed);

    private static void StopAgentAndWait(ServiceController service)
    {
        using var handle=service.ServiceHandle;
        if(!QueryServiceStatusEx(handle,0,out var status,Marshal.SizeOf<ServiceStatusProcess>(),out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        Process? process=null;
        try
        {
            if(status.ProcessId!=0)
                try {process=Process.GetProcessById(checked((int)status.ProcessId));}catch(ArgumentException){}
            service.Refresh();
            if(service.Status==ServiceControllerStatus.StartPending)
            {
                service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));
                service.Refresh();
            }
            if(service.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))service.Stop(stopDependentServices:false);
            service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30));
            if(process!=null&&!process.WaitForExit(30000))
                throw new System.TimeoutException("Служба остановлена, но процесс агента ещё не завершился. Замена файлов отложена.");
        }
        finally {process?.Dispose();}
    }
}
