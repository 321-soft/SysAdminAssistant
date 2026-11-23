using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace SysAdminAssistant.Server;
internal static class InvoiceProcessJob
{
    [StructLayout(LayoutKind.Sequential)] struct Basic{public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinWorking,MaxWorking;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)] struct Io{public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)] struct Limits{public Basic Basic;public Io Io;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateJobObject(IntPtr attrs,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(SafeFileHandle job,int info,ref Limits limits,uint length);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
    public static IDisposable? Attach(Process p){if(!OperatingSystem.IsWindows())return null;var job=CreateJobObject(IntPtr.Zero,null);var limits=new Limits{Basic=new Basic{Flags=0x2108,Active=1},ProcessMemory=new UIntPtr(536870912u)};if(job.IsInvalid||!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<Limits>())||!AssignProcessToJobObject(job,p.Handle)){job.Dispose();try{p.Kill(true);}catch(InvalidOperationException){}throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось ограничить процесс распознавания"));}return job;}
}
