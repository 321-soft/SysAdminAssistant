using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private int settingsTab;
    private async Task EditProductPreferences(){
        var f=new FormDialog(this,"Уведомления и обновления",760).Select("language","Язык серверных уведомлений",new[]{new Choice("ru","Русский"),new Choice("en","English")},settings.NotificationLanguage).Text("primary","Основной адрес release.json",settings.ProductUpdates.Primary).Text("mirror","Зеркало release.json (необязательно)",settings.ProductUpdates.Mirror).Note("Используются HTTPS и проверка подписи издателя. Сервер и клиент обновляются только кнопкой; автоматическое обновление агентов сохраняется.");
        if(f.ShowDialog()==true){var primary=f.Value("primary").Trim();var mirror=f.Value("mirror").Trim();_=ProductReleases.Https(primary);if(mirror.Length>0)_=ProductReleases.Https(mirror);await UpdateSettings(s=>{s.NotificationLanguage=(string?)f.Selected("language")??"ru";s.ProductUpdates.Primary=primary;s.ProductUpdates.Mirror=mirror;});}
    }
    private void ShowSettings()
    {
        var stack = new StackPanel();
        var sections = new TabControl{TabStripPlacement=Dock.Left,Background=Brushes.Transparent};
        sections.SizeChanged+=(_,_)=>sections.TabStripPlacement=sections.ActualWidth<780?Dock.Top:Dock.Left;
        var sectionPanels=new List<(TabItem Tab,StackPanel Panel)>();
        StackPanel Section(string name) {
            var panel = new StackPanel();
            var tab=new TabItem { Header=new TextBlock{Text=name,Width=145,TextWrapping=TextWrapping.Wrap}, Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new(16,0,0,0)} };
            sections.Items.Add(tab);sectionPanels.Add((tab,panel));
            return panel;
        }
        var accountSection=Section("Доступ");
        var serverSection=Section("Сервер");
        var mailSection=Section("Почта и отчёты");
        var invoiceSection=Section("Распознавание счетов");
        var notificationsSection=Section("Уведомления");
        var companySection=Section("Учёт");
        var routerSection=Section("Сеть");
        SettingsGroup(stack, "Моя учётная запись", remote.CurrentUser.Login + (remote.CurrentUser.Administrator ? " · главный администратор" : " · пользователь"), ActionButton("Сменить пароль входа", ChangeMyPassword));
        SettingsGroup(stack,"Сертификат подключения",remote.ServerFingerprint,ActionButton("Сертификат и доверие",ShowServerCertificate));
        if (AccessRules.Level(remote.CurrentUser,"settings")<1) { PageHost.Children.Add(new ScrollViewer { Content=stack }); return; }
        if(AccessRules.Level(remote.CurrentUser,"settings")==1) {
            SettingsGroup(stack,"Настройки сервера","Доступ только для просмотра",ActionButton("Просмотреть настройки",()=>{
                new FormDialog(this,"Настройки · просмотр",800).Text("value","Значения без секретов",System.Text.Json.JsonSerializer.Serialize(settings,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}),true,true).TextHeight("value",480).ViewOnly().ShowDialog();return Task.CompletedTask;
            }));PageHost.Children.Add(new ScrollViewer {Content=stack});return;
        }
        if(remote.CurrentUser.Administrator) SettingsGroup(accountSection, "Пользователи и аудит", "Управление учётными записями и доступом", ActionButton("Пользователи", ShowUsers), ActionButton("Журнал действий", ShowAudit));
        if(remote.CurrentUser.Administrator)SettingsGroup(routerSection,"Словари сетевых устройств","Просмотр, редактирование, импорт и экспорт сетевых словарей.",ActionButton("Сетевые словари",()=>ShowMonitoringDictionaries(false)));
        SettingsGroup(serverSection, "Сервер", remote.Address + " · база и проверки выполняются на сервере",
            ActionButton("Доверенные сети",EditServerNetworks),ActionButton("Часовой пояс",EditServerTimeZone),ActionButton("Сертификат и доверие",ShowServerCertificate),ActionButton("Подключиться заново", Reconnect), ActionButton("Хранение событий", EditRetention));
        SettingsGroup(serverSection,"Внешний контроль сервера","Проверка доступности с внешнего веб-сервера и уведомления о потере связи",ActionButton("Контроль доступности сервера",EditServerHeartbeat),ActionButton("Создать ZIP для хостинга",CreateWatchdogPackage));
        if(remote.CurrentUser.Administrator)SettingsGroup(serverSection,"Уведомления и обновления","Язык серверных уведомлений и источники обновлений",ActionButton("Настроить",EditProductPreferences));
        SettingsGroup(serverSection, "Мониторинг", "Постоянная работа серверной службы, независимо от клиента",
            ActionButton("Настроить мониторинг", EditMonitoring), ActionButton("Проверить сейчас", PollCycle));
        SettingsGroup(mailSection, "Почта / IMAP", "Отчёты → очередь → правила привязки → история устройства или задания",
            ActionButton("Почтовые ящики IMAP", EditImap), ActionButton("Проверить почту сейчас", RequestMailPoll), ActionButton("Диагностика почты", ShowMailDiagnostics), ActionButton("Сверить даты писем с IMAP", RepairMailDates), ActionButton("Правила привязки", ShowRules), ActionButton("Источники почты", ShowMailSources), ActionButton("Справочник предупреждений", ShowDiagnosticGuide), ActionButton("Импорт отчёта .eml", ImportEml));
        SettingsGroup(invoiceSection,"Счета из почты","Дополнительный сбор счетов; ручные и повторяющиеся платежи сохраняются",ActionButton("Почта для счетов",EditInvoiceSettings),ActionButton("Поставщики и обучение",ShowInvoiceTemplates),ActionButton("История обработки счетов",ShowInvoiceHistory),ActionButton("Применить правила к ожидающим",ReprocessInvoices),ActionButton("Проверить счета сейчас",async()=>{await remote.PostAsync<object,bool>("api/invoices/poll",new{},lifetime.Token);AppMessageBox.Show(this,"Проверка запущена на сервере. Результат — в истории обработки.","Счета");}));
        SettingsGroup(routerSection, "MikroTik", "Индивидуальные подключения, словари и логи · только мониторинг",
            ActionButton("Сетевое оборудование",()=>ShowPage(6)));
        SettingsGroup(notificationsSection, "SMTP", "Исходящие уведомления",
            ActionButton("Подключение SMTP", EditSmtp), ActionButton("Отправить тест", SendSmtpTest));
        SettingsGroup(notificationsSection, "Telegram", "Серверные уведомления · критичные, средние и информационные",
            ActionButton("Настроить Telegram", EditTelegram), ActionButton("Отправить тест", SendTelegramTest));
        SettingsGroup(companySection, "Компания", "Выходные применяются только к счетам и платежам",
            ActionButton("Рабочая неделя", EditCompanyCalendar));
        SettingsGroup(companySection, "Инвентаризация", "Реквизиты подставляются в новые акты; ранее проведённые акты сохраняют прежние данные",
            ActionButton("Реквизиты актов", EditInventorySettings));
        while(stack.Children.Count>0) {var child=stack.Children[0];stack.Children.RemoveAt(0);accountSection.Children.Insert(0,child);}
        sections.SelectedIndex=Math.Clamp(settingsTab,0,sections.Items.Count-1);
        sections.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,sections)&&sections.SelectedIndex>=0)settingsTab=sections.SelectedIndex;};
        var root=new DockPanel();var searchBar=new DockPanel{Margin=new(0,0,0,12)};DockPanel.SetDock(searchBar,Dock.Top);root.Children.Add(searchBar);
        var caption=new TextBlock{Text="Найти настройку",VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,10)};DockPanel.SetDock(caption,Dock.Left);searchBar.Children.Add(caption);
        var search=new TextBox{MinWidth=80,ToolTip="Поиск по всем группам: почта, Telegram, сертификат, мониторинг, пользователи…"};
        var reset=UiButton("Очистить");DockPanel.SetDock(reset,Dock.Right);searchBar.Children.Add(reset);searchBar.Children.Add(search);
        var searchResult=new TextBlock{Foreground=DesktopTheme.Brush("#64748B"),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)};DockPanel.SetDock(searchResult,Dock.Top);root.Children.Add(searchResult);root.Children.Add(sections);
        void Filter(){var query=search.Text.Trim();var total=0;TabItem? first=null;foreach(var item in sectionPanels){var visible=0;foreach(var card in item.Panel.Children.OfType<FrameworkElement>()){var text=card.Tag as string??"";var match=query.Length==0||text.Contains(query,StringComparison.OrdinalIgnoreCase);card.Visibility=match?Visibility.Visible:Visibility.Collapsed;if(match)visible++;}item.Tab.Visibility=visible>0||query.Length==0?Visibility.Visible:Visibility.Collapsed;if(visible>0)first??=item.Tab;total+=visible;}if(sections.SelectedItem is not TabItem selected||selected.Visibility!=Visibility.Visible)sections.SelectedItem=first;searchResult.Text=query.Length==0?"Выберите группу слева. Изменения сохраняются в открываемых формах.":total==0?"Настройки не найдены. Попробуйте другое слово.":$"Найдено групп: {total}. Доступные разделы показаны слева.";}
        search.TextChanged+=(_,_)=>Filter();reset.Click+=(_,_)=>search.Clear();Filter();PageHost.Children.Add(root);
    }
    private static void SettingsGroup(StackPanel parent, string title, string subtitle, params Button[] buttons)
    {
        var content = new StackPanel { Margin = new(14, 0, 14, 9) };
        content.Children.Add(new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Foreground = DesktopTheme.Brush("#64748B"), Margin = new(0, 0, 0, 12) });
        var actions = new WrapPanel(); foreach (var button in buttons) actions.Children.Add(button); content.Children.Add(actions);
        var card = Card(title, content);card.Tag=title+" "+subtitle+" "+string.Join(" ",buttons.Select(b=>b.Content?.ToString())); card.Margin = new(0, 0, 0, 11); parent.Children.Add(card);
    }
    private async Task EditServerHeartbeat(){
        var current=await remote.CommandAsync<ServerHeartbeatStatus>("server-heartbeat-status",new{},lifetime.Token);
        var form=new FormDialog(this,"Контроль сервера · свой PHP-наблюдатель",780)
         .Toggle("enabled","Посылать подписанный сигнал жизни раз в минуту",settings.ServerHeartbeat.Enabled)
         .Text("url","HTTPS-адрес heartbeat.php на своём сайте",settings.ServerHeartbeat.EndpointUrl)
         .Text("id","ID сервера · тот же, что в config.php",settings.ServerHeartbeat.ServerId)
         .Secret("secret","Общий ключ из config.php · 64 hex-символа · пусто = оставить сохранённый")
         .Note("Готовый архив создаётся кнопкой «Создать ZIP для хостинга» в этом разделе настроек. Пакет WebWatchdog также включён в сборку. Установите его на хостинге вне ДЦ, настройте cron каждые 5 минут и выполните команду test для Telegram. Сервер посылает подписанный сигнал раз в минуту после успешного чтения БД. При отсутствии сигналов более 5 минут cron отправит тревогу — фактически через 5–10 минут после последнего сигнала.")
         .Note("Токен Telegram хранится на сайте; можно использовать нынешнего бота. При плановом обслуживании включите maintenance в config.php. Сам HTTPS API Assistant наружу открывать не нужно. Секреты и состояние на сайте размещаются вне публичной папки.")
         .Note(current.Message+"\nПоследняя попытка: "+Display.Stamp(current.LastAttemptUtc?.ToServerTime())+"\nПоследний подтверждённый сигнал: "+Display.Stamp(current.LastSuccessUtc?.ToServerTime()));
        form.Validate=()=>{ServerHeartbeatSettings.ValidateIdentity(form.Value("id"));if(form.Value("url").Length>0)ServerHeartbeatSettings.ValidateUrl(form.Value("url"));if(form.Value("secret").Length>0)ServerHeartbeatSettings.ValidateSecret(form.Value("secret"));if(form.Checked("enabled")&&(form.Value("url").Length==0||form.Value("secret").Length==0&&settings.ServerHeartbeat.ProtectedSecret.Length==0))return "Укажите адрес и общий ключ PHP-наблюдателя";return null;};
        if(form.ShowDialog()!=true)return;await UpdateSettings(s=>{s.ServerHeartbeat.Enabled=form.Checked("enabled");s.ServerHeartbeat.EndpointUrl=form.Value("url");s.ServerHeartbeat.ServerId=form.Value("id");if(form.Value("secret").Length>0)s.ServerHeartbeat.ProtectedSecret=LocalSecret.Protect(form.Value("secret"));});
    }
    private async Task EditServerTimeZone(){
        var zones=TimeZoneInfo.GetSystemTimeZones().Select(z=>new Choice(z.Id,z.DisplayName+" · "+z.Id)).ToArray();
        var form=new FormDialog(this,"Сервер · часовой пояс",800).Select("zone","Часовой пояс",zones,settings.TimeZoneId).Text("now","Текущее время",AppTime.Now.ToString("dd.MM.yyyy HH:mm:ss"),readOnly:true).Note("Применяется к расписаниям, счетам, IMAP, журналам и Telegram. Все клиенты используют пояс сервера. Метки событий хранятся в UTC; календарные даты не сдвигаются. Изменение влияет на следующие запуски по расписанию.");
        form.SelectionChanged("zone",()=>{if(form.Selected("zone") is string id)form.Set("now",AppTime.InZone(DateTime.UtcNow,id).ToString("dd.MM.yyyy HH:mm:ss"));});
        if(form.ShowDialog()==true){var zone=(string?)form.Selected("zone")??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выберите часовой пояс"));await UpdateSettings(s=>s.TimeZoneId=zone);await remote.RefreshAsync(lifetime.Token);await ShowPage(page);}
    }
    private async Task ShowServerCertificate(){
        var s=await remote.GetAsync<CertificateStatus>("api/server-certificate",lifetime.Token);
        var form=new FormDialog(this,"Сертификат сервера",800)
            .Section("HTTPS-соединение")
            .Fact("Адрес сервера",remote.Address)
            .Fact("Действует до",Display.Stamp(AppTime.FromUtc(s.ExpiresUtc)))
            .Fact("Часовой пояс",TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId).DisplayName)
            .Fact("Отпечаток SHA-256",s.Fingerprint)
            .Action("Копировать отпечаток",()=>Clipboard.SetText(s.Fingerprint))
            .Section("Продление и доверие")
            .Fact("Режим","Автоматически · версия доверия "+s.TrustVersion)
            .Fact("Подтвердили текущую версию доверия",s.Agents==0?"Нет подтверждённых агентов":s.Confirmed+" из "+s.Agents)
            .Note("Это подтверждение получения настроек доверия HTTPS, а не разрешение администратора и не индикатор доступности. Агент сообщает свою версию в подписанном отчёте. Время последнего отчёта показано отдельно.");
        if(s.AgentDetails is {Length:>0}){
            form.Section("Подтверждения по агентам");
            foreach(var a in s.AgentDetails){var result=a.LastSeenUtc==null?"Отчётов ещё нет":a.TrustVersion>=s.TrustVersion?"Доверие подтверждено":"Ожидается версия "+s.TrustVersion+" · сообщена "+a.TrustVersion;
                form.Fact(a.MachineName.Length>0?a.MachineName:"Устройство #"+a.DeviceId,result+"\nПоследний отчёт: "+Display.Stamp(a.LastSeenUtc?.ToServerTime())+" · агент "+(a.AgentVersion.Length>0?a.AgentVersion:"версия не передана"));}
        }
        if(s.NextFingerprint!="")form.Section("Следующий сертификат").Fact("Переключение",Display.Stamp(AppTime.FromUtc(s.ActivateUtc))).Fact("Следующий SHA-256",s.NextFingerprint).Action("Копировать следующий отпечаток",()=>Clipboard.SetText(s.NextFingerprint));
        if(s.PendingTelegram>0)form.Note("Ожидают отправки в Telegram: "+s.PendingTelegram+". При отключённом Telegram включите его в настройках.");
        if(s.Error!="")form.Section("Требует внимания").Fact("Продление",s.Error);
        form.ViewOnly().ShowDialog();
    }
    private async Task Reconnect()
    {
        var connection = new ConnectionWindow { Owner = this };
        if (connection.ShowDialog() != true || connection.Repository == null) return;
        var next = new MainWindow(connection.Repository); Application.Current.MainWindow = next; next.Show(); exitRequested = true; Close(); await Task.CompletedTask;
    }
    private async Task UpdateSettings(Action<AppSettings> edit)
    {
        var proposed = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        edit(proposed); settings = await remote.SaveSettingsAsync(proposed, lifetime.Token); remote.AcceptSettings(settings);
    }
    private async Task EditTelegram()
    {
        var t = settings.Telegram;
        var pinStatus=await remote.CommandAsync<string>("telegram-pin-status",new{},lifetime.Token);
        var form = new FormDialog(this, "Telegram · уведомления")
            .Toggle("enabled", "Включить Telegram-уведомления", t.Enabled)
            .Select("delay","Задержка отправки Telegram",TelegramSettings.DelayOptions.Select(n=>new Choice(n,n==0?"Без задержки":n==1?"1 минута":n==3?"3 минуты":n+" минут")),t.DelayMinutes)
            .Text("chat", "Chat ID получателя / группы / канала", t.ChatId)
            .Toggle("pin", "Закреплять список всех активных проблем", t.PinActiveProblems)
            .Note("Закреп содержит активные критичные проблемы и предупреждения всех разделов независимо от фильтров обычных уведомлений. Обновляется без новых звуковых уведомлений. В группе боту нужны права закреплять сообщения; в канале — редактировать сообщения. При большом списке закрепляются несколько страниц.")
            .Secret("token", "Токен бота · пусто = оставить сохранённый")
            .Toggle("critical", "Критичные", t.Critical)
            .Toggle("warning", "Средние · предупреждения", t.Warning)
            .Toggle("info", "Информационные", t.Info)
            .Note("Создайте бота через @BotFather. Для личного чата сначала нажмите Start у бота, для группы добавьте бота в неё. Укажите числовой Chat ID либо @имя канала. Уровни выбираются независимо. Отправляются новые события журнала: уровень, источник, название и время; содержимое писем не отправляется. Сначала сохраните настройки, затем отправьте тест.");
        form.Note("Состояние закрепа: "+pinStatus);
        form.Tab("Категории событий");
        foreach(var group in TelegramTopics.All.Where(x=>x.Id!="ksc.security").GroupBy(x=>x.Group)){var rows=group.ToArray();var key="group:"+group.Key;form.Note(group.Key);form.Toggle(key,"Все события группы",rows.All(x=>t.TopicEnabled(x.Id)));foreach(var topic in rows)form.Toggle("topic:"+topic.Id,topic.Name,t.TopicEnabled(topic.Id));form.ToggleChanged(key,()=>{foreach(var topic in rows)form.SetChecked("topic:"+topic.Id,form.Checked(key));});}
        form.Note("Категории и уровни действуют совместно. Сообщения о перевыпуске сертификата сервера обязательны и не отключаются категориями. Значки: 🔴 критично, 🟠 предупреждение, 🟢 восстановлено. Наличие обновлений отображается в карточках и не отправляется, как настроено ранее.");
        form.Validate = () => {
            var next = new TelegramSettings { Enabled = form.Checked("enabled"), ChatId = form.Value("chat").Trim(), Critical = form.Checked("critical"), Warning = form.Checked("warning"), Info = form.Checked("info"), ProtectedToken = form.Value("token").Length > 0 ? "new" : t.ProtectedToken };
            try { next.Validate(); return null; } catch (ArgumentException e) { return e.Message; }
        };
        if (form.ShowDialog() == true) await UpdateSettings(s => {
            s.Telegram.Topics=TelegramTopics.All.Where(x=>x.Id!="ksc.security").ToDictionary(x=>x.Id,x=>form.Checked("topic:"+x.Id));
            s.Telegram.DelayMinutes=(int)(form.Selected("delay")??3);
            s.Telegram.PinActiveProblems=form.Checked("pin"); s.Telegram.Enabled = form.Checked("enabled"); s.Telegram.ChatId = form.Value("chat").Trim();
            s.Telegram.Critical = form.Checked("critical"); s.Telegram.Warning = form.Checked("warning"); s.Telegram.Info = form.Checked("info");
            if (form.Value("token").Length > 0) s.Telegram.ProtectedToken = LocalSecret.Protect(form.Value("token").Trim());
        });
        await ShowPage(page);
    }
    private async Task SendTelegramTest()
    {
        await remote.CommandAsync<bool>("telegram-test", new { }, lifetime.Token);
        AppMessageBox.Show(this, "Тестовое сообщение поставлено в очередь с общей задержкой Telegram", "Telegram");
    }
    private async Task EditCompanyCalendar()
    {
        var form = new FormDialog(this, "Компания · рабочая неделя")
            .Text("company", "Компания", settings.Inventory.Organization)
            .Text("soon","Счёт считается близким к оплате за N рабочих дней · 1–90",settings.Company.PaymentSoonDays.ToString())
            .Days("weekends", settings.Company.WeekendMask, "Выходные дни · отметьте нерабочие дни")
            .Note("Срок счёта переносится вперёд на ближайший рабочий день. В выходные напоминания по счетам не показываются. Исходная дата повторения сохраняется. Расписания бэкапов и другие события не меняются.");
        form.Validate = () => { _=form.Number("soon",1,90);return form.Mask("weekends") == 127 ? "Оставьте хотя бы один рабочий день" : null; };
        if (form.ShowDialog() == true) await UpdateSettings(x => { x.Inventory.Organization = form.Value("company"); x.Company.WeekendMask = form.Mask("weekends"); x.Company.PaymentSoonDays=form.Number("soon",1,90); });
        await ShowPage(page);
    }
    private async Task EditInventorySettings()
    {
        var defaults = settings.Inventory;
        var form = new FormDialog(this, "Реквизиты и основания актов", 700)
            .Tab("Реквизиты").Text("org", "Компания / организация", defaults.Organization)
            .Text("rep", "ФИО ответственного сотрудника", defaults.Representative)
            .Text("repPosition", "Должность ответственного", defaults.RepresentativePosition)
            .Text("place", "Город / место составления", defaults.Place)
            .Text("commission", "Комиссия для списания (ФИО, должности)", defaults.Commission, true)
            .Tab("Основания").Text("issueReason", "Выдача: основание по умолчанию", defaults.IssueReason, true)
            .Text("returnReason", "Возврат: основание по умолчанию", defaults.ReturnReason, true)
            .Text("writeOffReason", "Списание: основание по умолчанию", defaults.WriteOffReason, true)
            .Note("Реквизиты подставляются в новые акты. Сотрудник и техника берутся из карточек, дата — сегодняшняя. Проведённые акты сохраняют прежние данные.");
        if (form.ShowDialog() == true)
            await UpdateSettings(s => { s.Inventory.Organization = form.Value("org"); s.Inventory.Representative = form.Value("rep"); s.Inventory.RepresentativePosition = form.Value("repPosition"); s.Inventory.Place = form.Value("place"); s.Inventory.Commission = form.Value("commission"); s.Inventory.IssueReason = form.Value("issueReason"); s.Inventory.ReturnReason = form.Value("returnReason"); s.Inventory.WriteOffReason = form.Value("writeOffReason"); });
        return;
    }
    private async Task EditMonitoring()
    {
        var form = new FormDialog(this, "Мониторинг").Toggle("enabled", "Включить автоматические проверки", settings.MonitoringEnabled)
            .Text("seconds", "Период, секунд · 15–3600", settings.PollSeconds.ToString()).Text("lan", "LAN · IP / DNS через ; · пусто = отключено", settings.LanPingHosts).Text("ping", "WAN · IP / DNS через ;", settings.PingHost).Text("alertDelay", "Задержка уведомления о недоступности, минут · 0–1440", settings.AvailabilityAlertMinutes.ToString()).Text("dns", "Имя для DNS-проверки", settings.DnsName)
            .Note("Проверки выполняет сервер. LAN — шлюз или другой узел локальной сети, WAN — внешние адреса. Не отвечает часть точек — жёлтый, ни одна — красный. До истечения задержки уведомлений нет; восстановление сбрасывает таймер. Бэкапы и события безопасности задержкой не ограничены. Уведомление возможно на первом неуспешном опросе после задержки; период опроса выбирай не больше задержки.");
        form.Validate = () => { _ = form.Number("seconds", 15, 3600); _=form.Number("alertDelay",0,1440);ConnectivityProbes.Validate(new(){LanPingHosts=form.Value("lan"),PingHost=form.Value("ping"),DnsName=form.Value("dns")});return null; };
        if (form.ShowDialog() == true) { await UpdateSettings(s => { s.MonitoringEnabled = form.Checked("enabled"); s.PollSeconds = form.Number("seconds", 15, 3600); s.LanPingHosts=form.Value("lan");s.AvailabilityAlertMinutes=form.Number("alertDelay",0,1440);s.PingHost = form.Value("ping"); s.DnsName = form.Value("dns"); }); ShowSettingsRefresh(); }
        return;
    }
    private void ShowSettingsRefresh() { PageHost.Children.Clear(); ShowSettings(); }
    private Task EditImap() => ShowMailboxes();
    private async Task EditSmtp()
    {
        var s = settings.Smtp;
        var form = new FormDialog(this, "SMTP").Text("host", "Сервер", s.Host).Text("port", "Порт · 465 TLS / 587 STARTTLS", s.Port.ToString()).Text("user", "Логин", s.User).Text("from", "Адрес отправителя", s.From).Secret("password", "Новый пароль · пусто = оставить прежний");
        form.Validate = () => { _ = form.Number("port", 1, 65535); return MailboxAddress.TryParse(form.Value("from"), out var from) && from.Address.Contains('@') ? Required(form.Value("host")) : "Укажите полный адрес отправителя"; };
        if (form.ShowDialog() == true) { await UpdateSettings(x => { var proposedSmtp = x.Smtp; proposedSmtp.Host = form.Value("host"); proposedSmtp.Port = form.Number("port", 1, 65535); proposedSmtp.User = form.Value("user"); proposedSmtp.From = form.Value("from"); if (form.Value("password").Length > 0) proposedSmtp.ProtectedPassword = LocalSecret.Protect(form.Value("password")); }); ShowSettingsRefresh(); }
        return;
    }
    private async Task SendSmtpTest()
    {
        var form = new FormDialog(this, "Тест SMTP").Submit("Отправить тест").Text("to", "Адрес получателя"); form.Validate = () => MailboxAddress.TryParse(form.Value("to"), out var to) && to.Address.Contains('@') ? null : "Укажите полный адрес получателя";
        if (form.ShowDialog() != true) return; await new MonitoringService(factory, settings).SendTestMailAsync(form.Value("to"), lifetime.Token); AppMessageBox.Show(this, "Тест поставлен в очередь отправки", "SMTP");
    }
    private async Task ImportEml()
    {
        var picker = new OpenFileDialog { Title = "Отчёты .eml", Filter = "Почтовые сообщения (*.eml)|*.eml", Multiselect = true };
        if (picker.ShowDialog(this) != true) return;
        var store = new ReportStore(factory, settings.CustomDiagnostics); int count = 0;
        foreach (var file in picker.FileNames)
        {
            if (new FileInfo(file).Length > MailPayloadReader.MaxMessageBytes) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("EML превышает лимит 16 МБ"));
            var message = await MimeMessage.LoadAsync(file, lifetime.Token);
            await using var fileStream = File.OpenRead(file);
            var digest = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(fileStream, lifetime.Token));
            count += await store.IngestMessageAsync(MailPayloadReader.ReadSafe(message, "manual-eml", digest, false), "manual-eml", digest, lifetime.Token);
        }
        AppMessageBox.Show(this, "Добавлено в анализ: " + count, "Отчёты"); backupTab = 1; await ShowPage(11);
    }
}
