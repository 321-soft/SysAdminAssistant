// Compiled with Windows inbox .NET Framework; the bootstrap itself needs no .NET 10 or SDK.
using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using SysAdminAssistant.Core;
internal static class ProductLauncher
{
 [STAThread] static int Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);bool quiet=Array.IndexOf(args,"--verified-server-update")>=0;using(var form=new Setup(args,quiet)){if(quiet){try{return form.Execute();}catch(Exception error){Console.Error.WriteLine(error.ToString());return 1;}}Application.Run(form);return form.Result;}}
 sealed class Setup:Form
 {
  readonly CheckBox startMenu=new CheckBox(),desktop=new CheckBox(),startup=new CheckBox(),installWix=new CheckBox();readonly bool quiet;readonly ComboBox language=new ComboBox();readonly TextBox agreement=new TextBox();readonly CheckBox accepted=new CheckBox();readonly Button proceed=new Button(),saveAgreement=new Button();readonly Label status=new Label();readonly ProgressBar progress=new ProgressBar();bool running,finished,english;int waitPid;public int Result=1;
  string T(string ru,string en){return english?en:ru;}
  string Component {get{
#if SERVER
   return "server";
#else
   return "client";
#endif
  }}
  static string Agreement(string name){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var r=new StreamReader(s))return r.ReadToEnd();}
  static string Canonical(string value){return value.Replace("\r\n","\n").Replace("\r","\n").TrimEnd();}
  public Setup(string[] args,bool unattended){quiet=unattended;int i=Array.IndexOf(args,"--wait-pid");if(i>=0&&i+1<args.Length)int.TryParse(args[i+1],out waitPid);Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);Text="SysAdmin Assistant Setup · 321.su";Width=790;Height=660;MinimumSize=new Size(480,400);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(245,248,254);
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),RowCount=6,ColumnCount=1};layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));for(i=0;i<4;i++)layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   language.DropDownStyle=ComboBoxStyle.DropDownList;language.Items.AddRange(new object[]{"Русский","English"});language.SelectedIndex=(Environment.GetEnvironmentVariable("SAA_LANGUAGE")??(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="ru"?"ru":"en"))=="en"?1:0;english=language.SelectedIndex==1;language.Dock=DockStyle.Top;
   agreement.Multiline=true;agreement.ReadOnly=true;agreement.ScrollBars=ScrollBars.Vertical;agreement.WordWrap=true;agreement.Dock=DockStyle.Fill;accepted.AutoSize=true;accepted.Margin=new Padding(0,12,0,12);proceed.AutoSize=true;proceed.Enabled=false;proceed.Padding=new Padding(12,6,12,6);status.AutoSize=true;status.MaximumSize=new Size(700,0);progress.Dock=DockStyle.Bottom;
   startMenu.Checked=true;desktop.Checked=true;startup.Checked=false;
   if(Component=="client")try{using(var saved=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SysAdminAssistantClient")){if(saved!=null){startMenu.Checked=Convert.ToInt32(saved.GetValue("StartMenu",1))!=0;desktop.Checked=Convert.ToInt32(saved.GetValue("Desktop",1))!=0;startup.Checked=Convert.ToInt32(saved.GetValue("Startup",0))!=0;}}}catch(System.Security.SecurityException){}
   var choices=new FlowLayoutPanel{AutoSize=true,WrapContents=true};foreach(var choice in new[]{startMenu,desktop,startup}){choice.AutoSize=true;choice.Margin=new Padding(0,4,14,4);choices.Controls.Add(choice);}choices.Visible=Component=="client";
#if BUNDLED_WIX
   installWix.AutoSize=true;installWix.Margin=new Padding(0,4,14,4);choices.Controls.Add(installWix);
#endif
   saveAgreement.AutoSize=true;saveAgreement.Click+=delegate{SaveAgreement();};
   language.SelectedIndexChanged+=delegate{english=language.SelectedIndex==1;SetText();accepted.Checked=false;};accepted.CheckedChanged+=delegate{proceed.Enabled=accepted.Checked&&!running;};proceed.Click+=delegate{if(finished)Close();else Start();};FormClosing+=delegate(object sender,FormClosingEventArgs e){if(running)e.Cancel=true;};
   layout.Controls.Add(language,0,0);layout.Controls.Add(agreement,0,1);layout.Controls.Add(accepted,0,2);var bottom=new FlowLayoutPanel{AutoSize=true};bottom.Controls.Add(proceed);bottom.Controls.Add(saveAgreement);bottom.Controls.Add(status);layout.Controls.Add(choices,0,3);layout.Controls.Add(bottom,0,4);layout.Controls.Add(progress,0,5);Controls.Add(layout);SetText();
  }
  string ErrorText(string value){string[,] messages=new string[,] {
   {"Слишком большой пакет","Package is too large"},{"Пакет повреждён","Package is damaged"},{"Нет установочного пакета","Installation package is missing"},{"Неверные границы пакета","Invalid package boundaries"},{"Контрольная сумма пакета не совпадает","Package checksum does not match"},{"Слишком много файлов","Too many files"},{"Недопустимый путь","Invalid path"},{"Повтор или выход за каталог","Duplicate path or directory escape"},{"Превышен размер распаковки","Extraction size limit exceeded"},{"Превышен размер файла","File size limit exceeded"},{"Неверный размер файла","Invalid file size"},
   {"Закройте предыдущий клиент перед обновлением.","Close the previous client before updating."},{"Путь установщика содержит ссылку файловой системы.","Setup path contains a filesystem link."},{"Неверный компонент автоматической установки.","Invalid unattended component."},{"Неполный комплект установщика.","Incomplete setup package."}
  };for(int index=0;index<messages.GetLength(0);index++)if(value==messages[index,english?0:1])return messages[index,english?1:0];return value;}
  void SaveAgreement(){
   using(var dialog=new SaveFileDialog()){
    dialog.FileName="SysAdmin_Assistant_License_"+(english?"EN":"RU")+".txt";dialog.Filter=T("Текстовые файлы (*.txt)|*.txt","Text files (*.txt)|*.txt");
    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
    try{File.WriteAllText(dialog.FileName,agreement.Text,Encoding.UTF8);}catch(Exception e){MessageBox.Show(this,e.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}
   }
  }
  void SetText(){installWix.Text=T("Установить WiX для сборки MSI (.NET 10 SDK)","Install WiX to build MSI packages (.NET 10 SDK)");saveAgreement.Text=T("Сохранить текст…","Save text…");startMenu.Text=T("Меню «Пуск»","Start menu");desktop.Text=T("Ярлык на рабочем столе","Desktop shortcut");startup.Text=T("Запускать при входе (все пользователи)","Start at sign-in (all users)");Text=Component=="server"?T("SysAdmin Assistant · Установка сервера · 321.su","SysAdmin Assistant · Server setup · 321.su"):T("SysAdmin Assistant · Установка клиента · 321.su","SysAdmin Assistant · Client setup · 321.su");agreement.Text=Canonical(Agreement(english?"EULA.EN":"EULA.RU")).Replace("\n","\r\n");agreement.SelectionStart=0;agreement.SelectionLength=0;agreement.ScrollToCaret();accepted.Text=T("Я принимаю условия лицензионного соглашения","I accept the license agreement");proceed.Text=T("Продолжить установку","Continue setup");}
  void Start(){running=true;startMenu.Enabled=desktop.Enabled=startup.Enabled=installWix.Enabled=false;proceed.Enabled=language.Enabled=false;progress.Style=ProgressBarStyle.Marquee;status.Text=T("Подготовка установщика…","Preparing setup…");var worker=new BackgroundWorker();worker.DoWork+=delegate{Result=Execute();};worker.RunWorkerCompleted+=delegate(object sender,RunWorkerCompletedEventArgs e){running=false;finished=true;progress.Style=ProgressBarStyle.Blocks;progress.Value=Result==0?100:0;status.Text=e.Error!=null?ErrorText(e.Error.Message):Result==0?T("Мастер завершил работу","Setup wizard completed"):T("Установка отменена или завершилась с ошибкой. Код: ","Setup cancelled or failed. Code: ")+Result;if(e.Error!=null)MessageBox.Show(this,ErrorText(e.Error.Message),Text,MessageBoxButtons.OK,MessageBoxIcon.Error);proceed.Text=T("Закрыть","Close");proceed.Enabled=true;};worker.RunWorkerAsync();}
  public int Execute(){string temp=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"SAA-Setup-"+Guid.NewGuid().ToString("N"));
   try{
    if(waitPid>0)try{using(var old=Process.GetProcessById(waitPid)){if(!old.WaitForExit(60000))throw new InvalidOperationException("Close the previous client before updating.");}}catch(ArgumentException){}
    for(var d=new DirectoryInfo(temp);d!=null;d=d.Parent)if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Setup path contains a filesystem link.");
    var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);foreach(var sid in new[]{WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));Directory.CreateDirectory(temp,acl);
    ProductSetupBundle.Extract(Application.ExecutablePath,temp,null);
    string exe,arguments;
    if(quiet){if(Component!="server")throw new InvalidOperationException("Invalid unattended component.");exe=Path.Combine(temp,"SysAdminAssistant.ServerSetup.exe");arguments="--headless-update";}
    else{exe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");string script=Path.Combine(temp,Component=="server"?"INSTALL_SERVER_BOOTSTRAP.ps1":"INSTALL_CLIENT.ps1");if(!File.Exists(script))throw new InvalidDataException("Incomplete setup package.");arguments="-NoProfile -ExecutionPolicy Bypass -File \""+script+"\"";if(Component=="client")arguments+=" -Source \""+temp+"\" -SetupExe \""+Application.ExecutablePath+"\" -Language "+(english?"en":"ru")+" -StartMenu "+(startMenu.Checked?"1":"0")+" -Desktop "+(desktop.Checked?"1":"0")+" -Startup "+(startup.Checked?"1":"0")+" -InstallWix "+(installWix.Checked?"1":"0");}
    var start=new ProcessStartInfo(exe,arguments){UseShellExecute=false,CreateNoWindow=!quiet,RedirectStandardError=!quiet,WorkingDirectory=temp};start.EnvironmentVariables["SAA_LANGUAGE"]=english?"en":"ru";
    if(!quiet&&Component=="client")start.StandardErrorEncoding=Encoding.UTF8;
    if(!quiet)using(var hash=SHA256.Create())start.EnvironmentVariables["SAA_EULA_ACCEPTED"]=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Canonical(Agreement("EULA.RU"))+"\n"+Canonical(Agreement("EULA.EN"))))).Replace("-","");
    using(var process=Process.Start(start)){
     var diagnostics=new StringBuilder();
     if(!quiet){
      process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data==null)return;lock(diagnostics){diagnostics.AppendLine(e.Data);if(diagnostics.Length>4096)diagnostics.Remove(0,diagnostics.Length-4096);}};
      process.BeginErrorReadLine();
     }
     process.WaitForExit();
     if(!quiet&&process.ExitCode==1&&diagnostics.Length>0)throw new InvalidOperationException(diagnostics.ToString().Trim());
     return process.ExitCode;
    }
   }finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
  }
 }
}
