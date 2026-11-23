using System.IO;
using System.Windows;
using Microsoft.Win32;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private async Task CreateWatchdogPackage()
    {
        var profile=new FormDialog(this,"Наблюдатель · размещение",760)
            .Select("profile","Где будет работать PHP-наблюдатель",new[]{new Choice("beget","Beget · shared-хостинг"),new Choice("shared","Другой shared / облачный хостинг с PHP и cron"),new Choice("self","Собственный Linux-сервер · nginx / Apache + PHP")},"beget")
            .Note("Нужен обычный PHP-хостинг вне ДЦ: приватный каталог, HTTPS и cron. Облачный VPS — вариант собственного сервера. Статический хостинг без PHP/cron не подходит. Следующий шаг позволяет изменить все пути.")
            .Submit("Далее");
        if(profile.ShowDialog()!=true)return;
        var kind=profile.Selected<string>("profile");
        var directory=kind=="self"?"/var/www/saa-watchdog":"/home/USER/example.org";
        var php=kind=="beget"?"/usr/local/bin/php8.4":"/usr/bin/php8.4";
        var form=new FormDialog(this,"Наблюдатель · параметры архива",820)
            .Text("private","Приватный каталог · полный путь на хостинге",directory)
            .Text("public","Публичный каталог сайта · document root",directory+(kind=="self"?"/public":"/public_html"))
            .Text("php","PHP CLI · путь из панели хостинга / установленного PHP",php)
            .Text("url","Публичный HTTPS-адрес файла heartbeat.php",settings.ServerHeartbeat.EndpointUrl.Length>0?settings.ServerHeartbeat.EndpointUrl:"https://example.org/heartbeat.php")
            .Text("id","ID наблюдаемого сервера",settings.ServerHeartbeat.ServerId)
            .Text("name","Название в Telegram","SysAdmin Assistant")
            .Note("Замените USER и example.org своими значениями, сверяясь с файловым менеджером хостинга. Указанные пути — предложения, версия PHP должна быть установлена и выбрана в панели для сайта. Для Beget предлагается PHP 8.4; если панель дала иной путь — используйте его.")
            .Note("Общий ключ будет взят с сервера или создан автоматически. Используется сохранённый на сервере Telegram-бот и Chat ID. Архив с токеном доступен только главному администратору; храни его приватно. Экспорт не меняет настройки и не включает проверку.")
            .Submit("Показать файлы и команды");
        WatchdogDeployment Plan()=>new(kind,form.Value("private"),form.Value("public"),form.Value("php"),form.Value("url"),form.Value("id"),form.Value("name"),SysAdminAssistant.Core.ProductText.Language);
        form.Validate=()=>{WatchdogPackage.Validate(Plan());if(new[]{form.Value("private"),form.Value("public"),form.Value("url")}.Any(s=>s.Contains("USER",StringComparison.Ordinal)||s.Contains("example.org",StringComparison.OrdinalIgnoreCase)))return "Замените USER и example.org своими путями и доменом";return null;};
        if(form.ShowDialog()!=true)return;
        var plan=Plan();
        var preview=new FormDialog(this,"Наблюдатель · перед сохранением",920)
            .Text("instructions","Куда загрузить и что выполнить",WatchdogPackage.Instructions(plan),true,true).TextHeight("instructions",420)
            .Note("Будет создан готовый config.php с ключом и Telegram-токеном. Приватные файлы не должны попасть в public_html. Для повторного размещения действующего наблюдателя ключ сохраняется.")
            .Submit("Сохранить ZIP…");
        if(preview.ShowDialog()!=true)return;
        var save=new SaveFileDialog{Title="Сохранить пакет PHP-наблюдателя",Filter="ZIP-архив (*.zip)|*.zip",FileName="SAA-Watchdog-"+plan.ServerId+".zip",DefaultExt=".zip",AddExtension=true};
        if(save.ShowDialog(this)!=true)return;
        var bundle=await remote.CommandAsync<WatchdogPackageResult>("server-heartbeat-package",plan,lifetime.Token);
        // Atomic replacement; a failed download/write leaves an existing archive intact.
        var temporary=save.FileName+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{await File.WriteAllBytesAsync(temporary,bundle.Archive,lifetime.Token);File.Move(temporary,save.FileName,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        var done=new FormDialog(this,"Архив сохранён · следующие шаги",860)
            .Text("path","Архив",save.FileName,readOnly:true)
            .Text("instructions","Инструкция также внутри ZIP: START-HERE.txt",WatchdogPackage.Instructions(plan),true,true).TextHeight("instructions",360)
            .Note("После загрузки файлов, успешного test и настройки cron нажмите «Включить сигналы». Пока это окно открыто, можно настроить сайт. Кнопка применит URL, ID и ключ из архива. Если закрыть окно, настройки не изменятся; позже их можно взять из config.php. Не включайте до размещения сайта.")
            .Submit("Включить сигналы");
        if(done.ShowDialog()!=true)return;
        await UpdateSettings(s=>{s.ServerHeartbeat.EndpointUrl=plan.EndpointUrl;s.ServerHeartbeat.ServerId=plan.ServerId;s.ServerHeartbeat.ProtectedSecret=LocalSecret.Protect(bundle.Secret);s.ServerHeartbeat.Enabled=true;});
    }
}
