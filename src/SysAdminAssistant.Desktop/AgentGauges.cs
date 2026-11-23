using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
internal sealed class AgentGauges:StackPanel
{
    private readonly TextBlock stamp=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};
    private readonly WrapPanel cards=new();private readonly Dictionary<string,GaugeCard> rows=new();private readonly List<(DateTime Time,double Value)> samples=new();
    public AgentGauges(){Children.Add(new TextBlock{Text="Состояние системы",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,8)});Children.Add(stamp);Children.Add(cards);}
    public void Update(AgentView? view){if(view?.Report is not {} r){Visibility=Visibility.Collapsed;return;}Visibility=Visibility.Visible;bool stale=AgentGaugeModel.Stale(r,view.LastSeenUtc,view.Policy,DateTime.UtcNow)||view.Revoked;stamp.Text=(stale?"Данные устарели · ":"Данные агента · ")+Display.Stamp(r.CapturedUtc.ToServerTime())+"\nUptime "+TimeSpan.FromSeconds(r.UptimeSeconds).ToString(@"d\ \д\ hh\:mm\:ss")+" · загрузка "+Display.Stamp(r.CapturedUtc.AddSeconds(-r.UptimeSeconds).ToServerTime());stamp.Foreground=stale?DesktopTheme.Brush("#64748B"):DesktopTheme.Brush("#24364D");
        if(r.CpuPercent is double cpu&&(samples.Count==0||r.CapturedUtc>samples[^1].Time)){samples.Add((r.CapturedUtc,cpu));if(samples.Count>60)samples.RemoveAt(0);}
        var keys=new HashSet<string>();
        foreach(var row in AgentGaugeModel.Build(r,view.Policy)){
            keys.Add(row.Key);
            if(!rows.TryGetValue(row.Key,out var card)){card=new();rows.Add(row.Key,card);}
            if(card.Parent==null)cards.Children.Add(card);
            card.Update(row,stale,r.CapturedUtc,view.MachineName,row.Key=="cpu"?samples:null);
        }
        foreach(var key in rows.Keys.Where(k=>!keys.Contains(k)).ToArray()){
            if(rows[key].Parent is Panel panel)panel.Children.Remove(rows[key]);rows.Remove(key);
        }
    }
    private sealed class GaugeCard:Border
    {
        readonly TextBlock label=new(){FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};readonly TextBlock value=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,7,0,8)};readonly ProgressBar bar=new(){Minimum=0,Maximum=100,Height=12};readonly TextBlock percentage=new(){Margin=new(0,5,0,0)};readonly Polyline graph=new(){StrokeThickness=2};readonly Canvas plot=new(){Width=260,Height=38,Margin=new(0,8,0,0),ClipToBounds=true};readonly Button details=new(){Content="Подробности и пороги",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,10,0,0)};string description="";
        public GaugeCard(){Width=310;Margin=new(0,0,12,12);Padding=new(14);CornerRadius=new(10);Background=DesktopTheme.Brush("#FFFFFF");BorderBrush=Brushes.LightGray;BorderThickness=new(1);var stack=new StackPanel();Child=stack;foreach(var item in new UIElement[]{label,value,bar,percentage,plot,details})stack.Children.Add(item);plot.Children.Add(graph);details.Click+=(_,_)=>new FormDialog(Window.GetWindow(this),label.Text,800).Note(description).ViewOnly().ShowDialog();}
        public void Update(AgentGauge row,bool stale,DateTime time,string host,List<(DateTime Time,double Value)>? samples){label.Text=row.Label;value.Text=row.Value;var color=stale||row.Percent==null?Brushes.Gray:row.Severity==Severity.Critical?DesktopTheme.Brush("#B42335"):row.Severity==Severity.Warning?Brushes.DarkOrange:Brushes.SeaGreen;bar.Foreground=color;bar.Visibility=percentage.Visibility=row.Key.StartsWith("network:",StringComparison.Ordinal)&&row.Percent==null?Visibility.Collapsed:Visibility.Visible;bar.Value=Math.Clamp(row.Percent??0,0,100);bar.Opacity=stale?0.4:1;percentage.Text=(stale?"Устаревшее значение · ":"")+(row.Percent is double n?$"{n:0.0}%":"нет подтверждённого процента");description=$"Хост: {host}\nСобрано: {Display.Stamp(time.ToServerTime())}\n"+(stale?"Связь/данные устарели. Это не подтверждение текущего состояния.\n":"")+row.Value+"\n"+row.Detail;plot.Visibility=samples==null?Visibility.Collapsed:Visibility.Visible;graph.Stroke=color;if(samples!=null){graph.Points=new PointCollection(samples.Select((p,i)=>new Point(samples.Count<=1?0:i*260d/(samples.Count-1),38-Math.Clamp(p.Value,0,100)*.38)));plot.ToolTip="CPU: до 60 измерений, полученных пока открыта карточка. Не является суточной историей.";}}
    }
}
