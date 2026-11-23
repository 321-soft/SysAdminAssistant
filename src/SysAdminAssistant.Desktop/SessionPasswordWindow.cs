using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;

public sealed class SessionPasswordWindow : DialogWindow
{
    public SessionPasswordWindow(RemoteRepository remote, CancellationToken token)
    {
        HasOwnHeading=true;Title="Повторный вход";Width=470;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new(24)};
        panel.Children.Add(new TextBlock{Text="Сеанс завершён",FontSize=23,Margin=new(0,0,0,12)});
        panel.Children.Add(new TextBlock{Text=remote.CurrentUser.Login+" · "+remote.Address+"\nВведите пароль, чтобы продолжить работу в открытой форме.",TextWrapping=TextWrapping.Wrap});
        var password=new PasswordBox{Margin=new(0,16,0,8)};panel.Children.Add(password);
        var remember=new CheckBox{Content="Запомнить вход до закрытия приложения",IsChecked=remote.RememberSession,Margin=new(0,6,0,8)};panel.Children.Add(remember);
        var error=new TextBlock{Foreground=DesktopTheme.Brush("#B42335"),TextWrapping=TextWrapping.Wrap};panel.Children.Add(StatusViewport(error));
        var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,12,0,0)};
        var cancel=new Button{Content="Позже",IsCancel=true};var login=new Button{Content="Войти",IsDefault=true,Style=(Style)FindResource("PrimaryButton")};buttons.Children.Add(cancel);buttons.Children.Add(login);panel.Children.Add(buttons);Content=panel;
        var closing=new CancellationTokenSource();var linked=CancellationTokenSource.CreateLinkedTokenSource(token,closing.Token);
        login.Click+=async(_,_)=>{
            login.IsEnabled=false;password.IsEnabled=false;error.Text="Подключение…";
            try{remote.RememberSession=remember.IsChecked==true;await remote.ReauthenticateAsync(remote.CurrentUser.Login,password.Password,linked.Token);password.Clear();if(!linked.IsCancellationRequested)DialogResult=true;}
            catch(OperationCanceledException)when(linked.IsCancellationRequested){}
            catch(Exception e){error.Text=e.Message;password.Clear();}
            finally{login.IsEnabled=true;password.IsEnabled=true;password.Focus();}
        };
        Loaded+=(_,_)=>password.Focus();
        Closed+=(_,_)=>{closing.Cancel();password.Clear();linked.Dispose();closing.Dispose();};
    }
}
