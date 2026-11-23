using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private sealed record RouterPort(string Name,LampState State,string Detail);
    private sealed class RouterDisplayRow(RouterMonitorRow entity)
    {
        public RouterMonitorRow Entity=>entity;public OsUpdateStatus? Updates=>entity.Updates;public string Name=>entity.Name;public string Host=>entity.Host;public string Model=>entity.Model;public string Version=>entity.Version;public string Cpu=>entity.Cpu;public string Uptime=>entity.Uptime;public string Status=>entity.Status;public DateTime? CheckedAt=>entity.CheckedAt;
        public LampState State=>entity.State;
        public RouterPort[] Ports=>entity.Snapshot?.Interfaces.Where(HasInterfaceName).Select(p=>new RouterPort(p.GetValueOrDefault("name","?"),p.GetValueOrDefault("disabled") is "true" or "yes"?LampState.Gray:p.GetValueOrDefault("running") is "true" or "yes"?LampState.Green:LampState.Amber,string.Join(" · ",new[]{p.GetValueOrDefault("name",""),p.GetValueOrDefault("type",""),"RX "+p.GetValueOrDefault("rx-byte","—"),"TX "+p.GetValueOrDefault("tx-byte","—"),p.GetValueOrDefault("comment","")}))).ToArray()??[];
    }
    private async Task ShowRouter()
    {
        var rows=(await remote.CommandAsync<RouterMonitorRow[]>("router-monitor-list",new{},lifetime.Token)).Select(r=>new RouterDisplayRow(r)).ToArray();
        var table=Table(rows,("MikroTik","Name",null),("Адрес","Host",null),("Модель","Model",null),("RouterOS","Version",null),("Проверено","CheckedAt","dd.MM HH:mm"),("Состояние / ошибка","Status",null));table.Columns.Insert(0,LampColumn("State"));table.Columns.Insert(1,UpdatesColumn());
        table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=(_,_)=>{if(table.SelectedItem is RouterDisplayRow row)_=Run(()=>ShowNetworkCard(row.Entity));};
        var check=ActionButton("Проверить MikroTik сейчас",async()=>{await remote.CommandAsync<RouterMonitorRow[]>("router-monitor-refresh",new{},lifetime.Token);await ShowPage(6);});
        var actions=new TabControl{Margin=new Thickness(0,0,0,8)};
        void Group(string title,string hint,params Button[] buttons){
            var panel=new StackPanel{Margin=new Thickness(8)};
            panel.Children.Add(new TextBlock{Text=hint,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6)});
            var row=new WrapPanel();foreach(var b in buttons)row.Children.Add(b);panel.Children.Add(row);
            actions.Items.Add(new TabItem{Header=title,Content=panel});
        }
        Group("Наблюдение","Выберите устройство в списке. Двойной щелчок открывает его проблемы, историю, логи и интерфейсы.",
            RowAction(table,"Открыть состояние и события",()=>ShowNetworkCard(Selected<RouterDisplayRow>(table).Entity)),
            RowAction(table,"Анализ конфигурации",()=>ShowRouterAudit(Selected<RouterDisplayRow>(table).Entity)),
            RowAction(table,"Интерфейсы",()=>{ShowRouterDetails(Selected<RouterDisplayRow>(table).Entity);return Task.CompletedTask;}),check);
        Group("Устройства","Добавление оборудования, его адрес и учётная запись. Действия выполняются только в Assistant; RouterOS остаётся доступен только для чтения.",
            ActionButton("Добавить MikroTik",()=>EditMikroTik(new(){Type="MikroTik"})),ActionButton("Добавить коммутатор",()=>EditDevice(new(){Type="Коммутатор"})),
            RowAction(table,"Создать объект инвентаризации",async()=>{await using var db=factory.Create();await CreateInventoryFromDevice(await db.Devices.SingleAsync(d=>d.Id==Selected<RouterDisplayRow>(table).Entity.Id!.Value));}),
            RowAction(table,"Свойства и подключение",async()=>{await using var db=factory.Create();await EditDevice(await db.Devices.SingleAsync(d=>d.Id==Selected<RouterDisplayRow>(table).Entity.Id!.Value));}),
            RowAction(table,"Удалить из Assistant",async()=>{await using var db=factory.Create();var device=await db.Devices.SingleAsync(d=>d.Id==Selected<RouterDisplayRow>(table).Entity.Id!.Value);await DeleteCatalogEntity(device,device.Name);}));
        Group("Правила и уведомления","Профиль задаёт начальные правила. В редакторе отдельно фильтруются интерфейсы и логи; для каждого правила выбираются важность, задержка и Telegram.",
            RowAction(table,"Правила выбранного MikroTik",()=>EditNetworkRules(Selected<RouterDisplayRow>(table).Entity)),
            RowAction(table,"Профиль мониторинга",()=>EditNetworkConnection(Selected<RouterDisplayRow>(table).Entity)));
        PageHost.Children.Add(ListSurface(table,Array.Empty<Button>(),actions,true));
        PageSubtitle.Text="Наблюдение → состояние · Устройства → подключение · Правила и уведомления → условия тревог";
    }
    private async Task EditNetworkConnection(RouterMonitorRow router)
    {
        if(router.Kind!="MikroTik"){AppMessageBox.Show(this,"Для обычного коммутатора доступен контроль ICMP. Для RouterOS выберите тип MikroTik в карточке оборудования.","Подключение");return;}
        await using var db=factory.Create();
        await EditMikroTik(await db.Devices.SingleAsync(d=>d.Id==router.Id!.Value));
    }
    private async Task EditMikroTik(Device original)
    {
        var device=Copy(original);var isNew=device.Id==0;
        var connection=isNew?new NetworkConnectionView(0,8729,"",false,"","generic-network",new(),""):
            await remote.CommandAsync<NetworkConnectionView>("router-connection",new{DeviceId=device.Id},lifetime.Token);
        var dictionaries=await remote.CommandAsync<NetworkDictionary[]>("router-dictionaries",new{},lifetime.Token);
        var customRules=new Dictionary<string,List<NetworkRule>>();if(connection.Rules!=null)customRules[connection.DictionaryId]=CloneRules(connection.Rules);
        var mappings=new Dictionary<string,Dictionary<string,string[]>>();
        mappings[connection.DictionaryId]=connection.Interfaces.ToDictionary(p=>p.Key,p=>p.Value.ToArray());
        var form=new FormDialog(this,isNew?"Добавить MikroTik":"MikroTik · "+device.Name,680)
            .Tab("Устройство")
            .Text("name","Имя *",device.Name).Text("host","IP или DNS-имя *",device.Host)
            .Text("site","Площадка",device.Site).Text("role","Роль / описание",device.Role)
            .Toggle("enabled","Устройство активно",device.Enabled)
            .Text("maintenance","Обслуживание до · ДД.ММ.ГГГГ ЧЧ:ММ · пусто = нет",device.MaintenanceUntil?.ToString("dd.MM.yyyy HH:mm")??"")
            .Tab("Подключение")
            .Text("port","Порт API-SSL *",connection.Port.ToString())
            .Text("user","Логин * · права read + api",connection.User)
            .Secret("password",connection.HasPassword?"Новый пароль · пусто = оставить прежний":"Пароль *")
            .Text("pin","SHA-256 сертификата API-SSL · необязательно",connection.CertificateSha256)
            .Note("При указанном SHA-256 принимается сертификат с точно таким отпечатком. Без отпечатка требуется доверенный сертификат с подходящим именем. Пароль сохраняется на сервере в защищённом виде.")
            .Tab("Мониторинг")
            .Select("updateChannel","Канал сведений об обновлениях RouterOS",new[]{new Choice("stable","Stable"),new Choice("long-term","Long-term")},connection.UpdateChannel)
            .Select("dictionary","Сетевой словарь",dictionaries.Select(d=>new Choice(d.Id,d.Name)),dictionaries.Any(d=>d.Id==connection.DictionaryId)?connection.DictionaryId:"generic-network")
            .Note("Правила контролируют WAN, важные туннели, uplink и VPN-сессии. Используйте типовой словарь, импортируйте дополнительные словари или настройте правила, пороги, важность и уведомления в общем редакторе.");
        form.ActionAsync("Настроить правила устройства",()=>{
            var dictionary=dictionaries.Single(d=>d.Id==form.Selected<string>("dictionary"));
            var selected=mappings.GetValueOrDefault(dictionary.Id)??new Dictionary<string,string[]>();
            var initial=NetworkLogRules.Effective(dictionary,selected,customRules.GetValueOrDefault(dictionary.Id));
            var edited=OpenNetworkRuleEditor(form,initial,RuleTemplates(dictionaries,id=>mappings.GetValueOrDefault(id)??new Dictionary<string,string[]>()),dictionary.Id);
            if(edited!=null){customRules[dictionary.Id]=edited;form.MarkDirty();}
            return Task.CompletedTask;
        });
        form.Validate=()=>{
            _=MaintenanceTime(form.Value("maintenance"));_=form.Number("port",1,65535);_=RouterOsClient.NormalizePin(form.Value("pin"));_=form.Selected<string>("dictionary");
            if(form.Value("password").Length==0&&(!connection.HasPassword||!StoredCredential.SameEndpoint(form.Value("host").Trim(),form.Number("port",1,65535),form.Value("user"),original.Host,connection.Port,connection.User)||RouterOsClient.NormalizePin(form.Value("pin"))!=connection.CertificateSha256))return "Укажите пароль учётной записи MikroTik на вкладке «Подключение»";
            return Required(form.Value("name"),form.Value("host"),form.Value("user"));
        };
        if(form.ShowDialog()!=true)return;
        device.Type="MikroTik";device.Name=form.Value("name");device.Host=form.Value("host");device.Site=form.Value("site");device.Role=form.Value("role");device.Enabled=form.Checked("enabled");device.MaintenanceUntil=MaintenanceTime(form.Value("maintenance"));
        var profile=dictionaries.Single(d=>d.Id==form.Selected<string>("dictionary"));
        var overrides=mappings.GetValueOrDefault(profile.Id)??new Dictionary<string,string[]>();
        await SaveEntity(device);
        try {
            if(isNew)connection=await remote.CommandAsync<NetworkConnectionView>("router-connection",new{DeviceId=device.Id},lifetime.Token);
            await remote.CommandAsync<bool>("router-connection-save",new NetworkConnectionSave(device.Id,form.Number("port",1,65535),form.Value("user"),form.Value("password"),form.Value("pin"),profile.Id,overrides,connection.Hash,customRules.GetValueOrDefault(profile.Id),form.Selected<string>("updateChannel")),lifetime.Token);
        }catch(Exception e){throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Карточка MikroTik сохранена, но параметры подключения не сохранены. Выберите устройство → «Изменить» и повторите сохранение. ")+e.Message,e);}
        await ShowPage(page);
    }
    private async Task ShowNetworkCard(RouterMonitorRow router)
    {
        var history=await remote.CommandAsync<NetworkHistory>("router-events",new{DeviceId=router.Id!.Value},lifetime.Token);
        var root=new DockPanel{Margin=new(16)};var heading=new DockPanel{Margin=new(0,0,0,10)};DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        await using(var connectionDb=factory.Create()){var target=await connectionDb.Devices.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==router.Id);if(target!=null){var buttons=DeviceConnectionButtons(target);DockPanel.SetDock(buttons,Dock.Top);root.Children.Insert(1,buttons);}}
        var refresh=UiButton("Обновить события");DockPanel.SetDock(refresh,Dock.Right);heading.Children.Add(refresh);
        var info=new TextBlock{TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};heading.Children.Add(info);
        var tabs=new TabControl();root.Children.Add(tabs);bool loading=false;
        var pollText=DetailText();tabs.Items.Add(new TabItem{Header="Опросы сервера",Content=pollText});
        tabs.SelectedIndex=1;
        var problems=Table(Array.Empty<NetworkProblem>(),("Проблема","Title",null),("Уровень","Severity",null),("Возникло","OpenedAt","dd.MM HH:mm"),("Повторы","Repeats",null));
        var past=Table(Array.Empty<NetworkProblem>(),("Событие","Title",null),("Активно","Open",null),("Последний раз","LastSeen","dd.MM HH:mm"));
        var logs=Table(Array.Empty<NetworkLog>(),("Время RouterOS","RouterTime",null),("Уровень","Severity",null),("Событие","Message",null));
        var resolve=UiButton("Отметить решённым");resolve.ToolTip="Для событий журнала. Доступность восстанавливается по фактическому состоянию.";
        var resolutionHint=new TextBlock{TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new(10,4,0,4)};
        void UpdateResolve(){
            var selected=problems.SelectedItem as NetworkProblem;
            resolutionHint.Text=selected==null?"Выберите событие.":selected.Kind!="Log"?"Закроется автоматически после подтверждения восстановления опросом. «Обновить события» перечитывает результат сервера.":AccessRules.Level(remote.CurrentUser,"routers")<2?"Для закрытия требуется право изменения сети.":"Событие журнала можно закрыть вручную.";
            resolve.Visibility=selected!=null&&selected.Kind!="Log"?Visibility.Collapsed:Visibility.Visible;
            resolve.IsEnabled=!loading&&AccessRules.Level(remote.CurrentUser,"routers")>=2&&problems.SelectedItem is NetworkProblem p&&p.Open&&p.Kind=="Log";
        }
        ListInspector ProblemPane(DataGrid table,string caption){
            var overview=DetailText();var evidence=DetailText();var rules=DetailText();var detail=new TabControl();
            detail.Items.Add(new TabItem{Header="Событие",Content=overview});detail.Items.Add(new TabItem{Header="Исходные сообщения",Content=evidence});detail.Items.Add(new TabItem{Header="Правила",Content=rules});
            table.SelectionChanged+=(_,_)=>{
                if(table.SelectedItem is not NetworkProblem problem){overview.Text="Выберите событие в списке.";evidence.Clear();rules.Clear();UpdateResolve();return;}
                overview.Text=problem.Title+"\n\nУровень: "+UiValueConverter.Instance.Convert(problem.Severity,typeof(string),null!,System.Globalization.CultureInfo.CurrentCulture)+"\nСостояние: "+(problem.Open?"Активно":"Закрыто")+"\nВозникло: "+problem.OpenedAt?.ToServerTime().ToString("dd.MM.yyyy HH:mm:ss")+"\nПоследний раз: "+problem.LastSeen.ToServerTime().ToString("dd.MM.yyyy HH:mm:ss")+"\nПовторы: "+problem.Repeats+"\nЗакрыто: "+problem.RestoredAt?.ToServerTime().ToString("dd.MM.yyyy HH:mm:ss")+"\n"+problem.Resolution+"\n\nПравило: "+problem.RuleId;
                overview.Text+="\n\n"+NetworkIssueAdvice.Describe(problem);
                if(problem.Kind!="Log")overview.Text+="\n\nВосстановление подтверждается автоматически опросом.\nПоследняя проверка: "+history.CheckedAt?.ToServerTime().ToString("dd.MM.yyyy HH:mm:ss")+"\nОшибка мониторинга: "+(string.IsNullOrEmpty(history.Error)?"нет":history.Error);
                if(problem.Evidence.Count==0){evidence.Text=problem.Kind=="Log"?"У старого события исходные сообщения не сохранялись. При следующих срабатываниях они появятся здесь.":"Это событие контроля состояния, а не правило по сообщению журнала.";rules.Text="Снимок условий отсутствует. Причина не восстанавливается догадкой по времени.";}
                else{
                    evidence.Text=string.Join("\n\n────────────────────\n\n",problem.Evidence.Select((e,i)=>$"Сообщение {i+1} из {problem.Evidence.Count}\nВремя RouterOS: {e.Log.RouterTime}\nВремя события (UTC → пояс сервера): {e.Log.EventUtc?.ToServerTime():dd.MM.yyyy HH:mm:ss}\nПолучено сервером: {e.Log.ReceivedAt.ToServerTime():dd.MM.yyyy HH:mm:ss}\nОценено: {e.EvaluatedAtUtc.ToServerTime():dd.MM.yyyy HH:mm:ss} · совпадений в окне: {e.MatchedCount}\nТемы: {e.Log.Topics}\n\n{e.Log.Message}"));
                    rules.Text=string.Join("\n\n",problem.Evidence.Select(e=>DescribeEvidenceRule(e.Rule)).Distinct());
                }
                UpdateResolve();
            };
            return new ListInspector(table,detail,caption,"Активных проблем нет","Здесь появятся события, требующие внимания. Завершённые события доступны в истории.");
        }
        var active=new DockPanel();var activeActions=new WrapPanel();activeActions.Children.Add(resolve);activeActions.Children.Add(resolutionHint);DockPanel.SetDock(activeActions,Dock.Top);active.Children.Add(activeActions);active.Children.Add(ProblemPane(problems,"Активные проблемы"));
        tabs.Items.Add(new TabItem{Header="Проблемы",Content=active});
        if(router.Kind=="MikroTik")tabs.Items.Add(new TabItem{Header="Конфигурация",Content=await RouterAuditPanel(router)});
        tabs.Items.Add(new TabItem{Header="История",Content=ProblemPane(past,"История состояний")});
        var logText=DetailText("Выберите строку журнала.");
        logs.SelectionChanged+=(_,_)=>logText.Text=logs.SelectedItem is NetworkLog log?$"Время RouterOS: {log.RouterTime}\nПолучено сервером: {log.ReceivedAt.ToServerTime():dd.MM.yyyy HH:mm:ss}\nТемы: {log.Topics}\n\n{log.Message}\n\nОценка\n{log.Meaning}":"Выберите строку журнала.";
        tabs.Items.Add(new TabItem{Header="Журнал RouterOS",Content=new ListInspector(logs,logText,"Последние 1000 сообщений")});
        var interfaces=Table(Array.Empty<InterfaceRow>(),("Интерфейс","Name",null),("Тип","Type",null),("Состояние","Status",null),("Комментарий","Comment",null));
        var interfaceInfo=new TextBlock{Margin=new Thickness(0,0,0,8),TextWrapping=TextWrapping.Wrap};
        var interfacePanel=new DockPanel();DockPanel.SetDock(interfaceInfo,Dock.Top);interfacePanel.Children.Add(interfaceInfo);interfacePanel.Children.Add(TableSurface(interfaces));
        tabs.Items.Add(new TabItem{Header="Интерфейсы",Content=interfacePanel});
        void Render(){
            var rows=InterfaceRows(history.Snapshot);
            interfaces.ItemsSource=rows;
            interfaceInfo.Text=rows.Length==0?"В сохранённом снимке нет именованных интерфейсов. Проверьте вкладку «Опросы сервера».":$"Интерфейсов в снимке: {rows.Length} · последний успешный опрос: {history.LastSuccessfulPollUtc?.ToServerTime():dd.MM.yyyy HH:mm:ss}";
            if(!string.IsNullOrWhiteSpace(history.Error))interfaceInfo.Text+="\nПоследний опрос: "+history.Error;
            pollText.Text="Последние 200 завершённых опросов. Время в поясе сервера.\nПоследний успешный: "+history.LastSuccessfulPollUtc?.ToServerTime().ToString("dd.MM.yyyy HH:mm:ss")+"\n\n"+string.Join("\n\n",history.Polls.AsEnumerable().Reverse().Select(p=>$"{p.StartedUtc.ToServerTime():dd.MM.yyyy HH:mm:ss} → {p.CompletedUtc.ToServerTime():HH:mm:ss} · {(p.Success?"Успешно":"Ошибка")}\n{p.Error}"));InspectorRows(problems,history.Problems.Where(p=>p.Open&&p.Severity is Severity.Critical or Severity.Warning).OrderByDescending(p=>p.Severity),p=>p.Id);InspectorRows(past,history.Problems.Where(p=>p.OpenedAt!=null).OrderByDescending(p=>p.LastSeen),p=>p.Id);InspectorRows(logs,history.Logs.AsEnumerable().Reverse(),l=>l.Key);info.Text=router.Name+" · "+router.Host+"\nАвтоматический опрос: "+(settings.MonitoringEnabled?"включён · интервал "+settings.PollSeconds+" с":"ВЫКЛЮЧЕН в настройках мониторинга")+$"\nКритичных: {history.Problems.Count(p=>p.Open&&p.Severity==Severity.Critical)} · предупреждений: {history.Problems.Count(p=>p.Open&&p.Severity==Severity.Warning)} · время в поясе сервера";UpdateResolve();}
        refresh.Click+=async(_,_)=>{if(loading)return;loading=true;refresh.IsEnabled=false;UpdateResolve();try{history=await remote.CommandAsync<NetworkHistory>("router-events",new{DeviceId=router.Id!.Value},lifetime.Token);Render();}catch(Exception e){AppMessageBox.Show(Window.GetWindow(root)??this,e.Message,"События");}finally{loading=false;refresh.IsEnabled=true;UpdateResolve();}};
        resolve.Click+=async(_,_)=>{if(loading||problems.SelectedItem is not NetworkProblem problem)return;loading=true;refresh.IsEnabled=false;UpdateResolve();try{await remote.CommandAsync<bool>("router-problem-resolve",new{DeviceId=router.Id!.Value,ProblemId=problem.Id},lifetime.Token);problem.Open=false;problem.RestoredAt=DateTime.UtcNow;problem.Resolution="Отмечено решённым пользователем";Render();}catch(Exception e){AppMessageBox.Show(Window.GetWindow(root)??this,e.Message,"Событие");}finally{loading=false;refresh.IsEnabled=true;UpdateResolve();}};
        Render();tabs.SelectedIndex=1;ReaderWindow(router.Name+" · Состояние и события",root).ShowDialog();await ShowPage(page);
    }
    private static string DescribeEvidenceRule(NetworkRule rule)
    {
        string Terms(string[] values)=>values.Length==0?"не заданы":string.Join("; ",values);
        return $"Правило: {rule.Name} ({rule.Id})\nВажность: {rule.Severity}\n"+
            $"Фразы в сообщении ({(rule.MatchAll?"все":"любая")}): {Terms(rule.MessageTerms)}\n"+
            $"Темы (любая): {Terms(rule.TopicTerms)}\nИсключения (любая фраза): {Terms(rule.ExcludeTerms)}\n"+
            $"Порог: {rule.Threshold} за {rule.WindowSeconds} с\nГруппировка: {(rule.GroupBy=="All"?"все источники":"IP источника")}\n"+
            $"Предыдущие события: {rule.PrecedingCount}; фразы: {Terms(rule.PrecedingTerms)}\n"+
            $"Автозакрытие после тишины: {(rule.AutoCloseSeconds==0?"отключено":rule.AutoCloseSeconds+" с")}\n"+
            $"Telegram: {(rule.Notify?"да":"нет")}; восстановление: {(rule.NotifyRecovery?"да":"нет")}\n"+
            $"Группа эскалации: {rule.EscalationGroup}";
    }
    private void ShowRouterDetails(RouterMonitorRow router)
    {
        if(router.Snapshot==null){AppMessageBox.Show(this,router.Status,"MikroTik · "+router.Name);return;}
        var rows=InterfaceRows(router.Snapshot);if(rows.Length==0){AppMessageBox.Show(this,"В сохранённом снимке нет именованных интерфейсов. Проверьте вкладку «Опросы сервера».","MikroTik · "+router.Name);return;}var table=Table(rows,("Интерфейс","Name",null),("Тип","Type",null),("Состояние","Status",null),("RX, байт","Rx",null),("TX, байт","Tx",null),("Комментарий","Comment",null));table.Columns.Insert(0,LampColumn("State"));
        ReaderWindow(router.Name+" · "+router.Host+" · "+router.Model,TableSurface(table),1000,600).ShowDialog();
    }
    private static DataGridTemplateColumn EquipmentColumn(string bindingPath=".")
    {
        var image = new FrameworkElementFactory(typeof(Image)); image.SetValue(FrameworkElement.WidthProperty, 60.0); image.SetValue(FrameworkElement.HeightProperty, 36.0);
        image.SetBinding(Image.SourceProperty, new Binding(bindingPath) { Converter = new EquipmentConverter() });
        return new DataGridTemplateColumn { Header = "", Width = 72, CellTemplate = new DataTemplate { VisualTree = image } };
    }
    private static BitmapImage EquipmentImage(string family) => EquipmentImages.Get(family);
    private static string Family(string model)
    {
        var m = model.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("^", "").Replace("²", "2").Replace("³", "3");
        foreach (var (key, value) in new (string, string)[] { ("hapax3", "hap_ax3"), ("hapax2", "hap_ax2"), ("hapac3", "hap_ac3"), ("hapac2", "hap_ac2"), ("hapac", "hap_ac"), ("hexs", "hex_s"), ("hex", "hex"), ("rb5009", "rb5009"), ("l009", "l009"), ("ccr", "ccr"), ("crs", "crs"), ("sxtsq", "sxtsq"), ("sxt", "sxt"), ("lhg", "lhg"), ("netmetal", "netmetal"), ("mantbox", "mantbox"), ("groove", "groove"), ("cube", "cube"), ("ltap", "ltap"), ("wap", "wap"), ("cap", "cap"), ("chr", "chr"), ("rbd52", "hap_ac2"), ("rbd53", "hap_ac3"), ("c52i", "hap_ax2"), ("c53u", "hap_ax3"), ("rb962", "hap_ac"), ("rb760", "hex_s"), ("rb750", "hex"), ("rb", "rb") }) if (m.Contains(key)) return value;
        if (m.Contains("routerboard")) return "board";
        return "generic";
    }
    private static bool HasInterfaceName(Dictionary<string,string> row)=>!string.IsNullOrWhiteSpace(row.GetValueOrDefault("name"));
    private static InterfaceRow[] InterfaceRows(RouterSnapshot? snapshot)=>(snapshot?.Interfaces??[]).Where(HasInterfaceName).Select(row=>new InterfaceRow(row)).ToArray();
    private sealed class InterfaceRow(Dictionary<string, string> data)
    {
        public string Name => data.GetValueOrDefault("name", ""); public string Type => data.GetValueOrDefault("type", "");
        public string Rx => Counter("rx-byte"); public string Tx => Counter("tx-byte");
        private string Counter(string key) => long.TryParse(data.GetValueOrDefault(key), out var n) ? n.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) : "—"; public string Comment => data.GetValueOrDefault("comment", "");
        public LampState State => data.GetValueOrDefault("disabled") is "true" or "yes" ? LampState.Gray : data.GetValueOrDefault("running") is "true" or "yes" ? LampState.Green : LampState.Amber;
        public string Status => State switch { LampState.Green => "Работает", LampState.Gray => "Отключён", _ => "Нет линка" };
    }
}

public sealed class EquipmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is Device device ? EquipmentImages.ForDevice(device) : EquipmentImages.Get(value as string ?? "generic");
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
