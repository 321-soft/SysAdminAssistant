using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Agent;

internal static class StationPeripheralCollector
{
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern uint CM_Locate_DevNodeW(out uint node,string id,uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent,uint node,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern uint CM_Get_Device_IDW(uint node,[Out] char[] id,uint length,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern uint CM_Get_DevNode_PropertyW(uint node,
        ref PropertyKey key,out uint type,[Out] byte[] value,ref uint length,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern uint CM_Get_DevNode_Registry_PropertyW(uint node,
        uint property,out uint type,out uint value,ref uint length,uint flags);

    private static string Clean(string? s,int max)=>WindowsUpdateResults.Clean(s??"",max);
    private static bool Prefix(string id,string prefix)=>id.StartsWith(prefix,StringComparison.OrdinalIgnoreCase);
    private static string Txt(object? x)=>Convert.ToString(x,CultureInfo.InvariantCulture)??"";
    private static string Decode(object? value,int max)=>value is Array array
        ? Clean(new string(array.Cast<object>().Take(max).Select(x=>(char)Convert.ToUInt16(x,CultureInfo.InvariantCulture)).TakeWhile(x=>x!='\0').ToArray()),max) : "";
    private static string MonitorKey(string id) => id.EndsWith("_0",StringComparison.Ordinal) ? id[..^2] : id;
    private static void Release(object? value) { if(value is IDisposable disposable)disposable.Dispose();else if(value!=null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    private static (bool? Local,bool Removable,bool Bluetooth,bool Virtual,string Bus,string Model) Topology(string id)
    {
        bool virtualDevice=Prefix(id,@"ROOT\")||Prefix(id,@"SWD\")||Prefix(id,@"VMBUS\");
        if(CM_Locate_DevNodeW(out var node,id,0)!=0) return(null,false,false,virtualDevice,"","");
        bool? local=null;
        bool removable=false, bluetooth=false;
        string bus="",model="";
        var property=new PropertyKey{Format=new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"),Id=4};
        var value=new byte[1];uint length=1;
        if(CM_Get_DevNode_PropertyW(node,ref property,out var type,value,ref length,0)==0 && type==17 && length==1)
            local=value[0]!=0;
        var buffer=new char[512];
        for(int i=0;i<12;i++)
        {
            if(CM_Get_Device_IDW(node,buffer,(uint)buffer.Length,0)!=0)break;
            var n=Array.IndexOf(buffer,'\0');
            var current=new string(buffer,0,n<0?buffer.Length:n);
            if(Prefix(current,@"BTHENUM\")||Prefix(current,@"BTHLEDEVICE\")){bluetooth=true;bus="Bluetooth";break;}
            if(Prefix(current,@"USB\VID_"))
            {
                if(bus.Length==0)bus="USB";
                if(model.Length==0)model=Clean(string.Join("&",current.Split('\\')[1].Split('&').Take(2)),32);
                uint size=4;
                if(CM_Get_DevNode_Registry_PropertyW(node,16,out _,out var caps,ref size,0)==0 && (caps&4)!=0)removable=true;
            }
            if(bus.Length==0 && (Prefix(current,@"ACPI\")||Prefix(current,@"I2C\")))bus="ACPI / I²C";
            if(CM_Get_Parent(out var parent,node,0)!=0)break;
            node=parent;
        }
        return(local,removable,bluetooth,virtualDevice,bus,model);
    }

    private static object? Get(object row,string name)=>((NativeWmi.Row)row).Get(name);
    private static void Query(object locator,string scope,string query,Action<object> item,Action<Exception> failed)
        =>((NativeWmi)locator).Query(scope,query,item,failed);

    public static string ProbeWmi()
    {
        using var session=new NativeWmi();string caption="";
        session.Query(@"root\cimv2","SELECT Caption FROM Win32_OperatingSystem",row=>caption=Txt(Get(row,"Caption")),error=>throw error);
        if(caption.Length==0)throw new InvalidDataException("WMI returned no operating system caption.");
        return caption;
    }

    public static StationHardware ReadHardware()
    {
        var memory=new List<StationMemoryModule>();var disks=new List<StationPhysicalDisk>();var gpu=new List<string>();
        string manufacturer="",model="",board="",bios="",serial="",boardSerial="";int? slots=null;int moduleCount=0,diskCount=0,graphicsCount=0;bool hardwarePartial=false;bool memoryOk=false,disksOk=false,graphicsOk=false;
        using(var key=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS")){
            string R(string n)=>Clean(Txt(key?.GetValue(n)),128);
            manufacturer=R("SystemManufacturer");model=R("SystemProductName");
            board=Clean(string.Join(" · ",new[]{R("BaseBoardManufacturer"),R("BaseBoardProduct"),R("BaseBoardVersion")}.Where(x=>x.Length>0)),256);
            bios=Clean(string.Join(" · ",new[]{R("BIOSVendor"),R("BIOSVersion"),R("BIOSReleaseDate")}.Where(x=>x.Length>0)),256);
        }
        object? locator=null;
        try {
            locator=new NativeWmi();
            if(locator==null)hardwarePartial=true;
            bool Q(string query,Action<object> item){if(locator==null)return false;var ok=true;try{Query(locator,@"root\cimv2",query,item,_=>{hardwarePartial=true;ok=false;});return ok;}catch(Exception e)when(e is COMException or TargetInvocationException or InvalidCastException or MissingMethodException){hardwarePartial=true;return false;}}
            long L(object d,string n)=>long.TryParse(Txt(Get(d,n)),out var v)?Math.Max(0,v):0;
            string S(object d,string n)=>Clean(Txt(Get(d,n)).Trim(),128);
            Q("SELECT SerialNumber FROM Win32_BIOS",d=>serial=HardwareIdentity.Serial(S(d,"SerialNumber")));
            Q("SELECT Manufacturer,Product,Version,SerialNumber FROM Win32_BaseBoard",d=>{boardSerial=HardwareIdentity.Serial(S(d,"SerialNumber"));board=Clean(string.Join(" · ",new[]{S(d,"Manufacturer"),S(d,"Product"),HardwareIdentity.Serial(S(d,"Version"))}.Where(v=>v.Length>0)),256);});
            memoryOk=Q("SELECT DeviceLocator,Manufacturer,PartNumber,SerialNumber,Capacity,Speed,ConfiguredClockSpeed,SMBIOSMemoryType,FormFactor FROM Win32_PhysicalMemory",d=>{
                moduleCount++;
                if(memory.Count<32)memory.Add(new(S(d,"DeviceLocator"),S(d,"Manufacturer"),S(d,"PartNumber"),S(d,"SerialNumber"),L(d,"Capacity"),(int)Math.Min(100000,L(d,"ConfiguredClockSpeed") is >0 and var speed?speed:L(d,"Speed")),
                    L(d,"SMBIOSMemoryType") switch{24=>"DDR3",26=>"DDR4",30=>"LPDDR4",34=>"DDR5",35=>"LPDDR5",_=>"Не определено"},L(d,"FormFactor") switch{8=>"DIMM",12=>"SO-DIMM",_=>"Не определено"}));
            });
            Q("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray",d=>slots=Math.Min(256,(slots??0)+(int)Math.Min(256,L(d,"MemoryDevices"))));
            disksOk=Q("SELECT Model,SerialNumber,Size,InterfaceType FROM Win32_DiskDrive",d=>{diskCount++;if(disks.Count<16)disks.Add(new(S(d,"Model"),S(d,"SerialNumber"),L(d,"Size"),S(d,"InterfaceType")));});
            graphicsOk=Q("SELECT Name FROM Win32_VideoController",d=>{graphicsCount++;var name=S(d,"Name");if(gpu.Count<16&&name.Length>0)gpu.Add(name);});
        } catch(Exception e) when(e is COMException or TargetInvocationException or InvalidCastException or MissingMethodException){hardwarePartial=true;} finally {Release(locator);}
        var firmware=FirmwareMemory.Read();
        if(firmware.Complete){memory=firmware.Modules.ToList();moduleCount=memory.Count;slots=firmware.Slots;memoryOk=true;}
        if(string.IsNullOrWhiteSpace(serial))serial=firmware.Serial;
        if(boardSerial.Length==0)boardSerial=firmware.MotherboardSerial;
        return new("",memory.Sum(m=>m.Bytes),disks.Sum(d=>d.Bytes),manufacturer,model,board,bios,memory.ToArray(),slots,disks.ToArray(),gpu.ToArray(),Serial:serial,InstalledModules:Math.Min(256,moduleCount),Partial:hardwarePartial||moduleCount>memory.Count,InstalledMemoryBytes:FirmwareMemory.InstalledBytes(),MemoryComplete:memoryOk&&memory.Count>0&&moduleCount==memory.Count,DrivesComplete:disksOk&&diskCount==disks.Count,GraphicsComplete:graphicsOk&&graphicsCount==gpu.Count,ScannedUtc:DateTime.UtcNow,MotherboardSerial:boardSerial);
    }
    public static StationPeripheralScan Read()
    {
        var result=new List<StationPeripheral>();
        bool partial=false;var errors=new List<string>();
        void Failure(string scope,Exception error){
            partial=true;
            var root=error is TargetInvocationException {InnerException:{} inner}?inner:error;
            var select=scope.IndexOf("SELECT ",StringComparison.Ordinal);
            if(select>=0){var from=System.Text.RegularExpressions.Regex.Match(scope[select..],@"\bFROM\s+(\w+)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);scope=scope[..select].TrimEnd(' ','·')+(from.Success?" · "+from.Groups[1].Value:"");}
            var message=Clean(root.GetType().Name+" · 0x"+root.HResult.ToString("X8")+" · "+scope+" · "+root.Message,200);
            if(errors.Count<8&&!errors.Contains(message))errors.Add(message);
        }
        var connections=new Dictionary<string,uint>(StringComparer.OrdinalIgnoreCase);
        var displays=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object? locator=null;
        try
        {
            locator=new NativeWmi();
            if(locator==null)return new(DateTime.UtcNow,[],true,["Не удалось открыть провайдер WMI."]);
            void ReadQuery(string scope,string query,Action<object> item)
            {
                try {Query(locator,scope,query,item,e=>Failure(scope+" · "+query,e));}
                catch(Exception e) when(e is COMException or TargetInvocationException or MissingMethodException or InvalidCastException or FormatException or OverflowException)
                { Failure(scope+" · "+query,e); }
            }
            ReadQuery(@"root\wmi","SELECT InstanceName,VideoOutputTechnology FROM WmiMonitorConnectionParams WHERE Active=TRUE",d=>
                connections[MonitorKey(Txt(Get(d,"InstanceName")))]=Convert.ToUInt32(Get(d,"VideoOutputTechnology"),CultureInfo.InvariantCulture));
            ReadQuery(@"root\wmi","SELECT InstanceName,ManufacturerName,ProductCodeID,SerialNumberID,UserFriendlyName FROM WmiMonitorID WHERE Active=TRUE",d=>{
                string id=MonitorKey(Txt(Get(d,"InstanceName")));
                displays.Add(id);
                string vendor=Decode(Get(d,"ManufacturerName"),50), model=Decode(Get(d,"ProductCodeID"),32),name=Decode(Get(d,"UserFriendlyName"),120),serial=Decode(Get(d,"SerialNumberID"),64);
                if(name.Length==0)name=(vendor+" "+model).Trim();
                if(name.Length==0)name="Монитор";
                var link=connections.TryGetValue(id,out var technology)?StationPeripherals.DisplayLink(technology):("Unknown","");
                result.Add(new("Display",name,link.Item1,link.Item2,vendor,model,serial));
            });
            ReadQuery(@"root\cimv2","SELECT Name,PNPDeviceID,PNPClass,Manufacturer FROM Win32_PnPEntity WHERE Present=TRUE",d=>{
                string id=Txt(Get(d,"PNPDeviceID")),kind=Txt(Get(d,"PNPClass")), name=Clean(Txt(Get(d,"Name")),120),vendor=Clean(Txt(Get(d,"Manufacturer")),50);
                if(name.Length==0||id.Length==0)return;
                if(kind=="Monitor"&&displays.Contains(id))return;
                string? category=kind.ToUpperInvariant() switch {
                    "MONITOR"=>"Display","KEYBOARD"=>"Keyboard","MOUSE"=>"Mouse","CAMERA"=>"Camera",
                    "IMAGE"=>name.Contains("camera",StringComparison.OrdinalIgnoreCase)||name.Contains("камера",StringComparison.OrdinalIgnoreCase)?"Camera":"Scanner",
                    "PRINTER"=>Prefix(id,@"USBPRINT\")?"Printer":null,
                    "MEDIA" when Prefix(id,@"USB\")||Prefix(id,@"BTHENUM\")||Prefix(id,@"HDAUDIO\")=>"Audio",
                    "DISKDRIVE" when Prefix(id,@"USBSTOR\")=>"Storage",
                    "SMARTCARDREADER"=>"SmartCard","UPS"=>"Ups",
                    "SYSTEM" or "USBDEVICE" when name.Contains("dock",StringComparison.OrdinalIgnoreCase)=>"Dock",
                    "HIDCLASS" when name.Contains("touch",StringComparison.OrdinalIgnoreCase)||name.Contains("сенсор",StringComparison.OrdinalIgnoreCase)=>"Touch",
                    "HIDCLASS" when name.Contains("game",StringComparison.OrdinalIgnoreCase)||name.Contains("joystick",StringComparison.OrdinalIgnoreCase)=>"GameController",
                    "USBDEVICE" when Prefix(id,@"USB\VID_")=>"UsbPeripheral", _=>null
                };
                if(category==null)return;
                var info=Topology(id);
                var link=category=="Display"&&connections.TryGetValue(id,out var tech)?StationPeripherals.DisplayLink(tech):("Unknown","");
                var placement=link.Item1!="Unknown"?link.Item1:StationPeripherals.Placement(info.Local,info.Removable,info.Bluetooth,info.Virtual);
                result.Add(new(category,name,placement,link.Item2.Length>0?link.Item2:info.Bus,vendor,info.Model));
            });
            ReadQuery(@"root\cimv2","SELECT Name,DriverName,PortName,Network,WorkOffline FROM Win32_Printer",d=>{
                string name=Clean(Txt(Get(d,"Name")),120), port=Txt(Get(d,"PortName")), driver=Clean(Txt(Get(d,"DriverName")),120);
                if(name.Length==0)return;
                if(Get(d,"WorkOffline") is bool offline&&offline)return;
                bool network=Get(d,"Network") is bool net&&net||Prefix(port,"IP_")||Prefix(port,"WSD-")||Prefix(port,@"\\");
                bool virtualQueue=Prefix(port,"TS")&&port.Length>2&&port[2..].All(char.IsDigit)||port.Equals("PORTPROMPT:",StringComparison.OrdinalIgnoreCase)||port.Equals("FILE:",StringComparison.OrdinalIgnoreCase)||
                    port.Equals("NUL:",StringComparison.OrdinalIgnoreCase)||driver.Contains("Microsoft Print to PDF",StringComparison.OrdinalIgnoreCase)||
                    driver.Contains("XPS Document Writer",StringComparison.OrdinalIgnoreCase);
                result.Add(new("PrintQueue",name,network?"Network":virtualQueue?"Virtual":"Unknown",Prefix(port,"USB")?"USB":network?"Сеть":""));
            });
        }
        catch(COMException error) {Failure("WMI",error);}
        finally {Release(locator);}
        // Preserve physically distinct identical displays and input devices.
        var sorted=result.OrderBy(x=>x.Kind=="Display"?0:x.Kind=="UsbPeripheral"?2:1).ThenBy(x=>x.Kind).ThenBy(x=>x.Name).Take(StationPeripherals.Limit).ToArray();
        return new(DateTime.UtcNow,sorted,partial||result.Count>sorted.Length,errors.Count==0?null:errors.ToArray());
    }
}
