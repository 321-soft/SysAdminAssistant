using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.ProductMaintenance;

internal static class Program
{
 [STAThread] private static void Main(string[] args)
 {
  ApplicationConfiguration.Initialize();ProductText.DefaultLanguage=ProductText.Normalize(Environment.GetEnvironmentVariable("SAA_LANGUAGE"));
  try{
   using var principal=WindowsIdentity.GetCurrent();
   if(!new WindowsPrincipal(principal).IsInRole(WindowsBuiltInRole.Administrator))throw new UnauthorizedAccessException("Run the installed update helper with administrator approval.");
   // The caller's working directory can keep the old client directory locked.
   Directory.SetCurrentDirectory(AppContext.BaseDirectory);
   if(args.Length!=4||args[0]!="--client-update"||!int.TryParse(args[2],out var pid)||pid<1)throw new ArgumentException("Invalid client update request.");
   ProductText.DefaultLanguage=ProductText.Normalize(args[3]);
   using var lease=new Mutex(false,@"Global\SysAdminAssistantClientUpdate");bool held=false;
   try{try{held=lease.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Another client update is already running.");Application.Run(new MaintenanceWindow(args[1],pid));}finally{if(held)lease.ReleaseMutex();}
  }catch(Exception error){MessageBox.Show(ProductText.Get(error.Message),"SysAdmin Assistant",MessageBoxButtons.OK,MessageBoxIcon.Error);Environment.ExitCode=1;}
 }
 private sealed class MaintenanceWindow:Form
 {
  private static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant Client Maintenance");
  private static string Target=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant");
  private readonly TextBox status=new(){ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,Dock=DockStyle.Fill,BackColor=Color.White};
  private readonly ProgressBar progress=new(){Minimum=0,Maximum=100,Dock=DockStyle.Top,Height=18};
  private readonly Button close=new(){Text=ProductText.Get("Закрыть"),AutoSize=true,Enabled=false,Dock=DockStyle.Bottom};
  private readonly CancellationTokenSource cancellation=new();private bool installing,finished;
  public MaintenanceWindow(string request,int pid)
  {
   Text="SysAdmin Assistant · 321.su";ClientSize=new Size(640,320);MinimumSize=new Size(480,280);AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Segoe UI",10);Padding=new Padding(20);StartPosition=FormStartPosition.CenterScreen;
   Controls.Add(progress);Controls.Add(status);Controls.Add(close);close.Click+=(_,_)=>Close();
   FormClosing+=(_,e)=>{if(installing)e.Cancel=true;else if(!finished)cancellation.Cancel();};
   Shown+=async(_,_)=>{try{await ApplyAsync(request,pid);status.Text=ProductText.Get("Клиент обновлён. Запустите его из меню Пуск.");Environment.ExitCode=0;}
    catch(OperationCanceledException){status.Text=ProductText.Get(cancellation.IsCancellationRequested?"Обновление отменено.":"Истекло время ожидания. Проверьте состояние клиента перед повтором.");Environment.ExitCode=1;}
    catch(Exception error){status.Text=ProductText.Get(error.Message);Environment.ExitCode=1;}
    finally{finished=true;installing=false;close.Enabled=true;}};
  }
  private static void NoLinks(string path){for(var entry=new DirectoryInfo(path);entry!=null;entry=entry.Parent)if(entry.Exists&&(entry.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Update path contains a filesystem link.");}
  private static Process? FindClient(int pid)
  {
   Process process;
   try{process=Process.GetProcessById(pid);}catch(ArgumentException){return null;}
   try{
    if(process.HasExited){process.Dispose();return null;}
    using var self=Process.GetCurrentProcess();
    if(process.SessionId!=self.SessionId||!string.Equals(process.MainModule?.FileName,Path.Combine(Target,"SysAdminAssistant.exe"),StringComparison.OrdinalIgnoreCase))
     throw new InvalidOperationException("The update request does not identify the installed client in this session.");
    return process;
   }catch{process.Dispose();throw;}
  }

  private static async Task CloseClientAsync(Process client,CancellationToken token)
  {
   using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
   deadline.CancelAfter(TimeSpan.FromSeconds(30));
   try{
    while(!client.HasExited){
     try{
      using var close=EventWaitHandleAcl.OpenExisting(@"Local\SAA.Client.Close."+client.Id,EventWaitHandleRights.Modify);
      close.Set();
     }catch(Exception e)when(e is WaitHandleCannotBeOpenedException or UnauthorizedAccessException){}
     await Task.Delay(TimeSpan.FromSeconds(1),deadline.Token);
    }
   }catch(OperationCanceledException)when(!token.IsCancellationRequested){
    if(!client.HasExited)throw new InvalidOperationException("Клиент всё ещё открыт. Закройте диалоги и выберите «Выход» в трее, затем повторите обновление.");
   }
  }

  private async Task ApplyAsync(string request,int pid)
  {
   NoLinks(Root);NoLinks(Target);
   var expected=Path.Combine(Root,"versions",typeof(Program).Assembly.GetName().Version!.ToString(),"SysAdminAssistant.ProductMaintenance.exe");
   if(!string.Equals(Path.GetFullPath(Environment.ProcessPath!),Path.GetFullPath(expected),StringComparison.OrdinalIgnoreCase))throw new IOException("Use the protected installed update helper.");
   NoLinks(Path.GetDirectoryName(Path.GetFullPath(request))!);if((File.GetAttributes(request)&FileAttributes.ReparsePoint)!=0)throw new IOException("Update request is a link.");
   string envelope;using(var input=new FileStream(request,FileMode.Open,FileAccess.Read,FileShare.Read)){if(input.Length>100_000)throw new InvalidDataException("Release manifest exceeds the size limit.");using var reader=new StreamReader(input);envelope=await reader.ReadToEndAsync(cancellation.Token);}
   var release=ProductReleases.Verify(envelope,ProductReleases.PublicKey,DateTime.UtcNow);
   var installed=AssemblyName.GetAssemblyName(Path.Combine(Target,"SysAdminAssistant.dll")).Version!;ProductReleases.RequireUpgrade(release.Client,installed,"client");
   using var previous=FindClient(pid);
   var staging=Path.Combine(Root,"staging",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);NoLinks(staging);
   try{
    status.Text=ProductText.Get("Загрузка и проверка клиента…");var package=Path.Combine(staging,"ClientSetup.exe");
    await ProductReleases.DownloadAsync(release.Client,package,new Progress<int>(p=>progress.Value=Math.Clamp(p,0,100)),cancellation.Token);
    cancellation.Token.ThrowIfCancellationRequested();
    if(previous!=null)await CloseClientAsync(previous,cancellation.Token);
    cancellation.Token.ThrowIfCancellationRequested();
    ProductReleases.RequireUpgrade(release.Client,AssemblyName.GetAssemblyName(Path.Combine(Target,"SysAdminAssistant.dll")).Version!,"client");
    installing=true;status.Text=ProductText.Get("Установка клиента. Не выключайте компьютер.");
    var start=new ProcessStartInfo(package){UseShellExecute=false,WorkingDirectory=staging};start.Environment["SAA_LANGUAGE"]=ProductText.Language;
    using var process=Process.Start(start)??throw new IOException("Cannot start verified client setup.");await process.WaitForExitAsync();
    if(process.ExitCode!=0)throw new IOException("Client setup failed or was canceled. Exit code: "+process.ExitCode);
    var actual=AssemblyName.GetAssemblyName(Path.Combine(Target,"SysAdminAssistant.dll")).Version;
    if(actual!=Version.Parse(release.Client.Version))throw new IOException("Installed client version differs from the signed release.");
    var probe=new ProcessStartInfo(Path.Combine(Target,"SysAdminAssistant.exe")){UseShellExecute=false};probe.ArgumentList.Add("--preflight");
    using var check=Process.Start(probe)??throw new IOException("Cannot start client verification.");using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await check.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){try{if(!check.HasExited)check.Kill();}catch(InvalidOperationException){}throw new IOException("Client startup verification timed out.");}if(check.ExitCode!=0)throw new IOException("Client startup verification failed.");
   }finally{try{Directory.Delete(staging,true);}catch(IOException){/* Retain files if an installer still holds them. */}}
   // Never write to, delete or execute a path supplied by the unprivileged caller.
  }
 }
}
