using System.Windows;
using System.Windows.Controls;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private bool deniedStations;
    private sealed record StationRow(StationView Station,string Name,string Serial,string Ip,string Os,string Uptime,string User,string Agent,string Updates,string Seen,string State)
    {
        public long? UptimeSort=>Station.Sample?.Uptime;
        public DateTime SeenSort=>Station.Seen;
        public string? IpSort=>Station.Sample?.Addresses.FirstOrDefault()?.Address;
        public Version? AgentSort=>Version.TryParse(Station.Sample?.AgentVersion,out var value)?value:null;
    }
    private static string UpdateText(StationSample? s,StationConfig config)
    {
        var text=s?.Updates switch{
            {RebootRequired:true}=>"↻ Требуется перезагрузка",
            {PendingCount:>0} u=>$"⬆ Windows: {u.PendingCount}",
            {State:"Current",Stale:false}=>"Windows: актуально",
            _=>"—"
        };
        if(Version.TryParse(s?.AgentVersion,out var current)&&Version.TryParse(config.LatestAgent,out var next)&&next>current)text+=" · ⬆ Агент: "+next;
        return text;
    }
    private static string FormatStationUptime(long seconds)
    {
        var uptime = TimeSpan.FromSeconds(seconds);
        return $"{uptime.Days} д. {uptime.Hours:00} ч. {uptime.Minutes:00} мин.";
    }
    private async Task ShowWorkstations()
    {
        var admin=remote.CurrentUser.Administrator;
        var denied=admin&&deniedStations;
        var entries=await remote.CommandAsync<StationView[]>(denied?"workstations-denied":"workstations-list",new{},lifetime.Token);
        var c=await remote.CommandAsync<StationConfig>("workstations-config",new{},lifetime.Token);
        var rows=entries.Select(s=>{
            var m=s.Sample;
            var ips=m?.Addresses.Select(a=>a.Address).Distinct().ToArray()??[];
            var user=m?.User;
            var stale=DateTime.UtcNow-s.Seen>TimeSpan.FromMinutes(10);
            var state=s.Status switch{"Pending"=>"Ожидает подтверждения","Denied"=>"Отказано",_=>s.Alerts.FirstOrDefault()?.Title??(m==null?"Ожидается отчёт":stale?"Нет свежих данных":"На связи")};
            return new StationRow(s,s.Name,HardwareIdentity.Serial(m?.Serial) is {Length:>0} actualSerial?actualSerial:"—",(ips.FirstOrDefault()??"—")+(ips.Length>1?" +"+(ips.Length-1):""),m?.Os??"—",
                m==null?"—":FormatStationUptime(m.Uptime),
                m==null?"—":user==null?"Нет активного локального сеанса":stale?"Последний: "+user:user,
                m?.AgentVersion??"—",UpdateText(m,c),Display.Stamp(AppTime.FromUtc(s.Seen)),state);
        }).ToArray();
        var table=Table(rows,("Имя","Name",null),("Серийный номер","Serial",null),("IP-адрес","Ip",null),("Версия ОС","Os",null),("Аптайм","Uptime",null),
            ("Активный пользователь","User",null),("Версия агента","Agent",null),("Обновления","Updates",null),
            ("Последний контакт","Seen",null),("Состояние","State",null));
        table.SelectionMode=DataGridSelectionMode.Extended;
        var actions=new List<Button>{RowAction(table,"Карточка",()=>{ShowStationCard(Selected<StationRow>(table));return Task.CompletedTask;})};
        table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=(_,_)=>{if(table.SelectedItem is StationRow row)ShowStationCard(row);};
        if(admin){

            if(denied)actions.Add(RowAction(table,"Удалить",()=>Decide("delete"),true));
            else{
                actions.Add(RowAction(table,"Подтвердить",()=>Decide("approve"),true,()=>table.SelectedItems.Cast<StationRow>().Any(r=>r.Station.Status=="Pending")));
                actions.Add(RowAction(table,"Отказать",()=>Decide("deny"),true));
                actions.Add(RowAction(table,"Автообновление…",async ()=>{
                    var stations=table.SelectedItems.Cast<StationRow>().Select(r=>r.Station).ToArray();
                    if(stations.Any(s=>s.Status!="Approved"))throw new InvalidOperationException("Выберите только подтверждённые станции.");
                    var setup=new FormDialog(this,"Автообновление · "+stations.Length,550)
                        .Toggle("enabled","Разрешить установку подписанных обновлений",stations.All(s=>s.AutoUpdate));
                    if(setup.ShowDialog()!=true)return;
                    await ApplyBatch(stations,s=>remote.CommandAsync<bool>("workstations-autoupdate",new StationUpdatePolicy(s.Id,setup.Checked("enabled")),lifetime.Token));
                    await ShowPage(13);
                },true,()=>table.SelectedItems.Cast<StationRow>().All(r=>r.Station.Status=="Approved")));
                actions.Add(ActionButton("Создать установщик MSI",SaveWorkstationInstaller));
                actions.Add(ActionButton("Развёртывание GPO",()=>EditWorkstations(false)));
                actions.Add(ActionButton("Telegram рабочих станций",()=>EditWorkstations(true)));
            }
        }
        async Task Decide(string action)
        {
            var stations=table.SelectedItems.Cast<StationRow>().Select(r=>r.Station).ToArray();
            if(action=="approve")stations=stations.Where(s=>s.Status=="Pending").ToArray();
            if(stations.Length==0)throw new InvalidOperationException("Нет подходящих станций среди выбранных.");
            if(!Confirm(this,$"Применить действие «{(action=="approve"?"Подтвердить":action=="deny"?"Отказать":"Удалить")}» к станциям: {stations.Length}?"))return;
            await ApplyBatch(stations,s=>remote.CommandAsync<bool>("workstations-"+action,s.Id,lifetime.Token));
            await ShowPage(13);
        }
        if(AccessRules.Level(remote.CurrentUser,"inventory")>=2)
            actions.Add(RowAction(table,"Создать объект инвентаризации",async()=>{
                var s=Selected<StationRow>(table).Station;
                if(s.Status!="Approved")throw new InvalidOperationException("Сначала подтвердите станцию.");
                await CreateInventoryFromStation(s);
            }));
        ActionContextMenu(table,actions);
        var filter=new ComboBox{ItemsSource=new[]{"Все станции","Отклонённые"}.Select(ProductText.Get).ToArray(),SelectedIndex=denied?1:0,MinWidth=180,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,8),Visibility=admin?Visibility.Visible:Visibility.Collapsed};
        filter.SelectionChanged+=async(_,_)=>{deniedStations=filter.SelectedIndex==1;await Run(()=>ShowPage(13));};
        PageHost.Children.Add(ListSurface(table,actions,filter,search:true));
    }
    private async Task SaveWorkstationInstaller()
    {
        var dialog=new Microsoft.Win32.SaveFileDialog{Title="Сохранить общий установщик рабочих станций",
            FileName="SysAdminAssistant_Workstation_x64.msi",Filter="Пакет Windows Installer (*.msi)|*.msi",
            DefaultExt=".msi",AddExtension=true,OverwritePrompt=true};
        if(dialog.ShowDialog(this)!=true)return;
        var setupSource=Path.Combine(AppContext.BaseDirectory,"WorkstationSetupLauncher.exe");
        if(!File.Exists(setupSource))throw new FileNotFoundException("В комплекте клиента отсутствует графический установщик рабочих станций.",setupSource);
        var setupTarget=Path.ChangeExtension(dialog.FileName,".Setup.exe");
        if(File.Exists(setupTarget)&&AppMessageBox.Show(this,ProductText.Get("Заменить существующий графический установщик?")+"\n"+setupTarget,"Рабочие станции",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        var temp=dialog.FileName+"."+Guid.NewGuid().ToString("N")+".msi";
        try{
            await remote.SaveWorkstationInstallerAsync(temp,lifetime.Token);
            var trust=await remote.WorkstationTrustAsync(lifetime.Token);
            var signed=AgentTrust.Verify(trust.Trust,trust.AuthorityCertificate);
            if(!signed.Pins.Contains(remote.ServerFingerprint,StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Сертификат сервера не подтверждён");
            var enrollment=await remote.CommandAsync<string>("workstations-enrollment",new{},lifetime.Token);
            WorkstationMsi.Configure(temp,remote.Address,remote.ServerFingerprint,trust.AuthorityCertificate,enrollment);
            File.Move(temp,dialog.FileName,true);
            WorkstationInstallerBundle.Build(setupSource,dialog.FileName,setupTarget);
        }finally{if(File.Exists(temp))File.Delete(temp);}
        AppMessageBox.Show(this,"Общий MSI и графический Setup.exe сохранены. Параметры подключения записаны в MSI.","Рабочие станции");
    }
    private void ShowStationCard(StationRow row)
    {
        var dialog=new FormDialog(this,row.Name,780).Tab("Основное").Fact("Состояние",row.State).Fact("Последний контакт",row.Seen);
        if(row.Station.Sample is {} m){
            string Percent(double? x)=>x is double v?$"{v:0.0}%":"Нет данных";
            dialog.Fact("Серийный номер",row.Serial).Fact("Версия ОС",row.Os).Fact("Аптайм на момент отчёта",row.Uptime).Fact("Активный пользователь",row.User)
                .Fact("Версия агента",row.Agent).Fact("CPU",Percent(m.Cpu)).Fact("RAM",Percent(m.Ram)).Fact("Свободно на системном диске",Percent(m.DiskFree))
                .Tab("IP-адреса").Text("ips","Адаптеры",string.Join("\n",m.Addresses.Select(a=>$"{a.Adapter} · {a.Address}")),true,true)
                .Tab("Обновления").Fact("Доступность",row.Updates)
                .Fact("Последняя проверка Windows Update",m.Updates?.CheckedUtc is {} at?Display.Stamp(AppTime.FromUtc(at)):"Нет данных")
                .Text("updates","Обновления Windows",string.Join("\n",m.Updates?.Items??[]),true,true)
                .Tab("Проблемы").Text("alerts","Активные проблемы",string.Join("\n",row.Station.Alerts.Select(a=>$"{a.Title} · {Display.Stamp(AppTime.FromUtc(a.Since))}")),true,true);
        }
        dialog.Tab("Характеристики").Text("hardware","Аппаратные сведения",HardwareText(row.Station.Sample?.Hardware,row.Station.Sample?.Serial),true,true).FieldHeight("hardware",300);
        AddStationPeripherals(dialog,row.Station.Sample?.Peripherals);
        dialog.Tab("Изменения оборудования").ActionAsync("Открыть историю изменений",()=>ShowHardwareHistory("station",row.Station.Id.ToString(),dialog));
        dialog.ViewOnly().ShowDialog();
    }
    private async Task EditWorkstations(bool telegram)
    {
        var c=await remote.CommandAsync<StationConfig>("workstations-config",new{},lifetime.Token);
        var f=new FormDialog(this,telegram?"Telegram рабочих станций":"Развёртывание GPO",680);
        if(telegram){
            f.Note("Токен бота всегда берётся из настроек сервера. Можно использовать общий чат или указать отдельный чат и тему для рабочих станций.")
                .Toggle("enabled","Отправлять уведомления",c.Telegram)
                .Toggle("shared","Использовать настройки Telegram сервера",c.UseServerTelegram)
                .Text("chat","Chat ID",c.Chat)
                .Text("thread","ID темы (необязательно)",c.Thread?.ToString()??"")
                .Toggle("critical","Критические",c.Critical).Toggle("warning","Предупреждения",c.Warning);
            void RefreshDestination(){
                var separate=!f.Checked("shared");
                f.FieldEnabled("chat",separate);
                f.FieldEnabled("thread",separate);
            }
            f.ToggleChanged("shared",RefreshDestination);
            RefreshDestination();
        }
        else f.Note("Один MSI для всех рабочих станций. Каждая станция требует ручного подтверждения.")
            .Toggle("rotate","Выпустить новый ключ регистрации",false).Text("version","Доступная версия агента",c.LatestAgent);
        string key="";
        f.SaveAsync=async()=>{
            if(telegram){
                var t=f.Value("thread").Trim();
                var thread=t==""?null:int.TryParse(t,out var n)&&n>0?(int?)n:null;
                var shared=f.Checked("shared");
                if(!shared&&t!=""&&thread is null)throw new ArgumentException("ID темы должен быть положительным числом");
                var chat=f.Value("chat").Trim();
                c=c with{Telegram=f.Checked("enabled"),UseServerTelegram=shared,
                    Chat=shared?c.Chat:chat,Thread=shared?c.Thread:thread,
                    Critical=f.Checked("critical"),Warning=f.Checked("warning")};
            }else c=c with{Rotate=f.Checked("rotate"),LatestAgent=f.Value("version").Trim()};
            key=await remote.CommandAsync<string>("workstations-save",c,lifetime.Token);return true;
        };
        if(f.ShowDialog()==true&&key!="")new FormDialog(this,"Ключ регистрации",700)
            .Note("Скопируйте ключ в параметр Enrollment политики компьютера. Повторно он не показывается.")
            .Text("key","Enrollment",key,false,true).ViewOnly().ShowDialog();
    }
}
