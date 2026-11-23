using System.Windows;
using System.Windows.Controls;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
 private async Task ShowRouterAudit(RouterMonitorRow router){
  if(router.Kind!="MikroTik"||router.Id==null)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выберите MikroTik"));
  ReaderWindow(router.Name+" · Конфигурация",await RouterAuditPanel(router)).ShowDialog();await ShowPage(page);
 }
 private async Task<FrameworkElement> RouterAuditPanel(RouterMonitorRow router){
  var id=router.Id!.Value;var view=await remote.CommandAsync<RouterAuditView>("router-audit-view",new{DeviceId=id},lifetime.Token);
  var root=new DockPanel{Margin=new(12)};var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
  var summary=new TextBlock{TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,8)};header.Children.Add(summary);
  var bar=new WrapPanel();header.Children.Add(bar);
  var filter=new ComboBox{MinWidth=180,Margin=new(0,0,8,8),ItemsSource=new[]{"Проблемы","Все проверки","Игнорируемые"},SelectedIndex=0,ToolTip="Успешные проверки доступны только в «Все проверки»"};bar.Children.Add(filter);
  var table=Table(Array.Empty<RouterAuditFinding>(),("Важность","Level",null),("Проблема / проверка","Title",null),("Результат","Status",null));
  var detail=new DockPanel();var title=new TextBlock{Text="Выберите проверку",FontSize=19,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)};DockPanel.SetDock(title,Dock.Top);detail.Children.Add(title);
  var status=new TextBlock{Foreground=Brush("#64748B"),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};DockPanel.SetDock(status,Dock.Top);detail.Children.Add(status);
  var description=DetailText();var evidence=DetailText();var commands=DetailText();commands.FontFamily=new System.Windows.Media.FontFamily("Consolas");
  var ports=Table(Array.Empty<RouterAuditPortRow>(),("Протокол","Protocol",null),("Внешний порт","ExternalPort",null),("Внутренний порт","FinalPort",null),("Результат","State",null),("Вход","Input",null),("Внешний адрес","OriginalDestination",null),("Назначение","FinalDestination",null));
  var tabs=new TabControl();tabs.Items.Add(new TabItem{Header="Порты и пути",Content=TableSurface(ports)});tabs.Items.Add(new TabItem{Header="Что проверить",Content=description});tabs.Items.Add(new TabItem{Header="Основания",Content=evidence});tabs.Items.Add(new TabItem{Header="Команды",Content=commands});detail.Children.Add(tabs);
  var inspector=new ListInspector(table,detail,"Результаты анализа","Активных проблем нет","Успешные проверки доступны в списке «Все проверки». Если выбран поиск, попробуйте очистить его.");root.Children.Add(inspector);
  bool busy=false;Window Owner()=>Window.GetWindow(root)??this;
  Button? ignore=null,unignore=null,copy=null;
  void Select(){
   var f=table.SelectedItem as RouterAuditFinding;
   title.Text=f?.Title??"Выберите проверку";status.Text=f==null?"Кнопки «Предыдущее / Следующее» переключают записи без закрытия описания.":f.FirewallConclusion+" · "+f.Status;
   description.Text=f==null?"Выберите запись в списке, чтобы увидеть подробности.":f.Recommendation+(f.Ignored?"\n\nИгнорирует: "+f.IgnoredBy+" · "+f.IgnoredUtc?.ToServerTime().ToString("dd.MM.yyyy HH:mm"):"");
   InspectorRows(ports,f==null?Array.Empty<RouterAuditPortRow>():RouterAuditPresentation.Ports(f),r=>r.Finding.Key+"|"+r.Path.Input+"|"+r.Path.OriginalDestination+"|"+r.Path.Protocol+"|"+r.Path.ExternalPort);
   var selectedPath=ports.SelectedItem as RouterAuditPortRow;
   evidence.Text=selectedPath!=null?selectedPath.State+"\n\n"+selectedPath.Finding.Evidence:f?.Evidence??"Основания появятся после выбора записи.";
   commands.Text=selectedPath?.Finding.Commands??f?.Commands??"Выберите проверку. Команды здесь только для ручного применения.";
   ((TabItem)tabs.Items[0]).Visibility=f?.Paths.Count>0?Visibility.Visible:Visibility.Collapsed;
   if((f==null||f.Paths.Count==0)&&tabs.SelectedIndex==0)tabs.SelectedIndex=1;
   if(ignore!=null)ignore.IsEnabled=CanEdit()&&f!=null&&!f.Ignored&&f.Severity is Severity.Critical or Severity.Warning;
   if(unignore!=null)unignore.IsEnabled=CanEdit()&&f!=null&&(f.Ignored||f.Members.Any(m=>m.Ignored));
   if(copy!=null)copy.IsEnabled=f?.Commands.Length>0;
  }
  void Render(){
   var result=view.Result;
   summary.Text=result==null?"Проверка ещё не выполнена — нажмите «Проверить конфигурацию».":$"Критичных: {result.Critical} · предупреждений: {result.Warnings} · игнорируется: {result.DisplayFindings.Count(f=>f.Ignored)} · проверено {result.CheckedUtc.ToServerTime():dd.MM.yyyy HH:mm}";
   summary.ToolTip=result==null?"Чтение конфигурации выполняется учётной записью read + api.":"WAN: "+string.Join(", ",result.WanInterfaces)+"\n"+result.CoverageLabel+"\nИсключения сохраняются после повторных проверок и обновления сервера.";
   InspectorRows(table,(result?.DisplayFindings??[]).Where(f=>filter.SelectedIndex==2?f.Ignored||f.Members.Any(m=>m.Ignored):filter.SelectedIndex==1||!f.Ignored&&f.Severity is Severity.Critical or Severity.Warning).OrderByDescending(f=>f.Severity).ThenBy(f=>f.Title),f=>f.Key);
   Select();
  }
  async Task Refresh(){view=await remote.CommandAsync<RouterAuditView>("router-audit-view",new{DeviceId=id},lifetime.Token);Render();}
  Button Action(string label,Func<Task> action){var button=UiButton(label);button.Click+=async(_,_)=>{if(busy)return;busy=true;bar.IsEnabled=false;try{await action();}catch(Exception e){AppMessageBox.Show(Owner(),e.Message,"Анализ конфигурации",MessageBoxButton.OK,MessageBoxImage.Error);try{await Refresh();}catch{summary.Text="Нет связи с сервером. Повторите обновление списка.";}}finally{busy=false;bar.IsEnabled=true;Select();}};bar.Children.Add(button);return button;}
  Action("Проверить конфигурацию",async()=>{summary.Text="Чтение и анализ конфигурации MikroTik…";view=await remote.CommandAsync<RouterAuditView>("router-audit-refresh",new{DeviceId=id},lifetime.Token);Render();}).Style=(Style)FindResource("PrimaryButton");
  Action("Обновить список",Refresh).ToolTip="Показать сохранённый результат";
  ignore=Action("Игнорировать",async()=>{if(table.SelectedItem is not RouterAuditFinding f)return;if(!Confirm(Owner(),"Игнорировать «"+f.Title+"»? Публикация и все её проверки портов перестанут влиять на цвет и новые уведомления до нажатия «Не игнорировать»."))return;view=await remote.CommandAsync<RouterAuditView>("router-audit-ignore",new RouterAuditIgnore(id,f.Key,f.Fingerprint,true,view.Hash),lifetime.Token);Render();});
  unignore=Action("Не игнорировать",async()=>{if(table.SelectedItem is not RouterAuditFinding f||!f.Ignored&&!f.Members.Any(m=>m.Ignored))return;view=await remote.CommandAsync<RouterAuditView>("router-audit-ignore",new RouterAuditIgnore(id,f.Key,f.Fingerprint,false,view.Hash),lifetime.Token);Render();});
  copy=Action("Скопировать команды",()=>{if(table.SelectedItem is RouterAuditFinding f&&f.Commands!="")Clipboard.SetText(f.Commands);return Task.CompletedTask;});
  var wan=Action("Внешние интерфейсы",async()=>{var dialog=new FormDialog(Owner(),"Внешние интерфейсы анализа",680).Text("wan","Имена по одному на строку · пусто = автоматически",string.Join("\n",view.ManualWan),true).Note("Сейчас известны: "+string.Join(", ",view.Interfaces)+"\nВыбор влияет только на анализ в Assistant и не меняет MikroTik.");if(dialog.ShowDialog()!=true)return;var names=dialog.Value("wan").Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);view=await remote.CommandAsync<RouterAuditView>("router-audit-policy",new RouterAuditPolicy(id,names,view.Hash),lifetime.Token);Render();});wan.IsEnabled=CanEdit();
  bool CanEdit()=>AccessRules.Level(remote.CurrentUser,"routers")>=2;
  ports.SelectionChanged+=(_,_)=>{if(ports.SelectedItem is RouterAuditPortRow row){evidence.Text=row.State+"\n\n"+row.Finding.Evidence;commands.Text=row.Finding.Commands;}};
  table.SelectionChanged+=(_,_)=>Select();filter.SelectionChanged+=(_,_)=>Render();Render();return root;
 }
}
