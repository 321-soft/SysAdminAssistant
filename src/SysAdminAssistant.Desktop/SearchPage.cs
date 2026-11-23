using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private sealed record SearchResult(string Section,string Name,string Detail,int Page,Device? Device=null);
    private async Task ShowGlobalSearch()
    {
        await using var db=factory.Create();
        var results=new List<SearchResult>();
        if(CanRead("devices"))results.AddRange((await db.Devices.AsNoTracking().ToArrayAsync()).Select(x=>new SearchResult("Устройство",x.Name,x.Host+" · "+x.Site,1,x)));
        if(CanRead("services"))results.AddRange((await db.Services.AsNoTracking().ToArrayAsync()).Select(x=>new SearchResult("Сервис",x.Name,x.Address,2)));
        if(CanRead("payments"))results.AddRange((await db.Payments.AsNoTracking().ToArrayAsync()).Select(x=>new SearchResult("Счёт",x.Title,Display.Date(x.DueDate),3)));
        if(CanRead("jobs"))results.AddRange((await db.Jobs.AsNoTracking().ToArrayAsync()).Select(x=>new SearchResult("Задание",x.Title,x.Host,5)));
        if(CanRead("inventory")&&remote.InventoryOpen&&inventoryUntil>DateTime.UtcNow&&!remote.RequiresSignIn)results.AddRange((await db.Assets.AsNoTracking().ToArrayAsync()).Select(x=>new SearchResult("Имущество",x.InventoryNumber,x.Category+" · "+x.SerialNumber,9)));
        if(CanRead("vault"))results.AddRange((await remote.GetAsync<VaultItem[]>("api/personal/vault",lifetime.Token)).Select(x=>new SearchResult("Пароль",x.Name,x.OwnerLogin,7)));
        var table=Table(results.OrderBy(x=>x.Section).ThenBy(x=>x.Name),("Раздел","Section",null),("Название","Name",null),("Адрес / сведения","Detail",null));
        var window=new DialogWindow{Owner=this,Title="Поиск · только доступные разделы",Width=950,Height=600,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var open=UiButton("Открыть");open.IsEnabled=false;SearchResult? selected=null;
        table.SelectionChanged+=(_,_)=>open.IsEnabled=table.SelectedItem is SearchResult;
        void Select() {if(table.SelectedItem is SearchResult row){selected=row;window.Close();}}
        open.Click+=(_,_)=>Select();table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=(_,_)=>Select();
        window.Content=ListSurface(table,[open],search:true);window.ShowDialog();
        if(selected?.Device is Device device)await ShowDeviceCard(device);
        else if(selected!=null) {
            await ShowPage(selected.Page);
            var search=Children<TextBox>(PageHost).FirstOrDefault(t=>Equals(t.Tag,"ui-search-list"));
            if(search!=null)search.Text=selected.Name;
        }
    }
}
