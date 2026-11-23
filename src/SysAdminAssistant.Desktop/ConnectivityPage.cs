using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private Button ConnectivityDetailsButton() {var button=UiButton("LAN / WAN / DNS");button.Click+=async(_,_)=>await Run(ShowConnectivity);return button;}
    private Button ConnectivityButton(string group,string status,bool fresh)
    {
        var button=UiButton(group+" · "+(fresh?ConnectivityProbes.Label(status):"Нет свежих данных"));
        var color=fresh?ConnectivityProbes.Color(status):LampState.Gray;
        button.Foreground=color==LampState.Green?DesktopTheme.Brush("#16A34A"):color==LampState.Amber?DesktopTheme.Brush("#D97706"):color==LampState.Red?DesktopTheme.Brush("#B42335"):DesktopTheme.Brush("#64748B");
        button.ToolTip="Проверки выполняются с сервера · нажмите для списка адресов";
        button.Click+=async(_,_)=>await Run(ShowConnectivity);return button;
    }
    private async Task ShowConnectivity()
    {
        await using var db=factory.Create();var current=await db.Connectivity.AsNoTracking().FirstOrDefaultAsync();
        ConnectivityProbe[] probes=[];
        if(current!=null)try{probes=JsonSerializer.Deserialize<ConnectivityProbe[]>(current.ProbeResultsJson,RemoteData.Json)??[];}catch(JsonException){}
        var table=Table(probes,("Сеть","Group",null),("Адрес","Host",null),("Результат ICMP","Result",null),("Проверено сервером","CheckedAt","dd.MM.yyyy HH:mm:ss"));
        table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=(_,_)=>{if(table.SelectedItem is ConnectivityProbe probe)new FormDialog(this,"Результат проверки",850).Text("details","Факты и рекомендации",AvailabilityIssueAdvice.Probe(probe),true,true).TextHeight("details",350).ViewOnly().ShowDialog();};
        var root=new DockPanel();var note=new TextBlock{Text="Проверки с сервера · снимок "+Display.Stamp(remote.Snapshot.ServerTime)+"\nOK — получен ICMP-ответ. Отсутствие ответа может означать и блокировку ICMP. Жёлтый — отвечает часть адресов, красный — ни один. Обновить снимок можно кнопкой «Обновить данные» в основном окне.",TextWrapping=TextWrapping.Wrap,Margin=new(8)};
        DockPanel.SetDock(note,Dock.Top);root.Children.Add(note);root.Children.Add(table);
        new DialogWindow{Owner=this,Title="Мониторинг · LAN / WAN",Width=850,Height=480,Content=root,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();
    }
}
