using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<int,(Button Button,Ellipse Dot)> navigationDots=[];
    private bool navigationHealthPending;

    private void StartNavigationHealth()
    {
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(20)};
        timer.Tick+=async(_,_)=>await UpdateNavigationHealth();
        Closed+=(_,_)=>timer.Stop();
        timer.Start();
        _=UpdateNavigationHealth();
    }

    private async Task UpdateNavigationHealth()
    {
        if(navigationHealthPending||lifetime.IsCancellationRequested||remote.RequiresSignIn)return;
        navigationHealthPending=true;
        try{
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var states=await remote.NavigationHealthAsync(timeout.Token);
            if(lifetime.IsCancellationRequested)return;
            foreach(var (page,(button,dot)) in navigationDots){
                var state=states.GetValueOrDefault(page,LampState.Gray);
                var caption=state switch {
                    LampState.Red=>ProductText.Get("Есть критические проблемы"),
                    LampState.Amber=>ProductText.Get("Есть предупреждения"),
                    _=>""
                };
                dot.Visibility=caption.Length>0?Visibility.Visible:Visibility.Hidden;
                dot.Fill=state==LampState.Amber?Brush("#CC7000"):Brush("#DC2626");
                button.ToolTip=ProductText.Get(Subtitles[page])+(caption.Length>0?" · "+caption:"");
                AutomationProperties.SetHelpText(button,caption);
            }
        }
        catch(Exception error)when(error is HttpRequestException or OperationCanceledException or InvalidOperationException or System.IO.IOException or System.Text.Json.JsonException){
            // Preserve the last known state without interrupting the current page.
        }
        finally{navigationHealthPending=false;}
    }
}
