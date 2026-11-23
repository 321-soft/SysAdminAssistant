using System.Diagnostics;
namespace SysAdminAssistant.ServerSetup;
internal sealed class UninstallWindow : Form
{
    public UninstallWindow()
    {
        Shown += (_,_) => SetupTheme.Apply(this);
        Text="Удаление сервера и баз";ClientSize=new(650,200);StartPosition=FormStartPosition.CenterParent;
        var text=new Label{Text="Откройте мастер удаления. Выберите отдельно, следует ли удалить базу и локальные данные.",Dock=DockStyle.Top,Height=95,Padding=new(12)};
        var open=new Button{Text="Открыть мастер удаления…",AutoSize=true,Location=new(15,115)};
        open.Click+=async(_,_)=>{
            var installed=await UpdateEngine.DetectAsync();
            var exe=Path.Combine(installed.Target,"Uninstall.exe");
            if(!File.Exists(exe))throw new FileNotFoundException("Uninstaller package is missing.",exe);
            Process.Start(new ProcessStartInfo(exe){UseShellExecute=true,Arguments="--component server"});Application.Exit();
        };
        Controls.Add(text);Controls.Add(open);
    }
}
