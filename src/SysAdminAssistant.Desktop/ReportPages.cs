using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private async Task ShowReports()
    {
        await using var db=factory.Create();var sections=new List<(string Title, UIElement Content)>();
        var reports=await db.Reports.AsNoTracking().Where(x=>x.Provider!="DMARC").OrderByDescending(x=>x.ReceivedAt).Take(1000).ToArrayAsync();
        var all=Table(reports,("Получено","ReceivedAt","dd.MM.yyyy HH:mm"),("Категория","CategoryLabel",null),("Почтовый профиль","Provider",null),("Событие / задание","Task",null),("Привязка","TargetName",null),("Уровень","Severity",null),("Нужна проверка","IsPending",null));
        sections.Add(("Все · последние 1000",Panel(MailPreview(all,x=>x as ReportEvent),RowAction(all,"Привязать",()=>BindMail(Selected<ReportEvent>(all))),RowAction(all,"Открыть письмо",()=>ShowMail(Selected<ReportEvent>(all))),RowAction(all,"Разбор отчёта",()=>ShowReportDiagnosis(Selected<ReportEvent>(all))),RowAction(all,"Проверить выбранное письмо",()=>ReprocessMailSource(Selected<ReportEvent>(all).Id)),ActionButton("Проверить почту сейчас",RequestMailPoll),ActionButton("Диагностика почты",ShowMailDiagnostics),ActionButton("Правила привязки",ShowRules),ActionButton("Источники почты",ShowMailSources))));
        var count = await db.Pending.CountAsync(x=>db.Reports.Any(e=>e.Id==x.EventId && e.Provider!="DMARC")); queuePage = Math.Clamp(queuePage, 0, Math.Max(0, (count - 1) / 100));
        var pending = await (from p in db.Pending.AsNoTracking() join e in db.Reports.AsNoTracking() on p.EventId equals e.Id where e.Provider!="DMARC" orderby p.Id descending select new QueueRow(p.Subject, e)).Skip(queuePage * 100).Take(100).ToArrayAsync();
        var queueGrid = Table(pending, ("Получено", "Received", null), ("Почтовый профиль", "Provider", null), ("Уровень", "Severity", null), ("Тема", "Subject", null), ("Причина", "Issue", null));
        queueGrid.Tag = "paged";
        var queuePanel = Panel(MailPreview(queueGrid,x=>(x as QueueRow)?.Event), RowAction(queueGrid,"Открыть письмо",()=>ShowMail(Selected<QueueRow>(queueGrid).Event)),RowAction(queueGrid, "Разбор предупреждений", () => ShowReportDiagnosis(Selected<QueueRow>(queueGrid).Event)), RowAction(queueGrid, "Привязать", () => BindMail(Selected<QueueRow>(queueGrid).Event)),
            RowAction(queueGrid,"Создать источник по письму",()=>CreateMailSource(Selected<QueueRow>(queueGrid).Event)),
            RowAction(queueGrid,"Проверить выбранное письмо",()=>ReprocessMailSource(Selected<QueueRow>(queueGrid).Event.Id)),
            ActionButton("Повторить анализ очереди", async () => { await new Infrastructure.ReportStore(factory, settings.CustomDiagnostics).ReprocessAsync(lifetime.Token); await ShowPage(11); }),
            QueuePageButton("← Назад", queuePage > 0, -1), QueuePageButton("Далее →", (queuePage + 1) * 100 < count, 1));
        sections.Add(($"{ProductText.Get("Нераспознанные")} · {count} · {ProductText.Get("стр.")} {queuePage + 1}",queuePanel));
        var unbound=Table(reports.Where(r=>!r.IsPending&&r.DeviceId==null&&r.JobId==null&&r.TargetName==""),("Получено","ReceivedAt","dd.MM.yyyy HH:mm"),("Почтовый профиль","Provider",null),("Событие","Task",null),("Уровень","Severity",null));
        sections.Add(("Без привязки",Panel(MailPreview(unbound,x=>x as ReportEvent),RowAction(unbound,"Открыть письмо",()=>ShowMail(Selected<ReportEvent>(unbound))),RowAction(unbound,"Привязать",()=>BindMail(Selected<ReportEvent>(unbound))))));
        var incidents=await db.Incidents.AsNoTracking().Where(x=>!db.Reports.Any(e=>e.Id==x.ReportId && e.Provider=="DMARC")).OrderBy(x=>x.Closed).ThenByDescending(x=>x.CreatedAt).Take(500).ToArrayAsync();
        var eventReports=await db.Reports.AsNoTracking().Where(r=>r.JobId!=null).ToArrayAsync();var eventJobs=await db.Jobs.AsNoTracking().ToArrayAsync();
        var eventGroups=BackupIncidentLifecycle.Group(incidents,eventReports,eventJobs);
        var showClosed=new CheckBox{Content="Показывать закрытые события (история)",Margin=new Thickness(0,0,0,10)};
        var events=Table(eventGroups.Where(g=>!g.Closed&&BackupIncidentLifecycle.Fault(g.Incident)),("Дата","CreatedAt","dd.MM.yyyy HH:mm"),("Уровень","Severity",null),("Канал события","Source",null),("Событие","Title",null),("Повторов","Count",null),("Первое","FirstAt","dd.MM.yyyy HH:mm"),("Закрыто","Closed",null));
        showClosed.Checked+=(_,_)=>events.ItemsSource=eventGroups;showClosed.Unchecked+=(_,_)=>events.ItemsSource=eventGroups.Where(g=>!g.Closed&&BackupIncidentLifecycle.Fault(g.Incident)).ToArray();
        sections.Add(("События · активные / история",ListSurface(events,[RowAction(events,"Подробности",()=>{var e=Selected<IncidentGroupRow>(events);return ShowStoredDiagnosis(e.Title,Display.Stamp(e.CreatedAt),e.Details);}),RowAction(events,"Закрыть / открыть",async()=>{var group=Selected<IncidentGroupRow>(events);await using var changes=factory.Create();foreach(var id in group.IncidentIds){var item=await changes.Incidents.SingleAsync(i=>i.Id==id);item.Closed=!group.Closed;}await changes.SaveChangesAsync();await remote.RefreshAsync(lifetime.Token);await ShowPage(page);})],filter:showClosed,search:true)));
        if(remote.CurrentUser.Administrator)sections.Add(("Сводка Telegram",Panel(new TextBlock{Text="Ежедневный отчёт: новые, решённые и активные проблемы. Расписание — по часовому поясу сервера.",TextWrapping=TextWrapping.Wrap},ActionButton("Расписание и состав",EditDailySummary),ActionButton("Предпросмотр и статус",PreviewDailySummary))));
        backupTab=Math.Clamp(backupTab,0,sections.Count-1);
        var pageLayout=new DockPanel();
        var selector=new WrapPanel{Margin=new Thickness(0,0,0,8)};
        DockPanel.SetDock(selector,Dock.Top);pageLayout.Children.Add(selector);
        var content=new Grid();
        pageLayout.Children.Add(content);
        var group="reports-"+Guid.NewGuid().ToString("N");
        int visibleTab=backupTab;
        DispatcherOperation? pendingTab=null;
        pageLayout.Unloaded+=(_,_)=>{pendingTab?.Abort();pendingTab=null;};
        for(int i=0;i<sections.Count;i++){
            int index=i;
            var section=sections[i];
            section.Content.Visibility=i==backupTab?Visibility.Visible:Visibility.Collapsed;
            content.Children.Add(section.Content);
            var button=new RadioButton{Content=section.Title,GroupName=group,IsChecked=i==backupTab};
            button.Checked+=(_,_)=>{
                backupTab=index;
                pendingTab?.Abort();
                pendingTab=Dispatcher.InvokeAsync(()=>{
                    pendingTab=null;
                    if(!pageLayout.IsLoaded||page!=11||lifetime.IsCancellationRequested||visibleTab==index)return;
                    int previous=visibleTab;
                    try{
                        sections[previous].Content.Visibility=Visibility.Collapsed;
                        sections[index].Content.Visibility=Visibility.Visible;
                        visibleTab=index;
                    }catch(Exception e){
                        App.RecordClientFault($"mail-tab-{previous}-to-{index}",e);
                        sections[index].Content.Visibility=Visibility.Collapsed;
                        sections[previous].Content.Visibility=Visibility.Visible;
                        backupTab=previous;
                        StatusLine.Text=ProductText.Get("Не удалось открыть вкладку");
                    }
                },DispatcherPriority.Background);
            };
            selector.Children.Add(button);
        }
        PageHost.Children.Add(pageLayout);
    }
    private Button QueuePageButton(string title,bool enabled,int direction)
    {
        var button=ActionButton(title,async()=>{queuePage+=direction;backupTab=1;await ShowPage(11);});button.IsEnabled=enabled;return button;
    }
    private async Task RequestMailPoll()
    {
        await remote.CommandAsync<bool>("mail-poll",new{},lifetime.Token);
        AppMessageBox.Show(this,"Проверка почты поставлена в очередь сервера. Текущий этап и результат — в диагностике почты.","Проверка почты");
    }
    private async Task ShowMailDiagnostics()
    {
        var form=new FormDialog(this,"Диагностика почты",850).Note("Проверка выполняется сервером. Обновите результат кнопкой ниже. Время — UTC; счётчики относятся к текущему или последнему циклу.").Text("status","Проверки","",true,true).TextHeight("status",440);
        async Task<string> Refresh() {
            var rows=await remote.CommandAsync<MailboxPollInfo[]>("mail-status",new{},lifetime.Token);
            form.Set("status",rows.Length==0?"Проверок ещё нет. Проверьте, включён ли ящик, затем нажмите «Проверить почту сейчас»." : string.Join("\n\n",rows.Select(x=>$"{x.Name} · {x.Stage}\nПапка: {x.Folder}; режим: {(x.UnseenOnly?"непрочитанные":"все письма")}\nНачало UTC: {x.StartedUtc:dd.MM.yyyy HH:mm:ss}; конец: {x.FinishedUtc:dd.MM.yyyy HH:mm:ss}\nВсего в папке: {x.TotalInFolder}; найдено: {x.Found}; уже учтено: {x.AlreadyProcessed}; фильтр отправителя/темы: {x.Filtered}; SPF/DKIM отклонено: {x.AuthenticationRejected}; без текстового тела: {x.NoBody}; без заголовков: {x.MissingHeaders}; без подходящего содержимого: {x.NoPayload}\nПринято писем: {x.AcceptedMessages}; добавлено отчётов: {x.ImportedRecords}\n{x.Error}\n{(x.LastAuthenticationFailure.Length==0?"":"Последнее отклонение по подлинности:\n"+x.LastAuthenticationFailure)}")));
            return "Результат обновлён";
        }
        form.Action("Обновить результат",Refresh).Action("Проверить почту сейчас",async()=>{await remote.CommandAsync<bool>("mail-poll",new{},lifetime.Token);return "Проверка поставлена в очередь; обновите результат через несколько секунд";});
        await Refresh();form.ViewOnly().ShowDialog();
    }
}
