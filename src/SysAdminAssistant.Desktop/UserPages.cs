using System.Windows;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private async Task ChangeMyPassword()
    {
        var f=new FormDialog(this,"Смена пароля входа").Secret("old","Текущий пароль").Secret("next","Новый пароль · 9–256 символов").Secret("repeat","Повтор нового пароля").Note("Все ваши сеансы будут завершены. После смены подключитесь заново.");
        f.Validate=()=>f.Value("next").Length is <9 or >256?"Пароль: 9–256 символов":f.Value("next")!=f.Value("repeat")?"Пароли не совпадают":null;
        if(f.ShowDialog()!=true)return;
        await remote.PostAsync<PasswordChange,bool>("api/personal/password",new(f.Value("old"),f.Value("next")),lifetime.Token);
        AppMessageBox.Show(this,"Пароль изменён. Подключитесь заново.");await Reconnect();
    }
    private async Task ShowUsers()
    {
        var rows=await remote.GetAsync<UserView[]>("api/users",lifetime.Token);
        var f=new FormDialog(this,"Пользователи").Select("user","Учётная запись",rows.Select(x=>new Choice(x.Id,x.Login+(x.Administrator?" · главный администратор":x.Enabled?" · включён":" · заблокирован"))), rows.First().Id)
            .Select("action","Действие",new[]{new Choice("create","Создать пользователя"),new Choice("edit","Настроить выбранного")},"create");
        if(f.ShowDialog()!=true)return;
        if(f.Selected<string>("action")=="create")
        {
            var add=new FormDialog(this,"Новый пользователь").Tab("Учётная запись").Text("login","Логин").Secret("password","Начальный пароль · 9–256 символов").Secret("repeat","Повтор пароля")
                .Note("Для каждого раздела задайте уровень доступа. Управление пользователями доступно только главному администратору.");
            AddPermissionFields(add,null);
            add.Validate=()=>add.Value("password").Length is <9 or >256?"Пароль: 9–256 символов":add.Value("password")!=add.Value("repeat")?"Пароли не совпадают":Required(add.Value("login"));
            if(add.ShowDialog()==true)await remote.PostAsync<UserCreate,UserView>("api/users/create",new(add.Value("login"),add.Value("password"),ReadPermissions(add)),lifetime.Token);
        }
        else
        {
            var user=rows.Single(x=>x.Id==f.Selected<string>("user"));
            if(user.Administrator){AppMessageBox.Show(this,"Главного администратора нельзя заблокировать. Для своего пароля используйте «Сменить пароль входа».");return;}
            var change=new FormDialog(this,"Пользователь · "+user.Login).Tab("Учётная запись").Toggle("enabled","Учётная запись включена",user.Enabled).Secret("password","Новый пароль · пусто = не менять").Note("Изменение завершает все сеансы пользователя. Его записи Vault сохраняются.");
            AddPermissionFields(change,user);
            change.Validate=()=>change.Value("password").Length>0 && (change.Value("password").Length is <9 or >256)?"Пароль: 9–256 символов":null;
            if(change.ShowDialog()==true)await remote.PostAsync<UserUpdate,UserView>("api/users/update",new(user.Id,change.Checked("enabled"),user.Revision,change.Value("password"),ReadPermissions(change)),lifetime.Token);
        }
        await ShowUsers();
    }
    private static void AddPermissionFields(FormDialog f,UserView? user)
    {
        f.Tab("Разделы");
        for(var i=0;i<AccessRules.Modules.Length;i++) {
            var module=AccessRules.Modules[i];
            f.Select("access/"+module,AccessRules.Labels[i],new[]{new Choice(0,"Нет доступа"),new Choice(1,"Просмотр"),new Choice(2,"Просмотр и изменение")},user==null?0:AccessRules.Level(user,module));
        }
    }
    private static Dictionary<string,int> ReadPermissions(FormDialog f) => AccessRules.Modules.ToDictionary(x=>x,x=>f.Selected<int>("access/"+x));
    private async Task ShowAudit()
    {
        var rows=await remote.GetAsync<AuditView[]>("api/audit",lifetime.Token);
        PageHost.Children.Clear();PageHost.Children.Add(TableSurface(Table(rows,("UTC","Utc",null),("Пользователь","ActorLogin",null),("Действие","Action",null),("Владелец ID","OwnerId",null),("Запись","EntryId",null))));PageSubtitle.Text="Последние 1000 действий · пароли не записываются";
    }
}
