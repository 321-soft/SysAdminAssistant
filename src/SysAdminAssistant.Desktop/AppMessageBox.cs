using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace SysAdminAssistant.Desktop;

// All in-application notices share the same shell at every nesting depth.
// Windows pickers and early startup failures without Application resources stay native.
public static class AppMessageBox
{
    public static MessageBoxResult Show(string text,string caption="SysAdmin Assistant",MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage image=MessageBoxImage.None,MessageBoxResult defaultResult=MessageBoxResult.None)=>Show(null,text,caption,buttons,image,defaultResult);
    public static MessageBoxResult Show(Window? owner,string text,string caption="SysAdmin Assistant",MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage image=MessageBoxImage.None,MessageBoxResult defaultResult=MessageBoxResult.None)
    {
        text=SysAdminAssistant.Core.ProductText.Get(text);caption=SysAdminAssistant.Core.ProductText.Get(caption);
        var app=Application.Current;
        if(app!=null&&!app.Dispatcher.CheckAccess())return app.Dispatcher.Invoke(()=>Show(owner,text,caption,buttons,image,defaultResult));
        if(app==null||app.TryFindResource("PrimaryButton")==null)return owner==null?System.Windows.MessageBox.Show(text,caption,buttons,image,defaultResult):System.Windows.MessageBox.Show(owner,text,caption,buttons,image,defaultResult);
        var active=app.Windows.Cast<Window>().LastOrDefault(w=>w.IsVisible&&w.IsActive);
        if(owner==null||!owner.IsVisible||!owner.IsEnabled)owner=active;
        var result=buttons is MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel?MessageBoxResult.Cancel:buttons==MessageBoxButton.YesNo?MessageBoxResult.No:MessageBoxResult.OK;
        var dialog=new DialogWindow{Title=caption,Width=640,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.CanResizeWithGrip,WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner,ShowInTaskbar=owner==null};
        if(owner!=null)dialog.Owner=owner;
        var root=new DockPanel{Margin=new Thickness(8)};
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var body=new StackPanel();
        string kind=image==MessageBoxImage.Error?"Ошибка":image==MessageBoxImage.Warning?"Внимание":image==MessageBoxImage.Question?"Подтверждение":"Информация";
        var tone=image==MessageBoxImage.Error?"B42335":image==MessageBoxImage.Warning?"A45A06":"215CD9";
        var label=new TextBlock{Text=kind,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)};
        var toneKey="SaaTheme"+tone;
        if(Application.Current?.TryFindResource(toneKey) is Brush)label.SetResourceReference(TextBlock.ForegroundProperty,toneKey);
        else label.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString("#"+tone));
        body.Children.Add(label);
        body.Children.Add(new TextBox{Text=text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Padding=new Thickness(0),MinHeight=30,MaxHeight=Math.Min(360,SystemParameters.WorkArea.Height*.45),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0)});root.Children.Add(body);
        MessageBoxResult chosenDefault=defaultResult==MessageBoxResult.None?result:defaultResult;
        void Add(string label,MessageBoxResult value,bool cancel=false){var b=new Button{Content=label,MinWidth=100,IsDefault=value==chosenDefault,IsCancel=cancel};if(value==MessageBoxResult.OK||value==MessageBoxResult.Yes)b.Style=(Style)app.FindResource("PrimaryButton");b.Click+=(_,_)=>{result=value;dialog.Close();};actions.Children.Add(b);}
        switch(buttons){
            case MessageBoxButton.OK:Add("Понятно",MessageBoxResult.OK,true);break;
            case MessageBoxButton.OKCancel:Add("Отмена",MessageBoxResult.Cancel,true);Add("Продолжить",MessageBoxResult.OK);break;
            case MessageBoxButton.YesNo:Add("Нет",MessageBoxResult.No,true);Add("Да",MessageBoxResult.Yes);break;
            case MessageBoxButton.YesNoCancel:Add("Отмена",MessageBoxResult.Cancel,true);Add("Нет",MessageBoxResult.No);Add("Да",MessageBoxResult.Yes);break;
            default:throw new ArgumentOutOfRangeException(nameof(buttons));
        }
        dialog.Content=root;dialog.ShowDialog();return result;
    }
}
