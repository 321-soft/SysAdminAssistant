using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using Forms = System.Windows.Forms;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private Forms.NotifyIcon? tray;
    private bool exitRequested, trayHintShown;

    private void UpdateTrayLanguage()
    {
        if(tray?.ContextMenuStrip is not {} menu)return;
        foreach(Forms.ToolStripItem item in menu.Items)if(item is not Forms.ToolStripSeparator)item.Text=SysAdminAssistant.Core.ProductText.Get(item.Text);
    }

    private void InitializeTray()
    {
        try
        {
            using var identity=WindowsIdentity.GetCurrent();
            var security=new EventWaitHandleSecurity();
            security.SetAccessRuleProtection(true,false);
            security.AddAccessRule(new EventWaitHandleAccessRule(identity.User!,EventWaitHandleRights.FullControl,AccessControlType.Allow));
            var administrators=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null);
            security.AddAccessRule(new EventWaitHandleAccessRule(administrators,EventWaitHandleRights.Modify,AccessControlType.Allow));
            var request=EventWaitHandleAcl.Create(false,EventResetMode.AutoReset,@"Local\SAA.Client.Close."+Environment.ProcessId,out var created,security);
            if(!created){request.Dispose();throw new InvalidOperationException("Client shutdown event already exists.");}
            var waiter=ThreadPool.RegisterWaitForSingleObject(request,(_,timedOut)=>Dispatcher.BeginInvoke(new Action(()=>{
                if(busy&&OwnedWindows.Count==0)return;
                if(OwnedWindows.Count!=0){RestoreFromTray();OwnedWindows[OwnedWindows.Count-1].Activate();return;}
                exitRequested=true;Close();
            })),null,Timeout.Infinite,false);
            Closed+=(_,_)=>{waiter.Unregister(null);request.Dispose();};
            using var resource = Application.GetResourceStream(new Uri("Assets/SysAdminAssistant.ico", UriKind.Relative))?.Stream
                ?? throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Не найдена иконка приложения"));
            using var original = new System.Drawing.Icon(resource);
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(SysAdminAssistant.Core.ProductText.Get("Открыть"), null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(SysAdminAssistant.Core.ProductText.Get("Выход"), null, (_, _) => Dispatcher.Invoke(() =>
            {
                exitRequested = true;
                Application.Current.Shutdown();
            }));
            tray = new Forms.NotifyIcon
            {
                Icon = (System.Drawing.Icon)original.Clone(), Text = "SysAdmin Assistant", ContextMenuStrip = menu, Visible = true
            };
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
            Closing += CloseToTray;
            Closed += DisposeTray;
            Application.Current.SessionEnding += EndSession;
        }
        catch (Exception e)
        {
            tray?.Dispose(); tray = null;
            AppMessageBox.Show(this, "Не удалось включить значок в трее. Крестик завершит программу.\n" + e.Message, "SysAdmin Assistant");
        }
    }

    private async void CloseToTray(object? sender, CancelEventArgs e)
    {
        if (exitRequested || tray == null) return;
        e.Cancel = true;
        if (OwnedWindows.Count > 0) return;
        try { await LockOrganizationVaultAsync(); }
        catch (Exception error) { StatusLine.Text = "Не удалось подтвердить блокировку Vault на сервере: " + error.Message; }
        Hide();
        if (!trayHintShown)
        {
            trayHintShown = true;
            tray.ShowBalloonTip(4000, "SysAdmin Assistant", "Окно скрыто. Мониторинг продолжает выполнять сервер. Для завершения клиента выберите «Выход» в меню значка.", Forms.ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (page == 7) _ = Run(() => ShowPage(7));
    }

    private void EndSession(object sender, SessionEndingCancelEventArgs e) => exitRequested = true;

    private void DisposeTray(object? sender, EventArgs e)
    {
        Application.Current.SessionEnding -= EndSession;
        if (tray == null) return;
        tray.Visible = false; var icon = tray.Icon; var menu = tray.ContextMenuStrip;
        tray.Dispose(); icon?.Dispose(); menu?.Dispose(); tray = null;
    }
}
