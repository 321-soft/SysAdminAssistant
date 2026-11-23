using System.Windows;
using System.Windows.Controls;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private DateTime inventoryUntil;private bool inventoryMasked;
    private async Task<bool> UnlockInventory()
    {
        if(inventoryUntil>DateTime.UtcNow&&await remote.GetAsync<bool>("api/personal/inventory/status",lifetime.Token))return true;
        var form=new FormDialog(this,"Доступ к сотрудникам и актам").Secret("password","Пароль вашей учётной записи приложения").Note("Разблокировка на 5 минут. Она не открывает хранилище паролей.");
        if(form.ShowDialog()!=true)return false;
        var ok=await remote.PostAsync<RemoteLogin,bool>("api/personal/inventory/unlock",new(remote.CurrentUser.Login,form.Value("password")),lifetime.Token);
        if(!ok)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Неверный пароль или слишком много попыток. Повторите позже."));
        inventoryMasked=false;remote.InventoryOpen=true;inventoryUntil=DateTime.UtcNow.AddMinutes(5);await remote.RefreshAsync(lifetime.Token);return true;
    }
    private void ShowLockedInventory()
    {
        inventoryMasked=true;remote.InventoryOpen=false;
        var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text="Сотрудники и акты закрыты",FontSize=22,Margin=new Thickness(0,0,0,12)});
        panel.Children.Add(new TextBlock{Text="Для персональных данных требуется подтверждение пароля.",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(ActionButton("Открыть доступ",()=>ShowPage(9)));PageHost.Children.Add(panel);
    }
}
