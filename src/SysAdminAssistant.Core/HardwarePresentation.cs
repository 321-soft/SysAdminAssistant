namespace SysAdminAssistant.Core;
public static class HardwarePresentation
{
    public static string Text(StationHardware? h,string? fallbackSerial=null)
    {
        if(h==null)return ProductText.Get("Аппаратные сведения ещё не получены.");
        string Known(string? value)=>string.IsNullOrWhiteSpace(value)?ProductText.Get("не сообщается"):value;
        string Field(string label,string? value)=>ProductText.Get(label)+Known(value);
        var serial=HardwareIdentity.Serial(h.Serial);if(serial.Length==0)serial=HardwareIdentity.Serial(fallbackSerial);
        var lines=new List<string>{"КОМПЬЮТЕР",Field("Производитель: ",h.Manufacturer),Field("Модель: ",h.Model),ProductText.Get("Серийный номер: ")+(serial.Length>0?serial:ProductText.Get("не задан производителем")),
            Field("Материнская плата: ",string.Join(" · ",h.Motherboard.Split(" · ").Where(part=>HardwareIdentity.Serial(part).Length>0))),Field("Серийный номер материнской платы: ",HardwareIdentity.Serial(h.MotherboardSerial)),$"BIOS: {Known(h.Bios)}","","ПРОЦЕССОР",Known(h.Cpu),"","ОПЕРАТИВНАЯ ПАМЯТЬ"};
        var installed=SmbiosMemory.InstalledBytes(h);
        if(h.MemoryComplete&&h.MemoryModules is {Length:>0} modules){
            var grouping=string.Join(" + ",modules.GroupBy(m=>m.Bytes).OrderByDescending(g=>g.Key).Select(g=>$"{g.Count()} × {HardwareChanges.Ram(g.Key)}"));
            lines.Add($"{HardwareChanges.Ram(modules.Sum(m=>m.Bytes))} · {grouping}");
            lines.Add($"Установлено модулей: {h.InstalledModules??modules.Length}"+
                (h.MemorySlots is int slots?$" · слотов: {slots} · свободно: {Math.Max(0,slots-(h.InstalledModules??modules.Length))}":""));
            foreach(var m in modules)lines.Add(string.Join(" · ",new[]{Field("Слот: ",m.Slot),Field("Производитель: ",m.Manufacturer),Field("Модель: ",m.Model),Field("Форм-фактор: ",m.FormFactor),Field("Тип памяти: ",m.Kind),
                ProductText.Get("Объём: ")+HardwareChanges.Ram(m.Bytes),ProductText.Get("Частота: ")+(m.SpeedMhz>0?m.SpeedMhz+ProductText.Get(" МГц"):ProductText.Get("не сообщается")),Field("Серийный номер: ",m.Serial)}.Where(v=>v.Length>0)));
        }else{
            if(installed is long bytes)lines.Add("Установлено: "+HardwareChanges.Ram(bytes));
            lines.Add("Не удалось прочитать сведения о модулях памяти.");
        }
        lines.Add("");lines.Add("НАКОПИТЕЛИ");
        foreach(var d in h.Drives??[])lines.Add(string.Join(" · ",new[]{Known(d.Model),HardwareChanges.Size(d.Bytes),d.Interface,d.Serial.Length>0?"SN: "+d.Serial:"SN не получен"}.Where(v=>v.Length>0)));
        if(h.Drives is not {Length:>0})lines.Add("Сведения о накопителях не получены.");
        foreach(var volume in h.Volumes?.Where(v=>v.AliasOf.Length==0)??[])
            lines.Add(volume.Name+(volume.TotalBytes is long total?" · всего "+HardwareChanges.Size(total):"")+(volume.FreeBytes is long free?" · свободно "+HardwareChanges.Size(free):""));
        if(h.Graphics is {Length:>0}){lines.Add("");lines.Add("ВИДЕОКАРТЫ");lines.AddRange(h.Graphics);}
        return string.Join("\n",lines.Select(ProductText.Get));
    }
}
