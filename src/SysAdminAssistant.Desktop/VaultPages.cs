using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private sealed record VaultDisplayRow(VaultItem Entity,string Device,string Service) { public string Name=>Entity.Name;public string OwnerLogin=>Entity.OwnerLogin; }
    private DateTime organizationVaultUntil;
    private async Task ShowVault()
    {
        var entries=await remote.GetAsync<VaultItem[]>("api/personal/vault",lifetime.Token);
        await using var db=factory.Create();
        var names=(CanRead("devices")||CanRead("routers"))?await db.Devices.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.Name):new Dictionary<int,string>();
        var services=CanRead("services")?await db.Services.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.Name):new Dictionary<int,string>();
        var table=Table(entries.Select(e=>new VaultDisplayRow(e,e.DeviceId is int id ? names.GetValueOrDefault(id,(CanRead("devices")||CanRead("routers"))?"Архивное устройство":"Нет доступа к устройству"):"Без привязки",e.ServiceId is int sid?services.GetValueOrDefault(sid,CanRead("services")?"Удалённый сервис":"Нет доступа к сервису"):"Без привязки")),
            ("Название","Name",null),("Владелец","OwnerLogin",null),("Устройство","Device",null),("Сервис","Service",null));
        table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=async(_,_)=>{if(table.SelectedItem is VaultDisplayRow row)await Run(()=>OpenCredential(row.Entity));};
        ListPage(table,
            ActionButton("Скачать шаблон",()=>SaveExcel("vault",true)), ActionButton("Экспорт Excel",()=>SaveExcel("vault",false)), ActionButton("Импорт Excel",()=>ImportExcel("vault")),
            RowAction(table,"Открыть",()=>OpenCredential(Selected<VaultDisplayRow>(table).Entity)),
            ActionButton("Добавить",()=>EditOrganizationCredential(null)),
            RowAction(table,"Связанное устройство",async()=>{
                if(!CanRead("devices")&&!CanRead("routers"))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нет доступа к устройствам"));
                var id=Selected<VaultDisplayRow>(table).Entity.DeviceId;
                if(id==null)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Запись не связана с устройством"));
                await using var targetDb=factory.Create();
                var device=await targetDb.Devices.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==id);
                if(device!=null)await ShowDeviceCard(device);
            }),
            RowAction(table,"Связанный сервис",async()=>{if(!CanRead("services"))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Нет доступа к сервисам"));var id=Selected<VaultDisplayRow>(table).Entity.ServiceId??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Запись не связана с сервисом"));await using var context=factory.Create();var service=await context.Services.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сервис отсутствует"));new FormDialog(this,service.Name).Text("type","Проверка",service.Type,false,true).Text("address","Адрес",service.Address,false,true).Text("notes","Заметки",service.Notes,true,true).ViewOnly().ShowDialog();}),
            RowAction(table,"Изменить",()=>EditOrganizationCredential(Selected<VaultDisplayRow>(table).Entity)),
            RowAction(table,"Удалить",async()=>{
                if(!await UnlockOrganizationVault())return;var e=Selected<VaultDisplayRow>(table).Entity;
                if(!Confirm(this,"Удалить «"+e.Name+"» владельца "+e.OwnerLogin+"?"))return;
                await remote.PostAsync<VaultTarget,bool>("api/personal/vault/delete",new(e.Id,e.Revision),lifetime.Token);await ShowPage(7);
            }),
            ActionButton("Заблокировать",async()=>{await LockOrganizationVaultAsync();await ShowPage(7);}));
        PageSubtitle.Text="Хранилище паролей";
    }
    private async Task OpenCredential(VaultItem entry)
    {
        if(!await UnlockOrganizationVault())return;
        var secret=await remote.PostAsync<VaultTarget,CredentialPayload>("api/personal/vault/read",new(entry.Id,entry.Revision),lifetime.Token);
        var masked=new PasswordBox{Password=secret.Password,IsEnabled=false,Margin=new Thickness(0,4,0,10)};
        var revealed=new TextBox{IsReadOnly=true,Visibility=Visibility.Collapsed,Margin=new Thickness(0,4,0,10)};
        var root=new StackPanel{Margin=new Thickness(24)};
        root.Children.Add(new TextBlock{Text="Логин"});root.Children.Add(new TextBox{Text=secret.Login,IsReadOnly=true,Margin=new Thickness(0,4,0,12)});
        root.Children.Add(new TextBlock{Text="Пароль"});root.Children.Add(masked);root.Children.Add(revealed);
        var actions=new WrapPanel();var show=UiButton("Показать пароль");show.MinWidth=145;var copy=UiButton("Копировать пароль");actions.Children.Add(show);actions.Children.Add(copy);root.Children.Add(actions);
        var status=new TextBlock{Text="Пароль скрыт. Окно закроется через минуту.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)};root.Children.Add(DialogWindow.StatusViewport(status,60));
        root.Children.Add(new TextBlock{Text="Заметки"});var notes=new TextBox{Text=secret.Notes,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};root.Children.Add(notes);
        var window=new DialogWindow{Owner=this,Title=entry.Name,Width=580,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height-50,Content=new ScrollViewer{Content=root},WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var clipboardTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};string? copied=null;
        void ClearClipboard(){try{if(copied!=null&&Clipboard.ContainsText()&&Clipboard.GetText()==copied)Clipboard.Clear();}catch(System.Runtime.InteropServices.ExternalException){}copied=null;clipboardTimer.Stop();}
        clipboardTimer.Tick+=(_,_)=>ClearClipboard();
        show.Click+=(_,_)=>{var visible=revealed.Visibility!=Visibility.Visible;revealed.Text=visible?secret.Password:"";revealed.Visibility=visible?Visibility.Visible:Visibility.Collapsed;masked.Visibility=visible?Visibility.Collapsed:Visibility.Visible;show.Content=visible?"Скрыть пароль":"Показать пароль";};
        copy.Click+=(_,_)=>{try{if(secret.Password.Length==0){status.Text="Пароль пустой";return;}Clipboard.SetText(secret.Password);copied=secret.Password;clipboardTimer.Stop();clipboardTimer.Start();status.Text="Пароль скопирован. Буфер очистится через 30 секунд, если его содержимое не изменится.";}catch(System.Runtime.InteropServices.ExternalException){status.Text="Буфер обмена занят. Повторите копирование.";}};
        var expiry=new DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};expiry.Tick+=(_,_)=>window.Close();
        window.Loaded+=(_,_)=>expiry.Start();
        window.Deactivated+=(_,_)=>{revealed.Clear();revealed.Visibility=Visibility.Collapsed;masked.Visibility=Visibility.Visible;show.Content="Показать пароль";};
        window.Closed+=(_,_)=>{expiry.Stop();masked.Clear();revealed.Clear();notes.Clear();secret=new("","","");};
        window.ShowDialog();
    }
    private async Task LockOrganizationVaultAsync()
    {
        organizationVaultUntil = DateTime.MinValue;
        if(AccessRules.Level(remote.CurrentUser,"vault")>=1)await remote.PostAsync<object,bool>("api/personal/vault/lock",new{},lifetime.Token);
    }
    private async Task<bool> UnlockOrganizationVault()
    {
        if(organizationVaultUntil>DateTime.UtcNow)return true;
        var form=new FormDialog(this,"Доступ к хранилищу организации").Secret("password","Пароль вашей учётной записи приложения")
            .Note("Доступ открывается на 5 минут.");
        if(form.ShowDialog()!=true)return false;
        var ok=await remote.PostAsync<RemoteLogin,bool>("api/personal/vault/unlock",new(remote.CurrentUser.Login,form.Value("password")),lifetime.Token);
        if(!ok)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Неверный пароль"));organizationVaultUntil=DateTime.UtcNow.AddMinutes(5);return true;
    }
    private async Task EditOrganizationCredential(VaultItem? entry)
    {
        if(!await UnlockOrganizationVault())return;
        var payload=entry==null?new CredentialPayload("","",""):await remote.PostAsync<VaultTarget,CredentialPayload>("api/personal/vault/read",new(entry.Id,entry.Revision),lifetime.Token);
        var form=new FormDialog(this,entry==null?"Новая запись · владелец "+remote.CurrentUser.Login:"Запись владельца "+entry.OwnerLogin)
            .Text("name","Название",entry?.Name??"").Text("login","Логин",payload.Login).Secret("password",entry==null?"Пароль":"Новый пароль · пусто = прежний").Text("notes","Заметки",payload.Notes,true)
            .Note("Владелец записи не меняется.");
        if((AccessRules.Level(remote.CurrentUser,"devices")>=1||AccessRules.Level(remote.CurrentUser,"routers")>=1)) {
            await using var db=factory.Create();var devices=await db.Devices.AsNoTracking().OrderBy(x=>x.Name).ToArrayAsync();
            var choices=new[]{new Choice(null,"Без привязки")}.Concat(devices.Select(x=>new Choice(x.Id,x.Name))).ToList();
            if(entry?.DeviceId is int oldId && !devices.Any(x=>x.Id==oldId))choices.Add(new Choice(oldId,"Устройство #"+oldId));
            form.Select("device","Связать с устройством",choices,entry?.DeviceId);
        }
        if(CanRead("services")){await using var context=factory.Create();var services=await context.Services.AsNoTracking().OrderBy(x=>x.Name).ToArrayAsync();var choices=new[]{new Choice(null,"Без привязки")}.Concat(services.Select(x=>new Choice(x.Id,x.Name+" · "+x.Address))).ToList();if(entry?.ServiceId is int oldService&&!services.Any(x=>x.Id==oldService))choices.Add(new Choice(oldService,"Сервис #"+oldService));form.Select("service","Связать с сервисом",choices,entry?.ServiceId);}
        form.Validate=()=>Required(form.Value("name"));if(form.ShowDialog()!=true)return;
        await remote.PostAsync<VaultSave,bool>("api/personal/vault/save",new(entry?.Id??0,entry?.Revision??0,form.Value("name"),(AccessRules.Level(remote.CurrentUser,"devices")>=1||AccessRules.Level(remote.CurrentUser,"routers")>=1)?(form.Selected("device") as int?) : entry?.DeviceId,new(form.Value("login"),entry!=null&&form.Value("password").Length==0?payload.Password:form.Value("password"),form.Value("notes")),CanRead("services")?(form.Selected("service") as int?):entry?.ServiceId),lifetime.Token);
        await ShowPage(7);
    }
}
