using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private sealed record NetworkRuleRow(NetworkRule Rule)
    {
        public string Name=>ProductLabels.NetworkRule(Rule);public string Kind=>Rule.Kind=="Log"?"Журнал":"Интерфейс";public Severity Severity=>Rule.Severity;
        public bool Enabled=>Rule.Enabled;public bool Telegram=>Rule.Notify;
        public string Conditions=>Rule.Kind=="Log"?string.Join("; ",Rule.MessageTerms.Concat(Rule.TopicTerms.Select(t=>"topic="+t))):string.Join("; ",Rule.Interfaces.Concat(Rule.Comments.Select(t=>"comment="+t)));
        public string Threshold=>Rule.Kind=="Log"?$"{Rule.Threshold} за {Rule.WindowSeconds} с":$"задержка {Rule.DelaySeconds} с; восстановление {Rule.RecoveryChecks} опроса";
    }
    private static List<NetworkRule> CloneRules(List<NetworkRule> source)=>JsonSerializer.Deserialize<List<NetworkRule>>(JsonSerializer.Serialize(source))!;
    private sealed record RuleTemplate(string Id,string Label,Func<List<NetworkRule>> Create);
    private static RuleTemplate[] RuleTemplates(IEnumerable<NetworkDictionary> dictionaries,Func<string,IReadOnlyDictionary<string,string[]>> overrides) =>
        dictionaries.Select(d=>new RuleTemplate(d.Id,d.Name,()=>NetworkLogRules.Effective(d,overrides(d.Id),null))).ToArray();

    private List<NetworkRule>? OpenNetworkRuleEditor(Window owner,List<NetworkRule> original,IReadOnlyList<RuleTemplate> templates,string selectedTemplate,bool savedWithDevice=true)
    {
        var rules=CloneRules(original);
        var window=new DialogWindow{Owner=owner,Title="Правила мониторинга и безопасности",Width=1120,Height=680,MinWidth=520,MinHeight=420,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var table=Table(Array.Empty<NetworkRuleRow>(),("Включено","Enabled",null),("Название","Name",null),("Тип","Kind",null),("Уровень","Severity",null),("Условия","Conditions",null),("Порог","Threshold",null),("Telegram","Telegram",null));
        table.SelectionMode=DataGridSelectionMode.Single;
        table.IsReadOnly=true;
        table.Columns[0].Width=105;
        table.Columns[1].Width=new DataGridLength(2,DataGridLengthUnitType.Star);
        var kindFilter=new ComboBox{ItemsSource=new[]{new Choice("All","Все правила"),new Choice("Interface","Интерфейсы"),new Choice("Log","Журнал RouterOS")},SelectedIndex=0,MinWidth=160};
        var profile=new ComboBox{ItemsSource=templates,DisplayMemberPath=nameof(RuleTemplate.Label),SelectedItem=templates.FirstOrDefault(x=>x.Id==selectedTemplate)??templates.FirstOrDefault(),MinWidth=130};
        string Filter()=>(kindFilter.SelectedItem as Choice)?.Key as string??"All";
        NetworkRuleRow? SelectedRule()=>table.SelectedItem as NetworkRuleRow;
        void Refresh(string? keep=null)
        {
            keep??=SelectedRule()?.Rule.Id;
            var filter=Filter();
            var rows=rules.Where(r=>filter=="All"||r.Kind==filter).Select(r=>new NetworkRuleRow(r)).ToArray();
            table.ItemsSource=rows;
            table.SelectedItem=rows.FirstOrDefault(r=>r.Rule.Id==keep);
        }
        void Reveal(NetworkRule rule)
        {
            if(Filter()!="All"&&Filter()!=rule.Kind)kindFilter.SelectedIndex=0;
            Refresh(rule.Id);
            if(table.SelectedItem!=null)table.ScrollIntoView(table.SelectedItem);
        }
        void Edit(bool add,bool duplicate=false){
            NetworkRule rule;
            if(add)rule=new(){Id="rule-"+Guid.NewGuid().ToString("N"),Kind=Filter()=="Log"?"Log":"Interface",Role=Filter()=="Log"?"Security":"WAN",Severity=Severity.Warning};
            else{if(table.SelectedItem is not NetworkRuleRow selected){AppMessageBox.Show(window,"Выберите правило");return;}rule=CloneRules([selected.Rule])[0];if(duplicate){rule.Id="rule-"+Guid.NewGuid().ToString("N");rule.Name+=" · копия";}}
            var form=new FormDialog(window,add?"Новое правило":"Правило · "+rule.Name,690)
                .Tab("Общее").Text("name","Название *",rule.Name)
                .Select("kind","Проверять",new[]{new Choice("Interface","Состояние интерфейса"),new Choice("Log","Сообщения в логе")},rule.Kind)
                .Select("severity","Уровень",new[]{new Choice(Severity.Info,"Информация"),new Choice(Severity.Warning,"Предупреждение"),new Choice(Severity.Critical,"Критическое")},rule.Severity)
                .Toggle("enabled","Правило включено",rule.Enabled).Toggle("notify","Отправлять в Telegram · с учётом общих уровней",rule.Notify)
                .Toggle("recovery","Сообщать о закрытии / восстановлении",rule.NotifyRecovery)
                .Select("role","Назначение",new[]{new Choice("WAN","Интернет / WAN"),new Choice("Tunnel","Важный туннель"),new Choice("Uplink","Межсетевой линк / uplink"),new Choice("UserVpn","Пользовательский VPN"),new Choice("Security","Безопасность")},rule.Role)
                .Text("peers","IP / имена узлов для пояснения события · необязательно",string.Join("\n",rule.Peers),true)
                .Tab("Условия лога").Text("message","Фразы в сообщении · по одной строке",string.Join("\n",rule.MessageTerms),true)
                .Toggle("accumulate","Считать все повторы с момента открытия проблемы",rule.AccumulateMatches)
                .Toggle("all","Сообщение должно содержать все фразы · иначе любую",rule.MatchAll)
                .Text("topics","Темы RouterOS · любая из перечисленных, по одной строке",string.Join("\n",rule.TopicTerms),true)
                .Text("exclude","Исключения · любая фраза отменяет совпадение",string.Join("\n",rule.ExcludeTerms),true)
                .Text("preceding","Перед событием должны встретиться фразы · необязательно",string.Join("\n",rule.PrecedingTerms),true)
                .Text("precedingCount","Сколько предыдущих совпадений нужно · 0 = не проверять",rule.PrecedingCount.ToString())
                .Text("threshold","Сколько новых событий нужно",rule.Threshold.ToString()).Text("window","За сколько секунд",rule.WindowSeconds.ToString())
                .Select("group","Считать отдельно",new[]{new Choice("SourceIp","Для каждого IP из «from …»"),new Choice("All","Все источники вместе")},rule.GroupBy)
                .Text("escalation","Группа повышения важности · одинаковый ID объединяет правила",rule.EscalationGroup)
                .Text("quiet","Закрыть после тишины, секунд · 0 = вручную",rule.AutoCloseSeconds.ToString())
                .Note("Фразы проверяются буквально, без регулярных выражений. Темы и фразы — совместные условия. При первом подключении старый буфер не создаёт тревоги. Окно повторов считается по времени обнаружения новых записей сервером.")
                .Tab("Условия интерфейса").Text("interfaces","Точные имена интерфейсов · по одному на строку",string.Join("\n",rule.Interfaces),true)
                .Text("comments","Фразы в комментариях интерфейсов · по одной на строку",string.Join("\n",rule.Comments),true)
                .Text("delay","Задержка тревоги, секунд",rule.DelaySeconds.ToString()).Text("checks","Успешных опросов для восстановления",rule.RecoveryChecks.ToString())
                .Toggle("disabled","Игнорировать выключенные интерфейсы",rule.IgnoreDisabled)
                .Text("flaps","Предупреждать после N переключений · 0 = выключено",rule.FlapThreshold.ToString()).Text("flapWindow","Окно переключений, секунд",rule.FlapWindowSeconds.ToString()).Text("flapQuiet","Стабильная работа для закрытия предупреждения, секунд",rule.FlapQuietSeconds.ToString());
            void Conditions(){var log=form.Selected<string>("kind")=="Log";form.TabVisible("Условия лога",log).TabVisible("Условия интерфейса",!log);}
            form.SelectionChanged("kind",Conditions);Conditions();
            string[] Lines(string key)=>form.Value(key).Split(['\n','\r'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            void Read(){rule.Name=form.Value("name");rule.Kind=form.Selected<string>("kind");rule.Severity=form.Selected<Severity>("severity");rule.Enabled=form.Checked("enabled");rule.Notify=form.Checked("notify");rule.NotifyRecovery=form.Checked("recovery");rule.Peers=Lines("peers");rule.Role=form.Selected<string>("role");if(rule.Kind=="Log"){rule.PrecedingTerms=Lines("preceding");rule.PrecedingCount=form.Number("precedingCount",0,1000);rule.MessageTerms=Lines("message");rule.TopicTerms=Lines("topics");rule.ExcludeTerms=Lines("exclude");rule.AccumulateMatches=form.Checked("accumulate");rule.MatchAll=form.Checked("all");rule.Threshold=form.Number("threshold",1,1000);rule.WindowSeconds=form.Number("window",1,86400);rule.GroupBy=form.Selected<string>("group");rule.EscalationGroup=form.Value("escalation");rule.AutoCloseSeconds=form.Number("quiet",0,604800);}else{rule.Interfaces=Lines("interfaces");rule.Comments=Lines("comments");rule.DelaySeconds=form.Number("delay",0,3600);rule.RecoveryChecks=form.Number("checks",1,10);rule.IgnoreDisabled=form.Checked("disabled");rule.FlapThreshold=form.Number("flaps",0,1000);rule.FlapWindowSeconds=form.Number("flapWindow",1,86400);rule.FlapQuietSeconds=form.Number("flapQuiet",1,604800);}}
            form.Validate=()=>{Read();NetworkDictionaries.ValidateRules([rule]);return null;};
            if(form.ShowDialog()!=true)return;
            if(add||duplicate)rules.Add(rule);else rules[rules.FindIndex(r=>r.Id==rule.Id)]=rule;
            Reveal(rule);
        }

        var root=new DockPanel{Margin=new Thickness(8)};
        System.Windows.Automation.AutomationProperties.SetName(kindFilter,ProductText.Get("Тип правила"));
        var top=new StackPanel();
        DockPanel.SetDock(top,Dock.Top);
        root.Children.Add(top);
        top.Children.Add(new TextBlock{
            Text=savedWithDevice
                ?"Выберите строку для редактирования. Кнопка «Включить / выключить» меняет состояние правила. В карточке устройства изменения вступают в силу после её сохранения."
                :"Выберите строку для редактирования. Кнопка «Включить / выключить» меняет состояние правила.",
            TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)
        });
        Button Button(string text,Action action,string? style=null)
        {
            var button=UiButton(text,style);
            button.Click+=(_,_)=>{try{action();}catch(Exception e){AppMessageBox.Show(window,e.Message,"Правила");}};
            return button;
        }
        var replace=Button("Заменить из профиля…",()=>{
            if(profile.SelectedItem is not RuleTemplate source)return;
            var replacement=source.Create();
            if(!Confirm(window,"Заменить весь список, включая пользовательские правила? Профиль: "+source.Label+". Правил в новом списке: "+replacement.Count+"."))return;
            rules=CloneRules(replacement);
            kindFilter.SelectedIndex=0;
            Refresh();
        });
        replace.IsEnabled=profile.SelectedItem!=null;
        profile.SelectionChanged+=(_,_)=>replace.IsEnabled=profile.SelectedItem!=null;
        var profileLabel=new TextBlock{Text="Профиль для замены",VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0)};
        System.Windows.Automation.AutomationProperties.SetName(profile,ProductText.Get(profileLabel.Text));
        var profileRow=new DockPanel{Margin=new(0,0,0,6)};
        DockPanel.SetDock(profileLabel,Dock.Left);profileRow.Children.Add(profileLabel);
        DockPanel.SetDock(replace,Dock.Right);profileRow.Children.Add(replace);
        profileRow.Children.Add(profile);top.Children.Add(profileRow);

        var toolbar=new StackPanel{Orientation=Orientation.Horizontal};
        toolbar.Children.Add(kindFilter);
        toolbar.Children.Add(Button("Добавить",()=>Edit(true)));
        var edit=Button("Изменить",()=>Edit(false));
        var copy=Button("Копировать",()=>Edit(false,true));
        var toggle=Button("Включить / выключить",()=>{
            if(SelectedRule() is not {} selected)return;
            selected.Rule.Enabled=!selected.Rule.Enabled;
            Reveal(selected.Rule);
        });
        var remove=Button("Удалить",()=>{
            if(SelectedRule() is not {} selected||!Confirm(window,"Удалить выбранное правило?"))return;
            rules.Remove(selected.Rule);Refresh();
        });
        foreach(var button in new[]{edit,copy,toggle,remove})toolbar.Children.Add(button);
        toolbar.Children.Add(Button("Добавить правила безопасности",()=>{
            var missing=NetworkLogRules.Standard().Where(s=>rules.All(r=>r.Id!=s.Id)).ToArray();
            if(missing.Length==0){AppMessageBox.Show(window,"Стандартные правила безопасности уже в списке.","Правила");return;}
            rules.AddRange(missing);
            Reveal(missing[0]);
        }));
        top.Children.Add(new ScrollViewer{Content=toolbar,HorizontalScrollBarVisibility=ScrollBarVisibility.Visible,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new(0,0,0,8)});
        void UpdateActions(){foreach(var button in new[]{edit,copy,toggle,remove})button.IsEnabled=SelectedRule()!=null;}
        table.SelectionChanged+=(_,_)=>UpdateActions();
        kindFilter.SelectionChanged+=(_,_)=>Refresh();
        table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=(_,e)=>{
            if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(table,source) is DataGridRow {Item:NetworkRuleRow}){
                e.Handled=true;
                edit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
        };

        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,8,0,0)};
        var cancel=Button("Отмена",()=>window.DialogResult=false);cancel.IsCancel=true;
        var apply=Button("Применить",()=>{NetworkDictionaries.ValidateRules(rules);window.DialogResult=true;},"PrimaryButton");
        footer.Children.Add(cancel);footer.Children.Add(apply);
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        root.Children.Add(TableSurface(table,"Нет правил","Добавьте правило или выберите другой фильтр."));
        window.Content=root;
        Refresh();UpdateActions();
        return window.ShowDialog()==true?rules:null;
    }
    private async Task EditNetworkRules(RouterMonitorRow router)
    {
        if(router.Kind!="MikroTik")throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Правила логов и интерфейсов доступны для MikroTik / RouterOS"));
        var c=await remote.CommandAsync<NetworkConnectionView>("router-connection",new{DeviceId=router.Id!.Value},lifetime.Token);
        if(string.IsNullOrWhiteSpace(c.User))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Сначала укажите индивидуальное подключение MikroTik через «Устройства → Свойства и подключение», затем настройте правила."));
        var dictionaries=await remote.CommandAsync<NetworkDictionary[]>("router-dictionaries",new{},lifetime.Token);
        var dictionary=dictionaries.Single(d=>d.Id==c.DictionaryId);
        var rules=OpenNetworkRuleEditor(this,NetworkLogRules.Effective(dictionary,c.Interfaces,c.Rules),RuleTemplates(dictionaries,id=>id==c.DictionaryId?c.Interfaces:new Dictionary<string,string[]>()),dictionary.Id,false);if(rules==null)return;
        await remote.CommandAsync<bool>("router-connection-save",new NetworkConnectionSave(c.DeviceId,c.Port,c.User,"",c.CertificateSha256,c.DictionaryId,c.Interfaces,c.Hash,rules,c.UpdateChannel),lifetime.Token);await ShowPage(page);
    }
}
