using System.Windows;
using System.Windows.Controls;
using SysAdminAssistant.Infrastructure;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private const string KscFingerprintCommand = """
        $p=Join-Path $env:ProgramData 'KasperskyLab\adminkit\1093\cert\klserver.cer'; if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "KSC certificate not found: $p. Set the path in the command to the Administration Server certificate."}; $c=[System.Security.Cryptography.X509Certificates.X509Certificate2]::new($p); $sha=[System.Security.Cryptography.SHA256]::Create(); try { [BitConverter]::ToString($sha.ComputeHash($c.RawData)).Replace('-','') } finally { $sha.Dispose(); $c.Dispose() }
        """;

    private async Task ShowKsc()
    {
        var current=await remote.GetAsync<KscSectionView>("api/ksc/status",lifetime.Token);
        var panel=new StackPanel();
        var active=new CheckBox{Content="Активировать",IsChecked=current.Active,Margin=new(18,12,18,12)};
        var description=new TextBlock{Text="Kaspersky Security Center опрашивается отдельно от устройств Assistant.",Margin=new(18,0,18,16),TextWrapping=TextWrapping.Wrap};
        var statePanel=new StackPanel();
        statePanel.Children.Add(active);statePanel.Children.Add(description);
        var stateCard=Card("Мониторинг KSC",statePanel);stateCard.Margin=new(0,0,0,16);panel.Children.Add(stateCard);
        bool restoring=false;
        active.Click+=async(_,_)=>{
            if(restoring)return;
            try{
                await Run(async()=>{
                    await remote.PostAsync<KscActivation,bool>("api/ksc/activate",new(active.IsChecked==true),lifetime.Token);
                    await ShowPage(12);
                });
            }finally{
                restoring=true;
                active.IsChecked=current.Active;
                restoring=false;
            }
        };
        if(remote.CurrentUser.Administrator)SettingsGroup(panel,"Правила событий Kaspersky","Настройте, какие события открывают и закрывают проблемы. Правила можно подготовить до включения мониторинга.",ActionButton("Правила Kaspersky",()=>ShowMonitoringDictionaries(true)));
        if(current.Active){
            var status=current.Status;
            var statusText=status.Configured
                ?"Сбор настроен · Получено событий: "+status.Total+" · Последний успешный опрос: "+Display.Stamp(status.LastUtc is {} t?AppTime.FromUtc(t):(DateTime?)null)
                :"Настройте подключение, чтобы начать сбор событий.";
            if(status.Error.Length>0)statusText+="\n"+status.Error;
            SettingsGroup(panel,"Подключение KSC",statusText+"\n"+(current.Telegram.Enabled?"Уведомления Kaspersky включены.":"Уведомления Kaspersky отключены."),
                ActionButton("Подключение к KSC…",EditStandaloneKscConnection),
                ActionButton("Обновить список",()=>ShowPage(12)),
                ActionButton("Настроить Telegram для KSC…",()=>EditStandaloneKscTelegram(current.Telegram)),
                ActionButton("Последние события KSC",ShowRecentKscEvents));
            var issues=Table(current.Problems,("Компьютер","Computer",null),("Идентификатор узла KSC","Host",null),("Уровень","Severity",null),("Проблема","Title",null),("Последнее срабатывание","LatestEventSummary",null),("Событий","Count",null));
            issues.MaxHeight=280;issues.MinHeight=100;
            var issuePanel=new DockPanel();
            var resolve=ActionButton("Отметить решённым",async()=>{
                if(issues.SelectedItem is not KscIssue issue)return;
                if(!Confirm(this,"Отметить выбранную проблему KSC решённой?"))return;
                await remote.PostAsync<KscIssueResolution,bool>("api/ksc/resolve",new(issue.Key),lifetime.Token);
                await ShowPage(12);
            });
            resolve.IsEnabled=false;issues.SelectionChanged+=(_,_)=>resolve.IsEnabled=issues.SelectedItem is KscIssue;
            var details=RowAction(issues,"Подробности",()=>ShowKscFindings(Selected<KscIssue>(issues)));
            issues.SetValue(HasCustomDoubleClickProperty,true);issues.MouseDoubleClick+=async(_,e)=>{
                if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(issues,source) is DataGridRow {Item:KscIssue issue}){
                    e.Handled=true;await Run(()=>ShowKscFindings(issue));
                }
            };
            var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center};
            actions.Children.Add(details);actions.Children.Add(resolve);
            DockPanel.SetDock(actions,Dock.Bottom);issuePanel.Children.Add(actions);issuePanel.Children.Add(issues);
            var issueCard=Card("Открытые проблемы KSC",issuePanel);issueCard.Margin=new(0,0,0,16);panel.Children.Add(issueCard);
            var resolved=Table(current.Resolved,("Компьютер","Computer",null),("Проблема","Title",null),("IP","AttackerIp",null),("Причина закрытия","Resolution",null),("Событий","Count",null));
            resolved.MaxHeight=240;resolved.MinHeight=100;
            resolved.SetValue(HasCustomDoubleClickProperty,true);resolved.MouseDoubleClick+=async(_,e)=>{
                if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(resolved,source) is DataGridRow {Item:KscIssue issue}){
                    e.Handled=true;await Run(()=>ShowKscFindings(issue));
                }
            };
            panel.Children.Add(Card("Решённые проблемы KSC",resolved));
        }
        PageHost.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    private async Task ShowRecentKscEvents()
    {
        var latest=await remote.GetAsync<KscSectionView>("api/ksc/status",lifetime.Token);
        var window=new DialogWindow
        {
            Owner=this, Title="Последние события KSC", Width=Math.Min(1120,SystemParameters.WorkArea.Width),
            Height=Math.Min(640,SystemParameters.WorkArea.Height), WindowStartupLocation=WindowStartupLocation.CenterOwner
        };
        var events=Table(latest.Status.Recent.Reverse().ToArray(),("№","Id",null),("Компьютер","Computer",null),
            ("Время события (UTC)","Published",null),("Событие KSC","Summary",null),("Код события","Kind",null),
            ("Уровень KSC","Severity",null),("Идентификатор узла KSC","Host",null));
        events.SetValue(HasCustomDoubleClickProperty,true);events.MouseDoubleClick+=(_,e)=>{
            if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(events,source) is DataGridRow {Item:KscEvent item}){
                e.Handled=true;
                ShowKscEvent(item,window);
            }
        };
        var layout=new DockPanel();
        var close=new Button{Content="Закрыть",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,10,0,0)};
        close.Click+=(_,_)=>window.Close();
        DockPanel.SetDock(close,Dock.Bottom);
        layout.Children.Add(close);
        if(latest.Status.Recent.Length==0)
            layout.Children.Add(new TextBlock{Text="Нет событий KSC для отображения.",TextWrapping=TextWrapping.Wrap});
        else layout.Children.Add(events);
        window.Content=layout;
        window.ShowDialog();
    }
    private void ShowKscEvent(KscEvent item,Window owner)
    {
        string Field(string label,string? value)=>string.IsNullOrWhiteSpace(value)?"":ProductText.Get(label)+": "+value;
        var fields=new[]{
            Field("Код события",item.Kind),Field("Время события (UTC)",item.Published),Field("Продукт",item.Product),
            Field("Название угрозы",item.Threat),Field("Объект",item.ObjectName),
            Field("IP в событии",item.AttackerIp),Field("Действие защиты",item.IpBlocked?ProductText.Get("IP заблокирован"):null),
            Field("Описание KSC",!string.IsNullOrWhiteSpace(item.Description)?item.Description:ProductText.Get("KSC не передал подробное описание события."))
        };
        new FormDialog(owner,"Событие KSC",850).Note(item.Computer+" · "+item.EventName)
            .Text("details","Исходные данные KSC",string.Join(Environment.NewLine,fields.Where(x=>x.Length>0)),true,true)
            .TextHeight("details",400).ViewOnly().ShowDialog();
    }
    private async Task ShowKscFindings(KscIssue issue)
    {
        var result=await remote.PostAsync<KscIssueResolution,KscFindingList>("api/ksc/findings",new(issue.Key),lifetime.Token);
        var body=new System.Text.StringBuilder();
        string L(string label)=>ProductText.Get(label);
        void Field(string label,string value){if(value.Length>0)body.AppendLine(L(label)+": "+value);}
        string ActionTitle(KscFindingAction action)=>action.Title.Length>0?action.Title:L(action.Code switch{
            "GNRL_EV_OBJECT_CURED"=>"Объект вылечен",
            "GNRL_EV_OBJECT_DELETED"=>"Объект удалён",
            "GNRL_EV_OBJECT_QUARANTINED"=>"Объект помещён в карантин",
            "GNRL_EV_OBJECT_BLOCKED"=>"Объект заблокирован",
            "GNRL_EV_OBJECT_NOTCURED"=>"Не удалось вылечить объект",
            "GNRL_EV_OBJECT_PASSED"=>"Объект пропущен",
            _=>action.Code
        });
        foreach(var item in result.Events.Reverse()){
            if(body.Length>0)body.AppendLine();
            body.AppendLine($"#{item.Id} · {(item.Title.Length>0?item.Title:item.Code)}");
            Field("Код события",item.Code);
            Field("Время события (UTC)",item.Published);
            Field("Продукт",item.Product);
            if(item.RdpBruteForce){
                Field("Тип события","Подбор пароля RDP");
                Field("IP",item.AttackerIp??"");
                if(item.IpBlocked)Field("Действие защиты","IP заблокирован");
            }
            Field("Название угрозы",item.Threat);
            Field("Объект",item.ObjectName);
            Field("SHA-256",item.Sha256);
            Field("Описание KSC",item.Description.Length>0?item.Description:L("KSC не передал подробное описание события."));
            if(item.Actions.Length>0)
                foreach(var action in item.Actions)Field("Действие защиты",$"#{action.Id} · {ActionTitle(action)} {action.Published}".TrimEnd());
            else if(!item.IpBlocked&&(item.Code is "GNRL_EV_VIRUS_FOUND" or "GNRL_EV_VIRUS_FOUND_BY_KSN" or "GNRL_EV_SUSPICIOUS_OBJECT_FOUND"))
                body.AppendLine(L("Связанный результат обработки не найден в просмотренных событиях KSC."));
        }
        if(result.Events.Length==0)body.AppendLine(L("Исходные события не найдены в доступной части журнала KSC."));
        if(result.Limited||result.Events.LongLength<issue.Count)
            body.AppendLine(L("Показана доступная часть истории. Полный журнал можно проверить в консоли KSC."));
        new FormDialog(this,"Подробности проблемы KSC",900)
            .Note(issue.Computer+" · "+ProductText.Get(issue.Title)+" · "+L("Событий")+": "+issue.Count)
            .Text("findings","Исходные события KSC",body.ToString(),true,true).TextHeight("findings",450).ViewOnly().ShowDialog();
    }
    private async Task EditStandaloneKscConnection()
    {
        var saved=await remote.GetAsync<KscConnectionView>("api/ksc/connection",lifetime.Token);
            var form=new FormDialog(this,"Kaspersky Security Center")
                .Text("url","HTTPS-адрес и порт OpenAPI",saved.Address).Text("login","Имя пользователя",saved.Login)
                .Secret("password",saved.PasswordSaved?"Пароль · оставьте пустым, чтобы использовать сохранённый":"Пароль")
                .Text("domain","Домен (для доменной учётной записи)",saved.Domain).Toggle("internal","Внутренняя учётная запись KSC",saved.Internal)
                .Section("Сертификат OpenAPI")
                .Text("fingerprint","Отпечаток SHA-256 (необязательно)",saved.CertificateSha256)
                .Note("Оставьте поле пустым, если сертификат KSC доверен Windows на сервере Assistant.")
                .Note("Нажмите «Копировать команду». На самом сервере KSC откройте PowerShell от администратора, вставьте команду и нажмите Enter. Скопируйте строку из 64 символов в поле выше.")
                .Note("Команда читает стандартный файл klserver.cer сервера администрирования. Если он хранится в другом месте, измените путь в команде. Используйте сертификат, который KSC предъявляет на порту OpenAPI.");
            form.Action("Копировать команду",()=>{Clipboard.SetText(KscFingerprintCommand);AppMessageBox.Show(form,"Команда скопирована. Выполните её на сервере KSC.","Kaspersky Security Center");});
        KscConnection Read()=>new(form.Value("url"),form.Value("login"),form.Value("password"),form.Value("domain"),form.Checked("internal")){CertificateSha256=form.Value("fingerprint")};
        form.Section("Проверка и запуск сбора");
        form.Note("После сохранения Assistant проверит доступ и запустит сбор. Для нового подключения чтение начнётся с конца журнала.");
        form.Action("Проверить подключение",async()=>{var count=await remote.PostAsync<KscConnection,int>("api/ksc/probe",Read(),lifetime.Token);return "Чтение журнала разрешено. Доступно событий: "+count;});
        form.SaveAsync=()=>remote.PostAsync<KscConnection,bool>("api/ksc/enable",Read(),lifetime.Token);
        if(form.ShowDialog()==true)await ShowPage(12);
    }
    private async Task EditStandaloneKscTelegram(KscTelegramOptions settings)
    {
        var form=new FormDialog(this,"Telegram · Kaspersky")
            .Toggle("enabled","Отправлять уведомления Kaspersky",settings.Enabled)
            .Toggle("critical","Критичные",settings.Critical)
            .Toggle("warning","Предупреждения",settings.Warning)
            .Toggle("recovery","Восстановление и закрытие",settings.Recovery)
            .Note("Используется бот и чат из общих настроек Telegram. Уровни и восстановление для Kaspersky настраиваются здесь. Уведомления также должны быть разрешены в соответствующем правиле KSC.");
        if(form.ShowDialog()!=true)return;
        await remote.PostAsync<KscTelegramOptions,bool>("api/ksc/telegram",new(form.Checked("enabled"),form.Checked("critical"),form.Checked("warning"),form.Checked("recovery")),lifetime.Token);
        await ShowPage(12);
    }
}
