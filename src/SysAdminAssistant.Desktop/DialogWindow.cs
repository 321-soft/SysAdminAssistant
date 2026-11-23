using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace SysAdminAssistant.Desktop;

// Shared shell for application dialogs, including those opened from another dialog.
// It does not replace native file pickers, printing or Windows confirmation dialogs.
public class DialogWindow : Window
{
    public bool HasOwnHeading { get; set; }
    private bool decorated;
    public DialogWindow()
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Icon=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/"+typeof(DialogWindow).Assembly.GetName().Name+";component/Assets/SysAdminAssistant.ico"));
        Loaded += (_, _) => ApplyLayout();
    }
    // Keep auto-sized dialogs stable once the initial layout has been shown.
    protected override void OnContentRendered(EventArgs e)
    {
        if(SizeToContent==SizeToContent.Height&&ActualHeight>0)
        {
            var height=ActualHeight;
            SizeToContent=SizeToContent.Manual;
            Height=height;
        }
        base.OnContentRendered(e);
    }

    internal static ScrollViewer StatusViewport(TextBlock status,double height=60) => new()
    {
        Content=status, Height=height, VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled
    };

    private void ApplyLayout()
    {
        if(decorated)return;decorated=true;
        var area=SystemParameters.WorkArea;
        MaxWidth=Math.Min(MaxWidth,Math.Max(240,area.Width-32));
        MaxHeight=Math.Min(MaxHeight,Math.Max(240,area.Height-32));
        MinWidth=Math.Min(MinWidth,MaxWidth);MinHeight=Math.Min(MinHeight,MaxHeight);
        if(!double.IsNaN(Width))Width=Math.Min(Width,MaxWidth);
        if(!double.IsNaN(Height))Height=Math.Min(Height,MaxHeight);
        if(Content is not UIElement original)return;
        Content=null;
        var layout=new DockPanel{Margin=new Thickness(12)};
        if(!HasOwnHeading){
            var header=new DockPanel{Margin=new Thickness(2,0,2,14)};
            var logo=new Image{Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/"+typeof(DialogWindow).Assembly.GetName().Name+";component/Assets/SaaLogo.png")),Width=38,Height=38,Margin=new Thickness(0,0,12,0),Stretch=Stretch.Uniform};
            DockPanel.SetDock(logo,Dock.Left);header.Children.Add(logo);
            var words=new StackPanel();var title=new TextBlock{FontSize=20,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};title.SetBinding(TextBlock.TextProperty,new Binding(nameof(Title)){Source=this});words.Children.Add(title);words.Children.Add(new TextBlock{Text="SysAdmin Assistant",FontSize=11,Foreground=(TryFindResource("SaaTheme60738F") as Brush??new SolidColorBrush(Color.FromRgb(96,115,143))),Margin=new Thickness(0,3,0,0)});header.Children.Add(words);DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
        }
        UIElement body=original;
        // Stack-based short dialogs need a bounded viewport on small displays.
        // Readers and forms already own their list/field scrolling and remain unwrapped.
        if(original is StackPanel)body=new ScrollViewer{Content=original,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        layout.Children.Add(new Border{Child=body,Background=(TryFindResource("SaaThemeFFFFFF") as Brush??Brushes.White),BorderBrush=(TryFindResource("SaaThemeDFE7F1") as Brush??new SolidColorBrush(Color.FromRgb(223,231,241))),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(HasOwnHeading?0:12)});
        Content=layout;
    }
}
