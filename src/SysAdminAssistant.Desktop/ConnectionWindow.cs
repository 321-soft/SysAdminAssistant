using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;

public sealed class ClientProfile
{
    public string Address { get; set; } = "https://192.168.1.10:7443/";
    public string Fingerprint { get; set; } = "";
    public bool RememberSession {get;set;}=true;
    public string User { get; set; } = "admin";
    public static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysAdminAssistantClient", "connection.json");
}
public sealed class ConnectionWindow : DialogWindow
{
    private readonly TextBox address = new(), fingerprint = new(), user = new();
    private readonly PasswordBox password = new();
    private readonly CheckBox remember=new(){Content="Запомнить вход до закрытия приложения",Margin=new(0,12,0,0)};
    private readonly TextBlock error = new() { Foreground = DesktopTheme.Brush("#B42335"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) };
    private readonly Button connect = new() { Content = "Подключиться", IsDefault = true };
    private readonly CancellationTokenSource lifetime = new();
    public RemoteRepository? Repository { get; private set; }
    public ConnectionWindow()
    {
        HasOwnHeading=true;
        Title = "SysAdmin Assistant · подключение к серверу"; Width = 650; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var profile = new ClientProfile();
        try { if (File.Exists(ClientProfile.PathName)) profile = JsonSerializer.Deserialize<ClientProfile>(File.ReadAllText(ClientProfile.PathName)) ?? profile; } catch (Exception) { error.Text = "Настройки подключения повреждены. Укажите адрес заново."; }
        address.Text = profile.Address; fingerprint.Text = profile.Fingerprint; user.Text = profile.User;remember.IsChecked=profile.RememberSession;
        var stack = new StackPanel { Margin = new(28) };
        stack.Children.Add(new TextBlock { Text = "Подключение к серверу", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 18) });
        void Field(string label, FrameworkElement control) { stack.Children.Add(new TextBlock { Text = label, Margin = new(0, 8, 0, 4) }); stack.Children.Add(control); }
        Field("Адрес · https://IP:порт/", address); Field("SHA-256 сертификата · скопируйте с сервера", fingerprint); Field("Логин", user); Field("Пароль", password);
        stack.Children.Add(new TextBlock { Text = "Для сертификата доверенного центра поле SHA-256 можно оставить пустым. Самоподписанный сертификат принимается только при точном совпадении отпечатка. Пароль не сохраняется.", TextWrapping = TextWrapping.Wrap, Foreground = DesktopTheme.Brush("#64748B"), Margin = new(0, 14, 0, 0) });
        stack.Children.Add(remember);
        stack.Children.Add(new TextBlock{Text="Короткие обрывы: автоматическое подключение. Час без связи: повторный вход. Сотрудники и пароли открываются отдельно.",TextWrapping=TextWrapping.Wrap,Foreground=DesktopTheme.Brush("#64748B"),Margin=new(0,6,0,8)});
        stack.Children.Add(StatusViewport(error)); var actions = new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right };
        actions.Children.Add(new Button { Content = "Отмена", IsCancel = true }); actions.Children.Add(connect); stack.Children.Add(actions); Content = stack;
        connect.Style=(Style)FindResource("PrimaryButton");
        connect.Click += async (_, _) =>
        {
            error.Text = "Подключение…"; connect.IsEnabled = false; address.IsEnabled = fingerprint.IsEnabled = user.IsEnabled = password.IsEnabled = false;
            RemoteRepository? candidate = null;
            try
            {
                candidate = new RemoteRepository(address.Text.Trim(), fingerprint.Text.Trim()){RememberSession=remember.IsChecked==true}; await candidate.LoginAsync(user.Text.Trim(), password.Password, lifetime.Token);
                var next = new ClientProfile { Address = candidate.Address, Fingerprint = candidate.ServerFingerprint, User = user.Text.Trim(),RememberSession=remember.IsChecked==true };
                Directory.CreateDirectory(Path.GetDirectoryName(ClientProfile.PathName)!); File.WriteAllText(ClientProfile.PathName, JsonSerializer.Serialize(next));
                password.Clear(); Repository = candidate; candidate = null; DialogResult = true;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception e) { error.Text = e.Message; }
            finally { candidate?.Dispose(); connect.IsEnabled = true; address.IsEnabled = fingerprint.IsEnabled = user.IsEnabled = password.IsEnabled = true; }
        };
        Closed += (_, _) => { lifetime.Cancel(); password.Clear(); };
    }
}
