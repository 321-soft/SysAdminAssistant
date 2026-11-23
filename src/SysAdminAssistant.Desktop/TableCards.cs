using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private static readonly DependencyProperty HasCustomDoubleClickProperty=DependencyProperty.RegisterAttached("HasCustomDoubleClick",typeof(bool),typeof(MainWindow),new PropertyMetadata(false));
    private static readonly DependencyProperty SelectionActionProperty=DependencyProperty.RegisterAttached("SelectionAction",typeof(bool),typeof(MainWindow),new PropertyMetadata(false));
    private static readonly DependencyProperty OpenActionProperty=DependencyProperty.RegisterAttached("OpenAction",typeof(Func<Task>),typeof(MainWindow));
    private static void OpenOnDoubleClick(DataGrid table,Func<Task> action)=>table.SetValue(OpenActionProperty,action);
    private static void InstallDoubleClick(DataGrid table)
    {
        table.MouseDoubleClick+=async(_,e)=>{
            if(e.ChangedButton!=MouseButton.Left||ItemsControl.ContainerFromElement(table,e.OriginalSource as DependencyObject) is not DataGridRow||table.SelectedItems.Count!=1)return;
            var owner=Application.Current.MainWindow as MainWindow;if(owner==null)return;
            var action=table.GetValue(OpenActionProperty) as Func<Task>??owner.DefaultCard(table.SelectedItem);
            if(action==null&&table.GetValue(HasCustomDoubleClickProperty) is not true)action=()=>owner.GenericRowCard(table.SelectedItem,Window.GetWindow(table),table);
            if(action==null)return;
            e.Handled=true;await owner.Run(action);
        };
    }
    private Func<Task>? DefaultCard(object? row)=>row switch{
        DeviceRegistryRow d=>()=>ShowDeviceCard(d.Entity),
        Device d=>()=>ShowDeviceCard(d),
        StationRow s=>()=>{ShowStationCard(s);return Task.CompletedTask;},
        AssetRow a=>()=>EditAsset(a.Asset),
        InventoryAsset a=>()=>EditAsset(a),
        EmployeeRow e=>()=>OpenEmployeeCard(e.Person),
        Employee e=>()=>OpenEmployeeCard(e),
        ActRow a=>()=>PreviewInventoryAct(a.Act.Id),
        InventoryAct a=>()=>PreviewInventoryAct(a.Id),
        HistoryRow h=>()=>ShowMail(h.Entity),
        ReportEvent report=>()=>ShowMail(report),
        PaymentRow p=>()=>ShowPaymentCard(p.Entity),
        Payment p=>()=>ShowPaymentCard(p),
        ServiceRow s=>()=>OpenServiceCard(s.Entity),
        ServiceEntry s=>()=>OpenServiceCard(s),
        Infrastructure.BackupRunRow b=>()=>OpenBackupCard(b.Entity),
        SqlBackupJob job=>()=>OpenBackupCard(job),
        _=>null
    };
    private Task GenericRowCard(object? row,Window? owner,DataGrid table)
    {
        if(row==null)return Task.CompletedTask;
        var form=new FormDialog(owner??this,"Карточка записи",750);
        foreach(var column in table.Columns.OfType<DataGridTextColumn>()){
            if(column.Binding is not System.Windows.Data.Binding {Path.Path:{} path})continue;
            if(new[]{"password","secret","token","protected","credential","key","hash"}.Any(part=>path.Contains(part,StringComparison.OrdinalIgnoreCase)))continue;
            object? value=row;
            foreach(var part in path.Split('.')){
                try{value=value?.GetType().GetProperty(part)?.GetValue(value);}catch{value=null;break;}
            }
            var text=value is DateTime time?(time.Kind==DateTimeKind.Utc?Display.Stamp(AppTime.FromUtc(time)):time.ToString("dd.MM.yyyy HH:mm")):value?.ToString()??"—";
            form.Fact(column.Header?.ToString()??path,text.Length>8000?text[..8000]:text);
        }
        form.ViewOnly().ShowDialog();return Task.CompletedTask;
    }
    private Task OpenEmployeeCard(Employee e)
    {
        if(AccessRules.Level(remote.CurrentUser,"inventory")>=2)return EditEmployee(e);
        new FormDialog(this,e.FullName,650).Fact("Табельный номер",e.PersonnelNumber).Fact("Отдел",e.Department).Fact("Должность",e.Position).Fact("Контакт",e.Contact).ViewOnly().ShowDialog();return Task.CompletedTask;
    }
    private Task OpenServiceCard(ServiceEntry s)
    {
        var form=new FormDialog(this,s.Name,650).Fact("Тип",s.Type).Fact("Адрес",s.Address).Fact("Состояние",s.Status).Fact("Примечания",s.Notes);
        if(AccessRules.Level(remote.CurrentUser,"services")>=2)form.ActionAsync("Изменить",()=>EditService(s));
        form.ViewOnly().ShowDialog();return Task.CompletedTask;
    }
    private Task OpenBackupCard(SqlBackupJob job)
    {
        var form=new FormDialog(this,job.Title,700).Fact("Адрес",job.Host).ActionAsync("История отчётов",()=>ShowHistory(null,job.Id));
        if(AccessRules.Level(remote.CurrentUser,"jobs")>=2)form.ActionAsync("Изменить",()=>EditJob(job));
        form.ViewOnly().ShowDialog();return Task.CompletedTask;
    }
}
