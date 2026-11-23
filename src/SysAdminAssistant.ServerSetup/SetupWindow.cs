using System.Security.Principal;
namespace SysAdminAssistant.ServerSetup;
internal sealed class SetupWindow : Form
{
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox target, data, user, password, repeat, networks, sqlHost, sqlDatabase, sqlUser, sqlPassword;
    private readonly ComboBox ip = new() { Width = 580 }, provider = new() { Width = 580, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown port = new() { Minimum = 1, Maximum = 65535, Value = 7443, Width = 180 };
    private readonly CheckBox trustSql = new() { Text = "Доверять сертификату SQL Server без проверки", AutoSize = true };
    private readonly CheckBox serviceAccount = new() { Text = "Создать отдельную учётную запись для службы", AutoSize = true, Checked = true };
    private readonly CheckBox existing = new() { Text = "Использовать уже настроенные данные (повторная попытка)", AutoSize = true };
    private readonly TextBox summary = new() { Multiline = true, ReadOnly = true, Width = 690, Height = 125, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, Width = 690, Height = 240, ScrollBars = ScrollBars.Vertical };
    private readonly Button back = new() { Text = "Назад", AutoSize = true }, next = new() { Text = "Далее", AutoSize = true }, install = new() { Text = "Установить сервер", AutoSize = true }, close = new() { Text = "Закрыть", AutoSize = true }, copy = new() { Text = "Копировать подключение", AutoSize = true, Visible = false };
    private readonly Label status = new() { Text = "Заполните параметры установки.", Dock = DockStyle.Fill, AutoSize = true };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 10 };
    private readonly CheckBox startMenu=new(){Text="Добавить управление сервером в меню «Пуск»",Checked=true,AutoSize=true}, desktop=new(){Text="Ярлык управления на рабочем столе",Checked=true,AutoSize=true}, openManagement=new(){Text="Открыть управление после установки",Checked=true,AutoSize=true};
    private bool busy, finished;
    private string connection = "";
    public SetupWindow()
    {
        Shown += (_,_) => SetupTheme.Apply(this);
        Text = "SysAdmin Assistant · Установка сервера"; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new(810, 710); MinimumSize = new(830, 750); Font = new("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new(SizeType.Absolute, 65)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 42)); root.RowStyles.Add(new(SizeType.Absolute, 16)); root.RowStyles.Add(new(SizeType.Absolute, 45));
        root.Controls.Add(new Label { Text = "Установка сервера", Font = new("Segoe UI", 21, FontStyle.Bold), AutoSize = true }, 0, 0);
        root.Controls.Add(tabs, 0, 1); root.Controls.Add(status, 0, 2); root.Controls.Add(progress, 0, 3);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        buttons.Controls.AddRange([back, next, copy, install, close]); root.Controls.Add(buttons, 0, 4); Controls.Add(root);
        var folders = Page("1. Папки");
        target = Field(folders, "Папка программы", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SysAdmin Assistant Server 0.6")); Browse(folders, target);
        data = Field(folders, "Папка базы, настроек и сертификата", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SysAdminAssistantServer-0.6")); Browse(folders, data);
        existing.Visible = false; existing.Checked = false;
        var uninstall = new Button { Text = "Удалить старую установку и базы…", AutoSize = true };
        uninstall.Click += (_, _) => { using var dialog = new UninstallWindow(); dialog.ShowDialog(this); };
        folders.Controls.Add(uninstall);
        var update=new Button {Text="Обновить установленный сервер…",AutoSize=true};
        update.Click+=(_,_)=>{using var dialog=new UpdateWindow();dialog.ShowDialog(this);};
        folders.Controls.Add(update);
        folders.Controls.AddRange([startMenu,desktop,openManagement]);
        Note(folders, "Для установленной 0.6 выберите «Обновить установленный сервер»: пути определятся автоматически. Новая установка требует пустых папок и новой базы. Удаление старых баз — отдельный мастер с подтверждениями.");
        var network = Page("2. Сеть и база");
        Note(network, "Локальный IP сервера"); network.Controls.Add(ip);
        foreach (var address in SetupEngine.LocalIps()) ip.Items.Add(address.ToString());
        if (ip.Items.Count > 0) ip.SelectedIndex = 0;
        Note(network, "Порт HTTPS"); network.Controls.Add(port);
        networks = Field(network, "Разрешённые подсети CIDR через запятую");
        Note(network, "Например: 192.168.10.0/24,192.168.20.0/24. Между подсетями нужна маршрутизация. Firewall: только частный и доменный профили.");
        provider.Items.AddRange(["SQLite — база на этом сервере", "SQL Server — создать базу автоматически"]); provider.SelectedIndex = 0; network.Controls.Add(provider);
        sqlHost = Field(network, "SQL Server: сервер\\экземпляр или сервер,порт", "localhost");
        sqlDatabase = Field(network, "Имя новой базы", "SysAdminAssistantServer-0.6");
        sqlUser = Field(network, "SQL-логин");
        sqlPassword = Field(network, "Пароль SQL-логина", secret: true);
        foreach (var field in new[] { sqlHost, sqlDatabase, sqlUser, sqlPassword }) field.Enabled = false;
        trustSql.Enabled = false; network.Controls.Add(trustSql);
        provider.SelectedIndexChanged += (_, _) => { foreach (var field in new[] { sqlHost, sqlDatabase, sqlUser, sqlPassword }) field.Enabled = provider.SelectedIndex == 1; trustSql.Enabled = provider.SelectedIndex == 1; };
        Note(network, "База и таблицы создаются автоматически. SQL-логину нужны права создания базы. Существующая база не перезаписывается. Соединение использует TLS. Галочка доверия сохраняет шифрование, но отключает проверку подлинности SQL-сервера.");
        var access = Page("3. Доступ");
        user = Field(access, "Логин администратора приложения", "admin");
        password = Field(access, "Пароль приложения: 9–256 символов", secret: true);
        repeat = Field(access, "Повторите пароль приложения", secret: true);
        access.Controls.Add(serviceAccount);
        Note(access, "Учётка SysAdminSvc06 создаётся автоматически без прав администратора. Пароль генерируется и передаётся Windows для службы. Вход через рабочий стол и RDP запрещён. Если снять галочку, используется встроенная LocalService. Пароль вашей Windows-учётки не нужен.");
        var finish = Page("4. Установка"); finish.Controls.Add(summary); finish.Controls.Add(log);
        serviceAccount.CheckedChanged += (_, _) => RefreshSummary();
        existing.CheckedChanged += (_, _) => { network.Enabled = !existing.Checked; user.Enabled = password.Enabled = repeat.Enabled = !existing.Checked; RefreshSummary(); };
        tabs.SelectedIndexChanged += (_, _) => { RefreshSummary(); UpdateButtons(); };
        back.Click += (_, _) => { if (tabs.SelectedIndex > 0) tabs.SelectedIndex--; };
        next.Click += (_, _) => { if (tabs.SelectedIndex < 3) tabs.SelectedIndex++; };
        close.Click += (_, _) => Close();
        copy.Click += (_, _) => { try { Clipboard.SetText(connection); } catch (Exception e) { SetupMessageBox.Show(this, e.Message, "Копирование"); } };
        install.Click += async (_, _) => await InstallAsync();
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        UpdateButtons();
    }
    private FlowLayoutPanel Page(string title)
    {
        var page = new TabPage(title) { Padding = new(12) };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        page.Controls.Add(panel); tabs.TabPages.Add(page); return panel;
    }
    private static void Note(FlowLayoutPanel panel, string text) => panel.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new(690, 0), Margin = new(3, 12, 3, 5) });
    private static TextBox Field(FlowLayoutPanel panel, string label, string value = "", bool secret = false)
    {
        Note(panel, label); var field = new TextBox { Text = value, Width = 670, UseSystemPasswordChar = secret }; panel.Controls.Add(field); return field;
    }
    private void Browse(FlowLayoutPanel panel, TextBox field)
    {
        var button = new Button { Text = "Выбрать папку…", AutoSize = true };
        button.Click += (_, _) => { using var dialog = new FolderBrowserDialog { SelectedPath = field.Text }; if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.SelectedPath; };
        panel.Controls.Add(button);
    }
    private void RefreshSummary() => summary.Text = $"Программа: {target.Text}\r\nДанные: {data.Text}\r\nСлужба: {(serviceAccount.Checked ? "SysAdminSvc06" : "LocalService")}\r\n" +
        (existing.Checked ? "Сеть, база и администратор — из существующей конфигурации." : $"IP: {ip.Text}:{port.Value}\r\nПодсети: {networks.Text}\r\nБаза: {provider.Text}\r\nАдминистратор: {user.Text}");
    private void UpdateButtons()
    {
        back.Enabled = !busy && !finished && tabs.SelectedIndex > 0; next.Enabled = !busy && !finished && tabs.SelectedIndex < 3;
        install.Enabled = !busy && !finished && tabs.SelectedIndex == 3; close.Enabled = !busy; tabs.Enabled = !busy && !finished;
    }
    private async Task InstallAsync()
    {
        SetupOptions? options = null;
        try
        {
            if (!existing.Checked && password.Text != repeat.Text) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пароли приложения не совпадают."));
            options = new() { Target = target.Text.Trim(), Data = data.Text.Trim(), Existing = existing.Checked, Ip = ip.Text.Trim(), Port = (int)port.Value, Networks = networks.Text.Trim(), User = user.Text.Trim(), Password = password.Text, Provider = provider.SelectedIndex == 0 ? "sqlite" : "sqlserver", SqlHost = sqlHost.Text.Trim(), SqlDatabase = sqlDatabase.Text.Trim(), SqlUser = sqlUser.Text.Trim(), SqlPassword = sqlPassword.Text, TrustSqlCertificate = trustSql.Checked, NewServiceAccount = serviceAccount.Checked };
            SetupEngine.Validate(options);
            busy = true; UpdateButtons(); progress.Style = ProgressBarStyle.Marquee; log.Clear();
            password.Clear(); repeat.Clear(); sqlPassword.Clear();
            var report = new Progress<string>(text => { status.Text = SysAdminAssistant.Core.ProductText.Get(text); log.AppendText(SysAdminAssistant.Core.ProductText.Get(text) + "\r\n"); });
            var result = await Task.Run(() => SetupEngine.InstallAsync(options, report));
            connection = $"URL: {result.Url}\r\nSHA-256: {result.Pin}\r\nЛогин: {result.User}";
            log.AppendText("\r\n" + connection + "\r\nДанные: " + result.Data + "\r\n");
            try{await ServerShell.ApplyAsync(new(startMenu.Checked,desktop.Checked));if(openManagement.Checked)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ServerShell.Manager,"--manage"){UseShellExecute=true});}catch(Exception shellError){log.AppendText("Ярлыки: "+shellError.Message+"\r\n");}
            copy.Visible = true; finished = true; status.Text = "Сервер установлен, служба запущена, HTTPS и сертификат проверены.";
        }
        catch (Exception e) { log.AppendText("Ошибка: " + e.Message + "\r\n"); status.Text = "Установка не завершена."; SetupMessageBox.Show(this, e.Message, "Установка сервера", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            if (options != null) { options.Password = ""; options.SqlPassword = ""; }
            busy = false; progress.Style = ProgressBarStyle.Blocks; progress.Value = finished ? 100 : 0; UpdateButtons();
        }
    }
}
