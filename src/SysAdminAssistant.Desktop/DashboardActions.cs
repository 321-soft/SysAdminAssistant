using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    // Menus capture the clicked entity, never a previous table selection. Re-check permissions at execution.
    private void DashboardAction(ContextMenu menu,string title,Func<Task> action,params (string Module,int Level)[] required)
    {
        bool Allowed()=>required.All(r=>AccessRules.Level(remote.CurrentUser,r.Module)>=r.Level);
        var item=new MenuItem{Header=ProductText.Get(title),IsEnabled=Allowed()};
        menu.Opened+=(_,_)=>item.IsEnabled=Allowed();
        item.Click+=async(_,e)=>{e.Handled=true;await Run(async()=>{if(!Allowed())throw new UnauthorizedAccessException(ProductText.Get("Недостаточно прав для этого действия."));await action();});};menu.Items.Add(item);
    }
    private async Task<Device> DashboardDevice(int id){await using var db=factory.Create();return await db.Devices.AsNoTracking().SingleAsync(d=>d.Id==id);}
    private async Task<Payment> DashboardPayment(int id){await using var db=factory.Create();return await db.Payments.AsNoTracking().SingleAsync(p=>p.Id==id);}
    private async Task<SqlBackupJob> DashboardJob(int id){await using var db=factory.Create();return await db.Jobs.AsNoTracking().SingleAsync(j=>j.Id==id);}
    private ContextMenu DeviceDashboardMenu(Device device)
    {
        var menu=new ContextMenu();
        DashboardAction(menu,"Карточка",async()=>await ShowDeviceCard(await DashboardDevice(device.Id)),("devices",1));
        DashboardAction(menu,"Агент",async()=>await ShowDeviceCard(await DashboardDevice(device.Id),"Агент"),("devices",1));
        DashboardAction(menu,"Проблемы",async()=>await ShowDeviceCard(await DashboardDevice(device.Id),"Проблемы"),("devices",1),("reports",1));
        DashboardAction(menu,"Сервисы",async()=>await ShowDeviceCard(await DashboardDevice(device.Id),"Сервисы"),("devices",1),("services",1));
        DashboardAction(menu,"Задания",async()=>await ShowDeviceCard(await DashboardDevice(device.Id),"Задания"),("devices",1),("jobs",1));
        DashboardAction(menu,"Пароли",async()=>await ShowDeviceCard(await DashboardDevice(device.Id),"Пароли · двойной щелчок для просмотра"),("devices",1),("vault",1));
        DashboardAction(menu,"История отчётов",()=>ShowHistory(device.Id,null),("devices",1),("reports",1));
        DashboardConnections(menu,device);
        menu.Items.Add(new Separator());
        DashboardAction(menu,"Изменить",async()=>await EditDevice(await DashboardDevice(device.Id)),("devices",2));
        DashboardAction(menu,"Архивировать / вернуть",async()=>{var latest=await DashboardDevice(device.Id);latest.Enabled=!latest.Enabled;await SaveEntity(latest);},("devices",2));
        DashboardAction(menu,"Разобрать письма",()=>ReanalyzeDevice(device.Id),("devices",2),("reports",2));
        DashboardAction(menu,"Удалить",async()=>{var latest=await DashboardDevice(device.Id);await DeleteCatalogEntity(latest,latest.Name);},("devices",2));
        return menu;
    }
    private ContextMenu BackupDashboardMenu(SqlBackupJob job)
    {
        var menu=new ContextMenu();
        DashboardAction(menu,"История задания",()=>ShowHistory(null,job.Id),("jobs",1),("reports",1));
        if(job.DeviceId is int id)DashboardAction(menu,"Связанное устройство",async()=>await ShowDeviceCard(await DashboardDevice(id)),("devices",1));
        menu.Items.Add(new Separator());
        DashboardAction(menu,"Изменить",async()=>await EditJob(await DashboardJob(job.Id)),("jobs",2));
        DashboardAction(menu,"Разобрать письма",()=>ReanalyzeJob(job.Id),("jobs",2),("reports",2));
        DashboardAction(menu,"Удалить",async()=>{var latest=await DashboardJob(job.Id);await DeleteCatalogEntity(latest,latest.Title);},("jobs",2));
        return menu;
    }
    private ContextMenu PaymentDashboardMenu(Payment payment,bool hasDocument)
    {
        var menu=new ContextMenu();
        DashboardAction(menu,"Карточка / история",async()=>await ShowPaymentCard(await DashboardPayment(payment.Id)),("payments",1));
        if(hasDocument)DashboardAction(menu,"Скачать PDF",()=>DownloadInvoice(payment.Id),("payments",1));
        menu.Items.Add(new Separator());
        DashboardAction(menu,"Оплачено",async()=>{var latest=await DashboardPayment(payment.Id);if(!latest.Paid||latest.Repeat!=RepeatPeriod.None)await PayCurrentPeriod(latest);},("payments",2));
        DashboardAction(menu,"Изменить",async()=>await EditPayment(await DashboardPayment(payment.Id)),("payments",2));
        DashboardAction(menu,"Удалить",async()=>{var latest=await DashboardPayment(payment.Id);await DeleteCatalogEntity(latest,latest.Title);},("payments",2));
        return menu;
    }
    private ContextMenu RouterDashboardMenu(RouterMonitorRow router)
    {
        var menu=new ContextMenu();
        DashboardAction(menu,"Открыть состояние и события",()=>ShowNetworkCard(router),("routers",1));
        if(router.Kind=="MikroTik")DashboardAction(menu,"Анализ конфигурации",()=>ShowRouterAudit(router),("routers",1));
        if(router.Snapshot!=null)DashboardAction(menu,"Интерфейсы",()=>{ShowRouterDetails(router);return Task.CompletedTask;},("routers",1));
        DashboardAction(menu,"Проверить MikroTik сейчас",async()=>{await remote.CommandAsync<RouterMonitorRow[]>("router-monitor-refresh",new{},lifetime.Token);await remote.RefreshAsync(lifetime.Token);await ShowPage(page);},("routers",1));
        if(router.Id is int id){
            // Metadata comes from the same server-filtered snapshot as this dashboard.
            DashboardAction(menu,"Web / SSH / RDP",async()=>{var device=await DashboardDevice(id);var panel=DeviceConnectionButtons(device);new DialogWindow{Owner=this,Title=ProductText.Get("Подключения")+" · "+device.Name,Width=700,Height=240,Content=panel,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();},("routers",1));
            menu.Items.Add(new Separator());
            DashboardAction(menu,"Свойства и подключение",async()=>await EditDevice(await DashboardDevice(id)),("routers",2));
            if(router.Kind=="MikroTik")DashboardAction(menu,"Правила выбранного MikroTik",()=>EditNetworkRules(router),("routers",2));
            if(router.Kind=="MikroTik")DashboardAction(menu,"Профиль мониторинга",()=>EditNetworkConnection(router),("routers",2));
            DashboardAction(menu,"Удалить из Assistant",async()=>{var latest=await DashboardDevice(id);await DeleteCatalogEntity(latest,latest.Name);},("routers",2));
        }
        return menu;
    }
    private void DashboardConnections(ContextMenu menu,Device device)
    {
        // Reuse the existing validated launch and credential handling; no passwords in arguments.
        foreach(var button in ((WrapPanel)DeviceConnectionButtons(device)).Children.OfType<Button>()){
            var title=button.Content?.ToString()??"";var item=new MenuItem{Header=ProductText.Get(title),IsEnabled=button.IsEnabled,ToolTip=button.ToolTip};
            item.Click+=(_,e)=>{e.Handled=true;button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};menu.Items.Add(item);
        }
    }
}
