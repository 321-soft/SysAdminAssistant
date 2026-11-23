using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.ServerSetup;
internal sealed class ManagementWindow:Form
{
    private readonly TextBox details=new(){ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill};
    private readonly FlowLayoutPanel actions=new(){Dock=DockStyle.Top,AutoSize=true,WrapContents=true};
    private InstalledServer? installed;private string health="";private bool busy;
    public ManagementWindow()
    {
        Text="Управление сервером · SysAdmin Assistant";ClientSize=new(900,650);MinimumSize=new(660,440);Padding=new(18);AutoScaleMode=AutoScaleMode.Dpi;
        Controls.Add(details);Controls.Add(actions);
        Add("Русский / English",async()=>{var language=SysAdminAssistant.Setup.SetupLanguage.Choose(true);if(language==null)return;ProductText.DefaultLanguage=language;SetupTheme.Apply(this);await RefreshAsync();});
        Add("Обновить состояние",RefreshAsync);Add("Запустить службу",()=>ControlAsync("start"));Add("Остановить службу",()=>ControlAsync("stop"));Add("Перезапустить службу",()=>ControlAsync("restart"));
        Add("Резервная копия БД…",BackupAsync);Add("Журнал ошибок сервера",ErrorsAsync);Add("Сохранить диагностику…",ExportAsync);Add("Открыть папку данных и журналов",()=>{if(installed!=null)Process.Start(new ProcessStartInfo(installed.Data){UseShellExecute=true});return Task.CompletedTask;});
        Add("Настройки через клиент",()=>{var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant","SysAdminAssistant.exe");if(!File.Exists(path))throw new FileNotFoundException("Клиент не установлен на этом сервере. Откройте настройки с рабочего компьютера.");Process.Start(new ProcessStartInfo(path){UseShellExecute=true});return Task.CompletedTask;});
        Add("Удалить сервер…",()=>{using var dialog=new UninstallWindow();dialog.ShowDialog(this);return Task.CompletedTask;});
        Shown+=async(_,_)=>{SetupTheme.Apply(this);await Execute(RefreshAsync);};FormClosing+=(_,e)=>e.Cancel=busy;
    }
    private void Add(string title,Func<Task> action){var button=new Button{Text=ProductText.Get(title),AutoSize=true,Margin=new(0,0,8,8)};button.Click+=async(_,_)=>await Execute(action);actions.Controls.Add(button);}
    private async Task Execute(Func<Task> action){if(busy)return;busy=true;actions.Enabled=false;UseWaitCursor=true;try{await action();}catch(Exception e){SetupMessageBox.Show(this,ProductText.Get(e.Message),Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}finally{busy=false;actions.Enabled=true;UseWaitCursor=false;}}
    private async Task<string> ServerAsync(params string[] arguments)
    {
        installed??=await UpdateEngine.DetectAsync();var exe=Path.Combine(installed.Target,"SysAdminAssistant.Server.exe");
        var start=new ProcessStartInfo(exe){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};foreach(var argument in arguments)start.ArgumentList.Add(argument);start.ArgumentList.Add("--data-dir");start.ArgumentList.Add(installed.Data);
        using var process=Process.Start(start)??throw new IOException("Не удалось запустить проверку сервера.");var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();var result=await output;var failure=await error;if(process.ExitCode!=0)throw new IOException(ProductText.Get("Операция сервера не выполнена.")+"\n"+failure);return result;
    }
    private async Task RefreshAsync()
    {
        installed=await UpdateEngine.DetectAsync();health=await ServerAsync("--local-admin-status");using var json=JsonDocument.Parse(health);var c=json.RootElement;
        string V(string key)=>c.GetProperty(key).ToString();
        string uptime="—";try{using var process=Process.GetProcessesByName("SysAdminAssistant.Server").FirstOrDefault();if(process!=null)uptime=(DateTime.Now-process.StartTime).ToString(@"d\.hh\:mm\:ss");}catch(System.ComponentModel.Win32Exception){}
        details.Text=ProductText.Get("Служба")+": "+ProductText.Get(installed.State)+"\r\n"+ProductText.Get("Время работы")+": "+uptime+"\r\n"+ProductText.Get("Версия")+": "+V("Version")+"\r\nHTTPS: "+V("Url")+"\r\n"+ProductText.Get("База данных")+": "+V("Provider")+" · "+ProductText.Get(c.GetProperty("DatabaseAvailable").GetBoolean()?"Доступна":"Недоступна")+"\r\n"+ProductText.Get("Доверенные сети")+": "+string.Join(", ",c.GetProperty("Networks").EnumerateArray().Select(n=>n.GetString()))+"\r\nSHA-256: "+V("CertificateSha256")+"\r\n"+ProductText.Get("Сертификат до (UTC)")+": "+V("CertificateExpiresUtc")+"\r\n"+ProductText.Get("Проверено (UTC)")+": "+V("CheckedUtc")+"\r\n\r\n"+ProductText.Get("Настройки порта, сетей и уведомлений изменяются через клиент с правами главного администратора. Окно управления можно закрыть: служба продолжит работу.");
    }
    private async Task ControlAsync(string operation)
    {
        if(operation!="start"&&SetupMessageBox.Show(this,ProductText.Get("Остановить мониторинг на время операции?"),Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        await Task.Run(()=>{using var mutex=new Mutex(false,@"Global\SysAdminAssistantProductMaintenance");bool held=false;try{try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Server maintenance is already running.");using var service=new WindowsService();service.OpenInstalled();if(operation!="start")service.StopInstalled();if(operation!="stop"&&!service.IsRunning())service.Start();}finally{if(held)mutex.ReleaseMutex();}});await RefreshAsync();
    }
    private async Task BackupAsync()
    {
        await RefreshAsync();using var info=JsonDocument.Parse(health);var sql=info.RootElement.GetProperty("Provider").GetString()=="SqlServer";string destination="sql-default";
        if(sql){if(SetupMessageBox.Show(this,ProductText.Get("SQL Server создаст COPY_ONLY-копию с CHECKSUM в своём каталоге резервных копий. Итоговый путь будет показан после завершения. Продолжить?"),Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;}
        else{using var save=new SaveFileDialog{Filter="SQLite (*.db)|*.db",FileName="SAA-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".db",OverwritePrompt=true};if(save.ShowDialog(this)!=DialogResult.OK)return;if(File.Exists(save.FileName))throw new IOException("Выберите новое имя: существующий файл не перезаписывается.");destination=save.FileName;}
        var result=await ServerAsync("--local-admin-backup",destination);using var backup=JsonDocument.Parse(result);SetupMessageBox.Show(this,ProductText.Get("Резервная копия создана:")+"\n"+backup.RootElement.GetProperty("Path").GetString()+"\n"+backup.RootElement.GetProperty("Location").GetString(),Text,MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
    private async Task ErrorsAsync()
    {
        const string command="Get-WinEvent -FilterHashtable @{LogName=@('Application','System');Level=@(1,2,3);StartTime=(Get-Date).AddDays(-1)} -MaxEvents 2000 -ErrorAction SilentlyContinue | Where-Object {$_.Message -match 'SysAdmin.?Assistant' -or $_.ProviderName -match 'SysAdmin.?Assistant'} | Select-Object -First 30 TimeCreated,Id,LevelDisplayName,ProviderName,Message | Format-List | Out-String -Width 240";
        var text=await SetupEngine.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),["-NoProfile","-NonInteractive","-Command",command]);
        using var window=new Form{Text=ProductText.Get("Журнал ошибок сервера"),Width=1000,Height=650,StartPosition=FormStartPosition.CenterParent};window.Controls.Add(new TextBox{Text=string.IsNullOrWhiteSpace(text)?ProductText.Get("За последние сутки ошибки сервера не найдены в журналах Windows."):text,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,WordWrap=false});SetupTheme.Apply(window);window.ShowDialog(this);
    }
    private async Task ExportAsync()
    {
        await RefreshAsync();using var save=new SaveFileDialog{Filter="ZIP (*.zip)|*.zip",FileName="SAA-diagnostics-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"};if(save.ShowDialog(this)!=DialogResult.OK)return;
        // Diagnostic allowlist intentionally excludes config, tokens, DB, mail and raw journal bodies.
        var temp=save.FileName+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create)){using(var writer=new StreamWriter(zip.CreateEntry("health.json").Open()))await writer.WriteAsync(health);using(var writer=new StreamWriter(zip.CreateEntry("service.json").Open()))await writer.WriteAsync(JsonSerializer.Serialize(new{Service=WindowsService.Name,installed!.State,installed.StartMode,CollectedUtc=DateTime.UtcNow}));using var readme=new StreamWriter(zip.CreateEntry("README.txt").Open());await readme.WriteAsync("Contains only allowlisted health and service diagnostics. Configuration, secrets, database, emails and raw journal text are excluded. / Только состояние сервера и службы; без конфигурации, секретов, БД и исходных сообщений.");}File.Move(temp,save.FileName,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
