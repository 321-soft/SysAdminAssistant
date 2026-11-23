using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private static string MailDisplayLimit(string value,int length){
        if(value.Length<=length)return value;
        if(char.IsHighSurrogate(value[length-1]))length--;
        return value[..length]+"…";
    }
    private static string MailText(MailView mail,bool preview=false) =>
        $"{ProductText.Get("От:")} {mail.Sender}\n{ProductText.Get("Тема:")} {(preview?MailDisplayLimit(mail.Subject,360):mail.Subject)}\n{ProductText.Get("Поступило на IMAP:")} {Display.Stamp(mail.ImapReceivedAt)}\n{ProductText.Get("Дата отправителя:")} {Display.Stamp(mail.SentAt)}\n\n{(preview?MailDisplayLimit(mail.Body,16_384):mail.Body)}\n\n{ProductText.Get(mail.Note)}";
    private async Task ShowMail(ReportEvent report)
    {
        var mail=await remote.CommandAsync<MailView>("mail-view",new MailViewRequest(report.Id),lifetime.Token);
        var form=new FormDialog(this,"Письмо · "+mail.Subject,1000)
            .Tab("Письмо").Text("source","Отправитель, даты и текст",MailText(mail),true,true).TextHeight("source",500)
            .Tab("Разбор").Text("analysis","Результат обработки",mail.Analysis,true,true).TextHeight("analysis",500)
            .ViewOnly();
        if(report.Provider!="DMARC"&&AccessRules.Level(remote.CurrentUser,"reports")>=2){form.ActionAsync("Привязать",()=>BindMail(report));form.ActionAsync("Привязать как уведомление об обновлении",()=>BindMail(report,true));}
        form.ShowDialog();
    }
    private UIElement MailPreview(DataGrid table,Func<object,ReportEvent?> report)
    {
        var text=DetailText("Выберите письмо — здесь появятся отправитель, даты и текст. Кнопки сверху переключают письма без закрытия просмотра.");
        var root=new ListInspector(table,text,"Письма");
        var menu=new ContextMenu();
        var bind=new MenuItem {Header="Привязать"};
        var normal=new MenuItem{Header="Отчёт / событие"};normal.Click+=async(_,_)=>{if(table.SelectedItem is object item&&report(item) is ReportEvent selected)await Run(()=>BindMail(selected));};
        var update=new MenuItem{Header="Уведомление об обновлении"};update.Click+=async(_,_)=>{if(table.SelectedItem is object item&&report(item) is ReportEvent selected)await Run(()=>BindMail(selected,true));};bind.Items.Add(normal);bind.Items.Add(update);
        menu.Items.Add(bind);table.ContextMenu=menu;
        table.PreviewMouseRightButtonDown+=(_,e)=>{
            if(e.OriginalSource is DependencyObject element && ItemsControl.ContainerFromElement(table,element) is DataGridRow row){row.IsSelected=true;row.Focus();}
        };
        menu.Opened+=(_,_)=>bind.IsEnabled=AccessRules.Level(remote.CurrentUser,"reports")>=2&&table.SelectedItem is object item&&report(item) is ReportEvent selected&&selected.Provider!="DMARC";
        CancellationTokenSource? pending=null;
        int? displayed=null;
        // Layout/selection callbacks may run while WPF is realizing or hiding a DataGrid.
        // Do not change the reader or initiate a request from inside those callbacks.
        var previewDelay=new DispatcherTimer(DispatcherPriority.Background,root.Dispatcher){Interval=TimeSpan.FromMilliseconds(150)};
        void StopPreview(){
            previewDelay.Stop();
            var previous=pending;
            pending=null;
            previous?.Cancel();
        }
        void SchedulePreview(){
            StopPreview();
            if(root.IsLoaded&&root.IsVisible&&!lifetime.IsCancellationRequested)previewDelay.Start();
        }
        async Task LoadPreview(){
            if(!root.IsLoaded||!root.IsVisible||lifetime.IsCancellationRequested)return;
            if(table.SelectedItem is not object item || report(item) is not ReportEvent selected){
                displayed=null;
                text.Text="Выберите письмо";
                return;
            }
            if(displayed==selected.Id)return;
            displayed=null;
            using var request=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            pending=request;
            bool Current()=>ReferenceEquals(pending,request)&&root.IsLoaded&&root.IsVisible&&!request.IsCancellationRequested
                && ReferenceEquals(table.SelectedItem,item);
            try {
                text.Text="Загрузка письма…";
                var mail=await remote.CommandAsync<MailView>("mail-view",new MailViewRequest(selected.Id),request.Token);
                if(Current()){
                    text.Text=MailText(mail,true);
                    displayed=selected.Id;
                }
            }catch(OperationCanceledException)when(request.IsCancellationRequested){}
            catch(Exception e){
                if(Current())text.Text="Не удалось открыть письмо: "+e.Message;
            }
            finally{if(ReferenceEquals(pending,request))pending=null;}
        }
        previewDelay.Tick+=async(_,_)=>{
            previewDelay.Stop();
            try{await LoadPreview();}
            catch(Exception e){App.RecordClientFault("mail-preview",e);}
        };
        table.SelectionChanged+=(_,_)=>SchedulePreview();
        root.Loaded+=(_,_)=>SchedulePreview();
        root.IsVisibleChanged+=(_,_)=>SchedulePreview();
        root.Unloaded+=(_,_)=>StopPreview();
        table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=async(_,_)=>{
            if(table.SelectedItem is object item && report(item) is ReportEvent selected)await Run(()=>ShowMail(selected));
        };
        return root;
    }
    private async Task RepairMailDates()
    {
        var form=new FormDialog(this,"Сверить даты с IMAP",760).Select("mailbox","Сохранённый почтовый ящик",
            settings.Mailboxes.Select(m=>new Choice(m.Id,m.ToString())),settings.Mailboxes.FirstOrDefault()?.Id)
            .Note("Сверка использует UID и INTERNALDATE. Тела и вложения не скачиваются. Уже загруженные записи сохраняются без дублей. В большой папке повторите сверку до сообщения о завершении.")
            .Submit("Сверить даты");
        form.Validate=()=>form.HasSelection("mailbox")?null:"Выберите ящик";
        if(form.ShowDialog()!=true)return;
        var result=await remote.CommandAsync<string>("mail-receipt-repair",new MailReceiptRepair(form.Selected<string>("mailbox")),lifetime.Token);
        AppMessageBox.Show(this,result,"Сверка дат");
        await LoadPendingSnapshot(true);await ShowPage(page);
    }
}
