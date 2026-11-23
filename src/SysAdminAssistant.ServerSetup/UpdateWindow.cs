namespace SysAdminAssistant.ServerSetup;
internal sealed class UpdateWindow : Form
{
    private readonly Button update=new(){Text="Обновить сервер",AutoSize=true,Enabled=false};
    private readonly TextBox summary=new(){Multiline=true,ReadOnly=true,Dock=DockStyle.Fill};
    private readonly TextBox log=new(){Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical};
    private readonly CheckBox startMenu=new(){Text="Меню «Пуск»",AutoSize=true},desktop=new(){Text="Ярлык на рабочем столе",AutoSize=true};
    private bool busy;
    public UpdateWindow()
    {
        Shown += (_,_) => SetupTheme.Apply(this);
        Text="SysAdmin Assistant · Обновление сервера";ClientSize=new(760,580);StartPosition=FormStartPosition.CenterParent;Font=new("Segoe UI",10);
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new(16),RowCount=4,ColumnCount=1};
        root.RowStyles.Add(new(SizeType.Absolute,145));root.RowStyles.Add(new(SizeType.Absolute,135));root.RowStyles.Add(new(SizeType.AutoSize));root.RowStyles.Add(new(SizeType.Percent,100));
        root.Controls.Add(new Label {AutoSize=true,MaximumSize=new(710,0),Text="Обновление установленного сервера сохраняет базу, настройки, сертификат, пользователей, пароли и учётку службы. Пароли вводить не нужно.\nЗакрой клиенты на время обновления. Служба будет остановлена; перед заменой файлов создаётся защищённая локальная копия. SQL-база не копируется установщиком: сделайте её резервную копию средствами SQL Server. При запуске выполняются необходимые миграции базы."},0,0);
        root.Controls.Add(summary,0,1);var choices=ServerShell.Load();startMenu.Checked=choices.StartMenu;desktop.Checked=choices.Desktop;var links=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=true};links.Controls.AddRange([update,startMenu,desktop]);root.Controls.Add(links,0,2);root.Controls.Add(log,0,3);Controls.Add(root);
        Shown+=async(_,_)=>{
            try {var installed=await UpdateEngine.DetectAsync();summary.Text=$"Программа: {installed.Target}\r\nДанные: {installed.Data}\r\nУчётка службы: {installed.Account}\r\nСостояние: {installed.State}; запуск: {installed.StartMode}\r\nНовая версия: {typeof(UpdateWindow).Assembly.GetName().Version}";update.Enabled=true;}
            catch(Exception e){log.Text=e.Message;}
        };
        update.Click+=async(_,_)=>{
            if(busy)return;busy=true;update.Enabled=false;log.Clear();
            try {IProgress<string> report=new Progress<string>(text=>log.AppendText(SysAdminAssistant.Core.ProductText.Get(text)+"\r\n"));var backup=await UpdateEngine.UpdateAsync(report);try{await ServerShell.ApplyAsync(new(startMenu.Checked,desktop.Checked));}catch(Exception shellError){log.AppendText("Ярлыки: "+shellError.Message+"\r\n");}SetupMessageBox.Show(this,"Сервер обновлён и прошёл проверку HTTPS.\nКопия: "+backup,"Обновление");}
            catch(Exception e){log.AppendText(e.Message+"\r\n");SetupMessageBox.Show(this,e.Message,"Обновление не завершено",MessageBoxButtons.OK,MessageBoxIcon.Error);update.Enabled=true;}
            finally{busy=false;}
        };
        FormClosing+=(_,e)=>{if(busy)e.Cancel=true;};
    }
}
