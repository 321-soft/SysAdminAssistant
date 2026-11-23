using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace SysAdminAssistant.AgentLinux;
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class LinuxUpdateQueue:IDisposable
{
    [DllImport("libc",EntryPoint="open",SetLastError=true)] private static extern int Open(string path,int flags);
    [DllImport("libc",EntryPoint="fsync",SetLastError=true)] private static extern int Fsync(int fd);
    [DllImport("libc",EntryPoint="flock",SetLastError=true)] private static extern int Flock(int fd,int operation);
    private readonly SafeFileHandle handle;
    private LinuxUpdateQueue(SafeFileHandle handle){this.handle=handle;}
    public static LinuxUpdateQueue? TryAcquire(string dataDirectory)
    {
        // Lock the stable parent inode; no shared lock file to unlink, replace or chown.
        int fd=Open(dataDirectory,0x10000|0x20000|0x80000);if(fd<0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось открыть каталог очереди без перехода по ссылкам"));
        var h=new SafeFileHandle((IntPtr)fd,true);if(Flock(fd,2|4)==0)return new(h);
        var error=Marshal.GetLastPInvokeError();h.Dispose();if(error==11)return null;throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось захватить блокировку очереди обновления (errno ")+error+")");
    }
    public static void SyncDirectory(string path){var fd=Open(path,0x10000|0x20000|0x80000);if(fd<0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось открыть каталог для фиксации транзакции"));using var handle=new SafeFileHandle((IntPtr)fd,true);if(Fsync(fd)!=0)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Файловая система не подтвердила фиксацию каталога"));}
    public void Dispose(){Flock((int)handle.DangerousGetHandle(),8);handle.Dispose();}
}
