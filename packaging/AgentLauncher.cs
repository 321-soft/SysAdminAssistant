using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ComponentModel;
using Microsoft.Win32;
using SysAdminAssistant.Core;
[assembly:System.Reflection.AssemblyVersion("0.46.14.0")]
internal static class AgentLauncher
{
 static bool english;
 static string T(string ru,string en){return english?en:ru;}
 [STAThread]static void Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
 var configured=Environment.GetEnvironmentVariable("SAA_LANGUAGE");
 if(configured!="ru"&&configured!="en"){
  using(var dialog=new Form{Text="Language / Язык",ClientSize=new Size(390,125),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false}){
   var languages=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Left=20,Top=20,Width=350};languages.Items.AddRange(new object[]{"Русский","English"});languages.SelectedIndex=System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="ru"?0:1;
   var ok=new Button{Text="Продолжить / Continue",DialogResult=DialogResult.OK,Left=180,Top=70,Width=190,Height=32};dialog.Controls.Add(languages);dialog.Controls.Add(ok);dialog.AcceptButton=ok;if(dialog.ShowDialog()!=DialogResult.OK)return;configured=languages.SelectedIndex==0?"ru":"en";
  }
 }
 english=configured=="en";Environment.SetEnvironmentVariable("SAA_LANGUAGE",configured);Application.Run(new Setup());}
 sealed class Setup:Form
 {
  readonly Label label=new Label();readonly ProgressBar meter=new ProgressBar();readonly Button close=new Button();bool running=true;
  public Setup(){Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);Text=T("SysAdmin Assistant · Установка агента","SysAdmin Assistant · Agent setup");ClientSize=new Size(520,180);MinimumSize=Size;MaximumSize=Size;StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(245,248,254);label.SetBounds(24,24,470,60);label.Text=T("Подготовка установщика…","Preparing setup…");meter.SetBounds(24,94,470,14);close.Text=T("Закрыть","Close");close.SetBounds(384,130,110,32);close.Enabled=false;close.Click+=delegate{Close();};Controls.Add(label);Controls.Add(meter);Controls.Add(close);FormClosing+=delegate(object sender,FormClosingEventArgs e){if(running)e.Cancel=true;};Shown+=delegate{Start();};}
  void Stage(string text,int percent){BeginInvoke((Action)delegate{label.Text=text;meter.Value=Math.Max(0,Math.Min(100,percent));});}
  void Start(){var worker=new BackgroundWorker();worker.DoWork+=delegate{Run();};worker.RunWorkerCompleted+=delegate(object sender,RunWorkerCompletedEventArgs e){running=false;close.Enabled=true;if(e.Error!=null){label.Text=T("Установка не завершена: ","Installation did not complete: ")+e.Error.Message;meter.Value=0;MessageBox.Show(this,e.Error.Message,T("Установка не завершена","Installation did not complete"),MessageBoxButtons.OK,MessageBoxIcon.Error);}else{label.Text=T("Работа установщика завершена. Это окно можно закрыть.","Setup completed. You may close this window.");meter.Value=100;}};worker.RunWorkerAsync();}
  static void SafeParents(string path){for(var d=new DirectoryInfo(path);d!=null;d=d.Parent)if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Папка установки содержит ссылку");}
  void Run(){
   string temp=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"SAA-AgentSetup-"+Guid.NewGuid().ToString("N"));
   try{
    SafeParents(temp);var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);foreach(var id in new[]{WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(id,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));Directory.CreateDirectory(temp,acl);SafeParents(temp);
    AgentSetupBundle.Extract(Application.ExecutablePath,temp,delegate(int percent,string file){Stage(T("Распаковка файлов агента…","Extracting agent files…"),percent*65/100);});
    if(!File.Exists(Path.Combine(temp,"bootstrap.json"))||!File.Exists(Path.Combine(temp,"SysAdminAssistant.AgentSetup.exe"))||!File.Exists(Path.Combine(temp,"INSTALL_AGENT.ps1")))throw new InvalidDataException(T("Неполный установочный пакет","Incomplete setup package"));
    Stage(T("Проверка .NET Runtime и запуск установки агента…","Checking .NET Runtime and starting agent setup…"),70);
    var ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
    var start=new ProcessStartInfo(ps,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+Path.Combine(temp,"INSTALL_AGENT.ps1")+"\" -SingleFile");start.UseShellExecute=false;start.CreateNoWindow=true;start.WorkingDirectory=temp;start.EnvironmentVariables["SAA_LANGUAGE"]=english?"en":"ru";
    using(var process=Process.Start(start)){process.WaitForExit();if(process.ExitCode==3010)throw new InvalidOperationException(T(".NET установлен. Перезагрузите Windows и снова запустите этот Setup.exe.",".NET was installed. Restart Windows and run this Setup.exe again."));if(process.ExitCode!=0)throw new InvalidOperationException(T("Код ","Code ")+process.ExitCode+T(". Проверьте журнал установки агента в папке TEMP.",". Check the agent setup log in the TEMP folder."));}
   }finally{if(Directory.Exists(temp)){SafeParents(temp);Directory.Delete(temp,true);}}
  }
 }
}
