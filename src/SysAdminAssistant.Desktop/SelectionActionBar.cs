using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;
internal static class SelectionActionBar
{
    public static FrameworkElement Create(DataGrid? table,Button[] common,Button[] selected)
    {
        var body=new StackPanel{Margin=new(14,10,10,6)};
        body.Children.Add(new TextBlock{Text=ProductText.Get("ДЕЙСТВИЯ"),FontSize=10,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(96,115,143)),Margin=new(0,0,0,4)});
        static ScrollViewer Row(Button[] buttons,UIElement? prefix=null){var row=new StackPanel{Orientation=Orientation.Horizontal};if(prefix!=null)row.Children.Add(prefix);foreach(var button in buttons)row.Children.Add(button);return new(){Content=row,Height=64,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};}
        if(common.Length>0)body.Children.Add(Row(common));
        if(selected.Length>0){
            var caption=new TextBlock{VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold,Margin=new(0,0,12,0)};
            var region=Row(selected,caption);body.Children.Add(region);
            void Refresh(){var count=table?.SelectedItems.Count??0;caption.Text=ProductText.Get("Выбрано: ")+count;region.Visibility=count>0?Visibility.Visible:Visibility.Hidden;
                // Reserve geometry even when the available action set changes by row/status.
                foreach(var button in selected)button.Visibility=button.IsEnabled?Visibility.Visible:Visibility.Hidden;
            }
            if(table!=null)table.SelectionChanged+=(_,_)=>Refresh();Refresh();
        }
        var border=new Border{Child=body,Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(223,231,241)),BorderThickness=new(1),CornerRadius=new(10),Margin=new(0,0,0,12)};
        if(Application.Current.TryFindResource("SaaThemeFFFFFF")!=null)border.SetResourceReference(Border.BackgroundProperty,"SaaThemeFFFFFF");
        if(Application.Current.TryFindResource("SaaThemeDFE7F1")!=null)border.SetResourceReference(Border.BorderBrushProperty,"SaaThemeDFE7F1");
        return border;
    }
}
