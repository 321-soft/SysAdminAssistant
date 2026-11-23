using System.Runtime.InteropServices;
using SysAdminAssistant.Agent;

internal static class NativeWmiSpecs
{
    private sealed class Fake : IDisposable
    {
        private readonly List<IntPtr> allocated=[];
        private readonly List<Delegate> callbacks=[];
        internal IntPtr Node(int slots,params (int Slot,Delegate Callback)[] methods)
        {
            var table=Marshal.AllocHGlobal(slots*IntPtr.Size);allocated.Add(table);
            for(int i=0;i<slots;i++)Marshal.WriteIntPtr(table,i*IntPtr.Size,IntPtr.Zero);
            foreach(var (slot,callback) in methods){callbacks.Add(callback);Marshal.WriteIntPtr(table,slot*IntPtr.Size,Marshal.GetFunctionPointerForDelegate(callback));}
            var node=Marshal.AllocHGlobal(IntPtr.Size);allocated.Add(node);Marshal.WriteIntPtr(node,table);return node;
        }
        public void Dispose(){foreach(var pointer in allocated)Marshal.FreeHGlobal(pointer);GC.KeepAlive(callbacks);}
    }
    internal static void Run(Action<string,bool> check)
    {
        check("WMI unsigned display connector preserves the internal-panel high bit",NativeWmi.Normalize(unchecked((int)0x80000000),19) is uint technology&&technology==0x80000000);
        using var fake=new Fake();int rowReleased=0,rowsReleased=0,seen=0,failed=0,nextCalls=0;
        var row=fake.Node(5,(2,new NativeWmi.ReleaseCall(_=>{rowReleased++;return 0;})));
        var rows=fake.Node(5,(2,new NativeWmi.ReleaseCall(_=>{rowsReleased++;return 0;})),
            (4,new NativeWmi.NextCall((IntPtr self,int timeout,uint count,out IntPtr result,out uint returned)=>{
                nextCalls++;result=row;returned=1;return 1; // final row and end-of-enumeration together
            })));
        bool correctQuery=false;
        var service=fake.Node(21,(20,new NativeWmi.QueryCall((IntPtr self,string language,string query,int flags,IntPtr context,out IntPtr result)=>{
            correctQuery=language=="WQL"&&query=="SELECT Caption FROM Win32_OperatingSystem"&&flags==0x30&&context==IntPtr.Zero;
            result=rows;return 0;
        })));
        NativeWmi.Execute(service,"SELECT Caption FROM Win32_OperatingSystem",_=>seen++,_=>failed++,_=>{});
        check("Native WMI uses the native query language order and a null context pointer",correctQuery);
        check("WMI processes the last row returned together with WBEM_S_FALSE",seen==1&&failed==0&&nextCalls==1);
        check("WMI releases the last row and result enumerator exactly once",rowReleased==1&&rowsReleased==1);

        NativeWmi.Execute(service,"SELECT Caption FROM Win32_OperatingSystem",_=>throw new COMException("property failed",unchecked((int)0x80041002)),_=>failed++,_=>{});
        check("A failed property read is reported without leaking its row or enumerator",failed==1&&rowReleased==2&&rowsReleased==2);
        try{NativeWmi.Execute(service,"query",_=>{},_=>{},_=>throw new COMException("proxy security"));}catch(COMException){}
        check("Failure to secure a result proxy still releases its enumerator",rowsReleased==3&&rowReleased==2);

        var denied=fake.Node(21,(20,new NativeWmi.QueryCall((IntPtr self,string language,string query,int flags,IntPtr context,out IntPtr result)=>{result=IntPtr.Zero;return unchecked((int)0x80041003);})));
        bool original=false;
        try{NativeWmi.Execute(denied,"query",_=>{},_=>{},_=>{});}
        catch(COMException error){original=error.HResult==unchecked((int)0x80041003)&&error.Message.Contains("IWbemServices.ExecQuery");}
        check("Native WMI retains the provider HRESULT and operation instead of DISP_E_EXCEPTION",original);

        var empty=fake.Node(21,(20,new NativeWmi.QueryCall((IntPtr self,string language,string query,int flags,IntPtr context,out IntPtr result)=>{result=IntPtr.Zero;return 0;})));
        bool nullRejected=false;try{NativeWmi.Execute(empty,"query",_=>{},_=>{},_=>{});}catch(COMException error){nullRejected=error.Message.Contains("null interface");}
        check("Malformed WMI success with no result interface is rejected before dereferencing",nullRejected);
        var waiting=fake.Node(5,(2,new NativeWmi.ReleaseCall(_=>{rowsReleased++;return 0;})),
            (4,new NativeWmi.NextCall((IntPtr self,int timeout,uint count,out IntPtr result,out uint returned)=>{result=IntPtr.Zero;returned=0;return 0x40004;})));
        var waitingService=fake.Node(21,(20,new NativeWmi.QueryCall((IntPtr self,string language,string query,int flags,IntPtr context,out IntPtr result)=>{result=waiting;return 0;})));
        bool deadline=false;
        try{NativeWmi.Execute(waitingService,"query",_=>{},_=>{},_=>{},5);}catch(COMException error){deadline=error.Message.Contains("deadline");}
        check("Repeated WMI wait responses have a bounded deadline and release the enumerator",deadline&&rowsReleased==4);
    }
}
