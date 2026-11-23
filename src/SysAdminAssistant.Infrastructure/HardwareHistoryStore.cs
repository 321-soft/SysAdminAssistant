using System.Text.Json;
using System.Text.RegularExpressions;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed class HardwareHistoryState
{
    public DateTime? LastScan {get;set;}
    public Dictionary<string,HardwareSection> Baseline {get;set;}=[];
    public Dictionary<string,HardwareSection> Candidates {get;set;}=[];
    public List<HardwareChange> Events {get;set;}=[];
}
public sealed record InventoryHistoryLink(string Source,long? LastSequence);
public sealed class InventoryHistoryLinks {public Dictionary<int,List<InventoryHistoryLink>> Assets {get;set;}=[];}
public sealed class HardwareHistoryStore(string directory)
{
    public static string Station(Guid id)=>"station-"+id.ToString("N");
    public static string Device(int id)=>"device-"+id;
    private string Root=>Path.Combine(directory,"hardware-history");
    private string FileFor(string source){
        if(!Regex.IsMatch(source,@"^(?:station-[0-9a-f]{32}|device-[1-9][0-9]*|asset-[1-9][0-9]*)$",RegexOptions.CultureInvariant))throw new ArgumentException("Некорректный источник истории");
        return Path.Combine(Root,source+".json");
    }
    private static T Read<T>(string path) where T:new()=>File.Exists(path)?JsonSerializer.Deserialize<T>(File.ReadAllText(path),RemoteData.Json)??throw new InvalidDataException("Не удалось прочитать историю оборудования"):new();
    private static void Write<T>(string path,T value){
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(value,RemoteData.Json));File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public void Observe(string source,StationHardware? hardware,DateTime detectedUtc)
    {
        if(hardware?.ScannedUtc is not DateTime scanned||scanned.Kind!=DateTimeKind.Utc||!HardwareValidation.Valid(hardware))return;
        var path=FileFor(source);
        NetworkStore.Locked(path,()=>{
            var state=Read<HardwareHistoryState>(path);
            if(state.LastScan is DateTime last&&scanned<=last)return false;
            state.LastScan=scanned;
            var sections=HardwareChanges.Sections(hardware);
            var first=state.Baseline.Count==0;
            if(first&&sections.Count>0){
                state.Baseline=sections;
                Add(state,"Исходная конфигурация","",string.Join("\n\n",sections.Select(s=>s.Key+"\n"+HardwareChanges.Detail(s.Value))));
            }else foreach(var (key,observedSection) in sections){
                var current=observedSection;
                if(!state.Baseline.TryGetValue(key,out var old)){
                    state.Baseline[key]=current;
                    Add(state,"Получены сведения: "+key,"",HardwareChanges.Detail(current));continue;
                }
                if(key is "Накопители" or "Оперативная память")current=HardwareChanges.PreserveKnownSerials(old,current);
                if(HardwareChanges.Equal(old,current)){state.Candidates.Remove(key);continue;}
                if(HardwareChanges.Removes(old,current)&&(!state.Candidates.TryGetValue(key,out var pending)||!HardwareChanges.Equal(pending,current))){
                    state.Candidates[key]=current;continue;
                }
                Add(state,HardwareChanges.Title(key,old,current),HardwareChanges.Detail(old),HardwareChanges.Detail(current));
                state.Baseline[key]=current;state.Candidates.Remove(key);
            }
            // An unsuccessful/partial scan cannot confirm an earlier disappearance.
            foreach(var key in state.Candidates.Keys.Where(k=>!sections.ContainsKey(k)).ToArray())state.Candidates.Remove(key);
            Write(path,state);return true;
            void Add(HardwareHistoryState s,string title,string before,string after)=>s.Events.Add(new(source,s.Events.Count==0?1:s.Events[^1].Sequence+1,detectedUtc,title,before,after));
        });
    }
    public HardwareChange[] History(string source){
        var path=FileFor(source);return NetworkStore.Locked(path,()=>Read<HardwareHistoryState>(path).Events.Where(e=>e.Title is not ("Изменено: BIOS" or "Получены сведения: BIOS")).Select(e=>e.Title=="Исходная конфигурация"?e with{Before=WithoutBios(e.Before),After=WithoutBios(e.After)}:e).ToArray());
    }
    private static string WithoutBios(string text)=>string.Join("\n\n",text.Replace("\r\n","\n").Split("\n\n",StringSplitOptions.None).Where(section=>!section.StartsWith("BIOS\n",StringComparison.Ordinal)));
    public void Link(int assetId,string? source)
    {
        if(assetId<=0)throw new ArgumentException("Нет карточки имущества");
        if(source!=null)_=FileFor(source);
        var path=Path.Combine(Root,"inventory-links.json");
        NetworkStore.Locked(path,()=>{
            var data=Read<InventoryHistoryLinks>(path);
            if(!data.Assets.TryGetValue(assetId,out var links))data.Assets[assetId]=links=[];
            if(links.LastOrDefault() is {LastSequence:null} active&&active.Source==source)return false;
            for(int i=0;i<links.Count;i++)if(links[i].LastSequence==null)
                links[i]=links[i] with{LastSequence=History(links[i].Source).LastOrDefault()?.Sequence??0};
            if(source!=null)links.Add(new(source,null));
            Write(path,data);return true;
        });
    }
    public HardwareChange[] AssetHistory(int assetId){
        var path=Path.Combine(Root,"inventory-links.json");
        var links=NetworkStore.Locked(path,()=>Read<InventoryHistoryLinks>(path).Assets.GetValueOrDefault(assetId)?.ToArray()??[]);
        return links.SelectMany(l=>History(l.Source).Where(e=>l.LastSequence==null||e.Sequence<=l.LastSequence))
            .Concat(History("asset-"+assetId)).DistinctBy(e=>(e.Source,e.Sequence)).OrderByDescending(e=>e.DetectedUtc).ToArray();
    }
    public void Manual(int assetId,string before,string after,string actor,DateTime time){
        if(before==after)return;
        var source="asset-"+assetId;var path=FileFor(source);
        NetworkStore.Locked(path,()=>{
            var state=Read<HardwareHistoryState>(path);
            state.Events.Add(new(source,state.Events.Count==0?1:state.Events[^1].Sequence+1,time,"Карточка изменена пользователем",before,after,actor));Write(path,state);return true;
        });
    }
}
