using System.Diagnostics;
using System.Runtime.InteropServices;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Uninstall;
internal sealed class WorkstationSetupWindow:Form
{
    private readonly bool en;private string T(string ru,string english)=>en?english:ru;private bool busy;
    public WorkstationSetupWindow(string[] args,bool english)
    {
        en=english;Text=T("Установка агента рабочей станции","Workstation agent setup");ClientSize=new(760,630);MinimumSize=new(650,500);StartPosition=FormStartPosition.CenterScreen;Font=new("Segoe UI",10);Padding=new(20);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};root.RowStyles.Add(new(SizeType.AutoSize));root.RowStyles.Add(new(SizeType.AutoSize));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.AutoSize));root.RowStyles.Add(new(SizeType.AutoSize));
        root.Controls.Add(new Label{Text=T("Общий агент для Windows-компьютеров","Shared agent for Windows computers"),Font=new("Segoe UI",18),AutoSize=true},0,0);
        var row=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};var path=new TextBox{Width=530};var browse=new Button{Text=T("Выбрать MSI…","Select MSI…"),AutoSize=true};row.Controls.AddRange([path,browse]);root.Controls.Add(row,0,1);
        var agreement=new TextBox{Multiline=true,ReadOnly=true,WordWrap=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Text=LicenseAgreement.Text(en?"en":"ru")};root.Controls.Add(agreement,0,2);
        var accepted=new CheckBox{Text=T("Я принимаю условия лицензионного соглашения","I accept the license agreement"),AutoSize=true};root.Controls.Add(accepted,0,3);
        var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};var install=new Button{Text=T("Установить / обновить","Install / update"),AutoSize=true};var close=new Button{Text=T("Закрыть","Close"),AutoSize=true};var status=new Label{AutoSize=true,MaximumSize=new(650,0)};actions.Controls.AddRange([install,close,status]);root.Controls.Add(actions,0,4);Controls.Add(root);
        if(ProductSetupBundle.HasPayload(Environment.ProcessPath!)){
            var extracted=Path.Combine(AppContext.BaseDirectory,"workstation-payload");
            ProductSetupBundle.Extract(Environment.ProcessPath!,extracted,(_,_)=>{});
            path.Text=Path.Combine(extracted,WorkstationInstallerBundle.PackageName);path.ReadOnly=true;browse.Visible=false;
        }else{
            var index=Array.IndexOf(args,"--source-dir");if(index>=0&&index+1<args.Length&&Directory.Exists(args[index+1])){var files=Directory.GetFiles(args[index+1],"*.msi");if(files.Length==1)path.Text=files[0];}
        }
        browse.Click+=(_,_)=>{using var file=new OpenFileDialog{Filter="Windows Installer (*.msi)|*.msi"};if(file.ShowDialog(this)==DialogResult.OK)path.Text=file.FileName;};close.Click+=(_,_)=>Close();
        install.Click+=async(_,_)=>{
            if(!accepted.Checked){MessageBox.Show(this,T("Примите условия соглашения.","Accept the license agreement."));return;}
            try{
                Verify(path.Text);busy=true;install.Enabled=browse.Enabled=path.Enabled=accepted.Enabled=false;UseWaitCursor=true;status.Text=T("Установка…","Installing…");var package=Path.GetFullPath(path.Text);
                var result=await Task.Run(()=>{var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe")){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in new[]{"/i",package,"/qn","/norestart"})start.ArgumentList.Add(arg);using var process=Process.Start(start)??throw new IOException("Windows Installer did not start.");process.WaitForExit();return process.ExitCode;});
                if(result is not (0 or 3010))throw new IOException(T("Windows Installer завершился с кодом: ","Windows Installer returned code: ")+result);
                status.Text=T("Агент установлен. Подтвердите компьютер в Assistant.","Agent installed. Approve this computer in Assistant.")+(result==3010?T(" Требуется перезагрузка."," Restart required."):"");
            }catch(Exception e){status.Text=T("Установка не завершена.","Installation did not complete.");MessageBox.Show(this,e.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}
            finally{busy=false;install.Enabled=browse.Enabled=path.Enabled=accepted.Enabled=true;UseWaitCursor=false;}
        };FormClosing+=(_,e)=>e.Cancel=busy;
    }
    private void Verify(string path)
    {
        if(!File.Exists(path)||!path.EndsWith(".msi",StringComparison.OrdinalIgnoreCase))throw new IOException(T("Выберите MSI, созданный в Assistant.","Select an MSI created in Assistant."));
        object? installer=null,database=null,view=null,record=null;
        try{
            dynamic engine=installer=Activator.CreateInstance(Type.GetTypeFromProgID("WindowsInstaller.Installer")!)!;
            dynamic db=database=engine.OpenDatabase(Path.GetFullPath(path),0);dynamic query=view=db.OpenView("SELECT `Value` FROM `Property` WHERE `Property`='UpgradeCode'");query.Execute();dynamic? row=record=query.Fetch();
            if(row==null||!string.Equals((string)row!.StringData[1],"{CA2D822A-C915-4374-A05F-39AADE147BD4}",StringComparison.OrdinalIgnoreCase))throw new IOException(T("Это не MSI агента рабочих станций Assistant.","This is not an Assistant workstation agent MSI."));
        }finally{foreach(var item in new[]{record,view,database,installer})if(item!=null&&Marshal.IsComObject(item))Marshal.ReleaseComObject(item);}
    }
}
