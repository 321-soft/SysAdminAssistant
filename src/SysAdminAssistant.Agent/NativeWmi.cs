using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SysAdminAssistant.Agent;

// Read-only, local WMI. Uses the documented IUnknown interfaces rather than
// late-bound SWbem automation (and its optional VARIANT/IDispatch arguments).
internal sealed class NativeWmi : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate uint ReleaseCall(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int ConnectCall(IntPtr self,[MarshalAs(UnmanagedType.BStr)] string scope,
        [MarshalAs(UnmanagedType.BStr)] string? user,[MarshalAs(UnmanagedType.BStr)] string? password,
        [MarshalAs(UnmanagedType.BStr)] string? locale,int flags,[MarshalAs(UnmanagedType.BStr)] string? authority,
        IntPtr context,out IntPtr service);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int QueryCall(IntPtr self,[MarshalAs(UnmanagedType.BStr)] string language,
        [MarshalAs(UnmanagedType.BStr)] string query,int flags,IntPtr context,out IntPtr rows);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int NextCall(IntPtr self,int timeout,uint count,out IntPtr row,out uint returned);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetCall(IntPtr self,[MarshalAs(UnmanagedType.LPWStr)] string name,int flags,
        [MarshalAs(UnmanagedType.Struct)] out object? value,out int type,out int flavor);

    [DllImport("ole32.dll",ExactSpelling=true)] private static extern int CoInitializeEx(IntPtr reserved,uint model);
    [DllImport("ole32.dll",ExactSpelling=true)] private static extern void CoUninitialize();
    [DllImport("ole32.dll",ExactSpelling=true)] private static extern int CoCreateInstance(in Guid clsid,IntPtr outer,uint context,in Guid iid,out IntPtr result);
    [DllImport("ole32.dll",ExactSpelling=true)] private static extern int CoSetProxyBlanket(IntPtr proxy,uint authentication,uint authorization,IntPtr principal,uint level,uint impersonation,IntPtr identity,uint capabilities);

    private IntPtr locator;
    private bool uninitialize;
    private readonly int thread=Environment.CurrentManagedThreadId;
    internal static T Method<T>(IntPtr instance,int slot) where T:Delegate
        =>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size));
    internal static void Release(IntPtr instance){if(instance!=IntPtr.Zero)Method<ReleaseCall>(instance,2)(instance);}
    private static void Check(int result,string stage)
    {
        if(result<0)throw new COMException(stage+": "+(Marshal.GetExceptionForHR(result)?.Message??"WMI failure"),result);
    }
    private static void RequirePointer(IntPtr instance,string stage){if(instance==IntPtr.Zero)throw new COMException(stage+": null interface returned",unchecked((int)0x80041001));}
    private static void Secure(IntPtr instance)=>Check(CoSetProxyBlanket(instance,10,0,IntPtr.Zero,3,3,IntPtr.Zero,0),"CoSetProxyBlanket");
    private static void SecureEnumerator(IntPtr instance)
    {
        var result=CoSetProxyBlanket(instance,10,0,IntPtr.Zero,3,3,IntPtr.Zero,0);
        // A locally/custom marshaled enumerator need not expose IClientSecurity.
        // Access-denied and all other security errors still fail the query.
        if(result!=unchecked((int)0x80004002))Check(result,"CoSetProxyBlanket(enumerator)");
    }
    public NativeWmi()
    {
        var initialized=CoInitializeEx(IntPtr.Zero,0);
        // A pre-existing STA is valid too; only successful initialization needs balancing.
        if(initialized<0&&initialized!=unchecked((int)0x80010106))Check(initialized,"CoInitializeEx");
        uninitialize=initialized>=0;
        try{
            var clsid=new Guid("4590F811-1D3A-11D0-891F-00AA004B2E24");
            var iid=new Guid("DC12A687-737F-11CF-884D-00AA004B2E24");
            Check(CoCreateInstance(clsid,IntPtr.Zero,1,iid,out locator),"CoCreateInstance(IWbemLocator)");
            RequirePointer(locator,"CoCreateInstance(IWbemLocator)");
        }catch{Dispose();throw;}
    }
    public void Query(string scope,string query,Action<object> item,Action<Exception> failed)
    {
        if(locator==IntPtr.Zero)throw new ObjectDisposedException(nameof(NativeWmi));
        if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("WMI queries must run on their session thread.");
        IntPtr service=IntPtr.Zero;
        try{
            Check(Method<ConnectCall>(locator,3)(locator,scope,null,null,null,128,null,IntPtr.Zero,out service),"IWbemLocator.ConnectServer");
            RequirePointer(service,"IWbemLocator.ConnectServer");
            Secure(service);
            Execute(service,query,item,failed,SecureEnumerator);
        }finally{Release(service);}
    }
    internal static void Execute(IntPtr service,string query,Action<object> item,Action<Exception> failed,Action<IntPtr> secure,int deadlineMilliseconds=15000)
    {
        IntPtr rows=IntPtr.Zero;
        try{
            // IWbemServices::ExecQuery is opnum 20. Native argument order differs from SWbemServices.
            Check(Method<QueryCall>(service,20)(service,"WQL",query,0x30,IntPtr.Zero,out rows),"IWbemServices.ExecQuery");
            RequirePointer(rows,"IWbemServices.ExecQuery");
            secure(rows);
            Enumerate(rows,item,failed,deadlineMilliseconds);
        }finally{Release(rows);}
    }
    private static void Enumerate(IntPtr rows,Action<object> item,Action<Exception> failed,int deadlineMilliseconds)
    {
        var next=Method<NextCall>(rows,4);var watch=Stopwatch.StartNew();int seen=0;
        while(true){
            if(watch.ElapsedMilliseconds>=deadlineMilliseconds)throw new COMException("IEnumWbemClassObject.Next: query deadline exceeded",unchecked((int)0x80043001));
            IntPtr pointer=IntPtr.Zero;
            try{
                var result=next(rows,1000,1,out pointer,out var returned);
                Check(result,"IEnumWbemClassObject.Next");
                if(returned>1||returned==1&&pointer==IntPtr.Zero||returned==0&&pointer!=IntPtr.Zero)
                    throw new COMException("IEnumWbemClassObject.Next: invalid result",unchecked((int)0x80041001));
                if(returned==1){
                    if(++seen>4096)throw new COMException("WMI query exceeded 4096 objects",unchecked((int)0x8004106C));
                    try{item(new Row(pointer));}
                    catch(Exception error)when(error is not OutOfMemoryException and not StackOverflowException){failed(error);}
                }
                if(result==1)return; // WBEM_S_FALSE can still carry the final object.
                if(returned==0&&result!=0x40004)throw new COMException("IEnumWbemClassObject.Next: empty result without end marker",unchecked((int)0x80041001));
            }finally{Release(pointer);}
        }
    }
    internal sealed class Row(IntPtr pointer)
    {
        public object? Get(string name)
        {
            Check(Method<GetCall>(pointer,4)(pointer,name,0,out var value,out var type,out _),"IWbemClassObject.Get("+name+")");
            return Normalize(value,type);
        }
    }
    internal static object? Normalize(object? value,int type)=>value switch{
        DBNull=>null,
        int number when type==19=>unchecked((uint)number),
        short number when type==18=>unchecked((ushort)number),
        sbyte number when type==17=>unchecked((byte)number),
        long number when type==21=>unchecked((ulong)number),
        _=>value
    };
    public void Dispose()
    {
        if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("WMI session must be disposed on its creating thread.");
        var old=locator;locator=IntPtr.Zero;Release(old);
        if(uninitialize){uninitialize=false;CoUninitialize();}
    }
}
