using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private readonly Dictionary<int,List<Action<DeviceUpdateStamp>>> liveUpdateTargets=[];private bool updatingIcons;
    private void WatchUpdate(int id,Action<DeviceUpdateStamp> action){if(!liveUpdateTargets.TryGetValue(id,out var list))liveUpdateTargets[id]=list=[];list.Add(action);}
    private async Task RefreshUpdateIcons()
    {
        if(updatingIcons||busy||OwnedWindows.Count>0||!connected||!CanRead("devices")||page is not(0 or 1))return;updatingIcons=true;
        try{var rows=await remote.GetAsync<DeviceUpdateStamp[]>("api/device-update-status",lifetime.Token);foreach(var row in rows)if(liveUpdateTargets.TryGetValue(row.DeviceId,out var targets))foreach(var apply in targets.ToArray())apply(row);}
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}catch(Exception e)when(e is System.Net.Http.HttpRequestException or InvalidOperationException or OperationCanceledException){}finally{updatingIcons=false;}
    }
    private FrameworkElement LiveDashboardUpdate(Device device)
    {
        var icon=new TextBlock{Text="↻",FontSize=18,Padding=new Thickness(2),Margin=new Thickness(0,3,0,0),Cursor=System.Windows.Input.Cursors.Hand,Focusable=true,Visibility=Visibility.Collapsed};
        void Apply(DeviceUpdateStamp value){device.OsUpdatesJson=value.OsUpdatesJson;device.MailUpdatesJson=value.MailUpdatesJson;var u=MailUpdates.Overview(value.OsUpdatesJson,value.MailUpdatesJson);icon.Visibility=u?.HasUpdates==true?Visibility.Visible:Visibility.Collapsed;icon.ToolTip=u==null?"":UpdateDetails(u);icon.BeginAnimation(UIElement.OpacityProperty,null);if(u?.HasUpdates==true&&SystemParameters.ClientAreaAnimation)icon.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.5,1,TimeSpan.FromSeconds(1.5)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});}
        Apply(new(device.Id,device.OsUpdatesJson,device.MailUpdatesJson));WatchUpdate(device.Id,Apply);icon.MouseLeftButtonUp+=async(_,e)=>{e.Handled=true;await Run(()=>ShowDeviceUpdates(device.Id,OsUpdateStatus.Parse(device.OsUpdatesJson),this,AccessRules.Level(remote.CurrentUser,"devices")>=2));};icon.Unloaded+=(_,_)=>icon.BeginAnimation(UIElement.OpacityProperty,null);return icon;
    }
}
