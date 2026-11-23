using System.Buffers.Binary;
using System.Text;
namespace SysAdminAssistant.Core;
public sealed record SmbiosMemoryResult(StationMemoryModule[] Modules,int? Slots,string Serial,bool Complete,string MotherboardSerial="");
public static class SmbiosMemory
{
    public static SmbiosMemoryResult Read(byte[] data)
    {
        var modules=new List<StationMemoryModule>();int? slots=null;string serial="",boardSerial="";bool complete=true,ended=false;
        if(data.Length<8||BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4,4))>data.Length-8)return new([],null,"",false);
        var limit=8+(int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4,4));
        for(int at=8;at+4<=limit;){
            var type=data[at];int length=data[at+1];if(length<4||at+length+1>=limit){complete=false;break;}
            int end=at+length;while(end+1<limit&&(data[end]!=0||data[end+1]!=0))end++;
            if(end+1>=limit){complete=false;break;}
            string Text(int field){
                if(field>=length||data[at+field]==0)return "";
                var index=data[at+field]-1;
                var values=Encoding.UTF8.GetString(data,at+length,end-at-length).Split('\0');
                return index<values.Length?new string(values[index].Where(c=>!char.IsControl(c)).Take(128).ToArray()).Trim():"";
            }
            ushort U16(int offset)=>offset+2<=length?BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at+offset,2)):(ushort)0;
            if(type==127){ended=true;break;}
            if(type==1)serial=HardwareIdentity.Serial(Text(7));
            if(type==2)boardSerial=HardwareIdentity.Serial(Text(7));
            if(type==16&&length>=15)slots=Math.Min(256,(slots??0)+U16(13));
            if(type==17){
                if(length<27)complete=false;
                else {
                    var size=U16(12);long bytes=0;
                    if(size==ushort.MaxValue)complete=false;
                    else if(size==0x7fff){
                        if(length>=32)bytes=(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at+28,4))&0x7fffffffL)*1048576L;
                        else complete=false;
                    }else bytes=(long)(size&0x7fff)*((size&0x8000)!=0?1024L:1048576L);
                    if(size!=0){
                        if(modules.Count<32){
                            var speed=U16(32);if(speed==0)speed=U16(21);
                            modules.Add(new(Text(16),Text(23),Text(26),Text(24),bytes,speed,
                                data[at+18] switch{24=>"DDR3",26=>"DDR4",30=>"LPDDR4",34=>"DDR5",35=>"LPDDR5",_=>""},
                                data[at+14] switch{9=>"DIMM",13=>"SO-DIMM",15=>"FB-DIMM",17=>"CAMM",18=>"CUDIMM",19=>"CSODIMM",_=>""}));
                        }else complete=false;
                    }
                }
            }
            at=end+2;
            if(at==limit)ended=true;
        }
        return new(modules.ToArray(),slots,serial,complete&&ended&&slots!=null&&modules.Count>0,boardSerial);
    }
    public static long? InstalledBytes(StationHardware h) =>
        h.MemoryComplete&&h.MemoryModules is {Length:>0} modules&&modules.All(m=>m.Bytes>0)?modules.Sum(m=>m.Bytes):h.InstalledMemoryBytes is >0?h.InstalledMemoryBytes:null;
}
