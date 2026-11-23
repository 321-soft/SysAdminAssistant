using System.Windows;
using System.Windows.Controls;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private async Task ShowDeviceUpdates(int deviceId,OsUpdateStatus? os,Window owner,bool canEdit)
    {
        var window=new DialogWindow{Owner=owner,Title="Обновления устройства",Width=900,Height=620,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var tabs=new TabControl();var root=new DockPanel();var bar=new WrapPanel();DockPanel.SetDock(bar,Dock.Bottom);root.Children.Add(bar);root.Children.Add(tabs);window.Content=root;
        var table=Table(Array.Empty<MailUpdateItem>(),("Продукт","Product",null),("Доступная версия","Version",null),("Первое письмо","FirstReceived","dd.MM.yyyy HH:mm"),("Последнее письмо","LastReceived","dd.MM.yyyy HH:mm"),("Состояние","Status",null),("Подтверждено","InstalledUtc","dd.MM.yyyy HH:mm"),("Кем","ConfirmedBy",null));
        var mailPanel=new DockPanel();var explanation=new TextBlock{Text="Письмо сообщает о доступной версии, но не подтверждает установку. После обновления устройства выберите уведомление и нажмите «Обновление установлено». Подтверждение сохраняется; уведомление о новой версии появится отдельно.",TextWrapping=TextWrapping.Wrap,Margin=new(14)};
        DockPanel.SetDock(explanation,Dock.Top);mailPanel.Children.Add(explanation);mailPanel.Children.Add(TableSurface(table));
        tabs.Items.Add(new TabItem{Header="Из писем",Content=mailPanel});var osText=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(14)};tabs.Items.Add(new TabItem{Header="Опрос ОС / RouterOS",Content=new ScrollViewer{Content=osText}});
        MailUpdatesView view=new(deviceId,"",[]);bool busy=false,changed=false;Button? source=null,installed=null;
        void Actions()
        {
            if(source==null||installed==null)return;
            var show=tabs.SelectedIndex==0;var item=table.SelectedItem as MailUpdateItem;
            source.Visibility=installed.Visibility=show?Visibility.Visible:Visibility.Collapsed;
            source.IsEnabled=!busy&&CanRead("reports")&&item?.ReportId>0;
            installed.IsEnabled=!busy&&canEdit&&item is {InstalledUtc:null,Withdrawn:false};
            installed.ToolTip=!canEdit?"Нет права изменять устройство":item==null?"Выберите уведомление об обновлении":item.InstalledUtc!=null?"Установка этой версии уже подтверждена":item.Withdrawn?"Уведомление отозвано после исправления привязки":"Подтвердить установку версии из выбранного письма";
        }
        void Apply()
        {
            var selected=(table.SelectedItem as MailUpdateItem)?.Key;table.ItemsSource=view.Items;
            table.SelectedItem=view.Items.FirstOrDefault(i=>i.Key==selected)??view.Items.FirstOrDefault(i=>i.InstalledUtc==null&&!i.Withdrawn)??view.Items.FirstOrDefault();
            osText.Text=view.Os==null?"Данные прямого опроса отсутствуют":UpdateDetails(view.Os);
            if(view.Items.Length==0)tabs.SelectedIndex=1;
            Actions();
        }
        async Task Refresh(){view=await remote.CommandAsync<MailUpdatesView>("device-updates",new{DeviceId=deviceId},lifetime.Token);Apply();}
        Button AddButton(string name,Func<Task> action)
        {
            var button=UiButton(name);button.Click+=async(_,_)=>{if(busy)return;busy=true;bar.IsEnabled=false;Actions();try{await action();}catch(Exception e){AppMessageBox.Show(window,e.Message,"Обновления",MessageBoxButton.OK,MessageBoxImage.Error);}finally{busy=false;bar.IsEnabled=true;Actions();}};bar.Children.Add(button);return button;
        }
        AddButton("Обновить",Refresh);
        source=AddButton("Открыть исходное письмо",async()=>{
            if(table.SelectedItem is not MailUpdateItem item)return;MailView mail;
            try{mail=await remote.CommandAsync<MailView>("mail-view",new MailViewRequest(item.ReportId),lifetime.Token);}catch(Exception e){throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Исходное письмо недоступно; возможно, удалено по сроку хранения. ")+e.Message,e);}
            new FormDialog(window,"Исходное письмо",950).Text("source","Отправитель, даты и текст",MailText(mail),true,true).TextHeight("source",480).ViewOnly().ShowDialog();
        });
        installed=AddButton("Обновление установлено",async()=>{
            if(!canEdit||table.SelectedItem is not MailUpdateItem {InstalledUtc:null,Withdrawn:false} item)return;
            if(!Confirm(window,"Подтвердите, что на устройстве установлено "+item.Product+" "+(item.Version==""?"из этого уведомления":item.Version)+". Программа не проверяет установку и не выполняет обновление."))return;
            view=await remote.CommandAsync<MailUpdatesView>("device-update-installed",new MailUpdateConfirm(deviceId,item.Key,view.Hash),lifetime.Token);Apply();changed=true;
        });
        table.SelectionChanged+=(_,_)=>Actions();tabs.SelectionChanged+=(_,e)=>{if(e.Source==tabs)Actions();};
        AddButton("Закрыть",()=>{window.Close();return Task.CompletedTask;});await Refresh();window.ShowDialog();
        if(changed){await remote.RefreshAsync(lifetime.Token);await ShowPage(page);}
    }
}
