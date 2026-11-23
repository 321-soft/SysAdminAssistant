using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

internal sealed class ProductUpdateWindow:DialogWindow
{
    private sealed record UpdateStatus(string Stage,string Message,int Percent,DateTime TimeUtc);
    private readonly RemoteRepository remote;
    private readonly CancellationTokenSource lifetime=new();
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
    private readonly ProgressBar progress=new(){Minimum=0,Maximum=100,Height=14};
    private readonly Button check=new(){Content="Проверить обновления",Margin=new Thickness(4)};
    private readonly Button client=new(){Content="Обновить клиент",Margin=new Thickness(4),IsEnabled=false};
    private readonly Button server=new(){Content="Обновить сервер",Margin=new Thickness(4),IsEnabled=false};
    private string? clientManifest;
    private static string ClientHelper=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant Client Maintenance","versions",typeof(MainWindow).Assembly.GetName().Version!.ToString(),"SysAdminAssistant.ProductMaintenance.exe");
    private static bool ClientMaintenanceInstalled=>File.Exists(ClientHelper)&&string.Equals(Environment.ProcessPath,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant","SysAdminAssistant.exe"),StringComparison.OrdinalIgnoreCase);
    private ProductRelease? release;private bool busy,clientAvailable,serverAvailable;
    public ProductUpdateWindow(RemoteRepository repository,bool checkOnOpen=false)
    {
        remote=repository;Title="Обновления SysAdmin Assistant";Width=780;Height=470;MinWidth=450;MinHeight=340;
        var layout=new DockPanel();var intro=new TextBlock{Text="Сервер и клиент обновляются только по вашей команде. Обновление сервера включает резервное копирование и перезапуск. Закрытие этого окна не отменяет уже начатое обслуживание сервера.",TextWrapping=TextWrapping.Wrap};DockPanel.SetDock(intro,Dock.Top);layout.Children.Add(intro);
        var buttons=new WrapPanel();buttons.Children.Add(check);buttons.Children.Add(client);if(remote.CurrentUser.Administrator)buttons.Children.Add(server);var close=new Button{Content="Закрыть окно",Margin=new Thickness(4),IsCancel=true};close.Click+=(_,_)=>Close();buttons.Children.Add(close);DockPanel.SetDock(buttons,Dock.Bottom);layout.Children.Add(buttons);DockPanel.SetDock(progress,Dock.Bottom);layout.Children.Add(progress);layout.Children.Add(new ScrollViewer{Content=status,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=layout;
        status.Text="Клиент: "+typeof(MainWindow).Assembly.GetName().Version+"\nАдрес обновлений: "+remote.Settings.ProductUpdates.Primary;
        check.Click+=async(_,_)=>await Run(CheckAsync);client.Click+=async(_,_)=>await Run(ClientAsync);server.Click+=async(_,_)=>await Run(ServerAsync);Closed+=(_,_)=>lifetime.Cancel();
        if(checkOnOpen)Loaded+=async(_,_)=>await Run(CheckAsync);
    }
    private async Task Run(Func<Task> operation)
    {
        if(busy)return;busy=true;check.IsEnabled=client.IsEnabled=server.IsEnabled=false;
        try{await operation();}catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception error){if(!lifetime.IsCancellationRequested){status.Text=error.Message;AppMessageBox.Show(this,error.Message,Title);}}
        finally{busy=false;check.IsEnabled=true;client.IsEnabled=clientAvailable;server.IsEnabled=serverAvailable;}
    }
    private async Task CheckAsync()
    {
        progress.IsIndeterminate=true;clientAvailable=serverAvailable=false;release=null;clientManifest=null;
        try{
            var sources=remote.Settings.ProductUpdates;var json=await ProductReleases.ManifestAsync(sources.Primary,sources.Mirror,lifetime.Token);release=ProductReleases.Verify(json,ProductReleases.PublicKey,DateTime.UtcNow);clientManifest=json;
            const string licenseNotice="Для этого выпуска нужно принять новую редакцию лицензии. Запустите Setup.exe.";
            static bool SameLicense(ProductPackage p)=>p.EulaSha256.Equals(LicenseAgreement.Hash,StringComparison.OrdinalIgnoreCase);
            var installedClient=typeof(MainWindow).Assembly.GetName().Version!;
            var newerClient=Version.Parse(release.Client.Version)>installedClient;
            clientAvailable=ClientMaintenanceInstalled&&newerClient&&SameLicense(release.Client);
            status.Text="Клиент: "+installedClient+" → "+release.Client.Version;
            if(newerClient&&!SameLicense(release.Client))status.Text+="\n"+licenseNotice;
            if(remote.CurrentUser.Administrator){var result=await remote.GetAsync<RemoteRepository.ProductUpdateCheck>("api/product-updates/check",lifetime.Token);var verified=ProductReleases.Verify(result.Manifest,ProductReleases.PublicKey,DateTime.UtcNow);status.Text+="\nСервер: "+result.ServerVersion+" → "+verified.Server.Version;var newerServer=Version.Parse(verified.Server.Version)>Version.Parse(result.ServerVersion);serverAvailable=result.MaintenanceInstalled&&newerServer&&SameLicense(verified.Server);if(newerServer&&!SameLicense(verified.Server))status.Text+="\n"+licenseNotice;if(!result.MaintenanceInstalled)status.Text+="\nДля первого включения онлайн-обновлений установите сервер из нового Setup.exe.";}
            if(!ClientMaintenanceInstalled)status.Text+="\nДля онлайн-обновления клиента установите его один раз через Setup.exe.";
            status.Text+="\nПодпись выпуска проверена. Доступен до: "+release.ExpiresUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        }finally{progress.IsIndeterminate=false;}
    }
    private async Task ClientAsync()
    {
        if(release==null)return;ProductReleases.RequireUpgrade(release.Client,typeof(MainWindow).Assembly.GetName().Version!,"client");
        if(AppMessageBox.Show(this,"Клиент закроется после загрузки и проверки установщика. До этого можно продолжить работу. Продолжить?",Title,MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        if(!ClientMaintenanceInstalled||clientManifest==null)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Установите клиент через Setup.exe для включения защищённого обновления."));
        var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SysAdminAssistantClient","updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var request=Path.Combine(folder,"request.json");
        await File.WriteAllTextAsync(request,clientManifest,lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();var start=new ProcessStartInfo(ClientHelper){UseShellExecute=true,Verb="runas",WorkingDirectory=Path.GetDirectoryName(ClientHelper)!};
        start.ArgumentList.Add("--client-update");start.ArgumentList.Add(request);start.ArgumentList.Add(Environment.ProcessId.ToString());start.ArgumentList.Add(ProductText.Language);
        using var updater=Process.Start(start)??throw new IOException("Cannot start the client update helper.");
        Close();
    }
    private async Task ServerAsync()
    {
        if(!remote.CurrentUser.Administrator)throw new UnauthorizedAccessException();
        if(AppMessageBox.Show(this,"Обновить сервер с резервным копированием и перезапуском службы?",Title,MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        await remote.PostAsync<object,bool>("api/product-updates/server",new{},lifetime.Token);var deadline=DateTime.UtcNow.AddMinutes(28);serverAvailable=false;
        status.Text="Обновление разрешено. Ожидание механизма обслуживания…";
        while(DateTime.UtcNow<deadline){
            await Task.Delay(TimeSpan.FromSeconds(5),lifetime.Token);
            try{var result=await remote.GetAsync<UpdateStatus>("api/product-updates/status",lifetime.Token);status.Text=result.Stage+"\n"+result.Message;progress.Value=result.Percent;if(result.Stage=="completed"){await remote.RefreshAsync(lifetime.Token);return;}if(result.Stage=="failed")throw new InvalidOperationException(result.Message);}
            catch(HttpRequestException){status.Text="Сервер перезапускается. Ожидание восстановления соединения…";}
            catch(TaskCanceledException)when(!lifetime.IsCancellationRequested){status.Text="Ожидание ответа сервера…";}
        }
        throw new TimeoutException(global::SysAdminAssistant.Core.ProductText.Get("Срок ожидания истёк. Проверьте службу и журнал обслуживания; не запускайте второе обновление до проверки текущего."));
    }
}
