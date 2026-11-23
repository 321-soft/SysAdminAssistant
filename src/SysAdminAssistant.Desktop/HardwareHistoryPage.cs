using System.Windows;
using System.Windows.Controls;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private async Task ShowHardwareHistory(string kind,string id,Window? owner=null)
    {
        HardwareChange[] events;
        if(kind=="asset"){
            if(!await UnlockInventory())return;
            events=await remote.CommandAsync<HardwareChange[]>("inventory-hardware-history",new{Id=int.Parse(id),Revision=0},lifetime.Token);
        }else events=await remote.CommandAsync<HardwareChange[]>("hardware-history",new HardwareHistoryQuery(kind,id),lifetime.Token);
        var table=Table(events,("Обнаружено","DetectedUtc","dd.MM.yyyy HH:mm"),("Изменение","Title",null),("Было","Before",null),("Стало","After",null),("Источник","Actor",null));
        var window=new DialogWindow{Owner=owner??this,Title="Изменения оборудования",Width=1100,Height=650,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var open=UiButton("Подробности");
        void Open(){
            if(table.SelectedItem is not HardwareChange change)return;
            new FormDialog(window,change.Title,850).Fact("Обнаружено",Display.Stamp(AppTime.FromUtc(change.DetectedUtc)))
                .Fact("Источник",change.Actor).Text("before","Было",change.Before,true,true).TextHeight("before",200)
                .Text("after","Стало",change.After,true,true).TextHeight("after",200).ViewOnly().ShowDialog();
        }
        open.Click+=(_,_)=>Open();OpenOnDoubleClick(table,()=>{Open();return Task.CompletedTask;});
        window.Content=ListSurface(table,[open],search:true);
        window.ShowDialog();
    }
}
