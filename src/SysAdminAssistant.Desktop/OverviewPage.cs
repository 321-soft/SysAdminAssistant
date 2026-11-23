using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private TextBlock? overviewConnection;
    private bool CanRead(string module) => AccessRules.Level(remote.CurrentUser, module) >= 1;
    private Button NavigateButton(string title, int index)
    {
        var b = UiButton(title);
        b.Click += async (_, _) => await Run(() => ShowPage(index));
        return b;
    }
    private static Border OverviewCard(string title,UIElement content)
    {
        var dock=new DockPanel{Margin=new(10)};
        var header=new TextBlock{Text=title,FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,8),TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=title};DockPanel.SetDock(header,Dock.Top);dock.Children.Add(header);dock.Children.Add(content);
        return new Border{Background=DesktopTheme.Brush("#FFFFFF"),BorderBrush=Brush("#DFE7F1"),BorderThickness=new(1),CornerRadius=new(8),Margin=new(5),Child=dock};
    }
    private static DockPanel OverviewList(DataGrid table,params Button[] buttons)
    {
        table.FontSize=12;table.MinRowHeight=30;table.RowHeight=30;table.ColumnHeaderHeight=30;table.MinHeight=0;
        var cellStyle=new Style(typeof(DataGridCell),(Style)table.FindResource(typeof(DataGridCell)));cellStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(6,3,6,3)));table.CellStyle=cellStyle;
        var headerStyle=new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),(Style)table.FindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)));headerStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(6,4,6,4)));table.ColumnHeaderStyle=headerStyle;
        var root=new DockPanel();var bar=new WrapPanel{Margin=new(0,0,0,4)};
        foreach(var button in buttons){button.MinHeight=26;button.Padding=new(8,3,8,3);button.FontSize=12;button.Margin=new(0,0,6,4);bar.Children.Add(button);}
        DockPanel.SetDock(bar,Dock.Bottom);root.Children.Add(bar);root.Children.Add(table);return root;
    }
    private sealed class DeviceOverviewRow(Device entity, DeviceHealth health, string backup)
    {
        public Device Entity => entity;
        public OsUpdateStatus? Updates=>MailUpdates.Overview(entity.OsUpdatesJson,entity.MailUpdatesJson);
        public int Priority => health.Priority;
        public string Name => entity.Name;
        public string Host => entity.Host;
        public string Site => entity.Site;
        public string Status => health.Status;
        public string Detail => health.Detail;
        public LampState State => health.Lamp;
        public string Backup => backup;
        public DateTime? CheckedAt => entity.CheckedAt;
    }
    private async Task ShowOverview()
    {
        await using var db=factory.Create();var now=remote.Snapshot.ServerTime;
        var devices=CanRead("devices")?await db.Devices.AsNoTracking().Where(d=>d.Type!="MikroTik"&&d.Type!="Коммутатор").ToArrayAsync():[];
        var services=CanRead("services")?await db.Services.AsNoTracking().ToArrayAsync():[];
        var reports=CanRead("reports")?await db.Reports.AsNoTracking().Where(r=>r.Provider!="DMARC").ToArrayAsync():[];
        var incidents=CanRead("reports")?await db.Incidents.AsNoTracking().Where(i=>!i.Closed).ToArrayAsync():[];
        var jobs=CanRead("jobs")&&CanRead("reports")?await db.Jobs.AsNoTracking().ToArrayAsync():[];
        incidents=BackupIncidentLifecycle.Group(incidents,reports,jobs).Select(g=>g.Incident).ToArray();
        var reportDevices=reports.Where(r=>r.DeviceId!=null).ToDictionary(r=>r.Id,r=>r.DeviceId!.Value);
        var groups=incidents.Where(i=>i.ReportId!=null&&reportDevices.ContainsKey(i.ReportId.Value)).GroupBy(i=>reportDevices[i.ReportId!.Value]).ToDictionary(g=>g.Key,g=>g.ToArray());
        var rows=devices.Select(d=>new DeviceOverviewRow(d,DashboardState.WithProblems(d,DashboardState.Device(d,services,now,settings.PollSeconds,settings.MonitoringEnabled),groups.GetValueOrDefault(d.Id,[]),now),"")).OrderBy(r=>r.Priority).ThenBy(r=>r.Name).ToArray();

        var backups=jobs.SelectMany(j=>BackupQueries.RunRows(j,reports,now)).OrderBy(r=>r.DisplayState==LampState.Red?0:r.DisplayState==LampState.Amber?1:2).ThenBy(r=>r.Title).ToArray();
        var invoiceLinks=CanRead("payments")?(await OptionalInvoiceIndex<InvoicePaymentLink>("api/invoices/payment-links")).ToDictionary(l=>l.PaymentId):new Dictionary<int,InvoicePaymentLink>();
        var bills=CanRead("payments")?(await db.Payments.AsNoTracking().Where(p=>!p.Paid).ToArrayAsync()).Select(p=>new PaymentRow(p,settings.Company.WeekendMask,settings.Company.PaymentSoonDays,link:invoiceLinks.GetValueOrDefault(p.Id))).OrderBy(p=>p.Priority).ThenBy(p=>p.SortDate).ToArray():[];
        var routers=CanRead("routers")?await remote.CommandAsync<RouterMonitorRow[]>("router-monitor-list",new{},lifetime.Token):[];
        routers=routers.OrderBy(r=>r.State==LampState.Red?0:r.State==LampState.Amber?1:2).ThenBy(r=>r.Name).ToArray();
        var states=rows.Select(r=>r.State).Concat(backups.Select(r=>r.DisplayState)).Concat(bills.Select(r=>r.State)).Concat(routers.Select(r=>r.State)).ToArray();
        var root=new Grid();root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        var summary=new WrapPanel{Margin=new(14,8,14,8)};
        void Count(string label,string value,string color){var line=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,26,0)};line.Children.Add(new TextBlock{Text="●",Foreground=Brush(color),FontSize=18,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,10,0)});line.Children.Add(new TextBlock{Text=value,FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new(0,0,8,0)});line.Children.Add(new TextBlock{Text=label,Foreground=Brush("#64748B"),VerticalAlignment=VerticalAlignment.Center});summary.Children.Add(line);}
        Count("критичных",states.Count(s=>s==LampState.Red).ToString(),"#DC2626");Count("предупреждений",states.Count(s=>s==LampState.Amber).ToString(),"#CC7000");Count("в порядке",states.Count(s=>s==LampState.Green).ToString(),"#16A34A");
        overviewConnection=new TextBlock{Text="Снимок "+Display.Stamp(now),FontSize=12,Foreground=Brush("#71829D"),VerticalAlignment=VerticalAlignment.Center,Margin=new(0,6,0,6)};summary.Children.Add(overviewConnection);
        var strip=new Border{Background=DesktopTheme.Brush("#FFFFFF"),CornerRadius=new(10),BorderThickness=new(1),BorderBrush=Brush("#DFE7F1"),Margin=new(0,0,0,14),Child=summary,ToolTip="Количество объектов по текущему состоянию. Серый — нет подтверждённых данных."};root.Children.Add(strip);
        // A bounded 2x2 star grid fills the remaining viewport; only each list scrolls.
        var board=new Grid();Grid.SetRow(board,1);root.Children.Add(board);
        var cards=new List<FrameworkElement>();
        void Place(string title,int total,StackPanel list,int pageIndex,bool allowed){
            var dock=new DockPanel();var header=new Grid{Margin=new(16,13,12,10)};header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            header.Children.Add(new TextBlock{Text=title+"  "+total,FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,6,0)});
            var link=NavigateButton("Все →",pageIndex);link.IsEnabled=allowed;link.Background=Brushes.Transparent;link.BorderThickness=new(0);link.Foreground=Brush("#215CD9");link.Padding=new(8,5,8,5);link.Margin=new(0);link.MinHeight=30;Grid.SetColumn(link,1);header.Children.Add(link);DockPanel.SetDock(header,Dock.Top);dock.Children.Add(header);
            if(!allowed)list.Children.Add(new TextBlock{Text="Нет доступа к разделу",Foreground=DesktopTheme.Brush("#64748B"),Margin=new(16)});
            else if(list.Children.Count==0)list.Children.Add(new TextBlock{Text="Здесь пока нет записей",Foreground=Brush("#71829D"),Margin=new(16,22,16,22)});
            dock.Children.Add(new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            cards.Add(new Border{Child=dock,Background=DesktopTheme.Brush("#FFFFFF"),CornerRadius=new(10),BorderBrush=Brush("#DFE7F1"),BorderThickness=new(1),Margin=new(0),MinHeight=0});
        }
        var dl=new StackPanel();foreach(var row in rows)dl.Children.Add(DashboardLine(row.Name,row.Host,row.Status,row.Detail,row.State,()=>ShowDeviceCard(row.Entity),badge:LiveDashboardUpdate(row.Entity),menu:DeviceDashboardMenu(row.Entity)));Place("Устройства",rows.Length,dl,1,CanRead("devices"));
        var bl=new StackPanel();foreach(var row in backups)bl.Children.Add(DashboardLine(row.Title,row.BackupSystem,row.CurrentStatus,row.LastResultDetails,row.DisplayState,()=>ShowHistory(null,row.Entity.Id),wrapDetail:true,menu:BackupDashboardMenu(row.Entity)));Place("Резервное копирование",jobs.Length,bl,5,CanRead("jobs")&&CanRead("reports"));
        var pl=new StackPanel();foreach(var row in bills)pl.Children.Add(DashboardLine(row.Title,"Оплатить до "+row.Date,row.Status,"",row.State,()=>ShowPaymentCard(row.Entity),menu:PaymentDashboardMenu(row.Entity,row.HasDocument)));Place("Счета",bills.Length,pl,3,CanRead("payments"));
        var nl=new StackPanel();foreach(var row in routers)nl.Children.Add(DashboardLine(row.Name,row.Host,row.Status,row.Error,row.State,()=>ShowNetworkCard(row),row.Updates,menu:RouterDashboardMenu(row)));Place("Коммутаторы",routers.Length,nl,6,CanRead("routers"));
        if(Workspace(0).Tiles!=null){
            var original=WorkspaceLayout.Defaults(0).Select((t,i)=>(t.Id,Card:cards[i])).ToDictionary(x=>x.Id,x=>x.Card);
            var tiles=VisibleTiles(Workspace(0));var customized=await BuildTiles(tiles,original);
            var layout=Workspace(0);
            var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            scroll.Content=TileGrid(tiles,customized,fillRows:layout.FillRows,heightViewport:layout.FillHeight?scroll:null);
            root.Children.Remove(board);Grid.SetRow(scroll,1);root.Children.Add(scroll);
        }else{
            var permitted=new[]{CanRead("devices"),CanRead("jobs")&&CanRead("reports"),CanRead("payments"),CanRead("routers")};
            var visible=cards.Where((_,i)=>permitted[i]).ToArray();
            foreach(var card in visible){card.Margin=new(5);board.Children.Add(card);}
            int last=0;
            void Reflow(double width){
                int columns=width<420?1:width>=1450?4:width>=1150?3:2;
                if(columns==last)return;last=columns;
                board.ColumnDefinitions.Clear();board.RowDefinitions.Clear();
                var fill=Workspace(0).FillRows;
                int tracks=fill?12:columns; // Common multiple for rows of one to four cards.
                for(int i=0;i<tracks;i++)board.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
                for(int i=0;i<(visible.Length+columns-1)/columns;i++)board.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
                for(int i=0;i<visible.Length;i++){
                    int row=i/columns, rowCount=Math.Min(columns,visible.Length-row*columns);
                    int span=fill?tracks/rowCount:1;
                    Grid.SetRow(visible[i],row);Grid.SetColumn(visible[i],i%columns*span);Grid.SetColumnSpan(visible[i],span);
                }
            }
            Reflow(800);board.SizeChanged+=(_,e)=>{if(e.WidthChanged)Reflow(e.NewSize.Width);};
        }
        PageHost.Children.Add(root);
    }
    private FrameworkElement DashboardLine(string title,string subtitle,string status,string detail,LampState state,Func<Task> open,OsUpdateStatus? updates=null,FrameworkElement? badge=null,bool wrapDetail=false,ContextMenu? menu=null)
    {
        var grid=new Grid{Margin=new(0)};grid.ColumnDefinitions.Add(new(){Width=new(20)});grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        grid.Children.Add(new System.Windows.Shapes.Ellipse{Width=10,Height=10,Fill=state==LampState.Amber?Brush("#CC7000"):LampBrush(state),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left});
        var name=new StackPanel{Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};name.Children.Add(new TextBlock{Text=title,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=title});name.Children.Add(new TextBlock{Text=subtitle,FontSize=12,Foreground=Brush("#71829D"),TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=subtitle,Margin=new(0,4,0,0)});if(updates?.HasUpdates==true){var icon=new TextBlock{Text="↻",Foreground=Brush("#215CD9"),FontSize=12,Margin=new(0,4,0,0),Cursor=System.Windows.Input.Cursors.Hand,Focusable=true,HorizontalAlignment=HorizontalAlignment.Left,ToolTip=UpdateDetails(updates)};void ShowUpdates()=>new FormDialog(this,"Обновления ОС").Note(UpdateDetails(updates)).ViewOnly().ShowDialog();icon.MouseLeftButtonUp+=(_,e)=>{e.Handled=true;ShowUpdates();};icon.KeyDown+=(_,e)=>{if(e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space){e.Handled=true;ShowUpdates();}};if(SystemParameters.ClientAreaAnimation)icon.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(.55,1,TimeSpan.FromSeconds(1.5)){AutoReverse=true,RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever});icon.Unloaded+=(_,_)=>icon.BeginAnimation(UIElement.OpacityProperty,null);name.Children.Add(icon);}if(badge!=null)name.Children.Add(badge);Grid.SetColumn(name,1);grid.Children.Add(name);
        var result=new StackPanel{VerticalAlignment=VerticalAlignment.Center};result.Children.Add(new TextBlock{Text=status,Foreground=LampBrush(state),TextWrapping=TextWrapping.Wrap});if(!string.IsNullOrWhiteSpace(detail))result.Children.Add(new TextBlock{Text=detail,FontSize=12,Foreground=Brush("#71829D"),TextWrapping=wrapDetail?TextWrapping.Wrap:TextWrapping.NoWrap,TextTrimming=wrapDetail?TextTrimming.None:TextTrimming.CharacterEllipsis,ToolTip=detail,Margin=new(0,4,0,0)});Grid.SetColumn(result,2);grid.Children.Add(result);
        var line=new Button{Content=grid,HorizontalContentAlignment=HorizontalAlignment.Stretch,Background=Brushes.Transparent,BorderThickness=new(0,0,0,1),BorderBrush=Brush("#EDF1F7"),Padding=new(16,11,16,11),Margin=new(0),MinHeight=62,ToolTip="Открыть: "+title};line.ContextMenu=menu;line.Click+=async(_,_)=>await Run(open);return line;
    }
    private Task ShowPaymentDetails(Payment p) {
        new FormDialog(this,p.Title).Text("date","Срок оплаты",Display.Date(PaymentCalendar.EffectiveDueDate(p.DueDate,settings.Company.WeekendMask)),false,true)
            .Text("notes","Заметки",p.Notes,true,true).ViewOnly().ShowDialog();return Task.CompletedTask;
    }
    private async Task<Func<Task>> AddDeviceProblems(TabControl tabs,Device device)
    {
        var gate=new AsyncActionGate();async Task RunCard(Func<Task> action){try{await gate.TryRunAsync(action);}catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}catch(Exception e){AppMessageBox.Show(this,e.Message,"Карточка устройства",MessageBoxButton.OK,MessageBoxImage.Error);}}
        var table=Table(Array.Empty<UnifiedDeviceProblem>(),("Дата","Time","dd.MM.yyyy HH:mm:ss"),("Уровень","Level",null),("Канал события","Source",null),("Событие","Title",null));
        var details=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=120,MaxHeight=240,Margin=new(0,8,0,8)};var count=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(8)};var resolved=UiButton("Решено");var open=UiButton("Открыть письмо");var refresh=UiButton("Обновить список");var tab=new TabItem{Header="Проблемы"};
        void Selection(){var row=table.SelectedItem as UnifiedDeviceProblem;open.Visibility=row?.IsMail==true?Visibility.Visible:Visibility.Collapsed;open.IsEnabled=row?.Mail!=null;resolved.Visibility=row?.CanResolve==true?Visibility.Visible:Visibility.Collapsed;resolved.IsEnabled=row!=null&&row.CanResolve&&AccessRules.Level(remote.CurrentUser,row.IsMail?"reports":"devices")>=2;if(row!=null)details.Text=row.Details;}
        async Task Load(){var problems=await remote.CommandAsync<UnifiedDeviceProblem[]>("device-problems",new{DeviceId=device.Id},lifetime.Token);table.ItemsSource=problems;count.Text=$"Критических: {problems.Count(p=>p.Severity==Severity.Critical)} · предупреждений: {problems.Count(p=>p.Severity==Severity.Warning)}. Состояния восстанавливаются по свежим данным; история журналов сохранена в агенте.";tab.Header=$"Проблемы · {problems.Length}";details.Text=problems.Length==0?"Открытых событий нет. Доступность и состояние других проверок учитываются отдельно.":"Выберите строку для просмотра причины.";Selection();}
        table.SelectionChanged+=(_,_)=>Selection();open.Click+=async(_,_)=>{if(table.SelectedItem is UnifiedDeviceProblem {Mail:not null} p)await RunCard(()=>ShowMail(p.Mail));};table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=async(_,_)=>{if(table.SelectedItem is UnifiedDeviceProblem p){if(p.Mail!=null)await RunCard(()=>ShowMail(p.Mail));else new FormDialog(this,p.Title,850).Text("details","Причина и исходные события",p.Details,true,true).ViewOnly().ShowDialog();}};
        refresh.Click+=async(_,_)=>await RunCard(Load);resolved.Click+=async(_,_)=>await RunCard(async()=>{if(table.SelectedItem is not UnifiedDeviceProblem p||!p.CanResolve)return;resolved.IsEnabled=false;if(p.IsMail)await remote.CommandAsync<bool>("device-resolve",new ResolveDeviceProblem(device.Id,p.IncidentId!.Value),lifetime.Token);else await remote.CommandAsync<bool>("agent-event-resolve",new AgentEventResolve(device.Id,p.JournalId),lifetime.Token);await remote.RefreshAsync(lifetime.Token);await Load();});
        var root=new DockPanel{Margin=new(12)};DockPanel.SetDock(count,Dock.Top);root.Children.Add(count);var bottom=new StackPanel();var buttons=new WrapPanel();buttons.Children.Add(refresh);buttons.Children.Add(open);buttons.Children.Add(resolved);bottom.Children.Add(buttons);bottom.Children.Add(details);DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);root.Children.Add(table);tab.Content=root;tabs.Items.Add(tab);await Load();if(table.Items.Count>0)tabs.SelectedItem=tab;return Load;
    }
    private async Task ShowDeviceCard(Device device,string? initialTab=null)
    {
        if(NetworkDevices.IsNetwork(device.Type)){
            var routers=await remote.CommandAsync<RouterMonitorRow[]>("router-monitor-list",new{},lifetime.Token);
            var router=routers.SingleOrDefault(r=>r.Id==device.Id);if(router!=null)await ShowNetworkCard(router);return;
        }
        await using var db=factory.Create();
        var tabs=new TabControl();
        void Add(string title,UIElement content)=>tabs.Items.Add(new TabItem{Header=title,Content=content});
        var info=new StackPanel {Margin=new(16)};info.Children.Add(DeviceConnectionButtons(device));
        foreach(var text in new[]{device.Name,device.Host,device.Site,device.Role,"Последняя проверка: "+Display.Stamp(device.CheckedAt),"Последний ответ: "+Display.Stamp(device.LastSeen),device.LastError})
            info.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)});
        Add("Устройство",info);
        if(CanRead("devices")){
            Add("Агент",await AgentPanel(device));
            var hardware=await remote.CommandAsync<AgentViewResult>("agent-view",new{DeviceId=device.Id},lifetime.Token);
            Add("Характеристики",new TextBox{Text=HardwareText(hardware.Agent?.Report?.Hardware),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(16)});
        }
        Func<Task> refreshProblems=()=>Task.CompletedTask;
        if(CanRead("devices"))refreshProblems=await AddDeviceProblems(tabs,device);
        if(CanRead("services"))Add("Сервисы",Table(await db.Services.AsNoTracking().Where(s=>s.DeviceId==device.Id).ToArrayAsync(),("Сервис","Name",null),("Адрес","Address",null),("Состояние","Status",null)));
        if(CanRead("jobs"))Add("Задания",Table(await db.Jobs.AsNoTracking().Where(j=>j.DeviceId==device.Id).ToArrayAsync(),("Задание","Title",null),("Адрес","Host",null),("Активно","Active",null)));
        if(CanRead("reports")){
            var history=Table((await db.Reports.AsNoTracking().Where(r=>r.DeviceId==device.Id && r.Provider!="DMARC").OrderByDescending(r=>r.ReceivedAt).Take(200).ToArrayAsync()).Select(r=>new HistoryRow(r)),("Дата","Date",null),("Почтовый профиль","Provider",null),("Задание","Task",null),("Результат","Result",null),("Уровень","Severity",null));
            history.SetValue(HasCustomDoubleClickProperty,true);history.MouseDoubleClick+=async(_,_)=>{if(history.SelectedItem is HistoryRow row)await ShowMail(row.Entity);};
            var reanalyze=UiButton("Разобрать письма устройства");reanalyze.IsEnabled=AccessRules.Level(remote.CurrentUser,"reports")>=2&&AccessRules.Level(remote.CurrentUser,"devices")>=2;
            reanalyze.Click+=async(_,_)=>{reanalyze.IsEnabled=false;try{await ReanalyzeDevice(device.Id);await refreshProblems();}catch(Exception e){AppMessageBox.Show(this,e.Message,"Разбор писем");}finally{reanalyze.IsEnabled=true;}};
            var content=ListSurface(history,new[]{reanalyze},search:true);
            Add("Отчёты · последние 200",content);
        }
        if(CanRead("inventory")&&remote.InventoryOpen&&inventoryUntil>DateTime.UtcNow)Add("Имущество",Table(await db.Assets.AsNoTracking().Where(a=>a.DeviceId==device.Id).ToArrayAsync(),("Инвентарный номер","InventoryNumber",null),("Категория","Category",null),("Серийный номер","SerialNumber",null)));
        if(CanRead("vault")){
            var entries=(await remote.GetAsync<VaultItem[]>("api/personal/vault",lifetime.Token)).Where(v=>v.DeviceId==device.Id).ToArray();
            var table=Table(entries,("Запись","Name",null),("Владелец","OwnerLogin",null));
            
            // Metadata only: secrets stay behind the existing vault unlock flow.
            table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=async(_,_)=>{
                if(table.SelectedItem is not VaultItem entry)return;
                try {
                    await OpenCredential(entry);
                } catch(Exception e){AppMessageBox.Show(this,e.Message,"Хранилище");}
            };
            Add("Пароли · двойной щелчок для просмотра",TableSurface(table));
        }
        var hardwareHistory=UiButton("Открыть историю изменений");
        hardwareHistory.Click+=async(_,_)=>await Run(()=>ShowHardwareHistory("device",device.Id.ToString()));
        Add("Изменения оборудования",hardwareHistory);
        if(initialTab!=null)tabs.SelectedItem=tabs.Items.Cast<TabItem>().FirstOrDefault(t=>Equals(t.Header,initialTab))??tabs.Items[0];
        new DialogWindow{Owner=this,Title="Устройство · "+device.Name,Width=1000,Height=600,Content=tabs,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();
        await ShowPage(page);
    }
}
