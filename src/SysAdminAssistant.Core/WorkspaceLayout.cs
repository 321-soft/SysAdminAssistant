namespace SysAdminAssistant.Core;

public sealed record WorkspaceTile(string Id,int Width=1,int Height=1);
public sealed record WorkspacePage(int Page,WorkspaceTile[]? Tiles,bool Collapsed=false,bool FillRows=false,bool FillHeight=false);
public sealed record WorkspaceTileInfo(string Id,int Page,string Title,string? ExtraModule=null);
public sealed record WorkspacePlacement(int Row,int Column,int Width,int Height);

public static class WorkspaceLayout
{
    public static readonly WorkspaceTileInfo[] Catalog=[
        new("overview-devices",1,"Устройства"),new("overview-backups",5,"Резервное копирование","reports"),
        new("overview-payments",3,"Счета"),new("overview-network",6,"Коммутаторы"),
        new("devices-down",1,"Недоступные устройства"),new("devices-updates",1,"Устройства с обновлениями"),
        new("services-down",2,"Недоступные сервисы"),new("services-active",2,"Контролируемые сервисы"),
        new("payments-overdue",3,"Просроченные счета"),new("payments-week",3,"Счета на ближайшие 7 дней"),
        new("events-warning",4,"Активные предупреждения"),new("events-critical",4,"Активные критические события"),
        new("backup-overdue",5,"Просроченные задания","reports"),new("backup-enabled",5,"Задания под контролем"),
        new("network-total",6,"Сетевые устройства"),
        new("vault-status",7,"Сейф паролей"),new("settings-monitor",8,"Серверный мониторинг"),
        new("inventory-stock",9,"Свободное оборудование"),new("inventory-acts",9,"Акты за последние 30 дней"),
        new("dmarc-today",10,"Отчёты DMARC сегодня"),new("dmarc-queue",10,"Ошибки разбора DMARC"),
        new("mail-queue",11,"Нераспознанные письма"),new("mail-unbound",11,"Предупреждения без привязки"),
        new("ksc-status",12,"Мониторинг Kaspersky")
    ];
    public static WorkspaceTileInfo? Find(string id)=>Catalog.FirstOrDefault(t=>t.Id==id);
    public static bool IsOverviewCard(string id)=>id.StartsWith("overview-",StringComparison.Ordinal);
    public static WorkspaceTile[] Defaults(int page)=>Catalog.Where(t=>page==0?IsOverviewCard(t.Id):t.Page==page&&!IsOverviewCard(t.Id))
        .Select(t=>new WorkspaceTile(t.Id,1,IsOverviewCard(t.Id)?2:1)).ToArray();
    public static bool Fits(string id,int width,int height)=>((width is 1 or 2 && height is 1 or 2) || (width,height) is (2,4) or (4,2)) && (!IsOverviewCard(id)||height>=2);
    public static WorkspacePlacement[] Pack(IReadOnlyList<WorkspaceTile> tiles,int columns)
    {
        columns=Math.Clamp(columns,1,8);
        var used=new HashSet<(int Row,int Col)>();var placements=new List<WorkspacePlacement>(tiles.Count);
        int cursor=0;
        foreach(var tile in tiles){
            var width=Math.Clamp(tile.Width,1,columns);var height=Math.Clamp(tile.Height,1,4);
            while(true){
                int row=cursor/columns,col=cursor%columns;
                if(col+width<=columns&&!Enumerable.Range(row,height).Any(y=>Enumerable.Range(col,width).Any(x=>used.Contains((y,x))))){
                    for(int y=row;y<row+height;y++)for(int x=col;x<col+width;x++)used.Add((y,x));
                    placements.Add(new(row,col,width,height));cursor++;break;
                }
                cursor++;
            }
        }
        return placements.ToArray();
    }
    // Rows crossed by a tall card must share the same column widths.
    public static (int Start,int End,int Columns)[] Bands(IReadOnlyList<WorkspacePlacement> spots)
    {
        var bands=new List<(int,int,int)>();
        for(int start=0;start<spots.Count;){
            int end=start, bottom=spots[start].Row+1, columns=0;
            do{
                var p=spots[end++];
                bottom=Math.Max(bottom,p.Row+p.Height);
                columns=Math.Max(columns,p.Column+p.Width);
            }while(end<spots.Count&&spots[end].Row<bottom);
            bands.Add((start,end,columns));
            start=end;
        }
        return bands.ToArray();
    }
}
