using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private async Task<UIElement> AgentPanel(Device device)
    {
        var root=new DockPanel();var bar=new WrapPanel();DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);var body=new StackPanel{Margin=new(12)};root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});AgentView? current=null;var refreshStatus=new TextBlock{Margin=new(12,6,12,6),TextWrapping=TextWrapping.Wrap};DockPanel.SetDock(refreshStatus,Dock.Bottom);root.Children.Insert(1,refreshStatus);
        var gauges=new AgentGauges();body.Children.Add(gauges);var infoLines=new List<TextBlock>();int lineIndex=0;
        Button? windowsInstall=null,updateAgent=null;
        bool IsLinux()=>current?.Platform=="Linux"||current?.Report?.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)==true;
        async Task Refresh(){
            current=(await remote.CommandAsync<AgentViewResult>("agent-view",new AgentDeviceRequest(device.Id),lifetime.Token)).Agent;gauges.Update(current);lineIndex=0;foreach(var line in infoLines)line.Visibility=Visibility.Collapsed;
            var linux=IsLinux();
            if(windowsInstall!=null)windowsInstall.Visibility=linux?Visibility.Collapsed:Visibility.Visible;
            if(updateAgent!=null)updateAgent.ToolTip=ProductText.Get(linux?"Обновление Linux-агента":"Пакет обновления Windows-агента");
            
            void Line(string text){TextBlock line;if(lineIndex<infoLines.Count){line=infoLines[lineIndex];line.Visibility=Visibility.Visible;line.Text=text;}else{line=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new(0,5,0,5)};infoLines.Add(line);body.Children.Add(line);}lineIndex++;}

            if(current==null){Line("Агент не привязан. Создайте установочный пакет для этого устройства.");return;}
            Line(current.Revoked?"Доступ отозван":current.Approved?"Агент подтверждён":"Ожидает регистрации / подтверждения");
            Line("Компьютер: "+current.MachineName+"\nКлюч SHA-256: "+current.KeyFingerprint+"\nПоследняя связь: "+Display.Stamp(current.LastSeenUtc?.ToServerTime())+"\nСертификат до: "+(DateTime.TryParse(current.CertificateExpires,out var expires)?Display.Stamp(expires.ToServerTime()):"—")+"\nПродление сертификата: автоматически");
            if(current.Report is AgentReport r){
                Line(r.Os+"\nCPU: "+r.CpuName+" · ядер: "+r.CpuCount+"\nВерсия доверия HTTPS: "+r.TrustVersion+" · политика: "+r.AppliedPolicyVersion+" / "+current.Policy.Version);
                Line("Версия агента: "+r.AgentVersion+"\nАвтообновление: "+r.AgentUpdaterStatus+"\n"+AgentIssueAdvice.Updater(r.AgentUpdaterStatus,r.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)));if(r.Updates!=null)Line(UpdateDetails(r.Updates));
                if(current.RecentReboots.Length>0)Line("Наблюдавшиеся перезагрузки:\n"+string.Join("\n",current.RecentReboots.Reverse().Select(t=>Display.Stamp(t.ToServerTime()))));
                foreach(var f in current.Findings)Line(Display.Level(f.Severity)+" · "+f.Title+"\n"+AgentIssueAdvice.Metric(f,r,current.Policy));
                foreach(var error in r.Errors)Line("Сбор: "+error);
            }
        }
        // A device card is modal inside MainWindow.Run: use its own operation guard.
        bool agentBusy=false;
        Window Owner()=>Window.GetWindow(root)??this;
        Button Button(string text,Func<Task> action,bool edit=false){
            var b=UiButton(text);b.IsEnabled=!edit||AccessRules.Level(remote.CurrentUser,"devices")>=2;
            b.Click+=async(_,_)=>{
                if(agentBusy)return;
                agentBusy=true;bar.IsEnabled=false;
                try{await action();}
                catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
                catch(Exception e){AppMessageBox.Show(Owner(),e.Message,"Агент",MessageBoxButton.OK,MessageBoxImage.Error);}
                finally{bar.IsEnabled=true;agentBusy=false;}
            };bar.Children.Add(b);return b;
        }
        Button("Обновить",Refresh);
        Button("Журналы",()=>AgentEventsWindow(device,Owner(),current?.Report?.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)==true));
        Button("Сертификаты",async()=>{await ManageAgentCertificates(device,Owner());await Refresh();},true);
        Button("Установить Linux",async()=>{var result=await remote.CommandAsync<LinuxInstallCommand>("agent-linux-package",new AgentDeviceRequest(device.Id),lifetime.Token);Clipboard.SetText(result.Command);AppMessageBox.Show(Owner(),"Команда скопирована в буфер обмена. Вставьте её в терминал Debian/Ubuntu. Она действует 15 минут и используется один раз.\n\nПосле установки сверьте отпечаток и подтвердите агента в карточке.","Linux-агент",MessageBoxButton.OK,MessageBoxImage.Information);await Refresh();},true);
        windowsInstall=Button("Создать установщик Windows",async()=>{await CreateAgentPackage(device,Owner());await Refresh();},true);
        updateAgent=Button("Обновление агента",async()=>{
            if(IsLinux()){
                new FormDialog(Owner(),"Обновление Linux-агента")
                    .Note("Агент сам скачивает подписанный выпуск с обновлённого сервера. Таймер systemd применяет подготовленный пакет. Повторная регистрация не нужна.")
                    .Text("status","Проверить таймер и журнал на Linux", "systemctl status sysadminassistant-agent-update.timer --no-pager\n"+"sudo journalctl -u sysadminassistant-agent-update.service -n 30 --no-pager",true,true)
                    .Text("apply","Применить уже скачанный пакет", "sudo systemctl start sysadminassistant-agent-update.service",false,true)
                    .ViewOnly().ShowDialog();
                return;
            }
            await CreateAgentPackage(device,Owner(),true);await Refresh();
        },true);
        Button("Подтвердить",async()=>{if(current==null||current.KeyFingerprint=="")throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Дождитесь регистрации агента"));if(!Confirm(Owner(),"Сверьте отпечаток ключа с status.json на целевом сервере:\n"+current.KeyFingerprint+"\nКомпьютер: "+current.MachineName+"\nПодтвердить агента для «"+device.Name+"»?"))return;await remote.CommandAsync<bool>("agent-approve",new AgentDeviceRequest(device.Id),lifetime.Token);await Refresh();},true);
        Button("Пороги",async()=>{if(current==null)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сначала создайте пакет агента"));await EditAgentPolicy(device.Id,current.Policy,Owner(),current.Report?.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)==true);await Refresh();},true);
        Button("История проверок",()=>AgentMetricHistoryWindow(device.Id,Owner()));
        Button("Тома и пороги",async()=>{if(current?.Report?.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)!=true)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Нужен отчёт Linux-агента"));await LinuxVolumesWindow(device.Id,current,Owner());await Refresh();},true);
        Button("Пороги сети",async()=>{if(current?.Report?.Networks==null)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Дождитесь отчёта агента с сетевыми интерфейсами; для Windows нужен агент 0.33 или новее"));await EditLinuxNetworkRules(device.Id,current,Owner());await Refresh();},true);
        Button("Роли и службы",async()=>{if(current==null)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сначала создайте пакет агента"));await EditAgentServices(device.Id,current,Owner());await Refresh();},true);
        Button("Отозвать доступ",async()=>{if(current==null||!Confirm(Owner(),"Отозвать доступ агента? Он больше не сможет передавать данные. Для подключения потребуется новая регистрация."))return;await remote.CommandAsync<bool>("agent-revoke",new AgentDeviceRequest(device.Id),lifetime.Token);await Refresh();},true);
        bool refreshing=false;
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(10)};
        timer.Tick+=async(_,_)=>{
            if(agentBusy||refreshing||!root.IsLoaded)return;refreshing=true;
            try{await Refresh();refreshStatus.Text="Обновлено: "+AppTime.Now.ToString("HH:mm:ss")+" · автообновление каждые 10 с";}
            catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
            catch(Exception e){refreshStatus.Text="Не удалось обновить: "+e.Message+" · повтор через 10 с";}
            finally{refreshing=false;}
        };
        root.Loaded+=(_,_)=>timer.Start();root.Unloaded+=(_,_)=>timer.Stop();
        await Refresh();refreshStatus.Text="Автообновление каждые 10 с; при открытых настройках приостановлено";return root;
    }
    private async Task CreateAgentPackage(Device device,Window owner,bool upgrade=false)
    {
        var template=Path.Combine(AppContext.BaseDirectory,"AgentTemplate");var agent=Path.Combine(template,"Agent");var setup=Path.Combine(template,"Setup");
        var agentAssembly=Path.Combine(agent,"SysAdminAssistant.Agent.dll");
        if(!File.Exists(agentAssembly)||!File.Exists(Path.Combine(agent,"SysAdminAssistant.Agent.exe"))||!File.Exists(Path.Combine(setup,"SysAdminAssistant.AgentSetup.exe")))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нет шаблона установщика. Выполните PUBLISH.cmd и запустите клиент из опубликованной папки целиком."));
        var launcher=Path.Combine(template,"AgentSetupLauncher.exe");if(!File.Exists(launcher))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нет оболочки Setup.exe. Пересоберите полный пакет через PUBLISH.bat."));
        AgentTemplate.Validate(template); // Before runtime download and before issuing a registration token.
        var uri=new Uri(remote.Address);var ip=System.Net.IPAddress.Parse(uri.Host.Trim('[',']'));var network=ip+"/"+(ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork?"32":"128");
        var form=new FormDialog(owner,"Установщик агента · "+device.Name).Text("server","HTTPS-адрес Assistant",remote.Address).Text("networks","Разрешённые подсети назначения · через ;",network).Note("По умолчанию разрешён только адрес этого сервера. Агент не принимает входящих подключений. Пакет действует 24 часа и предназначен одному устройству. Храните его как временный секрет; после установки сверьте ключ и подтвердите агента.");
        form.Validate=()=>Required(form.Value("server"),form.Value("networks"));if(form.ShowDialog()!=true)return;
        var agentVersion=System.Reflection.AssemblyName.GetAssemblyName(agentAssembly).Version??throw new InvalidDataException("Не удалось определить версию агента в шаблоне.");
        var save=new SaveFileDialog{Title="Сохранить установщик агента",Filter="Установщик агента (*.exe)|*.exe",FileName=AgentPackageFileName.Create(device.Name,agentVersion,upgrade),DefaultExt=".exe",AddExtension=true,OverwritePrompt=true};if(save.ShowDialog(owner)!=true)return;
        var server=form.Value("server");var networks=form.Value("networks").Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        var text=new TextBlock{Text="Подготовка файлов…",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};
        var meter=new ProgressBar{Minimum=0,Maximum=100,Height=18};var layout=new StackPanel{Margin=new(20)};layout.Children.Add(DialogWindow.StatusViewport(text,64));layout.Children.Add(meter);
        var progress=new DialogWindow{Owner=owner,Title="Создание установщика агента",Width=460,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=layout,ShowInTaskbar=false};
        bool finished=false;progress.Closing+=(_,e)=>{if(!finished)e.Cancel=true;};
        var updates=new Progress<(int Percent,string Stage)>(step=>{meter.Value=step.Percent;meter.IsIndeterminate=false;text.Text=step.Stage;});
        IProgress<(int Percent,string Stage)> status=updates;
        var temp=Path.Combine(Path.GetDirectoryName(save.FileName)!,".agent-"+Guid.NewGuid().ToString("N")+".tmp");var packed=temp+".exe";
        owner.IsEnabled=false;progress.Show();
        AgentBootstrap verified;
        try{
            var runtime=await AgentRuntimePackage.EnsureAsync(AppContext.BaseDirectory,status,lifetime.Token,()=>Task.FromResult(AppMessageBox.Show(progress,"Исправный установщик .NET не найден в локальном хранилище. Скачать его с Microsoft один раз и сохранить для следующих пакетов агента?","Установщик .NET",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes));
            var binaries=await Task.Run(()=>Directory.GetFiles(agent).ToDictionary(p=>Path.GetFileName(p)!,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))),lifetime.Token);
            text.Text="Регистрация пакета на сервере…";meter.Value=20;meter.IsIndeterminate=true;
            var package=await remote.CommandAsync<AgentSignedBootstrap>("agent-package",new AgentPackageRequest(device.Id,server,networks,binaries!,upgrade),lifetime.Token);
            verified=AgentWire.Verify(package);
            text.Text="Упаковка файлов…";meter.IsIndeterminate=false;meter.Value=35;
            await Task.Run(()=>{
                var files=new List<(string Source,string Entry)>();
                files.AddRange(Directory.GetFiles(agent).Select(p=>(p,"Agent/"+Path.GetFileName(p))));
                files.AddRange(Directory.GetFiles(setup).Select(p=>(p,Path.GetFileName(p))));
                foreach(var name in AgentTemplate.RequiredScripts){
                    var path=Path.Combine(template,name);if(!File.Exists(path))throw new FileNotFoundException(global::SysAdminAssistant.Core.ProductText.Get("Неполный шаблон установщика: ")+name,path);files.Add((path,name));
                }
                files.Add((runtime,"Prerequisites/"+AgentRuntimePackage.Name));
                using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create)){
                    for(int i=0;i<files.Count;i++){
                        lifetime.Token.ThrowIfCancellationRequested();
                        status.Report((35+55*i/files.Count,"Упаковка: "+files[i].Entry));
                        zip.CreateEntryFromFile(files[i].Source,files[i].Entry,CompressionLevel.SmallestSize);
                    }
                    using var writer=new StreamWriter(zip.CreateEntry("bootstrap.json").Open());writer.Write(JsonSerializer.Serialize(package,AgentWire.Json));
                }
                status.Report((90,"Проверка установочного пакета…"));
                using(var check=ZipFile.OpenRead(temp)){
                    if(check.Entries.Count!=files.Count+1||check.GetEntry("bootstrap.json")==null)throw new IOException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось проверить установочный архив"));
                }
                status.Report((95,"Создание единого Setup.exe…"));AgentSetupBundle.Pack(launcher,temp,packed);
                lifetime.Token.ThrowIfCancellationRequested();File.Move(packed,save.FileName,true);
            },lifetime.Token);
            meter.Value=100;text.Text="Установщик сохранён";
        }
        catch(Exception e) when(e is not OperationCanceledException){throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Установщик не создан: ")+e.Message+(e is HttpRequestException?ProductText.Get("\nПроверьте подключение к серверу и повторите создание пакета."):e is UnauthorizedAccessException?ProductText.Get("\nНет доступа к выбранной папке. Выберите другую папку для сохранения."):e is FileNotFoundException?ProductText.Get("\nВосстановите полный комплект клиента из публикации. Изменение сети не требуется."):ProductText.Get("\nПодробности приведены выше. Регистрация работающего агента сохраняется.")),e);}
        finally{
            try{if(File.Exists(temp))File.Delete(temp);if(File.Exists(packed))File.Delete(packed);}finally{finished=true;progress.Close();owner.IsEnabled=true;owner.Activate();}
        }
        AppMessageBox.Show(owner,"Сохранено: "+save.FileName+"\n\nИздатель пакета SHA-256: "+Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(verified.IssuerCertificate)))+"\nСверь этот отпечаток в установщике перед установкой.\nПеренеси этот Setup.exe на целевой Windows Server и запустите его. Файлы и .NET Runtime включены; распаковывать вручную не нужно. После регистрации сверьте ключ и нажмите «Подтвердить» в карточке.","Установщик агента создан",MessageBoxButton.OK,MessageBoxImage.Information);
    }
    private async Task EditAgentPolicy(int deviceId,AgentPolicy original,Window owner,bool linux)
    {
        var p=Copy(original);var form=new FormDialog(owner,"Пороги агента").Text("interval","Передача, секунд · 15–300",p.IntervalSeconds.ToString()).Text("delay","Устойчивый сбой, минут · 0–1440",p.AlertMinutes.ToString()).Text("boot","Ожидание служб после загрузки, минут · 0–60",p.BootGraceMinutes.ToString()).Text("cpu","CPU ≥ %",p.CpuPercent.ToString()).Text("ram","RAM занято ≥ %",p.RamPercent.ToString()).Text("diskPercent","Диск свободно ≤ % · 0 отключает процентный порог",p.DiskFreePercent.ToString()).Text("diskGb","Диск свободно ≤ ГБ · 0 отключает порог ГБ",p.DiskFreeGb.ToString()).Text("exclude","Исключить тома · через ;",string.Join(";",p.ExcludedVolumes));if(!linux)form.Toggle("standard","Базовые службы Windows · EventLog, RPC, WMI",p.StandardServices);
        void Read(){p.IntervalSeconds=form.Number("interval",15,300);p.AlertMinutes=form.Number("delay",0,1440);p.BootGraceMinutes=form.Number("boot",0,60);p.CpuPercent=form.Number("cpu",1,100);p.RamPercent=form.Number("ram",1,100);p.DiskFreePercent=form.Number("diskPercent",0,95);p.DiskFreeGb=form.Number("diskGb",0,100000);p.ExcludedVolumes=form.Value("exclude").Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);if(!linux)p.StandardServices=form.Checked("standard");AgentWire.Validate(p);}
        form.Validate=()=>{Read();return null;};if(form.ShowDialog()!=true)return;Read();await remote.CommandAsync<AgentPolicy>("agent-policy",new AgentPolicySave(deviceId,p,original.Version),lifetime.Token);
    }
    private async Task EditLinuxNetworkRules(int deviceId,AgentView view,Window owner)
    {
        var p=Copy(view.Policy);p.NetworkRules??=[];
        var window=new DialogWindow{Owner=owner,Title="Пороги сетевых интерфейсов",Width=760,Height=480,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var table=Table(Array.Empty<AgentNetworkRule>(),("Интерфейс","Name",null),("Порог, %","WarnPercent",null),("Скорость, Мбит/с","CapacityMbps",null),("Задержка, мин","DelayMinutes",null),("Включено","Enabled",null));
        void Refresh()=>table.ItemsSource=p.NetworkRules.ToArray();Refresh();
        async Task Save(){AgentWire.Validate(p);p=await remote.CommandAsync<AgentPolicy>("agent-policy",new AgentPolicySave(deviceId,p,p.Version),lifetime.Token);p.NetworkRules??=[];Refresh();}
        async Task Edit(AgentNetworkRule? old){var available=(view.Report is {} report?AgentNetworkNames.VisibleInterfaces(report):[]).Select(n=>new Choice(n.Name,n.Name+" · "+n.State+(n.LinkBitsPerSecond>0?" · "+(n.LinkBitsPerSecond/1000000)+" Мбит/с":" · скорость неизвестна"))).ToArray();var f=new FormDialog(window,"Порог интерфейса").Select("interface","Интерфейс",available,old?.Name??available.FirstOrDefault()?.Key).Text("percent","Загрузка, %", (old?.WarnPercent??90).ToString()).Text("delay","Задержка, минут",(old?.DelayMinutes??2).ToString()).Text("capacity","Скорость канала, Мбит/с · 0 = автоматически",(old?.CapacityMbps??0).ToString()).Toggle("enabled","Контролировать",old?.Enabled??true).Note("Учитывается более загруженное направление. Для виртуального интерфейса без известной скорости укажите скорость вручную.");f.Validate=()=>{_=f.Number("percent",1,100);_=f.Number("delay",0,1440);_=f.Number("capacity",0,1000000);return Required(f.Selected<string>("interface"));};if(f.ShowDialog()!=true)return;var name=f.Selected<string>("interface");if(old!=null)p.NetworkRules!.Remove(old);p.NetworkRules!.RemoveAll(x=>x.Name==name);p.NetworkRules.Add(new(name,f.Number("percent",1,100),f.Number("delay",0,1440),f.Checked("enabled"),f.Number("capacity",0,1000000)));await Save();}
        var bar=new WrapPanel();var add=UiButton("Добавить");var change=UiButton("Изменить");var delete=UiButton("Удалить");
        add.Click+=async(_,_)=>{try{if((view.Report?.Networks??[]).Length==0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Агент не сообщил сетевые интерфейсы"));await Edit(null);}catch(Exception e){AppMessageBox.Show(window,e.Message,"Сеть",MessageBoxButton.OK,MessageBoxImage.Error);}};
        change.Click+=async(_,_)=>{try{if(table.SelectedItem is AgentNetworkRule r)await Edit(r);}catch(Exception e){AppMessageBox.Show(window,e.Message,"Сеть",MessageBoxButton.OK,MessageBoxImage.Error);}};
        delete.Click+=async(_,_)=>{try{if(table.SelectedItem is AgentNetworkRule r){p.NetworkRules!.Remove(r);await Save();}}catch(Exception e){AppMessageBox.Show(window,e.Message,"Сеть",MessageBoxButton.OK,MessageBoxImage.Error);}};
        bar.Children.Add(add);bar.Children.Add(change);bar.Children.Add(delete);var layout=new DockPanel();DockPanel.SetDock(bar,Dock.Top);layout.Children.Add(bar);layout.Children.Add(table);window.Content=layout;window.ShowDialog();
    }
    private async Task EditAgentServices(int deviceId,AgentView view,Window owner)
    {
        var p=Copy(view.Policy);var observed=view.Report?.Services??[];var table=Table(Array.Empty<AgentServiceRule>(),("Системное имя","Name",null),("Добавлено через","Origin",null),("Контроль","Enabled",null),("Ожидается","Expected",null),("Важность","Severity",null),("Задержка, мин","DelayMinutes",null));
        var window=new DialogWindow{Owner=owner,Title="Службы агента",Width=950,Height=600,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        bool linux=view.Report?.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)==true;
        void Refresh(){table.ItemsSource=linux?LinuxPolicy.EffectiveServices(p):AgentServiceProfiles.Effective(p,observed); }
        async Task Save(){AgentWire.Validate(p);p=await remote.CommandAsync<AgentPolicy>("agent-policy",new AgentPolicySave(deviceId,p,p.Version),lifetime.Token);Refresh();}
        var roles=UiButton("Роли сервера…");var add=UiButton("Добавить службу");var edit=UiButton("Изменить");var remove=UiButton("Удалить");var toggle=UiButton("Приостановить / включить");var excluded=UiButton("Исключённые…");
        async Task Edit(AgentServiceRule? rule){
            var choices=observed.Select(s=>new Choice(s.Name,s.DisplayName+" · "+s.Name+" · "+s.Status+" · "+s.StartMode)).ToArray();
            var form=new FormDialog(window,"Контроль службы").Text("name","Точное системное имя службы",rule?.Name??"").Select("detected","Обнаруженные службы",new[]{new Choice("","Указать имя вручную")}.Concat(choices),rule?.Name??"").Toggle("enabled","Контролировать",rule?.Enabled??true).Select("expected","Ожидаемое состояние",new[]{new Choice("Running","Работает"),new Choice("Stopped","Остановлена")},rule?.Expected??"Running").Select("level","Важность",new[]{new Choice(Severity.Warning,"Предупреждение"),new Choice(Severity.Critical,"Критично")},rule?.Severity??Severity.Critical).Text("delay","Задержка, минут",(rule?.DelayMinutes??2).ToString());
            if(linux)form.Text("interval","Ожидаемый интервал выполнения, минут · 0 = не контролировать расписание",(rule?.MaxIntervalMinutes??0).ToString()).Note("Для oneshot Running означает успешное завершение; inactive/dead после кода 0 допустимо. Можно выбрать .timer: контролируются таймер и связанное задание отдельно.");
            form.Validate=()=>{if(linux)_=form.Number("interval",0,525600);_=form.Number("delay",0,1440);return Required(form.Selected<string>("detected")==""?form.Value("name"):form.Selected<string>("detected"));};if(form.ShowDialog()!=true)return;
            var name=form.Selected<string>("detected");if(name=="")name=form.Value("name");if(rule!=null)p.Services.RemoveAll(s=>s.Name.Equals(rule.Name,StringComparison.OrdinalIgnoreCase));p.Services.RemoveAll(s=>s.Name.Equals(name,StringComparison.OrdinalIgnoreCase));AgentServiceProfiles.Restore(p,name);p.Services.Add(new(name,form.Checked("enabled"),form.Selected<string>("expected"),form.Selected<Severity>("level"),form.Number("delay",0,1440),MaxIntervalMinutes:linux&&form.Number("interval",0,525600)>0?form.Number("interval",0,525600):null));await Save();
        }
        bool saving=false;
        async Task Operate(Func<Task> action){if(saving)return;saving=true;var previous=Copy(p);roles.IsEnabled=add.IsEnabled=edit.IsEnabled=remove.IsEnabled=toggle.IsEnabled=excluded.IsEnabled=false;try{await action();}catch(Exception e){p=previous;Refresh();AppMessageBox.Show(window,e.Message,"Службы агента",MessageBoxButton.OK,MessageBoxImage.Error);}finally{saving=false;roles.IsEnabled=add.IsEnabled=edit.IsEnabled=remove.IsEnabled=toggle.IsEnabled=excluded.IsEnabled=true;}}
        roles.Click+=async(_,_)=>await Operate(async()=>{
            var selection=new FormDialog(window,"Роли сервера · службы",960);
            selection.Note("Выберите все роли устройства. Галочки меняют только мониторинг. Список ниже показывает результат до сохранения.");
            var catalog=linux?LinuxServiceProfiles.Catalog:AgentServiceProfiles.Catalog;
            foreach(var role in catalog)selection.Toggle(role.Id,role.Title,p.ServiceRoles.Contains(role.Id)).Note(role.Description);
            selection.Text("preview","Результат и изменения","",true,true).TextHeight("preview",250)
                .Note("Снятие роли убирает только её автоматические записи. Общие службы сохраняются для других ролей. Ручные и прежние записи не удаляются. Изменение строки профиля превращает её в ручную; «Приостановить / включить» оставляет строку, «Удалить» убирает её из списка. Вернуть можно через «Исключённые». Отключённые старые экземпляры SQL/1С не добавляются впервые, но исчезновение ранее выбранной службы остаётся проблемой.")
                .Note("Для кластеров и пассивных узлов задайте ожидаемое состояние служб вручную.");
            AgentPolicy Draft(){var draft=Copy(p);draft.RoleProfilesConfigured=true;draft.ServiceRoles=catalog.Where(r=>selection.Checked(r.Id)).Select(r=>r.Id).ToArray();if(linux)LinuxServiceProfiles.Sync(draft,observed);else AgentServiceProfiles.Sync(draft,observed);return draft;}
            void Preview(){var draft=Draft();var before=linux?p.Services.ToArray():AgentServiceProfiles.Effective(p,observed);var after=linux?draft.Services.ToArray():AgentServiceProfiles.Effective(draft,observed);var added=after.Where(a=>!before.Any(b=>a.Name.Equals(b.Name,StringComparison.OrdinalIgnoreCase))).Select(a=>a.Name);var removed=before.Where(b=>!after.Any(a=>a.Name.Equals(b.Name,StringComparison.OrdinalIgnoreCase))).Select(b=>b.Name);selection.Set("preview","Добавится: "+string.Join(", ",added)+"\nУберётся: "+string.Join(", ",removed)+"\n\n"+string.Join("\n",after.Select(a=>a.Name+" · "+a.Origin+" · "+(a.Enabled?"контроль включён":"исключено")))+"\n"+(linux?"":string.Join("\n",AgentServiceProfiles.Findings(draft,observed).Select(f=>f.Title))));}
            foreach(var role in catalog)selection.ToggleChanged(role.Id,Preview);
            Preview();if(selection.ShowDialog()!=true)return;p=Draft();await Save();
        });
        add.Click+=async(_,_)=>await Operate(()=>Edit(null));edit.Click+=async(_,_)=>{if(table.SelectedItem is AgentServiceRule rule)await Operate(()=>Edit(rule));};remove.Click+=async(_,_)=>{if(table.SelectedItem is AgentServiceRule rule)await Operate(async()=>{AgentServiceProfiles.Delete(p,rule.Name);await Save();});};
        toggle.Click+=async(_,_)=>{if(table.SelectedItem is AgentServiceRule rule)await Operate(async()=>{p.Services.RemoveAll(s=>s.Name.Equals(rule.Name,StringComparison.OrdinalIgnoreCase));p.Services.Add(rule with{Enabled=!rule.Enabled,Profiles=null});await Save();});};
        excluded.Click+=async(_,_)=>await Operate(async()=>{
            if(p.ExcludedServices.Length==0){AppMessageBox.Show(window,"Исключённых служб нет.","Мониторинг служб",MessageBoxButton.OK,MessageBoxImage.Information);return;}
            var form=new FormDialog(window,"Исключённые службы",680).Select("name","Вернуть в список мониторинга",p.ExcludedServices.Order(StringComparer.OrdinalIgnoreCase).Select(n=>new Choice(n,n)),p.ExcludedServices[0])
                .Note("Эти службы удалены только из мониторинга. Профили не возвращают их автоматически. При восстановлении служба добавится с ожиданием Running, важностью Critical и задержкой 2 минуты; параметры можно изменить в списке.").Submit("Вернуть в список");
            if(form.ShowDialog()!=true)return;var name=form.Selected<string>("name");AgentServiceProfiles.Restore(p,name);p.Services.RemoveAll(s=>s.Name.Equals(name,StringComparison.OrdinalIgnoreCase));p.Services.Add(new(name));await Save();
        });

        var root=ListSurface(table,new[]{roles,add,edit,toggle,remove,excluded},new TextBlock{Text="Роли сервера → отметьте AD, SQL, 1С и другие роли. Источник показывает, кем добавлена служба. Ручные записи имеют приоритет. Политика передаётся при следующем обмене. «Удалить» убирает из мониторинга; восстановление через «Исключённые». Агент не изменяет службы операционной системы.",TextWrapping=TextWrapping.Wrap,Margin=new(8)},true);Refresh();window.Content=root;window.ShowDialog();
    }
}
