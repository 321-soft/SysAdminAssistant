using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private Dictionary<int,WorkspacePage>? workspacePages;
    private bool workspaceAvailable=true;
    private Func<Task>? workspaceFold;
    private void ClearWorkspaceFold()
    {
        workspaceFold=null;
        WorkspaceFoldButton.Visibility=Visibility.Collapsed;
    }
    private async void WorkspaceFold_Click(object sender,RoutedEventArgs e)
    {
        if(workspaceFold is {} toggle)await Run(toggle);
    }
    private void UpdateWorkspaceFold(bool collapsed)
    {
        WorkspaceFoldButton.Content=ProductText.Get(collapsed?"Показать сводку":"Свернуть сводку");
        WorkspaceFoldButton.Visibility=Visibility.Visible;
    }
    private static WorkspacePage DefaultWorkspace(int index)=>new(index,null,Collapsed:index!=0);
    private WorkspacePage Workspace(int index)=>workspacePages?.GetValueOrDefault(index)??DefaultWorkspace(index);
    private bool CanShowTile(WorkspaceTileInfo t)=>(t.Page==12?remote.CurrentUser.Administrator:CanRead(AccessRules.PageModule(t.Page)))&&(t.ExtraModule==null||CanRead(t.ExtraModule));
    private WorkspaceTile[] VisibleTiles(WorkspacePage value)=>(value.Tiles??WorkspaceLayout.Defaults(value.Page))
        .Where(t=>WorkspaceLayout.Find(t.Id) is {} item&&CanShowTile(item)).ToArray();
    private async Task LoadWorkspace()
    {
        try{
            var loaded=await remote.GetAsync<WorkspacePage[]>("api/personal/workspace",lifetime.Token);
            workspacePages=loaded.ToDictionary(p=>p.Page);workspaceAvailable=true;
        }
        catch(ServerResponseException e)when(e.StatusCode==System.Net.HttpStatusCode.NotFound){
            workspacePages=[];workspaceAvailable=false;
        }
    }

    private static Grid TileGrid(IReadOnlyList<WorkspaceTile> tiles,IReadOnlyList<FrameworkElement> cards,bool compact=false,bool fillRows=false,ScrollViewer? heightViewport=null)
    {
        var grid=new Grid();int previous=0;
        for(int i=0;i<cards.Count;i++)cards[i].Margin=new Thickness(4);
        var hosts=new Grid[cards.Count];
        for(int i=0;i<hosts.Length;i++)hosts[i]=new Grid();
        void Place(Grid host,int i,WorkspacePlacement p,int offset){
            Grid.SetRow(cards[i],p.Row-offset);Grid.SetColumn(cards[i],p.Column);
            Grid.SetRowSpan(cards[i],p.Height);Grid.SetColumnSpan(cards[i],p.Width);
            host.Children.Add(cards[i]);
        }
        var rowDefinitions=new List<RowDefinition>();
        var minimum=compact?94:116;
        GridLength RowHeight(int rows){
            var height=heightViewport?.ViewportHeight??0;
            return new GridLength(rows>0&&double.IsFinite(height)?Math.Max(minimum,height/rows):minimum);
        }
        void Arrange(double width){
            int columns=Math.Clamp((int)(width/250),1,6);
            if(columns==previous)return;
            previous=columns;
            var spots=WorkspaceLayout.Pack(tiles,columns);
            if(fillRows)foreach(var host in hosts)host.Children.Clear();
            grid.Children.Clear();
            grid.RowDefinitions.Clear();grid.ColumnDefinitions.Clear();
            int rows=spots.Length==0?0:spots.Max(p=>p.Row+p.Height);
            rowDefinitions.Clear();
            var rowHeight=RowHeight(rows);
            void AddRow(Grid host){
                var row=new RowDefinition{Height=rowHeight};
                host.RowDefinitions.Add(row);rowDefinitions.Add(row);
            }
            for(int i=0;i<rows;i++)AddRow(grid);
            if(fillRows){
                foreach(var (start,end,span) in WorkspaceLayout.Bands(spots)){
                    var host=hosts[start];host.RowDefinitions.Clear();host.ColumnDefinitions.Clear();
                    int top=spots[start].Row,bottom=spots[start..end].Max(p=>p.Row+p.Height);
                    for(int i=top;i<bottom;i++)AddRow(host);
                    for(int i=0;i<span;i++)host.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
                    for(int i=start;i<end;i++)Place(host,i,spots[i],top);
                    Grid.SetRow(host,top);Grid.SetRowSpan(host,bottom-top);grid.Children.Add(host);
                }
            }else{
                for(int i=0;i<columns;i++)grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
                for(int i=0;i<cards.Count;i++)Place(grid,i,spots[i],0);
            }
        }
        Arrange(500);
        bool queued=false;
        grid.SizeChanged+=(_,e)=>{
            if(!e.WidthChanged||queued)return;
            queued=true;
            _=grid.Dispatcher.InvokeAsync(()=>{queued=false;if(grid.IsLoaded)Arrange(grid.ActualWidth);},System.Windows.Threading.DispatcherPriority.Loaded);
        };
        if(heightViewport!=null)heightViewport.ScrollChanged+=(_,e)=>{
            if(!ReferenceEquals(e.OriginalSource,heightViewport)||e.ViewportHeightChange==0)return;
            var rowHeight=RowHeight(grid.RowDefinitions.Count);
            foreach(var row in rowDefinitions)if(row.Height!=rowHeight)row.Height=rowHeight;
        };
        return grid;
    }

    private async Task AttachWorkspace(int index)
    {
        var layout=Workspace(index);var tiles=VisibleTiles(layout);
        if(tiles.Length==0)return;
        var content=new Grid();
        var existing=PageHost.Children.Cast<UIElement>().ToArray();PageHost.Children.Clear();
        foreach(var child in existing)content.Children.Add(child);
        var root=new Grid();
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        var header=new StackPanel{Margin=new Thickness(0,0,0,8)};
        Grid.SetRow(content,1);root.Children.Add(header);root.Children.Add(content);
        var scroller=new ScrollViewer{Tag="workspace-scroll",VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=246};
        root.SizeChanged+=(_,e)=>scroller.MaxHeight=Math.Clamp(e.NewSize.Height*.43,52,320);
        header.Children.Add(scroller);
        void ApplyVisibility()
        {
            scroller.Visibility=header.Visibility=layout.Collapsed?Visibility.Collapsed:Visibility.Visible;
            UpdateWorkspaceFold(layout.Collapsed);
        }
        async Task Fill(){
            if(scroller.Content!=null)return;
            var cards=await BuildTiles(tiles);
            scroller.Content=TileGrid(tiles,cards,fillRows:layout.FillRows,heightViewport:layout.FillHeight?scroller:null);
        }
        PageHost.Children.Add(root);
        ApplyVisibility();
        if(!layout.Collapsed)await Fill();
        workspaceFold=async()=>{
            var next=layout with{Collapsed=!layout.Collapsed};
            if(!next.Collapsed)await Fill();
            if(workspaceAvailable)await SaveWorkspace(next);
            layout=next;
            ApplyVisibility();
        };
    }

    private async Task<FrameworkElement[]> BuildTiles(WorkspaceTile[] tiles, IReadOnlyDictionary<string,FrameworkElement>? original=null)
    {
        await using var db=factory.Create();
        var cards=new List<FrameworkElement>();
        foreach(var tile in tiles){
            if(original!=null&&original.TryGetValue(tile.Id,out var card)){cards.Add(card);continue;}
            var info=WorkspaceLayout.Find(tile.Id)!;
            var data=await ReadTile(tile.Id,db);
            var stack=new StackPanel();
            stack.Children.Add(new TextBlock{Text=info.Title,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
            stack.Children.Add(new TextBlock{Text=data.Value,Foreground=Brush("#215CD9"),FontSize=19,Margin=new(0,6,0,0),TextWrapping=TextWrapping.Wrap});
            if(tile.Height==2&&data.Rows.Length>0)stack.Children.Add(new TextBlock{Text=string.Join("\n",data.Rows.Take(3)),TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new(0,5,0,0)});
            var button=new Button{Content=stack,Padding=new(12,7,12,7),HorizontalContentAlignment=HorizontalAlignment.Left,ToolTip=info.Title};
            if(tile.Id.StartsWith("inventory-",StringComparison.Ordinal))LanguageUi.Preserve(stack);
            button.Click+=async(_,_)=>await Run(async()=>{
                if(!CanShowTile(info))return;
                if(data.Rows.Length>0){
                    var dialog=new FormDialog(this,info.Title,680).Text("rows","Записи",string.Join("\n",data.Rows),true,true).TextHeight("rows",280).Submit("Открыть раздел");
                    if(dialog.ShowDialog()!=true)return;
                }
                if(tile.Id=="mail-queue")backupTab=1;
                if(page!=info.Page){await LoadPendingSnapshot();await ShowPage(info.Page);}
                else if(data.Rows.Length==0&&tile.Id is ("vault-status" or "mail-queue" or "inventory-stock" or "inventory-acts"))await ShowPage(info.Page);
            });
            cards.Add(button);
        }
        return cards.ToArray();
    }

    private sealed record TileData(string Value,string[] Rows);
    private async Task<TileData> ReadTile(string id,AssistantDb db)
    {
        var ct=lifetime.Token;var now=remote.Snapshot.ServerTime;var today=now.Date;
        TileData Number(int n)=>new(n.ToString(CultureInfo.CurrentCulture),[]);
        TileData List(string[] list)=>new(list.Length.ToString(CultureInfo.CurrentCulture),list.Take(200).ToArray());
        switch(id){
            case "overview-devices":return Number(await db.Devices.CountAsync(d=>d.Enabled&&d.Type!="MikroTik"&&d.Type!="Коммутатор",ct));
            case "devices-down":return List(await db.Devices.Where(d=>d.Enabled&&d.Status=="OFFLINE"&&d.Type!="MikroTik"&&d.Type!="Коммутатор").OrderBy(d=>d.Name).Select(d=>d.Name).ToArrayAsync(ct));
            case "devices-updates":{
                var rows=await db.Devices.AsNoTracking().Where(d=>d.Enabled&&d.Type!="MikroTik"&&d.Type!="Коммутатор").ToArrayAsync(ct);
                return List(rows.Where(d=>MailUpdates.Overview(d.OsUpdatesJson,d.MailUpdatesJson)?.HasUpdates==true).Select(d=>d.Name).ToArray());}
            case "services-active":return Number(await db.Services.CountAsync(ct));
            case "services-down":return List(await db.Services.Where(s=>s.Status=="OFFLINE"||s.Status=="ERROR").Select(s=>s.Name).ToArrayAsync(ct));
            case "overview-payments":return Number(await db.Payments.CountAsync(p=>!p.Paid,ct));
            case "payments-overdue":case "payments-week":{
                var rows=(await db.Payments.AsNoTracking().Where(p=>!p.Paid).ToArrayAsync(ct)).Select(p=>new{p.Title,Date=PaymentCalendar.EffectiveDueDate(p.DueDate,settings.Company.WeekendMask).Date});
                return List(rows.Where(p=>id=="payments-overdue"?p.Date<today:p.Date>=today&&p.Date<today.AddDays(7)).OrderBy(p=>p.Date).Select(p=>Display.Date(p.Date)+" · "+p.Title).ToArray());}
            case "events-warning":case "events-critical":{
                var severity=id=="events-warning"?Severity.Warning:Severity.Critical;
                return List(await db.Incidents.Where(i=>!i.Closed&&i.Severity==severity&&!db.Reports.Any(r=>r.Id==i.ReportId&&r.Provider=="DMARC")).OrderByDescending(i=>i.CreatedAt).Select(i=>i.Title).ToArrayAsync(ct));}
            case "overview-backups":case "backup-enabled":return Number(await db.Jobs.CountAsync(j=>j.Active,ct));
            case "backup-overdue":{
                var jobs=await db.Jobs.AsNoTracking().Where(j=>j.Active).ToArrayAsync(ct);
                var reports=await db.Reports.AsNoTracking().Where(r=>r.JobId!=null).ToArrayAsync(ct);
                return List(jobs.SelectMany(j=>BackupQueries.RunRows(j,reports,now)).Where(r=>r.Schedule.State==LampState.Red&&r.Schedule.Status=="Нет отчёта").Select(r=>r.Title+" · "+r.CurrentStatus).ToArray());}
            case "overview-network":case "network-total":return Number(await db.Devices.CountAsync(d=>d.Enabled&&(d.Type=="MikroTik"||d.Type=="Коммутатор"),ct));
            case "vault-status":return new(ProductText.Get("Открыть сейф"),[]);
            case "settings-monitor":return new(ProductText.Get(settings.MonitoringEnabled?"Включён":"Выключен"),[]);
            case "inventory-stock":case "inventory-acts":return !remote.InventoryOpen||page!=9?new(ProductText.Get("Требуется вход"),[]):id=="inventory-stock"
                ?Number(await db.Assets.CountAsync(a=>a.State==AssetState.Stock,ct))
                :Number(await db.Acts.CountAsync(a=>a.Date>=today.AddDays(-30),ct));
            case "dmarc-today":return Number(await db.Reports.CountAsync(r=>r.Provider=="DMARC"&&r.ReceivedAt>=today&&r.ReceivedAt<today.AddDays(1),ct));
            case "dmarc-queue":return Number(await db.Reports.CountAsync(r=>r.Provider=="DMARC"&&r.IsPending,ct));
            case "mail-queue":return Number(await db.Pending.CountAsync(p=>db.Reports.Any(r=>r.Id==p.EventId&&r.Provider!="DMARC"),ct));
            case "mail-unbound":return List(await db.Reports.Where(r=>r.Provider!="DMARC"&&r.DeviceId==null&&r.JobId==null&&r.Severity==Severity.Warning).OrderByDescending(r=>r.ReceivedAt).Select(r=>r.Task).ToArrayAsync(ct));
            case "ksc-status":{
                try{var status=await remote.GetAsync<KscSectionView>("api/ksc/status",ct);return status.Active?Number(status.Problems.Length):new(ProductText.Get("Выключен"),[]);}
                catch(ServerResponseException){return new(ProductText.Get("Нет данных"),[]);}}
            default:return new("—",[]);
        }
    }

    private async Task SaveWorkspace(WorkspacePage layout)
    {
        var result=await remote.PostAsync<WorkspacePage,WorkspacePage>("api/personal/workspace",layout,lifetime.Token);
        (workspacePages??=[])[layout.Page]=result;
    }
    private async void Workspace_Click(object sender,RoutedEventArgs e)=>await Run(EditWorkspace);
    private async Task EditWorkspace()
    {
        if(!workspaceAvailable){AppMessageBox.Show(this,"Для настройки рабочих столов обновите сервер.");return;}
        var index=page;if(index==8&&!CanRead("settings")){AppMessageBox.Show(this,"У этой учётной записи нет доступа к рабочему столу настроек.");return;}
        var original=Workspace(index);var draft=VisibleTiles(original).ToList();bool reset=false,tilesChanged=false;
        var dialog=new FormDialog(this,"Рабочий стол · "+ProductText.Get(Pages[index]),760).Submit("Сохранить раскладку")
            .Note("Перетащите карточку в предпросмотре или поменяйте порядок кнопками. Изменения вступают в силу после сохранения.")
            .Toggle("fillRows","100% ширины ряда",original.FillRows)
            .Toggle("fillHeight","100% высоты",original.FillHeight)
            .Select("current","Карточка",[],null)
            .Select("add","Добавить из раздела",[],null);
        var preview=new Border{MaxHeight=275};
        dialog.InsertWorkspacePreview(preview);
        void Render(int selected){
            var available=WorkspaceLayout.Catalog.Where(t=>CanShowTile(t)&&draft.All(d=>d.Id!=t.Id)).OrderBy(t=>t.Page).ToArray();
            dialog.Choices("current",draft.Select((t,i)=>new Choice(i,ProductText.Get(WorkspaceLayout.Find(t.Id)!.Title)+" · "+t.Width+"×"+t.Height)),draft.Count==0?null:Math.Clamp(selected,0,draft.Count-1));
            dialog.Choices("add",available.Select(t=>new Choice(t.Id,ProductText.Get(Pages[t.Page])+" · "+ProductText.Get(t.Title))),available.FirstOrDefault()?.Id);
            var views=draft.Select((tile,i)=>{
                var b=UiButton(WorkspaceLayout.Find(tile.Id)!.Title);b.MaxWidth=double.PositiveInfinity;b.AllowDrop=true;b.ToolTip="Перетащите для изменения порядка";
                Point? origin=null;
                b.PreviewMouseLeftButtonDown+=(_,a)=>origin=a.GetPosition(b);
                b.PreviewMouseLeftButtonUp+=(_,_)=>origin=null;
                b.PreviewMouseMove+=(_,a)=>{
                    if(origin is not {} from||a.LeftButton!=MouseButtonState.Pressed)return;
                    var to=a.GetPosition(b);
                    if(Math.Abs(to.X-from.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(to.Y-from.Y)<SystemParameters.MinimumVerticalDragDistance)return;
                    origin=null;DragDrop.DoDragDrop(b,new DataObject("SAA.WorkspaceTile",i),DragDropEffects.Move);
                };
                b.Drop+=(_,a)=>{if(a.Data.GetData("SAA.WorkspaceTile") is int from&&from>=0&&from<draft.Count&&from!=i){var item=draft[from];draft.RemoveAt(from);draft.Insert(i,item);Changed(i);}a.Handled=true;};
                b.Click+=(_,_)=>dialog.Choices("current",draft.Select((t,j)=>new Choice(j,ProductText.Get(WorkspaceLayout.Find(t.Id)!.Title)+" · "+t.Width+"×"+t.Height)),i);
                return (FrameworkElement)b;
            }).ToArray();
            var scroll=new ScrollViewer{Height=270,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            scroll.Content=TileGrid(draft.ToArray(),views,true,dialog.Checked("fillRows"),dialog.Checked("fillHeight")?scroll:null);
            preview.Child=scroll;
        }
        void Changed(int i){reset=false;tilesChanged=true;dialog.MarkDirty();Render(i);}
        void Move(int step){if(dialog.Selected("current") is not int i||i+step<0||i+step>=draft.Count)return;(draft[i],draft[i+step])=(draft[i+step],draft[i]);Changed(i+step);}
        void Resize(int w,int h){
            if(dialog.Selected("current") is not int i)return;
            if(!WorkspaceLayout.Fits(draft[i].Id,w,h)){AppMessageBox.Show(dialog,"Этой карточке нужен высокий размер.");return;}
            draft[i]=draft[i] with {Width=w,Height=h};Changed(i);
        }
        void RefreshPreview(){dialog.MarkDirty();Render(dialog.Selected("current") is int i?i:0);}
        dialog.ToggleChanged("fillRows",RefreshPreview);
        dialog.ToggleChanged("fillHeight",RefreshPreview);
        dialog.Action("Добавить карточку",()=>{if(dialog.Selected("add") is not string id)return;draft.Add(new(id,1,WorkspaceLayout.IsOverviewCard(id)?2:1));Changed(draft.Count-1);})
            .Action("Убрать карточку",()=>{if(dialog.Selected("current") is int i){draft.RemoveAt(i);Changed(i);}})
            .Action("← Раньше",()=>Move(-1)).Action("Позже →",()=>Move(1))
            .Action("Размер 1×1",()=>Resize(1,1)).Action("Размер 2×1",()=>Resize(2,1))
            .Action("Размер 1×2",()=>Resize(1,2)).Action("Размер 2×2",()=>Resize(2,2))
            .Action("Размер 2×4",()=>Resize(2,4)).Action("Размер 4×2",()=>Resize(4,2))
            .Action("Вернуть раскладку по умолчанию",()=>{dialog.SetChecked("fillRows",false);dialog.SetChecked("fillHeight",false);reset=true;draft=VisibleTiles(DefaultWorkspace(index)).ToList();dialog.MarkDirty();Render(0);});
        Render(0);
        if(dialog.ShowDialog()==true){await SaveWorkspace(new(index,reset?null:tilesChanged?draft.ToArray():original.Tiles,reset?DefaultWorkspace(index).Collapsed:original.Collapsed,dialog.Checked("fillRows"),dialog.Checked("fillHeight")));await ShowPage(index);}
    }
}
