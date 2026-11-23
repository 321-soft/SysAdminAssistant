using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow : Window
{
    private readonly RemoteRepository remote;
    private Task watchTask = Task.CompletedTask;
    private bool connected = true;
    private AppSettings settings;
    private DatabaseFactory factory;
    private readonly CancellationTokenSource lifetime = new();
    private bool snapshotPending;
    private long announcedRevision;
    private bool busy, ready;
    private int page, queuePage, eventPage;
    internal string CurrentHelpTopic=>new[]{"overview","devices","services","bills","events","backups","routers","vault","settings","inventory","dmarc","mail","ksc-connection-guide","overview"}[page];
    
    private static readonly string[] Pages = ["Обзор", "Устройства", "Сервисы", "Счета", "События", "Резервное копирование", "Коммутаторы", "Пароли", "Настройки", "Инвентаризация", "DMARC", "Почта и отчёты", "Kaspersky", "Рабочие станции"];
    private static readonly string[] Subtitles = ["Вся инфраструктура под контролем", "Реестр оборудования и история отчётов", "Проверки HTTP и TCP", "Сроки и повторяющиеся платежи", "Структурированные события инфраструктуры", "Расписания и контроль выполнения заданий", "Доступность, события и конфигурация сетевого оборудования", "Зашифрованные учётные данные", "Хранилище, мониторинг и почтовые интеграции", "Имущество, сотрудники, выдача, возврат и списание", "Отчёты о проверках почты домена: SPF, DKIM и возможные подделки", "Все отчёты и общая очередь нераспознанных писем", "События Kaspersky Security Center и уведомления", "Мониторинг Windows-компьютеров"];
    public MainWindow(RemoteRepository repository)
    {
        InitializeComponent();LanguageUi.Preserve(UserNameText);LanguageUi.Preserve(DeveloperCreditBlock);DeveloperCredit.Text=ProductText.Get("Бесплатная программа · Разработано ").TrimEnd()+" "; remote = repository; settings = remote.Settings;
        remote.SessionExpired = RenewSession;
        Width=Math.Min(Width,SystemParameters.WorkArea.Width-32);Height=Math.Min(Height,SystemParameters.WorkArea.Height-32);
        MinWidth=Math.Min(MinWidth,SystemParameters.WorkArea.Width-32);MinHeight=Math.Min(MinHeight,SystemParameters.WorkArea.Height-32);
        UserNameText.Text=remote.CurrentUser.Login;UserRoleText.Text=remote.CurrentUser.Administrator?"Администратор":"Пользователь";
        ClientVersionText.Text="Версия "+typeof(MainWindow).Assembly.GetName().Version?.ToString(3)+" · клиент";
        string[] glyphs=["\uE80F","\uE7F4","\uE9D9","\uE8C7","\uE81C","\uE753","\uE968","\uE72E","\uE713","\uE8F1","\uE8F2","\uE8A5","\uEA18","\uE7F4"];
        factory = new(settings, remote);
        UpdateThemeButton();
        foreach (var (group, indices) in new[] { ("", new[] { 0 }), ("ИНФРАСТРУКТУРА", new[] { 1, 13, 5, 6, 2, 11, 10, 12 }), ("УЧЁТ", new[] { 3, 9 }), ("ДОСТУП И НАСТРОЙКИ", new[] { 7, 8 }) })
        {
            if (group.Length > 0) Navigation.Children.Add(new TextBlock { Text = group, FontSize = 10, Foreground = Brush("#7C8DA6"), Margin = new(10, 10, 0, 6), FontWeight = FontWeights.SemiBold });
            foreach (var index in indices)
            {
                if(index==12&&!remote.CurrentUser.Administrator)continue;
                if(index!=8 && AccessRules.Level(remote.CurrentUser,AccessRules.PageModule(index))<1)continue;
                var button = new Button { Style = (Style)FindResource("NavigationButton"), Content = ProductText.Get(Pages[index]), HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, Foreground = Brush("#293B5B"), BorderThickness = new(0), Padding = new(10, 8, 10, 8), Tag = index };
                var label=index==8 && AccessRules.Level(remote.CurrentUser,"settings")==0?"Учётная запись":Pages[index];
                var nav=new DockPanel();var icon=new TextBlock{Text=glyphs[index],FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=17,Width=27,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(icon,Dock.Left);nav.Children.Add(icon);
                var dot=new Ellipse{Width=8,Height=8,Margin=new(6,1,2,0),VerticalAlignment=VerticalAlignment.Top,Visibility=Visibility.Hidden,IsHitTestVisible=false};
                DockPanel.SetDock(dot,Dock.Right);nav.Children.Add(dot);navigationDots[index]=(button,dot);
                nav.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center});button.Content=nav;button.ToolTip=Subtitles[index];
                button.Click += async (_, _) => await Run(async () => { await LoadPendingSnapshot(); await ShowPage(index); }); Navigation.Children.Add(button);
            }
        }
        SearchButton.ToolTip="Поиск по программе · Ctrl+K";RefreshButton.ToolTip="Перечитать текущий список, сохранив поиск и выделение · F5";
        PreviewKeyDown+=async(_,e)=>{if(busy)return;if(e.Key==System.Windows.Input.Key.K&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control){e.Handled=true;await Run(ShowGlobalSearch);}else if(e.Key==System.Windows.Input.Key.F5){e.Handled=true;RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}};
        SearchButton.Click += async (_,_)=>await Run(ShowGlobalSearch);
        RefreshButton.Click += async (_, _) => await Run(async () => { await LoadPendingSnapshot(true); ready = true; await ShowPage(page); });
        Loaded += async (_, _) => await Run(async () => { ready = true; await ShowPage(0); watchTask = remote.WatchAsync(RefreshFromServer, ServerDisconnected, lifetime.Token); StartNavigationHealth(); });
        var privacyTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
        privacyTimer.Tick+=async(_,_)=>{
            if(lifetime.IsCancellationRequested||busy||OwnedWindows.Count>0)return;
            if(page==9&&!inventoryMasked&&(inventoryUntil<=DateTime.UtcNow||remote.RequiresSignIn)){
                inventoryUntil=DateTime.MinValue;ClearWorkspaceFold();PageHost.Children.Clear();ShowLockedInventory();
                await Run(()=>remote.ClearInventoryAsync(lifetime.Token));
            }
        };privacyTimer.Start();Closed+=(_,_)=>privacyTimer.Stop();
        var updatesTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(20)};updatesTimer.Tick+=async(_,_)=>await RefreshUpdateIcons();updatesTimer.Start();Closed+=(_,_)=>updatesTimer.Stop();
        Loaded += (_, _) => StartProductUpdateChecks();
        InitializeTray();
        Closed += async (_, _) => { lifetime.Cancel();  try { await watchTask; } catch (OperationCanceledException) { } finally { await remote.LogoutAsync();remote.Dispose(); lifetime.Dispose(); } };
        Deactivated += (_, _) => { if (page == 7 && !IsActive && OwnedWindows.Count == 0) { organizationVaultUntil = DateTime.MinValue; _ = Run(() => ShowPage(7)); } };
    }
    private Task<bool> RenewSession(CancellationToken token) => Dispatcher.InvokeAsync(() =>
    {
        if(lifetime.IsCancellationRequested||token.IsCancellationRequested)return false;
        organizationVaultUntil=DateTime.MinValue;inventoryUntil=DateTime.MinValue;remote.InventoryOpen=false;
        connected=false;StatusLine.Text="Требуется повторный вход";
        var owner=Application.Current.Windows.OfType<Window>().LastOrDefault(w=>w.IsActive&&w.IsVisible)??this;
        var dialog=new SessionPasswordWindow(remote,token){Owner=owner};
        if(dialog.ShowDialog()!=true)return false;
        connected=true;snapshotPending=true;RefreshButton.Content="Обновить •";
        StatusLine.Text="Подключение восстановлено. Открытая форма сохранена.";
        return true;
    }).Task;
    // Notifications never rebuild the page or replace the replica being edited.
    private Task RefreshFromServer(long revision) => Dispatcher.InvokeAsync(() =>
    {
        if(lifetime.IsCancellationRequested)return;
        announcedRevision=revision;connected=true;ConnectionBadge.Text="● Сервер доступен";ConnectionBadge.Foreground=Brush("#16A34A");
        if(revision==remote.Revision){snapshotPending=false;RefreshButton.Content="Обновить";return;}
        snapshotPending=true;
        RefreshButton.Content="Обновить •";
        StatusLine.Text="Данные на сервере изменились. Нажмите «Обновить», чтобы перечитать список.";
    }).Task;
    private async Task LoadPendingSnapshot(bool force=false)
    {
        if(!force && !snapshotPending)return;
        await remote.RefreshAsync(lifetime.Token);
        settings=remote.Settings;connected=true;
        snapshotPending=announcedRevision!=0 && announcedRevision!=remote.Revision;
        RefreshButton.Content=snapshotPending?"Обновить •":"Обновить";
    }
    private Task ServerDisconnected(Exception error) => Dispatcher.InvokeAsync(() =>
    {
        if (lifetime.IsCancellationRequested) return;
        connected = false;ConnectionBadge.Text="● Нет связи с сервером";ConnectionBadge.Foreground=Brush("#DC2626"); if(overviewConnection!=null) { overviewConnection.Text="НЕТ СВЯЗИ С СЕРВЕРОМ · данные на "+Display.Stamp(remote.Snapshot.ServerTime); overviewConnection.Foreground=DesktopTheme.Brush("#B42335"); } organizationVaultUntil = DateTime.MinValue; StatusLine.Text = "Нет связи с сервером · снимок " + Display.Stamp(remote.Snapshot.ServerTime) + " · " + error.Message;
        if (!busy) RefreshButton.IsEnabled = true;
    }).Task;

    private async Task Run(Func<Task> action)
    {
        if (busy || lifetime.IsCancellationRequested) return;
        remote.AllowSessionPrompt();
        busy = true; BusyIndicator.Visibility = Visibility.Visible; RefreshButton.IsEnabled = false; Navigation.IsEnabled = false; PageHost.IsEnabled = false; StatusLine.Text = "Выполняется…";
        try { await action(); StatusLine.Text = connected ? "Сервер · снимок " + Display.Stamp(remote.Snapshot.ServerTime) + (remote.Snapshot.MonitorErrors.Length > 0 ? " · " + string.Join("; ", remote.Snapshot.MonitorErrors) : "") : "Нет связи · показан снимок " + Display.Stamp(remote.Snapshot.ServerTime); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (SessionRenewalCanceledException e) { StatusLine.Text=e.Message; }
        catch (Exception e) { StatusLine.Text = e.Message; if (IsVisible) AppMessageBox.Show(this, e.Message, "SysAdmin Assistant", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { BusyIndicator.Visibility = Visibility.Collapsed; RefreshButton.IsEnabled = true; busy = false; Navigation.IsEnabled = true; PageHost.IsEnabled = true; }
    }
    private async Task PollCycle()
    {
        await remote.PostAsync<object, bool>("api/poll", new { }, lifetime.Token);
    }
    private async Task ShowPage(int index)
    {
        if (!ready && index != 8) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("База не подключена. Подключитесь к серверу."));
        if (page == 7 && index != 7) organizationVaultUntil = DateTime.MinValue;
        if(page==9&&index!=9){inventoryUntil=DateTime.MinValue;remote.InventoryOpen=false;
            try{using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(2));await remote.PostAsync<object,bool>("api/personal/inventory/lock",new{},timeout.Token);}catch(Exception e)when(e is OperationCanceledException or System.Net.Http.HttpRequestException or InvalidOperationException){}
            await remote.ClearInventoryAsync(lifetime.Token);}
        if(index==12&&!remote.CurrentUser.Administrator||index!=8&&AccessRules.Level(remote.CurrentUser,AccessRules.PageModule(index))<1)index=8;
        if(workspacePages==null)await LoadWorkspace();
        liveUpdateTargets.Clear();
        CapturePageState();
        ClearWorkspaceFold();
        page = index; PageTitle.Text = Pages[index]; PageSubtitle.Text = Subtitles[index]; PageHost.Children.Clear();
        foreach (var button in Navigation.Children.OfType<Button>()){bool selected=(int)button.Tag==index;button.Background=selected?Brush("#E2EBFF"):Brushes.Transparent;button.Foreground=selected?Brush("#1948AE"):Brush("#293B5B");button.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;}
        switch (index)
        {
            case 0: await ShowOverview(); break; case 1: await ShowDevices(); break; case 2: await ShowServices(); break;
            case 3: await ShowPayments(); break; case 4: await ShowEvents(); break; case 5: await ShowBackups(); break;
            case 6: await ShowRouter(); break; case 7: await ShowVault(); break; case 8: ShowSettings(); break; case 9: await ShowInventory(); break; case 10: await ShowDmarc(); break; case 11: await ShowReports(); break; case 12: await ShowKsc(); break; case 13: await ShowWorkstations(); break;
        }
        if(index!=0&&index!=13)await AttachWorkspace(index);
        RestorePageState(index + ":" + (index == 9 ? inventoryTab : 0));
    }
    private static Border Metric(string title, string value, string color)
    {
        var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = title, FontSize = 13, MinHeight = 40, Foreground = Brush("#64748B"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new Viewbox { Height = 44, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 0), Child = new TextBlock { Text = value, FontSize = 32, FontWeight = FontWeights.SemiBold, Foreground = Brush(color) } });
        return new Border { Background = DesktopTheme.Brush("#FFFFFF"), BorderBrush = Brush("#DFE7F1"), BorderThickness = new(1), CornerRadius = new(10), Padding = new(18), Margin = new(0, 0, 12, 0), Child = stack };
    }
    private static Border Card(string title, UIElement content)
    {
        var dock = new DockPanel(); var header = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(14, 12, 14, 12) };
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header); dock.Children.Add(TableSurface(content));
        return new Border { BorderBrush = Brush("#DFE7F1"), BorderThickness = new(1), Background = DesktopTheme.Brush("#FFFFFF"), CornerRadius = new(10), Child = dock };
    }
    private static FrameworkElement Lamp(string title, LampState state)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(12, 9, 12, 9) };
        panel.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = LampBrush(state), Margin = new(0, 0, 8, 0) }); panel.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }); return new Border { Background = DesktopTheme.Brush("#FFFFFF"), BorderBrush = Brush("#DFE7F1"), BorderThickness = new(1), CornerRadius = new(6), Margin = new(0, 0, 12, 6), Child = panel };
    }
    private static Brush Brush(string color) => DesktopTheme.Brush(color);
    private static Brush LampBrush(LampState state) => Brush(state switch { LampState.Green => "#16A34A", LampState.Amber => "#D97706", LampState.Red => "#DC2626", _ => "#94A3B8" });
    private static DataGridTemplateColumn LampColumn(string path,string header="")
    {
        var ellipse = new FrameworkElementFactory(typeof(Ellipse)); ellipse.SetValue(FrameworkElement.WidthProperty, 16.0); ellipse.SetValue(FrameworkElement.HeightProperty, 16.0);
        ellipse.SetBinding(Shape.FillProperty, new Binding(path) { Converter = new LampConverter() });
        return new DataGridTemplateColumn { Header = header, Width = header.Length==0?40:100, CellTemplate = new DataTemplate { VisualTree = ellipse } };
    }
    private static DataGrid Table<T>(IEnumerable<T> rows, params (string Title, string Path, string? Format)[] columns)
    {
        var grid = new TypedDataGrid { ItemsSource = rows.ToArray(), Margin = new(0, 0, 0, 0) };
        InstallDoubleClick(grid);
        foreach (var (title, path, format) in columns)
        {
            Binding CellBinding() => new(path) { StringFormat = format, Converter = UiValueConverter.Instance, ConverterParameter=path };
            var text = new Style(typeof(TextBlock)); text.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis)); text.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, CellBinding())); text.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            grid.Columns.Add(new DataGridTextColumn { Header = ProductText.Get(title), Binding = CellBinding(), SortMemberPath = typeof(T).GetProperty(path+"Sort") != null ? path+"Sort" : typeof(T).GetProperty("Sort"+path) != null ? "Sort"+path : path, ElementStyle = text, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = path is "Title" or "Subject" or "Specifications" or "Name" ? 140 : path is "Date" or "CreatedAt" or "LastSeen" or "LastReport" or "Received" ? 140 : 95 });
        }
        return grid;
    }
    private DockPanel ListPage(UIElement content, params Button[] buttons)
    {
        var root = ListSurface(content, buttons, search: true); PageHost.Children.Add(root); return root;
    }
    private Button ActionButton(string title, Func<Task> action)
    {
        var allowed=AccessRules.Level(remote.CurrentUser,AccessRules.PageModule(page))>=2 || IsReadAction(title);
        if(title=="Создать объект инвентаризации")allowed=AccessRules.Level(remote.CurrentUser,"inventory")>=2;
        if(title is "Привязать" or "Правила привязки" or "Источники и словари" or "Источники почты")allowed=AccessRules.Level(remote.CurrentUser,"reports")>=2;
        if(title=="Проверить MikroTik сейчас")allowed=AccessRules.Level(remote.CurrentUser,"routers")>=1;
        if(title=="Разобрать письма устройства")allowed=AccessRules.Level(remote.CurrentUser,"reports")>=2 && AccessRules.Level(remote.CurrentUser,"devices")>=2;
        if(title is "Разобрать письма")allowed=AccessRules.Level(remote.CurrentUser,"reports")>=2 && AccessRules.Level(remote.CurrentUser,AccessRules.PageModule(page))>=2;
        if(title is "Пользователи" or "Журнал действий")allowed=remote.CurrentUser.Administrator;
        if(title is "Почтовые ящики" or "Подключение")allowed=AccessRules.Level(remote.CurrentUser,"settings")>=2;
        var button = UiButton(title);button.Tag=allowed;button.IsEnabled=allowed; button.Click += async (_, _) => await Run(action); return button;
    }
    private static bool IsReadAction(string title) => title.StartsWith("Счета из почты",StringComparison.Ordinal) || title is "Открыть доступ" or "Скачать шаблон" or "Экспорт Excel" or "Анализ конфигурации" or "Открыть состояние и события" or "Карточка / история" or "Открыть" or "Связанное устройство" or "Открыть письмо" or "Получить состояние" or "Firewall" or "Имущество" or "Сотрудники" or "Акты" or "Карточка" or "История актов" or "Имущество сотрудника" or "Просмотр / печать" or "История отчётов" or "Подробности" or "Просмотреть настройки" or "Показать" or "Заблокировать" or "Сменить пароль входа" or "Подключиться заново" or "Диагностика почты" or "Проверить почту сейчас" or "Разбор отчёта" or "Разбор предупреждений" or "Понятный отчёт" or "История задания" or "История" or "← Назад" or "Далее →" or "Обновить" or "Интерфейсы" or "События" or "Последние события KSC" or "Печать" or "Открыть акт";
    private static T Selected<T>(DataGrid table) where T : class => table.SelectedItem as T ?? throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Выберите строку"));
    private static string? Required(params string[] values) => values.Any(string.IsNullOrWhiteSpace) ? "Заполните обязательные поля" : null;
    private static IEnumerable<Choice> EnumChoices<T>() where T : struct, Enum => Enum.GetValues<T>().Select(x => new Choice(x, x.ToString()));
    private async Task SaveEntity<T>(T entity) where T : Entity
    {
        System.ComponentModel.DataAnnotations.Validator.ValidateObject(entity, new System.ComponentModel.DataAnnotations.ValidationContext(entity), true);
        await using var db = factory.Create();
        if (entity is Device device && device.Id != 0)
        {
            var current = await db.Devices.SingleAsync(d => d.Id == device.Id);
            if (current.Host != device.Host) { current.Status = "UNKNOWN"; current.LastSeen = null; current.LastError = ""; }
            current.ConnectionsJson=device.ConnectionsJson; current.Name = device.Name; current.Type = device.Type; current.Host = device.Host; current.Site = device.Site; current.Role = device.Role; current.Enabled = device.Enabled; current.MaintenanceUntil = device.MaintenanceUntil;
        }
        else if (entity is ServiceEntry service && service.Id != 0)
        {
            var current = await db.Services.SingleAsync(s => s.Id == service.Id);
            if (current.Address != service.Address || current.Type != service.Type) current.Status = "UNKNOWN";
            current.Name = service.Name; current.Type = service.Type; current.Address = service.Address; current.Notes = service.Notes; current.DeviceId = service.DeviceId;
        }
        else if (entity.Id == 0) db.Add(entity); else db.Update(entity); await db.SaveChangesAsync(); await remote.RefreshAsync(lifetime.Token); settings = remote.Settings; await ShowPage(page);
    }
    private static bool Confirm(Window owner, string text) => AppMessageBox.Show(owner, text, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
public sealed class LampConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => new SolidColorBrush(value is LampState s ? s switch { LampState.Green => Color.FromRgb(22, 163, 74), LampState.Red => Color.FromRgb(220, 38, 38), LampState.Amber => Color.FromRgb(217, 119, 6), _ => Color.FromRgb(148, 163, 184) } : Color.FromRgb(148, 163, 184));
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
