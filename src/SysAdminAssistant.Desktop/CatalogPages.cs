using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private async Task ShowDevices()
    {
        await using var db = factory.Create();
        var devices = await db.Devices.AsNoTracking().Where(d=>d.Type!="MikroTik"&&d.Type!="Коммутатор").ToArrayAsync();
        var services=CanRead("services")?await db.Services.AsNoTracking().ToArrayAsync():[];
        var reports=CanRead("reports")?await db.Reports.AsNoTracking().Where(r=>r.DeviceId!=null&&r.Provider!="DMARC").ToArrayAsync():[];
        var incidents=CanRead("reports")?await db.Incidents.AsNoTracking().Where(i=>!i.Closed).ToArrayAsync():[];
        var now=remote.Snapshot.ServerTime;
        var servicesByDevice=services.Where(s=>s.DeviceId!=null).ToLookup(s=>s.DeviceId!.Value);
        var reportDevice=reports.ToDictionary(r=>r.Id,r=>r.DeviceId!.Value);
        var problemsByDevice=incidents.Where(i=>i.ReportId is int id && reportDevice.ContainsKey(id)).ToLookup(i=>reportDevice[i.ReportId!.Value]);
        var rows=devices.Select(d=>new DeviceRegistryRow(d,DashboardState.WithProblems(d,DashboardState.Device(d,servicesByDevice[d.Id],now,settings.PollSeconds,settings.MonitoringEnabled),problemsByDevice[d.Id],now),remote.Snapshot.AgentVersions?.GetValueOrDefault(d.Id))).OrderBy(r=>r.Priority).ThenBy(r=>r.Name).ToArray();
        foreach(var row in rows)WatchUpdate(row.Entity.Id,row.RefreshUpdates);
        var table = Table(rows, ("Имя", "Name", null), ("Тип", "Type", null), ("Адрес", "Host", null), ("Площадка", "Site", null), ("Версия агента","AgentVersion",null), ("Активно", "Enabled", null), ("Статус", "Status", null), ("Причина","Detail",null), ("Последний ответ", "LastSeen", "dd.MM.yyyy HH:mm"));
        table.Columns.Insert(0, LampColumn("State"));table.Columns.Insert(1, EquipmentColumn("Entity"));table.Columns.Insert(2,UpdatesColumn());
        table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=async(_,_)=>{if(table.SelectedItem is DeviceRegistryRow row)await Run(()=>ShowDeviceCard(row.Entity));};
        var actions=new List<Button>{
            ActionButton("Скачать шаблон",()=>SaveExcel("devices",true)),ActionButton("Экспорт Excel",()=>SaveExcel("devices",false)),ActionButton("Импорт Excel",()=>ImportExcel("devices")),
            RowAction(table,"Карточка",()=>ShowDeviceCard(Selected<DeviceRegistryRow>(table).Entity)),ActionButton("Добавить",()=>EditDevice(new())),
            RowAction(table,"Изменить",()=>EditDevice(Selected<DeviceRegistryRow>(table).Entity)),
            RowAction(table,"Удалить",async()=>{
                var selected=table.SelectedItems.Cast<DeviceRegistryRow>().ToArray();
                if(!Confirm(this,$"Удалить устройств: {selected.Length}? При наличии связанных данных удаление будет запрещено."))return;
                await ApplyBatch(selected,async row=>{await using var edit=factory.Create();edit.Remove(row.Entity);await edit.SaveChangesAsync();});
                await remote.RefreshAsync(lifetime.Token);await ShowPage(1);
            },true),
            RowAction(table,"Архивировать / вернуть",async()=>{
                var selected=table.SelectedItems.Cast<DeviceRegistryRow>().Select(r=>Copy(r.Entity)).ToArray();
                var f=new FormDialog(this,"Активность устройств",500).Toggle("enabled","Устройства активны",selected.All(d=>d.Enabled));
                if(f.ShowDialog()!=true)return;
                await ApplyBatch(selected,async d=>{await using var edit=factory.Create();var current=await edit.Devices.SingleAsync(x=>x.Id==d.Id);current.Enabled=f.Checked("enabled");await edit.SaveChangesAsync();});
                await remote.RefreshAsync(lifetime.Token);await ShowPage(1);
            },true),
            RowAction(table,"История отчётов",()=>ShowHistory(Selected<DeviceRegistryRow>(table).Entity.Id,null)),
            RowAction(table,"Разобрать письма",async()=>{
                var selected=table.SelectedItems.Cast<DeviceRegistryRow>().ToArray();
                await ApplyBatch(selected,async row=>{await remote.CommandAsync<Infrastructure.MailBindResult>("device-reanalyze",new{DeviceId=row.Entity.Id},lifetime.Token);});
                await remote.RefreshAsync(lifetime.Token);await ShowPage(1);
            },true)};
        if(AccessRules.Level(remote.CurrentUser,"inventory")>=2)
            actions.Add(RowAction(table,"Создать объект инвентаризации",()=>CreateInventoryFromDevice(Selected<DeviceRegistryRow>(table).Entity)));
        ActionContextMenu(table,actions);
        ListPage(table,actions.ToArray());
    }
    private sealed class DeviceRegistryRow(Device entity,DeviceHealth health,string? version):System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public string AgentVersion=>string.IsNullOrWhiteSpace(version)?"—":version;
        public void RefreshUpdates(DeviceUpdateStamp stamp){if(stamp.AgentVersion!=null&&version!=stamp.AgentVersion){version=stamp.AgentVersion;PropertyChanged?.Invoke(this,new(nameof(AgentVersion)));}if(entity.OsUpdatesJson==stamp.OsUpdatesJson&&entity.MailUpdatesJson==stamp.MailUpdatesJson)return;entity.OsUpdatesJson=stamp.OsUpdatesJson;entity.MailUpdatesJson=stamp.MailUpdatesJson;PropertyChanged?.Invoke(this,new(nameof(Updates)));}
        public Device Entity=>entity;
        public OsUpdateStatus? Updates=>MailUpdates.Overview(entity.OsUpdatesJson,entity.MailUpdatesJson);public string Name=>entity.Name;public string Type=>entity.Type;public string Host=>entity.Host;public string Site=>entity.Site;public bool Enabled=>entity.Enabled;public DateTime? LastSeen=>entity.LastSeen;
        public string Status=>health.Status;public string Detail=>health.Detail;public LampState State=>health.Lamp;public int Priority=>health.Priority;
    }
    private async Task EditDevice(Device d)
    {
        if(page==6&&d.Type=="MikroTik"){await EditMikroTik(d);return;}
        d = Copy(d);
        var types = page==6?new[]{"Коммутатор"}:new[]{"Авто","Сервер","ПК","Synology NAS","QNAP NAS","Принтер","ИБП","Другое"};
        if(page==6&&d.Id==0)d.Type="Коммутатор";
        var form = new FormDialog(this, d.Id == 0 ? "Новое устройство" : "Устройство")
            .Text("name", "Имя *", d.Name).Select("type", "Тип", types.Append(d.Type).Distinct().Select(x => new Choice(x, x)), d.Id == 0 && page!=6 ? "Авто" : d.Type)
            .Text("host", "IP или DNS-имя", d.Host).Text("site", "Площадка", d.Site).Text("role", "Роль / описание", d.Role).Toggle("enabled", "Устройство активно", d.Enabled)
            .Text("maintenance", "Обслуживание до · ДД.ММ.ГГГГ ЧЧ:ММ · пусто = нет", d.MaintenanceUntil?.ToString("dd.MM.yyyy HH:mm") ?? "")
            .Note(page==6?"Коммутатор проверяется по ICMP. Для подключения RouterOS используйте «Добавить MikroTik».":"Авто определяет тип по имени и описанию: Windows Server, Synology, QNAP и т. п. Для MikroTik используйте раздел «Коммутаторы / сеть».");
        form.Validate = () => { _ = MaintenanceTime(form.Value("maintenance")); return Required(form.Value("name")); };
        if (form.ShowDialog() != true) return;
        d.Name = form.Value("name"); d.Type = form.Selected<string>("type"); d.Host = form.Value("host"); d.Site = form.Value("site"); d.Role = form.Value("role"); d.Enabled = form.Checked("enabled"); d.MaintenanceUntil = MaintenanceTime(form.Value("maintenance"));
        if (d.Type == "Авто") {d.Type = DeviceTypes.Detect(d.Name, d.Role);if(NetworkDevices.IsNetwork(d.Type))d.Type="Другое";}
        await SaveEntity(d);
    }
    private async Task ReanalyzeDevice(int id)
    {
        var result=await remote.CommandAsync<Infrastructure.MailBindResult>("device-reanalyze",new{DeviceId=id},lifetime.Token);
        await remote.RefreshAsync(lifetime.Token);await ShowPage(page);
        AppMessageBox.Show(this,$"Разобрано писем устройства: {result.Bound}\nРезультат требует уточнения: {result.NeedsReview}","Анализ писем");
    }
    private async Task ShowServices()
    {
        await using var db = factory.Create(); var rows = await db.Services.AsNoTracking().OrderBy(x => x.Name).ToArrayAsync();
        var names=CanRead("devices")?await db.Devices.AsNoTracking().ToDictionaryAsync(d=>d.Id,d=>d.Name):new Dictionary<int,string>();
        var table = Table(rows.Select(x=>new ServiceRow(x,x.DeviceId is int id ? names.GetValueOrDefault(id,CanRead("devices")?"Архивное устройство":"Нет доступа"):"Самостоятельный сервис")),
            ("Имя", "Name", null), ("Тип", "Type", null), ("Устройство","Device",null), ("Адрес", "Address", null), ("Статус", "Status", null), ("Заметки", "Notes", null));
        ListPage(table, ConnectivityDetailsButton(), RowAction(table,"Пароли сервиса",()=>ShowServiceCredentials(Selected<ServiceRow>(table).Entity)),ActionButton("Добавить", () => EditService(new())), RowAction(table, "Изменить", () => EditService(Selected<ServiceRow>(table).Entity)),
            RowAction(table, "Удалить", async () => { var row = Selected<ServiceRow>(table).Entity; if (!Confirm(this, "Удалить сервис «" + row.Name + "»?")) return; await using var edit = factory.Create(); edit.Remove(row); await edit.SaveChangesAsync(); await ShowServicesRefresh(); }));
    }
    private async Task ShowServiceCredentials(ServiceEntry service){if(!CanRead("vault"))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Нет права на раздел «Пароли»"));var entries=(await remote.GetAsync<VaultItem[]>("api/personal/vault",lifetime.Token)).Where(x=>x.ServiceId==service.Id).ToArray();if(entries.Length==0){AppMessageBox.Show(this,"Нет доступных связанных записей. В разделе «Пароли» откройте запись и выберите «Связать с сервисом».",service.Name);return;}VaultItem entry;if(entries.Length==1)entry=entries[0];else{var form=new FormDialog(this,"Пароли · "+service.Name).Select("entry","Запись",entries.Select(x=>new Choice(x,x.Name+" · "+x.OwnerLogin)),entries[0]).Note("Будут показаны только доступные вам записи. Для чтения секрета потребуется обычное подтверждение пароля.");if(form.ShowDialog()!=true)return;entry=form.Selected("entry") as VaultItem??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выберите запись"));}await OpenCredential(entry);}
    private sealed record ServiceRow(ServiceEntry Entity,string Device) {
        public string Name=>Entity.Name; public string Type=>Entity.Type; public string Address=>Entity.Address;
        public string Status=>Entity.Status; public string Notes=>Entity.Notes;
    }
    private Task ShowServicesRefresh() => ShowPage(2);
    private async Task EditService(ServiceEntry s)
    {
        s = Copy(s);
        await using var deviceDb = factory.Create();
        var devices = await deviceDb.Devices.AsNoTracking().OrderBy(d=>d.Name).ToArrayAsync();
        var form = new FormDialog(this, "Сервис").Text("name", "Название *", s.Name)
            .Select("type", "Проверка", new[] { new Choice("HTTP", "HTTP / HTTPS"), new Choice("TCP", "TCP-порт") }, s.Type)
            .Select("device", "Связанное устройство", new[]{new Choice(null,"Самостоятельный сервис")}.Concat(devices.Select(d=>new Choice(d.Id,d.Name))), s.DeviceId)
            .Text("url", "Адрес * (https://... или host:port)", s.Address).Text("notes", "Заметки", s.Notes, true);
        form.Validate = () =>
        {
            var required = Required(form.Value("name"), form.Value("url")); if (required != null) return required;
            if (form.Selected<string>("type") == "HTTP") return Uri.TryCreate(form.Value("url"), UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? null : "Укажите HTTP/HTTPS URL";
            return Uri.TryCreate("tcp://" + form.Value("url"), UriKind.Absolute, out var t) && t.Port > 0 && t.Port <= 65535 && t.Host.Length > 0 && t.UserInfo.Length == 0 && (t.AbsolutePath is "" or "/") && t.Query.Length == 0 && t.Fragment.Length == 0 ? null : "Укажите host:port";
        };
        if (form.ShowDialog() != true) return;
        s.Name = form.Value("name"); s.Type = form.Selected<string>("type"); s.Address = form.Value("url"); s.Notes = form.Value("notes"); if(CanRead("devices"))s.DeviceId=form.Selected("device") as int?; await SaveEntity(s);
    }
    private async Task ShowPayments()
    {
        var invoices=await OptionalInvoiceIndex<InvoiceView>("api/invoices");var links=(await OptionalInvoiceIndex<InvoicePaymentLink>("api/invoices/payment-links")).ToDictionary(x=>x.PaymentId);
        await using var db = factory.Create(); var rows = await db.Payments.AsNoTracking().OrderBy(x => x.DueDate).ToArrayAsync();
        var table = Table(rows.Select(x => new PaymentRow(x, settings.Company.WeekendMask, settings.Company.PaymentSoonDays,invoices.Where(i=>i.PaymentId==x.Id).ToArray(),links.GetValueOrDefault(x.Id))).OrderBy(x => x.SortDate).ToArray(), ("Платёж", "Title", null), ("Вид платежа", "Source", null),("Сумма", "Amount", null), ("По расписанию", "ScheduledDate", null), ("Срок оплаты", "Date", null), ("Повторение", "Repeat", null), ("Статус", "Status", null), ("Заметки", "Notes", null));
        table.Columns.Insert(0,LampColumn("State"));table.Columns.Add(InvoiceDownloadColumn());
        table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=async(_,_)=>{if(table.SelectedItem is PaymentRow row)await Run(()=>ShowPaymentCard(row.Entity));};
        ListPage(table,ActionButton("Распознать счёт из файла",UploadInvoice),ActionButton("Счета из почты · проверить ("+invoices.Count(x=>x.Status=="Review")+")",ShowInvoiceQueue),RowAction(table,"Карточка / история",()=>ShowPaymentCard(Selected<PaymentRow>(table).Entity)), ActionButton("Добавить", () => EditPayment(new())), RowAction(table, "Изменить", () => EditPayment(Selected<PaymentRow>(table).Entity)),
            RowAction(table,"Удалить",()=>DeleteCatalogEntity(Selected<PaymentRow>(table).Entity,Selected<PaymentRow>(table).Entity.Title)),
            RowAction(table, "Оплачено", async () => { var p = Copy(Selected<PaymentRow>(table).Entity); if (p.Paid && p.Repeat == RepeatPeriod.None) return; await PayCurrentPeriod(p); }));
    }
    private async Task EditPayment(Payment p)
    {
        p = Copy(p);
        var choices = new[] { new Choice(RepeatPeriod.None, "Не повторять"), new Choice(RepeatPeriod.Weekly, "Еженедельно"), new Choice(RepeatPeriod.Monthly, "Ежемесячно"), new Choice(RepeatPeriod.Yearly, "Ежегодно") };
        var form = new FormDialog(this, "Платёж").Text("title", "Название *", p.Title).Text("amount", "Сумма, ₽ · пусто = не указана", p.AmountKopecks is long money ? (money/100m).ToString("0.00") : "").Text("date", "Срок · ДД.ММ.ГГГГ", Display.Date(p.DueDate)).Select("repeat", "Повторение", choices, p.Repeat).Toggle("paid", "Отметить текущий период оплаченным", false).Text("notes", "Заметки", p.Notes, true);
        form.Note("Указывайте исходную дату. Если она приходится на выходной компании, срок оплаты переносится на ближайший рабочий день. Повторение сохраняет исходное число.")
            .Action("Проверить срок оплаты", () => AppMessageBox.Show(form, Display.Date(PaymentCalendar.EffectiveDueDate(form.Date("date"), settings.Company.WeekendMask)), "Срок оплаты"));
        form.Validate = () => { _ = form.Date("date"); _ = PaymentAmount(form.Value("amount")); return Required(form.Value("title")); };
        if (form.ShowDialog() != true) return;
        var date = form.Date("date"); if (date != p.DueDate || form.Selected<RepeatPeriod>("repeat") != p.Repeat) p.AnchorDay = date.Day;
        p.AmountKopecks = PaymentAmount(form.Value("amount")); p.Title = form.Value("title"); p.DueDate = date; p.Repeat = form.Selected<RepeatPeriod>("repeat"); var wasPaid = p.Paid; var markPaid=form.Checked("paid")&&(!wasPaid||p.Repeat!=RepeatPeriod.None);if(!wasPaid)p.Paid=false; p.Notes = form.Value("notes"); await SaveEntity(p);if(markPaid)await PayCurrentPeriod(p);
    }
    private async Task PayCurrentPeriod(Payment p)
    {
        await remote.CommandAsync<bool>("payment-pay",new PaymentOperation(p.Id,RemoteData.EntityStamp(p),Guid.NewGuid().ToString()),lifetime.Token);
        await remote.RefreshAsync(lifetime.Token);await ShowPage(page);
    }
    private async Task ShowPaymentCard(Payment payment)
    {
        var documents=await OptionalInvoiceIndex<InvoiceView>("api/invoices?paymentId="+payment.Id);
        var window=new DialogWindow{Owner=this,Title=payment.Title+" · история оплат",Width=960,Height=580,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var root=new DockPanel{Margin=new Thickness(16)};var info=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(info,Dock.Top);root.Children.Add(info);
        var table=Table(Array.Empty<PaymentReceipt>(),("Период / срок","ScheduledDate","dd.MM.yyyy"),("Отмечено оплачено","PaidAt","dd.MM.yyyy HH:mm"),("Кто оплатил","PaidBy",null),("Отменено","Cancelled",null),("Дата отмены","CancelledAt","dd.MM.yyyy HH:mm"),("Кто отменил","CancelledBy",null));
        table.Columns.Add(new DataGridTextColumn{Header="Сумма, ₽",Binding=new System.Windows.Data.Binding("AmountKopecks"){Converter=new ReceiptMoneyConverter()}});
        bool pending=false;Payment current=payment;
        async Task Load(){await remote.RefreshAsync(lifetime.Token);await using var db=factory.Create();current=await db.Payments.AsNoTracking().SingleAsync(p=>p.Id==payment.Id);var receipts=await remote.CommandAsync<PaymentReceipt[]>("payment-history",new{PaymentId=payment.Id},lifetime.Token);table.ItemsSource=receipts;info.Text="Текущий неоплаченный срок: "+Display.Date(current.DueDate)+" · "+DashboardState.Payment(current,remote.Snapshot.ServerTime,settings.Company.WeekendMask,settings.Company.PaymentSoonDays).Status+"\nСнятие отметки возвращает этот период к оплате. Последующие оплаченные периоды сохраняются.";}
        var box=new FrameworkElementFactory(typeof(CheckBox));box.SetBinding(CheckBox.IsCheckedProperty,new System.Windows.Data.Binding("Cancelled"){Converter=new InverseBooleanConverter(),Mode=System.Windows.Data.BindingMode.OneWay});
        box.SetValue(CheckBox.IsEnabledProperty,AccessRules.Level(remote.CurrentUser,"payments")>=2);
        box.AddHandler(CheckBox.ClickEvent,new RoutedEventHandler(async(sender,_)=>{
            if(sender is not CheckBox check||check.DataContext is not PaymentReceipt receipt)return;
            if(pending||receipt.Cancelled){check.IsChecked=!receipt.Cancelled;return;}pending=true;table.IsEnabled=false;
            try{await remote.CommandAsync<bool>("payment-cancel",new PaymentOperation(payment.Id,RemoteData.EntityStamp(current),Guid.NewGuid().ToString(),receipt.Id),lifetime.Token);await Load();}
            catch(Exception e){check.IsChecked=!receipt.Cancelled;AppMessageBox.Show(window,e.Message,"Отмена оплаты");}finally{pending=false;table.IsEnabled=true;}
        }));
        table.Columns.Insert(0,new DataGridTemplateColumn{Header="Оплачено",CellTemplate=new DataTemplate{VisualTree=box}});
        // Template checkboxes handle cancellation; normal history fields remain immutable.
        if(documents.Length>0){var actions=new WrapPanel();var details=UiButton("Счёт · реквизиты и документ");var download=UiButton("Скачать PDF");actions.Children.Add(details);actions.Children.Add(download);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
            details.Click+=async(_,_)=>{try{var selected=documents[0];if(documents.Length>1){var choose=new FormDialog(window,"Выбрать счёт").Select("doc","Документ",documents.Select(d=>new Choice(d.Id,d.Data.Number+" · "+d.Data.LinkedPeriod?.ToString("dd.MM.yyyy"))),documents[0].Id);if(choose.ShowDialog()!=true)return;selected=documents.Single(d=>d.Id==choose.Selected<int>("doc"));}await ReviewInvoice(selected.Id,window);}catch(Exception e){AppMessageBox.Show(window,e.Message,"Счёт");}};
            download.Click+=async(_,_)=>{try{await DownloadInvoice(payment.Id);}catch(Exception e){AppMessageBox.Show(window,e.Message,"Документ");}};
        }
        table.IsReadOnly=true;root.Children.Add(TableSurface(table));window.Content=root;window.Closing+=(_,e)=>{if(pending)e.Cancel=true;};await Load();window.ShowDialog();await ShowPage(page);
    }
    private async Task ShowEvents()
    {
        await using var db = factory.Create(); var count = await db.Incidents.CountAsync(); eventPage = Math.Clamp(eventPage, 0, Math.Max(0, (count - 1) / 100));
        var rows = await db.Incidents.AsNoTracking().OrderBy(x => x.Closed).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip(eventPage * 100).Take(100).ToArrayAsync();
        PageSubtitle.Text += $" · записей: {count} · стр. {eventPage + 1}";
        var table = Table(rows, ("Дата", "CreatedAt", "dd.MM.yyyy HH:mm"), ("Уровень", "Severity", null), ("Канал события", "Source", null), ("Событие", "Title", null), ("Закрыто", "Closed", null));
        table.Tag = "paged";
        ListPage(table, RowAction(table, "Подробности", () => { var e = Selected<Incident>(table); return ShowStoredDiagnosis(e.Title, Display.Stamp(e.CreatedAt), e.Details); }),
            RowAction(table, "Закрыть / открыть", async () => { var e = Copy(Selected<Incident>(table)); e.Closed = !e.Closed; await SaveEntity(e); }),
            EventPageButton("← Назад", eventPage > 0, -1), EventPageButton("Далее →", (eventPage + 1) * 100 < count, 1));
    }
    private Button EventPageButton(string title, bool enabled, int direction)
    {
        var button = ActionButton(title, async () => { eventPage += direction; await ShowPage(4); }); button.IsEnabled = enabled; return button;
    }
    private async Task ShowHistory(int? deviceId, int? jobId)
    {
        var table = Table(Array.Empty<HistoryRow>(), ("Дата", "Date", null), ("Почтовый профиль", "Provider", null), ("Задание", "Task", null), ("Результат", "Result", null), ("Уровень", "Severity", null), ("Размер", "Size", null));
        var window = new DialogWindow { Owner = this, Title = "История отчётов", Width = 1000, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(20) }; var details = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0), MinHeight = 50 }; var detailScroll = new ScrollViewer { Content = details, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; DockPanel.SetDock(detailScroll, Dock.Bottom); root.Children.Add(detailScroll);
        table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=async(_,_)=>{if(table.SelectedItem is HistoryRow row)await Run(()=>ShowMail(row.Entity));};
        table.SelectionChanged += (_, _) => { if (table.SelectedItem is HistoryRow r) details.Text = $"{r.Entity.Description}\n{r.Entity.Host}; {r.Entity.Duration}; {r.Entity.Evidence}"; };
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top);
        var prev = new Button { Content = "← Назад" }; var next = new Button { Content = "Далее →" }; var meta = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 8) };
        var explain = new Button { Content = "Разбор отчёта", IsEnabled = false }; explain.Click += async (_, _) => { if (table.SelectedItem is HistoryRow r) await ShowReportDiagnosis(r.Entity); }; table.SelectionChanged += (_, _) => explain.IsEnabled = table.SelectedItem != null;
        toolbar.Children.Add(prev); toolbar.Children.Add(next); toolbar.Children.Add(meta); toolbar.Children.Add(explain); root.Children.Add(toolbar); root.Children.Add(table); window.Content = root;
        int current = 0; bool loading = false;
        prev.Click += async (_, _) => { if (!loading) { current--; await ReadPage(); } }; next.Click += async (_, _) => { if (!loading) { current++; await ReadPage(); } };
        await ReadPage(); window.ShowDialog();
        async Task ReadPage()
        {
            loading = true;
            try
            {
                await using var db = factory.Create(); var query = db.Reports.AsNoTracking().Where(x => x.Provider!="DMARC" && (deviceId == null || x.DeviceId == deviceId) && (jobId == null || x.JobId == jobId));
                var count = await query.CountAsync(lifetime.Token); current = Math.Clamp(current, 0, Math.Max(0, (count - 1) / 100));
                var rows = await query.OrderByDescending(x => x.OccurredAt ?? x.ReceivedAt).ThenByDescending(x => x.Id).Skip(current * 100).Take(100).ToArrayAsync(lifetime.Token);
                table.ItemsSource = rows.Select(x => new HistoryRow(x)).ToArray(); meta.Text = $"Страница {current + 1} · всего: {count}"; prev.IsEnabled = current > 0; next.IsEnabled = (current + 1) * 100 < count;
            }
            catch (Exception e) { AppMessageBox.Show(this, e.Message, "История"); }
            finally { loading = false; }
        }
    }
    private sealed class HistoryRow(ReportEvent entity)
    {
        public ReportEvent Entity => entity; public DateTime SortDate => entity.OccurredAt ?? entity.ReceivedAt; public string Date => Display.Stamp(SortDate); public string Provider => entity.Provider;
        public string Task => entity.Task; public string Result => entity.IsPending ? "Требует разбора: "+entity.Issue : Display.Result(entity.Result); public string Severity => Display.Level(entity.Severity); public string Size => entity.SizeText;
    }
    private static DateTime? MaintenanceTime(string value) => string.IsNullOrWhiteSpace(value) ? null :
        DateTime.TryParseExact(value.Trim(), "dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date :
        throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Дата обслуживания: ДД.ММ.ГГГГ ЧЧ:ММ"));
    private static long? PaymentAmount(string value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        if(!decimal.TryParse(value.Replace(',','.'),System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var money) ||
            money<0 || money>999999999999.99m || decimal.Round(money,2)!=money)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Укажите сумму с точностью до копеек"));
        return (long)(money*100);
    }
    private sealed class PaymentRow(Payment entity, int weekendMask, int soonDays = 3,InvoiceView[]? invoices=null,InvoicePaymentLink? link=null)
    {
        public Payment Entity => entity;
        private (int Priority,LampState Lamp,string Status) Health => DashboardState.Payment(entity,AppTime.Today,weekendMask,soonDays);
        public int Priority=>Health.Priority; public LampState State=>Health.Lamp;
        public string Title => entity.Title;
        public bool HasDocument=>link?.HasDocument==true||invoices?.Any(x=>x.Files.Length>0)==true;
        public string Source=>entity.Repeat!=RepeatPeriod.None?((link!=null||invoices?.Length>0)?"Повторяющийся · счёт из почты":"Повторяющийся"):(link!=null||invoices?.Length>0)?"Из почты":"Вручную";
        public string Amount => entity.AmountKopecks is long money ? (money/100m).ToString("N2")+" RUB" : invoices?.FirstOrDefault(x=>x.Status=="Added") is InvoiceView bill?$"{bill.Data.AmountCents/100m:N2} {bill.Data.Currency}":link!=null&&entity.Notes.StartsWith("Из почты · ",StringComparison.Ordinal)?entity.Notes.Split('·')[1].Trim():"Не указана";
        public DateTime SortDate => PaymentCalendar.EffectiveDueDate(entity.DueDate, weekendMask);
        public string Date => Display.Date(SortDate); public string ScheduledDate => Display.Date(entity.DueDate);
        public string Repeat => entity.Repeat switch { RepeatPeriod.Weekly => "Еженедельно", RepeatPeriod.Monthly => "Ежемесячно", RepeatPeriod.Yearly => "Ежегодно", _ => "Нет" };
        public string Status => Health.Status; public string Notes => entity.Notes;
    }
}
