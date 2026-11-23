using System.Globalization;
using System.Windows;
using System.Windows.Markup;
namespace SysAdminAssistant.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if(e.Args.Contains("--preflight")){_ = SysAdminAssistant.Core.LicenseAgreement.Hash;Shutdown(0);return;}
        DesktopTheme.Initialize();
        LanguageUi.Initialize();TableSorting.Initialize();HelpWindow.Register();
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!AgreementWindow.EnsureAccepted()) { Shutdown(); return; }
        var connection = new ConnectionWindow();
        if (connection.ShowDialog() != true || connection.Repository == null) { Shutdown(); return; }
        MainWindow = new MainWindow(connection.Repository); ShutdownMode = ShutdownMode.OnMainWindowClose; MainWindow.Show();
    }
    public App()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("ru-RU")));
        AppDomain.CurrentDomain.UnhandledException+=(_,e)=>{if(e.ExceptionObject is Exception error)RecordClientFault("fatal",error);};
        DispatcherUnhandledException += (_, e) => { RecordClientFault("dispatcher",e.Exception); AppMessageBox.Show(e.Exception.Message, "SysAdmin Assistant", MessageBoxButton.OK, MessageBoxImage.Error); e.Handled = true; };
    }
    internal static void RecordClientFault(string kind,Exception error)
    {
        try{
            var directory=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SysAdminAssistantClient");
            System.IO.Directory.CreateDirectory(directory);
            var path=System.IO.Path.Combine(directory,"client-crash.log");
            if(System.IO.File.Exists(path)&&new System.IO.FileInfo(path).Length>512_000)System.IO.File.Move(path,path+".old",true);
            var frames=new System.Diagnostics.StackTrace(error,false);
            System.IO.File.AppendAllText(path,$"{DateTimeOffset.Now:O} {kind} v{typeof(App).Assembly.GetName().Version} {error.GetType().FullName} 0x{error.HResult:X8}\n{frames}\n");
        }catch(Exception){}
    }
}
