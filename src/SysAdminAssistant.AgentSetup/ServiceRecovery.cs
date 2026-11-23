using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace SysAdminAssistant.AgentSetup;

internal static partial class Program
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceConfig
    {
        public uint Type,Start,ErrorControl;
        public IntPtr BinaryPath,LoadOrderGroup;
        public uint Tag;
        public IntPtr Dependencies,Account,DisplayName;
    }
    [DllImport("advapi32.dll",EntryPoint="QueryServiceConfigW",SetLastError=true)]
    private static extern bool QueryServiceConfig(IntPtr service,IntPtr buffer,uint size,out uint needed);

    private static void RequireInstalledService()
    {
        var manager=OpenSCManager(null,null,1);
        if(manager==IntPtr.Zero)throw Error();
        var service=IntPtr.Zero;
        try{
            service=OpenService(manager,Service,1);
            if(service==IntPtr.Zero){
                var code=Marshal.GetLastWin32Error();
                if(code==1060)throw new Win32Exception(code,"Windows не нашла службу SysAdminAssistantAgent. Проверьте её через sc.exe query SysAdminAssistantAgent.");
                throw new Win32Exception(code);
            }
            _=QueryServiceConfig(service,IntPtr.Zero,0,out var size);
            if(size<Marshal.SizeOf<ServiceConfig>()||size>8192)throw Error();
            var buffer=Marshal.AllocHGlobal((int)size);
            try{
                if(!QueryServiceConfig(service,buffer,size,out _))throw Error();
                var config=Marshal.PtrToStructure<ServiceConfig>(buffer);
                var expected=Path.Combine(Target,"SysAdminAssistant.Agent.exe");
                if(config.Type!=0x10||!AgentServiceImage.Matches(Marshal.PtrToStringUni(config.BinaryPath),expected))
                    throw new InvalidOperationException("Служба SysAdminAssistantAgent есть, но её тип или путь запуска не совпадает со штатной установкой. Проверьте BINARY_PATH_NAME через sc.exe qc SysAdminAssistantAgent.");
            }finally{Marshal.FreeHGlobal(buffer);}
        }finally{
            if(service!=IntPtr.Zero)CloseServiceHandle(service);
            CloseServiceHandle(manager);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FailureActions
    {
        public uint Reset;
        public IntPtr RebootMessage,Command;
        public uint Count;
        public IntPtr Actions;
    }
    [DllImport("advapi32.dll",EntryPoint="QueryServiceConfig2W",SetLastError=true)]
    private static extern bool QueryServiceConfig2(IntPtr service,uint level,IntPtr buffer,uint size,out uint needed);

    private static bool RecoveryMatches()
    {
        var manager=OpenSCManager(null,null,1);
        if(manager==IntPtr.Zero)throw Error();
        IntPtr service=IntPtr.Zero;
        try
        {
            service=OpenService(manager,Service,1);
            if(service==IntPtr.Zero)throw Error();
            T Read<T>(uint level,Func<IntPtr,T> decode)
            {
                _=QueryServiceConfig2(service,level,IntPtr.Zero,0,out var size);
                if(size==0||size>65536)throw Error();
                var buffer=Marshal.AllocHGlobal((int)size);
                try
                {
                    if(!QueryServiceConfig2(service,level,buffer,size,out _))throw Error();
                    return decode(buffer);
                }
                finally{Marshal.FreeHGlobal(buffer);}
            }
            return Read(4,p=>Marshal.ReadInt32(p)!=0)&&Read(2,p=>{
                var settings=Marshal.PtrToStructure<FailureActions>(p);
                if(settings.Count!=3||settings.Reset!=86400||settings.Actions==IntPtr.Zero)return false;
                int[] delays=[15000,60000,300000];
                return delays.Select((delay,i)=>Marshal.ReadInt32(settings.Actions,i*8)==1&&Marshal.ReadInt32(settings.Actions,i*8+4)==delay).All(x=>x);
            });
        }
        finally
        {
            if(service!=IntPtr.Zero)CloseServiceHandle(service);
            CloseServiceHandle(manager);
        }
    }

    private static void EnsureSlotRecovery()
    {
        RequireInstalledService();
        if(!RecoveryMatches())ConfigureRecovery();
    }

    private static void ConfigureRecovery()
    {
        RunSc("failure",Service,"reset=","86400","actions=","restart/15000/restart/60000/restart/300000");
        RunSc("failureflag",Service,"1");
        if(!RecoveryMatches())throw new InvalidOperationException("Windows не подтвердила политику восстановления агента.");
    }

    private static void RunSc(params string[] args)
    {
        var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"sc.exe")){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in args)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)??throw new InvalidOperationException("Не удалось запустить sc.exe.");
        if(!process.WaitForExit(15000))throw new TimeoutException("sc.exe не завершил настройку восстановления службы.");
        if(process.ExitCode!=0)throw new InvalidOperationException($"sc.exe {args[0]}: код {process.ExitCode}. Проверьте права изменения службы {Service}.");
    }
}
