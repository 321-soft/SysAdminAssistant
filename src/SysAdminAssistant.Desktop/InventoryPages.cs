using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private int inventoryTab;
    private async Task ShowInventory()
    {
        if(!await UnlockInventory()){remote.InventoryOpen=false;await remote.ClearInventoryAsync(lifetime.Token);ShowLockedInventory();return;}
        var tabs = new WrapPanel();
        foreach (var (name, index) in new[] { "Имущество", "Сотрудники", "Акты" }.Select((s, i) => (s, i)))
        {
            var button = ActionButton(name, async () => { inventoryTab = index; await ShowPage(9); });
            button.Style = (Style)FindResource(inventoryTab == index ? "PrimaryButton" : "SegmentButton"); tabs.Children.Add(button);
        }
        var root = new DockPanel();var tabCard=new Border{Child=tabs,Padding=new(12,10,12,2),Margin=new(0,0,0,12),Background=DesktopTheme.Brush("#FFFFFF"),BorderBrush=Brush("#DFE7F1"),BorderThickness=new(1),CornerRadius=new(10)};DockPanel.SetDock(tabCard,Dock.Top);root.Children.Add(tabCard);
        await using var db = factory.Create();
        if (inventoryTab == 0)
        {
            var employees = await db.Employees.AsNoTracking().ToDictionaryAsync(e => e.Id);
            var assets = await db.Assets.AsNoTracking().OrderBy(a => a.InventoryNumber).ToArrayAsync();
            var rows = assets.Select(a => new AssetRow(a, a.EmployeeId is int id && employees.TryGetValue(id, out var e) ? e.FullName : "—")).ToArray();
            var table = Table(rows, ("Инв. №", "Number", null), ("Имущество", "Name", null), ("WIN-имя", "WindowsName", null), ("SN", "Serial", null), ("Состояние", "State", null), ("Сотрудник", "Employee", null), ("Характеристики", "Specifications", null));
            table.SelectionMode = DataGridSelectionMode.Extended;
            var filter = new TextBox { Tag = "ui-search-inventory", MinWidth = 220, ToolTip = "Номер, WIN-имя, SN, сотрудник или характеристики", Margin = new(0, 0, 10, 8) };
            var state = new ComboBox { Tag = "ui-filter", ItemsSource = new[] { new Choice(null, "Все состояния") }.Concat(Enum.GetValues<AssetState>().Select(s => new Choice(s, InventoryDisplay.State(s)))), SelectedIndex = 0, MinWidth = 160 };
            void Apply()
            {
                var selectedState = (state.SelectedItem as Choice)?.Key as AssetState?;
                table.ItemsSource = rows.Where(r => (selectedState == null || r.Asset.State == selectedState.Value) && (filter.Text.Length == 0 || string.Join(" ", r.Number, r.Name, r.WindowsName, r.Serial, r.Employee, r.Specifications).Contains(filter.Text, StringComparison.OrdinalIgnoreCase))).ToArray();
            }
            filter.TextChanged += (_, _) => Apply(); state.SelectionChanged += (_, _) => Apply();
            var search = new WrapPanel(); search.Children.Add(new TextBlock { Text = "Поиск ", VerticalAlignment = VerticalAlignment.Center }); search.Children.Add(filter); search.Children.Add(state);
            var panel = InventoryList(table, search,
                ActionButton("Добавить", () => EditAsset(new())),
                ActionButton("Создать на основании…", () => InventoryFromMonitoring(new() { Category = "", DiskType = "" })),
                RowAction(table, "Карточка", () => EditAsset(Selected<AssetRow>(table).Asset)),
                RowAction(table, "Выдать", () => ConductInventory(InventoryOperation.Issue, Selection()), true),
                RowAction(table, "Вернуть", () => ConductInventory(InventoryOperation.Return, Selection()), true),
                RowAction(table, "Списать", () => ConductInventory(InventoryOperation.WriteOff, Selection()), true),
                RowAction(table, "Ремонт / склад", async () => { var a = Selected<AssetRow>(table).Asset; await new InventoryService(factory).SetRepairAsync(a.Id, a.Revision, a.State != AssetState.Repair, lifetime.Token); await ShowPage(9); }),
                RowAction(table,"Отвязать от мониторинга",async()=>{
                    var asset=Copy(Selected<AssetRow>(table).Asset);
                    if(asset.DeviceId==null&&asset.WorkstationId==null)return;
                    if(!Confirm(this,"Отвязать имущество от мониторинга? Последние характеристики сохранятся."))return;
                    await new InventoryService(factory).UnlinkAssetAsync(asset.Id,asset.Revision,lifetime.Token);await ShowPage(9);
                }),
                RowAction(table,"Удалить",async()=>{
                    var asset=Selected<AssetRow>(table).Asset;
                    if(!Confirm(this,"Удалить карточку имущества без возможности восстановления? Мониторинг устройства останется."))return;
                    await new InventoryService(factory).DeleteAssetAsync(asset.Id,asset.Revision,lifetime.Token);await ShowPage(9);
                }),
                RowAction(table,"Изменения оборудования",()=>ShowHardwareHistory("asset",Selected<AssetRow>(table).Asset.Id.ToString())),
                RowAction(table, "История актов", () => ShowAssetActs(Selected<AssetRow>(table).Asset.Id)));
            root.Children.Add(panel);
            InventoryAsset[] Selection() { var selected = table.SelectedItems.Cast<AssetRow>().Select(r => r.Asset).ToArray(); if (selected.Length == 0) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Выберите имущество; Ctrl/Shift — несколько строк")); return selected; }
        }
        else if (inventoryTab == 1)
        {
            var people = await db.Employees.AsNoTracking().OrderBy(e => e.FullName).ToArrayAsync();
            var counts = await db.Assets.Where(a => a.EmployeeId != null).GroupBy(a => a.EmployeeId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count);
            var rows = people.Select(e => new EmployeeRow(e, counts.GetValueOrDefault(e.Id))).ToArray();
            var table = Table(rows, ("ФИО", "Name", null), ("Табельный №", "Number", null), ("Отдел", "Department", null), ("Должность", "Position", null), ("Имущества", "Count", null), ("Статус", "Status", null));
            var search = new TextBox { Tag = "ui-search-employees", ToolTip = "ФИО, табельный номер, отдел", MinWidth = 280 };
            search.TextChanged += (_, _) => table.ItemsSource = rows.Where(r => string.Join(" ", r.Name, r.Number, r.Department).Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            root.Children.Add(InventoryList(table, search, ActionButton("Скачать шаблон",()=>SaveExcel("employees",true)), ActionButton("Экспорт Excel",()=>SaveExcel("employees",false)), ActionButton("Импорт Excel",()=>ImportExcel("employees")), ActionButton("Добавить сотрудника", () => EditEmployee(new())), RowAction(table, "Изменить", () => EditEmployee(Selected<EmployeeRow>(table).Person)),
                RowAction(table, "Имущество сотрудника", () => ShowEmployeeAssets(Selected<EmployeeRow>(table).Person)),
                RowAction(table, "Возврат при увольнении", async () =>
                {
                    var employee = Selected<EmployeeRow>(table).Person;
                    await using var listDb = factory.Create(); var assets = await listDb.Assets.AsNoTracking().Where(a => a.EmployeeId == employee.Id).ToArrayAsync();
                    if (assets.Length == 0) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("За сотрудником нет имущества. Можно снять отметку «Работает» в его карточке"));
                    await ConductInventory(InventoryOperation.Return, assets, employee.Id, "В связи с увольнением работника", dismissal: true);
                })));
        }
        else
        {
            var acts = await db.Acts.AsNoTracking().OrderByDescending(a => a.CreatedAt).ToArrayAsync();
            var rows = acts.Select(a => new ActRow(a)).ToArray();
            var table = Table(rows, ("Номер", "Number", null), ("Дата", "Date", "dd.MM.yyyy"), ("Операция", "Operation", null), ("Сотрудник", "Employee", null), ("Основание", "Reason", null));
            var search = new TextBox { Tag = "ui-search-acts", MinWidth = 280, ToolTip = "Номер акта, сотрудник, основание" };
            search.TextChanged += (_, _) => table.ItemsSource = rows.Where(r => string.Join(" ", r.Number, r.Employee, r.Reason).Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            root.Children.Add(InventoryList(table, search, RowAction(table, "Просмотр / печать", () => PreviewInventoryAct(Selected<ActRow>(table).Act.Id))));
        }
        PageHost.Children.Add(root);
    }
    private static DockPanel InventoryList(UIElement table, UIElement filter, params Button[] buttons)
    {
        if(table is DataGrid grid)ActionContextMenu(grid,buttons);
        return ListSurface(table, buttons, filter);
    }
    private async Task EditAsset(InventoryAsset a)
    {
        a = Copy(a);
        if (a.State == AssetState.WrittenOff || AccessRules.Level(remote.CurrentUser,"inventory")<2) { ShowAssetDetails(a); return; }
        await using var db = factory.Create();
        var usedCpu = await db.Assets.Where(x => x.Cpu != "").Select(x => x.Cpu).Distinct().ToArrayAsync();
        var form = new FormDialog(this, "Карточка имущества", 780)
            .Note("Привязка к сотруднику меняется актом выдачи или возврата. Ctrl/Shift в реестре позволяет выбрать несколько предметов для одного акта.")
            .Tab("Основное").Text("number", "Инвентарный номер *", a.InventoryNumber)
            .Suggest("category", "Категория *", ["Компьютер", "Ноутбук", "Сервер", "Монитор", "Принтер", "МФУ", "NAS", "Маршрутизатор", "Коммутатор", "ИБП", "Телефон", "Планшет", "Клавиатура", "Мышь", "Другое"], a.Category)
            .Text("win", "WIN-имя / имя устройства", a.WindowsName).Text("manufacturer", "Производитель", a.Manufacturer).Text("model", "Модель", a.Model).Text("sn", "Серийный номер (SN)", a.SerialNumber)
            .Tab("Характеристики").Suggest("cpu", "Процессор: модель или семейство (можно вписать свою)", HardwareCatalog.CpuModels.Concat(usedCpu).Distinct().OrderBy(x => x), a.Cpu)
            .Suggest("ram", "ОЗУ, ГБ (0 — не указано)", HardwareCatalog.Ram.Select(x => x.ToString()), a.RamGb.ToString())
            .Suggest("disk", "Суммарный объём дисков, ГБ (1 ТБ = 1000 ГБ)", HardwareCatalog.Disk.Select(x => x.ToString()), a.DiskGb.ToString())
            .Suggest("diskType", "Тип накопителей", ["SSD", "NVMe", "HDD", "SSD + HDD", "NVMe + HDD", "RAID", "Другое"], a.DiskType)
            .Text("diskDetails", "Отдельные диски: модель, объём, SN / RAID", a.DiskDetails, true)
            .Text("gpu", "Видеокарта", a.Gpu).Suggest("os", "Операционная система", ["Windows 11 Pro", "Windows 10 Pro", "Windows Server 2025", "Windows Server 2022", "Windows Server 2019", "Linux", "macOS", "Без ОС"], a.OperatingSystem)
            .Tab("Учёт и комплектность").Text("location", "Расположение", a.Location).Suggest("condition", "Техническое состояние", ["Исправно", "Неисправно", "Требует диагностики", "Повреждено", "Неполный комплект"], a.Condition)
            .Text("value", "Стоимость для актов, руб. (необязательно)", InventoryMoney.Edit(a.ValueKopecks))
            .Text("purchase", "Дата покупки, ДД.ММ.ГГГГ (необязательно)", a.PurchasedAt is null ? "" : Display.Date(a.PurchasedAt))
            .Text("warranty", "Гарантия до, ДД.ММ.ГГГГ (необязательно)", a.WarrantyUntil is null ? "" : Display.Date(a.WarrantyUntil))
            .Text("notes", "Комплектность и примечания", a.Notes, true);
        string LinkLabel() => a.WorkstationId != null ? "Рабочая станция" : a.DeviceId != null ? "Устройство" : "Без привязки";
        var linkLabel = new TextBlock { Text = ProductText.Get(LinkLabel()) };
        form.Tab("Мониторинг").InsertWorkspacePreview(linkLabel);
        if ((AccessRules.Level(remote.CurrentUser,"devices") > 0 || AccessRules.Level(remote.CurrentUser,"workstations") > 0)
            && (a.DeviceId == null || AccessRules.Level(remote.CurrentUser,"devices") > 0)
            && (a.WorkstationId == null || AccessRules.Level(remote.CurrentUser,"workstations") > 0))
            form.Action("Привязать к мониторингу…", async () =>
            {
                var draft = Copy(a);
                draft.WindowsName = form.Value("win"); draft.SerialNumber = form.Value("sn");
                draft.Category = form.Value("category"); draft.Location = form.Value("location");
                draft.OperatingSystem = form.Value("os"); draft.Cpu = form.Value("cpu");
                draft.RamGb = form.Number("ram", 0, 1048576);
                draft.Manufacturer=form.Value("manufacturer");draft.Model=form.Value("model");
                draft.DiskGb=form.Number("disk",0,int.MaxValue);draft.DiskDetails=form.Value("diskDetails");draft.Gpu=form.Value("gpu");
                if (await PickInventorySource(draft, form) is not { } selected) return "";
                a.DeviceId = selected.DeviceId; a.WorkstationId = selected.WorkstationId;
                form.Set("win", selected.WindowsName); form.Set("sn", selected.SerialNumber);
                form.Set("location", selected.Location); form.SetEditableChoice("category", selected.Category);
                form.SetEditableChoice("os", selected.OperatingSystem); form.SetEditableChoice("cpu", selected.Cpu);
                form.SetEditableChoice("ram", selected.RamGb.ToString());
                form.Set("manufacturer",selected.Manufacturer);form.Set("model",selected.Model);
                form.SetEditableChoice("disk",selected.DiskGb.ToString());
                form.Set("diskDetails",selected.DiskDetails);form.Set("gpu",selected.Gpu);
                a.HardwareJson=selected.HardwareJson;
                linkLabel.Text = ProductText.Get(LinkLabel());
                form.MarkDirty();
                return "";
            });
        if(a.DeviceId!=null||a.WorkstationId!=null)
            form.Action("Отвязать от мониторинга",()=>{
                a.DeviceId=null;a.WorkstationId=null;linkLabel.Text="Без привязки";form.MarkDirty();return Task.FromResult("Отвязка будет сохранена вместе с карточкой.");
            });
        StationHardware? hardware=null;
        try{if(a.HardwareJson.Length>0)hardware=System.Text.Json.JsonSerializer.Deserialize<StationHardware>(a.HardwareJson);}catch(System.Text.Json.JsonException){}
        form.Tab("Аппаратные сведения").Text("hardware","Детализация мониторинга",HardwareText(hardware,a.SerialNumber),true,true).FieldHeight("hardware",300);
        if(a.Id>0)form.Tab("Изменения оборудования").ActionAsync("Открыть историю изменений",()=>ShowHardwareHistory("asset",a.Id.ToString(),form));
        AddInventoryPhotoTab(form,()=>new InventoryModelKey(form.Value("category"),form.Value("manufacturer"),form.Value("model")),
            true,()=>form.Value("sn"),()=>form.Value("win"));
        form.Validate = () => { _ = InventoryMoney.Parse(form.Value("value")); form.Number("ram", 0, 1048576); form.Number("disk", 0, int.MaxValue); if (form.Value("purchase").Length > 0) form.Date("purchase"); if (form.Value("warranty").Length > 0) form.Date("warranty"); return Required(form.Value("number"), form.Value("category")); };
        if (form.ShowDialog() != true) return;
        a.ValueKopecks = InventoryMoney.Parse(form.Value("value")); a.InventoryNumber = form.Value("number"); a.Category = form.Value("category"); a.WindowsName = form.Value("win"); a.Manufacturer = form.Value("manufacturer"); a.Model = form.Value("model"); a.SerialNumber = form.Value("sn"); a.Cpu = form.Value("cpu"); a.RamGb = form.Number("ram", 0, 1048576); a.DiskGb = form.Number("disk", 0, int.MaxValue); a.DiskType = form.Value("diskType"); a.DiskDetails = form.Value("diskDetails"); a.Gpu = form.Value("gpu"); a.OperatingSystem = form.Value("os"); a.Location = form.Value("location"); a.Condition = form.Value("condition"); a.PurchasedAt = form.Value("purchase").Length == 0 ? null : form.Date("purchase"); a.WarrantyUntil = form.Value("warranty").Length == 0 ? null : form.Date("warranty"); a.Notes = form.Value("notes");
        if (a.SerialNumber.Length > 0 && await db.Assets.AnyAsync(x => x.SerialNumber == a.SerialNumber && x.Id != a.Id))
            if (!Confirm(this, "Такой SN уже указан у другого имущества. Сохранить карточку с совпадающим SN?")) return;
        await new InventoryService(factory).SaveAssetAsync(a, lifetime.Token); await ShowPage(9);
    }
    private async Task EditEmployee(Employee e)
    {
        e = Copy(e);
        var form = new FormDialog(this, "Сотрудник", 650).Text("name", "ФИО *", e.FullName).Text("number", "Табельный номер", e.PersonnelNumber).Text("department", "Отдел", e.Department).Text("position", "Должность", e.Position).Text("contact", "Контакт", e.Contact).Toggle("active", "Работает", e.Active).Note("Снять отметку «Работает» можно после возврата всего имущества. Сотрудник и его история остаются в базе.");
        form.Validate = () => Required(form.Value("name")); if (form.ShowDialog() != true) return;
        e.FullName = form.Value("name"); e.PersonnelNumber = form.Value("number"); e.Department = form.Value("department"); e.Position = form.Value("position"); e.Contact = form.Value("contact"); e.Active = form.Checked("active");
        await new InventoryService(factory).SaveEmployeeAsync(e, lifetime.Token); await ShowPage(9);
    }
    private async Task ConductInventory(InventoryOperation operation, InventoryAsset[] assets, int? employeeId = null, string reason = "", bool dismissal = false)
    {
        await using var db = factory.Create(); var employees = await db.Employees.AsNoTracking().OrderBy(e => e.FullName).ToArrayAsync();
        if (operation == InventoryOperation.Return)
        {
            var owners = assets.Select(a => a.EmployeeId).Distinct().ToArray();
            if (owners.Length != 1 || owners[0] is null) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Для возврата выберите имущество одного сотрудника")); employeeId = owners[0];
        }
        var defaults = settings.Inventory;
        if (reason.Length == 0) reason = operation switch { InventoryOperation.Issue => defaults.IssueReason, InventoryOperation.Return => defaults.ReturnReason, _ => defaults.WriteOffReason };
        var candidates = employees.Where(e => operation == InventoryOperation.Return ? e.Id == employeeId : e.Active).ToArray();
        if (employeeId == null && candidates.Length == 1) employeeId = candidates[0].Id;
        var form = new FormDialog(this, dismissal ? "Возврат при увольнении" : "Акт: " + InventoryDisplay.Operation(operation), 780)
            .Submit("Провести акт", operation == InventoryOperation.WriteOff)
            .Tab("Операция").Note("Компания: " + defaults.Organization + "\nОтветственный: " + defaults.Representative + "\nГород / место: " + defaults.Place + "\nИзменить можно во вкладке «Реквизиты».")
            .Text("date", "Дата акта, ДД.ММ.ГГГГ", Display.Date(AppTime.Today));
        if (operation != InventoryOperation.WriteOff) form.Select("employee", "Сотрудник *", candidates.Select(e => new Choice(e.Id, e.ToString())), employeeId);
        if (dismissal) form.Text("dismissalDate", "Дата увольнения, ДД.ММ.ГГГГ", Display.Date(AppTime.Today));
        form.Text("reason", "Основание *", reason, true)
            .Tab("Реквизиты").Text("org", "Компания / организация *", defaults.Organization)
            .Text("rep", "ФИО ответственного *", defaults.Representative)
            .Text("repPosition", "Должность ответственного", defaults.RepresentativePosition)
            .Text("place", "Город / место составления", defaults.Place);
        if (operation == InventoryOperation.WriteOff) form.Text("commission", "Комиссия (ФИО, должности)", defaults.Commission, true);
        form.ActionAsync("Запомнить реквизиты для следующих актов", async () =>
        {
            await UpdateSettings(s => { s.Inventory.Organization = form.Value("org"); s.Inventory.Representative = form.Value("rep"); s.Inventory.RepresentativePosition = form.Value("repPosition"); s.Inventory.Place = form.Value("place"); if (operation == InventoryOperation.WriteOff) s.Inventory.Commission = form.Value("commission"); });
            AppMessageBox.Show(form, "Реквизиты сохранены для следующих актов", "Реквизиты");
        }).Tab("Имущество").Note("В акт войдут " + assets.Length + " предметов. Каждый инвентарный объект — одна штука. Стоимость и характеристики берутся из карточек.")
            .Text("items", "Проверка состава акта", string.Join("\n\n", assets.Select(a => $"{a.InventoryNumber} · {a.Category} {a.Manufacturer} {a.Model}\nWIN: {a.WindowsName} · SN: {a.SerialNumber}\n{InventoryDisplay.Specs(a)}\nСтоимость: {InventoryMoney.Print(a.ValueKopecks)} руб. · Состояние: {a.Condition}")), true, true)
            .Note("«Провести акт» сохраняет операцию и снимок данных. Отмена печати не отменяет проведение. Возврат при увольнении не архивирует сотрудника автоматически.");
        form.Validate = () => { form.Date("date"); if (dismissal) form.Date("dismissalDate"); if (operation != InventoryOperation.WriteOff && !form.HasSelection("employee")) return "Выберите сотрудника"; return Required(form.Value("org"), form.Value("rep"), form.Value("reason")); };
        if (form.ShowDialog() != true) return;
        var id = await new InventoryService(factory).ConductAsync(new(operation, assets.Select(a => a.Id).ToArray(), operation == InventoryOperation.WriteOff ? null : form.Selected<int>("employee"), form.Date("date"), form.Value("org"), form.Value("rep"), form.Value("place"), form.Value("reason"), operation == InventoryOperation.WriteOff ? form.Value("commission") : "", assets.ToDictionary(a => a.Id, a => a.Revision), form.Value("repPosition"), dismissal ? form.Date("dismissalDate") : null), lifetime.Token);
        await ShowPage(9); await PreviewInventoryAct(id);
    }
    private async Task ShowEmployeeAssets(Employee employee)
    {
        await using var db = factory.Create(); var assets = await db.Assets.AsNoTracking().Where(a => a.EmployeeId == employee.Id).OrderBy(a => a.InventoryNumber).ToArrayAsync();
        var table = Table(assets.Select(a => new AssetRow(a, employee.FullName)), ("Инв. №", "Number", null), ("Наименование", "Name", null), ("WIN", "WindowsName", null), ("SN", "Serial", null), ("Характеристики", "Specifications", null));
        new DialogWindow { Owner = this, Title = "Имущество — " + employee.FullName, Width = 1000, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = table }.ShowDialog();
    }
    private async Task ShowAssetActs(int assetId)
    {
        await using var db = factory.Create(); var actIds = db.ActLines.Where(l => l.AssetId == assetId).Select(l => l.ActId);
        var acts = await db.Acts.AsNoTracking().Where(a => actIds.Contains(a.Id)).OrderByDescending(a => a.CreatedAt).ToArrayAsync();
        var table = Table(acts.Select(a => new ActRow(a)), ("Номер", "Number", null), ("Дата", "Date", "dd.MM.yyyy"), ("Операция", "Operation", null), ("Сотрудник", "Employee", null));
        var window = new DialogWindow { Owner = this, Title = "История имущества", Width = 900, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var print = new Button { Content = "Просмотр / печать", IsEnabled = false }; table.SelectionChanged += (_, _) => print.IsEnabled = table.SelectedItems.Count == 1; print.Click += async (_, _) => { try { if (table.SelectedItem is ActRow row) await PreviewInventoryAct(row.Act.Id, window); } catch (Exception e) { AppMessageBox.Show(window, e.Message); } };
        window.Content = InventoryList(table, new TextBlock { Text = "Выберите акт для повторной печати" }, print); window.ShowDialog();
    }
    private void ShowAssetDetails(InventoryAsset a)
    {
        var view=new FormDialog(this,"Карточка имущества",700).Tab("Основное")
            .Fact("Имущество",$"{a.InventoryNumber} · {a.Category} {a.Model}")
            .Fact("WIN-имя",a.WindowsName).Fact("Серийный номер",a.SerialNumber)
            .Fact("Характеристики",InventoryDisplay.Specs(a)).Fact("Техническое состояние",a.Condition)
            .Fact("Примечания",a.Notes);
        if(!string.IsNullOrWhiteSpace(a.Manufacturer)&&!string.IsNullOrWhiteSpace(a.Model))
            AddInventoryPhotoTab(view,()=>new(a.Category,a.Manufacturer,a.Model),false,()=>a.SerialNumber,()=>a.WindowsName);
        if(a.Id>0)view.Tab("Изменения оборудования").ActionAsync("Открыть историю изменений",()=>ShowHardwareHistory("asset",a.Id.ToString(),view));
        view.ViewOnly().ShowDialog();
    }
    private sealed class AssetRow(InventoryAsset asset, string employee)
    {
        public InventoryAsset Asset => asset; public string Number => asset.InventoryNumber; public string Name => string.Join(" ", asset.Category, asset.Manufacturer, asset.Model); public string WindowsName => asset.WindowsName; public string Serial => asset.SerialNumber; public string State => InventoryDisplay.State(asset.State); public string Employee => employee; public string Specifications => InventoryDisplay.Specs(asset);
    }
    private sealed class EmployeeRow(Employee employee, int count)
    {
        public Employee Person => employee; public string Name => employee.FullName; public string Number => employee.PersonnelNumber; public string Department => employee.Department; public string Position => employee.Position; public int Count => count; public string Status => employee.Active ? "Работает" : "Архив";
    }
    private sealed class ActRow(InventoryAct act)
    {
        public InventoryAct Act => act; public string Number => act.Number; public DateTime Date => act.Date; public string Operation => act.Operation == InventoryOperation.Return && act.DismissalDate != null ? "Возврат при увольнении" : InventoryDisplay.Operation(act.Operation); public string Employee => act.EmployeeName; public string Reason => act.Reason;
    }
}
