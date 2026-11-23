using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private async Task ShowMonitoringDictionaries(bool kscOnly)
    {
        var window=new DialogWindow{Owner=this,Title=kscOnly?"Правила событий Kaspersky":"Словари сетевых устройств",Width=980,Height=650,MinWidth=640,MinHeight=420,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var root=new DockPanel();
        var instructions=new TextBlock{Text="Перед сохранением импорта можно просмотреть список изменений.",TextWrapping=TextWrapping.Wrap,Margin=new(4,2,4,10)};
        DockPanel.SetDock(instructions,Dock.Top);root.Children.Add(instructions);
        var network=Table(Array.Empty<NetworkDictionaryRow>(),("Словарь","Name",null),("ID","Id",null),("Правил","Count",null),("Тип","Kind",null));
        var kaspersky=Table(Array.Empty<KscDictionaryRow>(),("Правило","Title",null),("Код события","Code",null),("Уровень","Severity",null),("Состояние","Status",null),("Telegram","Notify",null));
        var footer=new StackPanel{Margin=new(4,8,4,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var hint=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,6)};footer.Children.Add(hint);
        var actions=new WrapPanel();footer.Children.Add(actions);root.Children.Add(TableSurface(kscOnly?kaspersky:network));window.Content=root;
        async Task<MonitoringDictionaryCatalog> LoadCatalog(){
            try{return await remote.GetAsync<MonitoringDictionaryCatalog>("api/dictionaries/catalog",lifetime.Token);}
            catch(ServerResponseException e)when(e.StatusCode==System.Net.HttpStatusCode.NotFound){
                throw new InvalidOperationException(ProductText.Get("На подключённом сервере недоступен каталог словарей. Проверьте адрес подключения и версию службы SysAdmin Assistant Server, затем обновите сервер до версии клиента."),e);
            }
        }
        MonitoringDictionaryCatalog catalog=await LoadCatalog();
        Button? export=null,edit=null,create=null,standard=null;
        async Task Reload(){catalog=await LoadCatalog();Render();}
        async Task Execute(Func<Task> action)
        {
            root.IsEnabled=false;
            try{await action();}
            catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
            catch(Exception e){AppMessageBox.Show(window,e.Message,"Словари мониторинга",MessageBoxButton.OK,MessageBoxImage.Error);}
            finally{root.IsEnabled=true;}
        }
        Button Add(string title,Func<Task> action)
        {
            var button=UiButton(title);button.Click+=async(_,_)=>await Execute(action);actions.Children.Add(button);return button;
        }
        async Task Apply(string json,bool ksc,string? expected=null)
        {
            var preview=await remote.PostAsync<DictionaryImport,DictionaryChanges>("api/dictionaries/preview",new(json),lifetime.Token);
            if(expected!=null&&preview.Revision!=expected)throw new InvalidOperationException("Словарь изменился. Обновите список и повторите.");
            var form=new FormDialog(window,"Импорт словаря мониторинга",760)
                .Note(ksc?"Назначение: общие правила событий Kaspersky.":"Назначение: сетевые словари. Привязки устройств не изменятся.")
                .Text("changes","Изменения",string.Join("\n",preview.Changes),true,true).TextHeight("changes",310);
            if(preview.Changed==0){form.ViewOnly().ShowDialog();return;}
            if(form.ShowDialog()!=true)return;
            await remote.PostAsync<DictionaryImport,DictionaryChanges>("api/dictionaries/import",new(json,preview.Revision),lifetime.Token);
            await Reload();
        }
        Add("Импорт JSON…",async()=>{
            var open=new Microsoft.Win32.OpenFileDialog{Filter="JSON (*.json)|*.json",CheckFileExists=true};
            if(open.ShowDialog(window)!=true)return;
            if(new System.IO.FileInfo(open.FileName).Length>2_000_000)throw new ArgumentException("Файл словарей слишком большой.");
            var json=await System.IO.File.ReadAllTextAsync(open.FileName,lifetime.Token);
            if(MonitoringDictionaryFormat.IsKaspersky(json)!=kscOnly){
                throw new ArgumentException(ProductText.Get(kscOnly?"Откройте Настройки → Сеть → Сетевые словари для импорта сетевого файла.":"Откройте Kaspersky → Правила Kaspersky для импорта правил Kaspersky."));
            }
            await Apply(json,kscOnly);
        });
        export=Add("Экспорт JSON…",async()=>{
            var ksc=kscOnly;
            var save=new Microsoft.Win32.SaveFileDialog{Filter="JSON (*.json)|*.json",FileName=ksc?"ksc-rules.json":"network-dictionaries.json",AddExtension=true,DefaultExt=".json"};
            if(save.ShowDialog(window)!=true)return;
            var json=await remote.GetAsync<string>("api/dictionaries/export"+(ksc?"?product=KSC":""),lifetime.Token);
            await System.IO.File.WriteAllTextAsync(save.FileName,json,lifetime.Token);
        });
        async Task Edit(bool create=false)
        {
            if(!kscOnly && network.SelectedItem is NetworkDictionaryRow {Dictionary:var d}){
                if(d.Id=="generic-network"){
                    new FormDialog(window,"Типовой сетевой словарь",740)
                        .Text("rules","Правила",string.Join("\n",d.Rules.Select(r=>ProductLabels.NetworkRule(r)+" · "+r.Severity)),true,true).TextHeight("rules",290).ViewOnly().ShowDialog();
                    return;
                }
                string Pack(List<NetworkRule> rules)=>JsonSerializer.Serialize(new NetworkDictionaryPackage{Dictionaries=[new(){Id=d.Id,Name=d.Name,Rules=rules}]});
                var before=await remote.PostAsync<DictionaryImport,DictionaryChanges>("api/dictionaries/preview",new(Pack(d.Rules)),lifetime.Token);
                if(before.Changed!=0)throw new InvalidOperationException("Словарь изменился. Обновите список и повторите.");
                var edited=OpenNetworkRuleEditor(window,d.Rules,[new("generic-network","Типовой сетевой профиль",()=>NetworkDictionaries.Generic().Rules)],"generic-network",false);
                if(edited==null)return;
                await remote.PostAsync<DictionaryImport,DictionaryChanges>("api/dictionaries/import",new(Pack(edited),before.Revision),lifetime.Token);
                await Reload();return;
            }
            if(!kscOnly)return;
            var r=create?new KscRule("rule-"+Guid.NewGuid().ToString("N"),"","","",Severity.Warning,false):(kaspersky.SelectedItem as KscDictionaryRow)?.Rule;
            if(r==null)return;
            var form=new FormDialog(window,"Правило Kaspersky",700)
                .Text("title","Название",r.Title).Text("open","Код события проблемы",r.OpenEvent)
                .Text("recovery","Код восстановления (необязательно)",r.RecoveryEvent)
                .Select("level","Уровень",new[]{new Choice(Severity.Warning,"Предупреждение"),new Choice(Severity.Critical,"Критично")},r.Level)
                .Toggle("enabled","Правило включено",r.Enabled).Toggle("notify","Уведомлять в Telegram",r.Notify)
                .Note("Изменения действуют на новые события. Уже открытые проблемы сохраняют прежний способ закрытия.");
            KscRule Read()=>r with{Title=form.Value("title").Trim(),OpenEvent=form.Value("open").Trim(),RecoveryEvent=form.Value("recovery").Trim(),Level=form.Selected<Severity>("level"),Enabled=form.Checked("enabled"),Notify=form.Checked("notify")};
            form.Validate=()=>{try{_=KscRules.Parse(KscRules.Export([Read()]));return null;}catch(ArgumentException e){return e.Message;}};
            if(form.ShowDialog()!=true)return;
            await remote.PostAsync<DictionaryImport,DictionaryChanges>("api/dictionaries/import",new(KscRules.Export([Read()]),catalog.Kaspersky.Revision),lifetime.Token);
            await Reload();
        }
        edit=Add("Открыть правила…",()=>Edit());
        create=Add("Новое правило…",()=>Edit(true));
        standard=Add("Добавить типовые правила Kaspersky…",async()=>{
            var installed=catalog.Kaspersky.Rules;
            var missing=KscRules.Standard().Where(r=>!installed.Any(i=>i.Id==r.Id||i.OpenEvent==r.OpenEvent)).ToArray();
            if(missing.Length==0){AppMessageBox.Show(window,"Типовые правила уже добавлены.","Kaspersky");return;}
            await Apply(KscRules.Export(missing),true,catalog.Kaspersky.Revision);
        });
        Add("Обновить",Reload);
        actions.Children.Add(new Button{Content="Закрыть",IsCancel=true});
        void UpdateButtons(){
            bool ksc=kscOnly;
            if(standard==null||create==null||edit==null||export==null)return;
            standard.Visibility=create.Visibility=ksc?Visibility.Visible:Visibility.Collapsed;
            edit.IsEnabled=ksc?kaspersky.SelectedItem is KscDictionaryRow:network.SelectedItem is NetworkDictionaryRow;
            export.IsEnabled=ksc?catalog.Kaspersky.Rules.Length>0:catalog.Network.Any(d=>d.Id!="generic-network");
            hint.Text=ksc
                ?"Правила действуют на новые события всех узлов подключённого KSC. Уведомления включаются в разделе Kaspersky."
                :"Словарь назначается в карточке сетевого устройства. Типовой словарь доступен только для чтения.";
        }
        void Render(){
            var nid=(network.SelectedItem as NetworkDictionaryRow)?.Id;var kid=(kaspersky.SelectedItem as KscDictionaryRow)?.Rule.Id;
            var nr=catalog.Network.Select(d=>new NetworkDictionaryRow(d)).ToArray();var kr=catalog.Kaspersky.Rules.Select(r=>new KscDictionaryRow(r)).ToArray();
            network.ItemsSource=nr;kaspersky.ItemsSource=kr;
            network.SelectedItem=nr.FirstOrDefault(r=>r.Id==nid)??nr.FirstOrDefault();
            kaspersky.SelectedItem=kr.FirstOrDefault(r=>r.Rule.Id==kid)??kr.FirstOrDefault();UpdateButtons();
        }
        network.SelectionChanged+=(_,_)=>UpdateButtons();kaspersky.SelectionChanged+=(_,_)=>UpdateButtons();
        Render();window.ShowDialog();
    }
    private sealed record NetworkDictionaryRow(NetworkDictionary Dictionary)
    {
        public string Id=>Dictionary.Id;
        public string Name=>Id=="generic-network"?ProductText.Get(Dictionary.Name):Dictionary.Name;
        public int Count=>Dictionary.Rules.Count;
        public string Kind=>ProductText.Get(Id=="generic-network"?"Встроенный":"Пользовательский");
    }
    private sealed record KscDictionaryRow(KscRule Rule)
    {
        public string Title=>Rule.Title;
        public string Code=>Rule.OpenEvent;
        public Severity Severity=>Rule.Level;
        public string Status=>ProductText.Get(Rule.Enabled?"Включено":"Отключено");
        public string Notify=>ProductText.Get(Rule.Notify?"Да":"Нет");
    }
}
