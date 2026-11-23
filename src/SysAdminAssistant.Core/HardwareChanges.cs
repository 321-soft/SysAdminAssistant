using System.Globalization;
using System.Text.Json;
namespace SysAdminAssistant.Core;
public sealed record HardwareChange(string Source,long Sequence,DateTime DetectedUtc,string Title,string Before,string After,string Actor="Агент");
public sealed record HardwareHistoryQuery(string Kind,string Id);
public sealed record HardwareSection(string Summary,Dictionary<string,string> Items);
public static class HardwareChanges
{
    public static string Size(long bytes)=>bytes>=1000000000000L?$"{bytes/1000000000000d:0.##} ТБ":bytes>=1000000000?$"{bytes/1000000000d:0.##} ГБ":bytes>=1000000?$"{bytes/1000000d:0.##} МБ":bytes>=1000?$"{bytes/1000d:0.##} КБ":$"{bytes} Б";
    public static string Ram(long bytes)=>$"{bytes/1073741824d:0.##} ГБ";
    public static Dictionary<string,HardwareSection> Sections(StationHardware h)
    {
        var result=new Dictionary<string,HardwareSection>();
        void Scalar(string key,string value){if(!string.IsNullOrWhiteSpace(value))result[key]=new(value,new(){{key,value}});}
        Scalar("Процессор",h.Cpu);Scalar("Материнская плата",h.Motherboard);
        if(h.MemoryComplete&&h.MemoryModules is {} modules){
            var items=new Dictionary<string,string>();int i=0;
            foreach(var m in modules.OrderBy(m=>m.Slot).ThenBy(m=>m.Serial).ThenBy(m=>m.Model)){
                var slot=m.Slot.Length>0?m.Slot:"#"+i;
                var key=slot;while(items.ContainsKey(key))key=slot+"#"+(++i);
                items[key]=string.Join(" · ",new[]{m.Slot,m.Manufacturer,m.Model,m.FormFactor,m.Kind,Ram(m.Bytes),m.SpeedMhz+" МГц",m.Serial.Length>0?"SN: "+m.Serial:""}.Where(v=>v.Length>0));
                i++;
            }
            result["Оперативная память"]=new($"{Ram(modules.Sum(m=>m.Bytes))} · модулей: {modules.Length}",items);
        }
        if(h.DrivesComplete&&h.Drives is {} drives){
            var items=new Dictionary<string,string>();
            foreach(var d in drives.OrderBy(d=>d.Serial).ThenBy(d=>d.Model).ThenBy(d=>d.Bytes)){
                var id=d.Serial.Length>0?d.Serial:d.Model+"|"+d.Interface+"|"+d.Bytes.ToString(CultureInfo.InvariantCulture);
                var key=id;int i=0;while(items.ContainsKey(key))key=id+"#"+(++i);
                items[key]=string.Join(" · ",new[]{d.Model,Size(d.Bytes),d.Interface,d.Serial.Length>0?"SN: "+d.Serial:""}.Where(v=>v.Length>0));
            }
            result["Накопители"]=new($"Накопителей: {drives.Length} · {Size(drives.Sum(d=>d.Bytes))}",items);
        }
        if(h.GraphicsComplete&&h.Graphics is {} graphics){
            var items=new Dictionary<string,string>();int i=0;
            foreach(var value in graphics.OrderBy(x=>x))items[(i++).ToString(CultureInfo.InvariantCulture)]=value;
            result["Видеокарты"]=new(string.Join("; ",graphics),items);
        }
        return result;
    }
    public static bool Equal(HardwareSection a,HardwareSection b)=>a.Items.Count==b.Items.Count&&a.Items.All(x=>b.Items.TryGetValue(x.Key,out var v)&&v==x.Value);
    private static string WithoutSerial(string value)=>value.Split(" · SN: ",StringSplitOptions.None)[0];
    public static HardwareSection PreserveKnownSerials(HardwareSection before,HardwareSection after)
    {
        var oldValues=before.Items.OrderBy(p=>p.Key).ToList();
        var items=new Dictionary<string,string>();
        foreach(var pair in after.Items){
            var match=oldValues.FindIndex(p=>WithoutSerial(p.Value)==WithoutSerial(pair.Value));
            if(match>=0&&!pair.Value.Contains("SN: ",StringComparison.Ordinal)&&oldValues[match].Value.Contains("SN: ",StringComparison.Ordinal)){
                items[oldValues[match].Key]=oldValues[match].Value;oldValues.RemoveAt(match);
            }else{items[pair.Key]=pair.Value;if(match>=0)oldValues.RemoveAt(match);}
        }
        return after with{Items=items};
    }
    public static bool Enriched(HardwareSection before,HardwareSection after)=>before.Items.Count==after.Items.Count
        &&before.Items.Values.Any(v=>!v.Contains("SN: ",StringComparison.Ordinal))
        &&before.Items.Values.Select(WithoutSerial).OrderBy(x=>x).SequenceEqual(after.Items.Values.Select(WithoutSerial).OrderBy(x=>x));
    public static bool Removes(HardwareSection a,HardwareSection b)=>!Enriched(a,b)&&a.Items.Any(x=>!b.Items.TryGetValue(x.Key,out var v)||v!=x.Value);
    public static string Detail(HardwareSection section)=>section.Summary+(section.Items.Count>0?"\n"+string.Join("\n",section.Items.Values):"");
    public static string Title(string section,HardwareSection before,HardwareSection after)
    {
        if(Enriched(before,after))return "Уточнены сведения: "+section;
        if(section=="Накопители"){
            int removed=before.Items.Count(x=>!after.Items.TryGetValue(x.Key,out var value)||value!=x.Value);
            int added=after.Items.Count(x=>!before.Items.TryGetValue(x.Key,out var value)||value!=x.Value);
            return (removed,added) switch{(0,1)=>"Добавлен накопитель",(1,0)=>"Извлечён накопитель",(1,1)=>"Заменён накопитель",_=>"Изменён состав накопителей"};
        }
        if(section=="Оперативная память")return before.Summary!=after.Summary?"Изменён объём оперативной памяти":"Изменены модули оперативной памяти";
        return "Изменено: "+section;
    }
}
