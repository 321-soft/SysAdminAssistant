namespace SysAdminAssistant.ServerSetup;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Contains("--maintenance")){ProductMaintenance.RunAsync().GetAwaiter().GetResult();return;}
        if(args.Contains("--headless-update")){
            try{UpdateEngine.UpdateAsync(new Progress<string>(text=>File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"update.log"),DateTime.UtcNow.ToString("O")+" "+text+Environment.NewLine))).GetAwaiter().GetResult();Environment.ExitCode=0;}
            catch(Exception error){File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"update.log"),error.ToString());Environment.ExitCode=1;}return;
        }
        var language=SysAdminAssistant.Setup.SetupLanguage.Choose();if(language==null)return;
        SysAdminAssistant.Core.ProductText.DefaultLanguage=language;
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => SetupMessageBox.Show(e.Exception.Message, "Установка сервера", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try {
            if(args.Contains("--manage")){Application.Run(new ManagementWindow());return;}
            if(args.Contains("--uninstall")){Application.Run(new UninstallWindow());return;}
            if (!AgreementForm.EnsureAccepted()) return;
            using var service=new WindowsService();
            Application.Run(args.Contains("--update") || service.Exists() ? (Form)new UpdateWindow() : new SetupWindow());
        } catch(Exception e){SetupMessageBox.Show(e.Message,"Установщик",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
}
