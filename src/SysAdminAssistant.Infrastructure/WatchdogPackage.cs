using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
namespace SysAdminAssistant.Infrastructure;

public sealed record WatchdogDeployment(string Profile, string PrivateDirectory, string PublicDirectory, string PhpExecutable, string EndpointUrl, string ServerId, string Name,string Language="ru");
public sealed record WatchdogPackageResult(byte[] Archive, string Secret);
/// <summary>Explicit administrator export; no filesystem or network access and no configuration mutation.</summary>
public static class WatchdogPackage
{
    public static string PhpString(string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    public static string Shell(string s) => "'" + s.Replace("'", "'\"'\"'") + "'";
    private static string PathValue(string value)
    {
        if (value.Length is < 2 or > 512 || !value.StartsWith('/') || value.EndsWith('/') || value.Contains("//") || value.Split('/').Any(p => p is "." or "..") || value.Any(c => c < 32 || c == 127 || c is '%' or '\\'))
            throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите абсолютный Linux-путь без завершающего /, .., %, обратных слешей и управляющих символов"));
        return value;
    }
    public static void Validate(WatchdogDeployment p)
    {
        if(p.Language is not ("ru" or "en"))throw new ArgumentException("Unsupported documentation language");
        if (p.Profile is not ("beget" or "shared" or "self")) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выберите вариант размещения"));
        var a=PathValue(p.PrivateDirectory);var b=PathValue(p.PublicDirectory);PathValue(p.PhpExecutable);
        if (a==b || a.StartsWith(b+"/",StringComparison.Ordinal)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Приватный каталог должен находиться вне публичного; public_html внутри приватного родителя допустим"));
        ServerHeartbeatSettings.ValidateUrl(p.EndpointUrl);ServerHeartbeatSettings.ValidateIdentity(p.ServerId);
        if (!new Uri(p.EndpointUrl).AbsolutePath.EndsWith("/heartbeat.php",StringComparison.Ordinal)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("URL должен заканчиваться /heartbeat.php; положи публичный файл в соответствующий каталог сайта"));
        if (p.Name.Length is <1 or >100 || p.Name.Any(c=>char.IsControl(c))) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Название сервера: 1–100 символов без переносов"));
    }
    public static string Instructions(WatchdogDeployment p)
    {
        Validate(p);
        if(SysAdminAssistant.Core.ProductText.Normalize(p.Language)=="en")return InstructionsEnglish(p);
        var worker=Shell(p.PhpExecutable)+" "+Shell(p.PrivateDirectory+"/worker.php");
        return $"""
        Внешний наблюдатель · {p.Name}
        Профиль: {p.Profile}
        URL: {p.EndpointUrl}
        ID сервера: {p.ServerId}

        1. Содержимое private/ загрузить в {p.PrivateDirectory}
           Получатся {p.PrivateDirectory}/config.php, lib.php, receiver.php, worker.php.
           Файл public/heartbeat.php загрузить как {p.PublicDirectory}/heartbeat.php
           Только heartbeat.php находится в публичной папке. Остальные файлы НЕ публиковать.
           Состояние создаётся рядом с config.php автоматически. Отдельная папка не нужна.
           Не заменяй public root сайта приватным каталогом. Для каждого наблюдаемого сервера нужен свой приватный каталог.

        2. PHP сайта и CLI: 8.1+ (64-bit), json/hash/curl; HTTPS и исходящий доступ к api.telegram.org.
           Путь PHP из панели хостинга: {p.PhpExecutable}
           Проверить версию (не использовать просто php, он может быть старым):
           {Shell(p.PhpExecutable)} -v
           При exit 255 повторить команду с -d display_errors=1 -d error_reporting=-1 перед путём worker.php.
           Дать PHP сайта и cron запись в приватный каталог. config.php: 600, каталог: 700 при одном пользователе.
           При разных пользователях согласовать группу/ACL; не ставить 777. На self-hosted настроить document root/PHP-FPM отдельно.

        3. Однократно выполнить в терминале:
           {worker} init
           {worker} test
           test отправляет настоящее тестовое сообщение Telegram. Он не имитирует heartbeat сервера.
           Если терминала нет: через панель выполнить эти задания по одному и удалить временные задания после успеха.
           Секреты уже заполнены. Не генерировать ключ заново и не присылать config.php в чат.

        4. Cron: каждые 5 минут, расписание */5 * * * *
           В поле «Команда» панели вставить ТОЛЬКО:
           {worker} check >> {Shell(p.PrivateDirectory+"/cron.log")} 2>&1
           Полная строка crontab находится в cron.txt. Не оставлять init/test в регулярном задании.
           Ограничить размер cron.log средствами хостинга/ротации.

        5. После загрузки, успешного test и настройки cron включить heartbeat в Assistant.
           Сигнал раз в минуту; при отсутствии более 5 минут проверка cron отправляет тревогу (обычно 5–10 минут).
           Восстановление — на следующем cron, до 5 минут. Должен появиться подтверждённый сигнал в настройках Assistant.
           Проверка состояния: {worker} status
           Плановые работы: maintenance=true в приватном config.php, затем вернуть false.
           Проверить вручную контролируемую остановку и восстановление.

        Архив содержит общий ключ и токен Telegram. Храни приватно; не загружай ZIP/инструкцию/cron в public root.
        Отказ самого сайта, cron или Telegram этим наблюдателем не обнаруживается. Время сервера и хостинга должно быть точным.
        """;
    }
    private static string InstructionsEnglish(WatchdogDeployment p)=>$"""
        SysAdmin Assistant external PHP watchdog

        1. Use PHP hosting OUTSIDE the monitored data center, with HTTPS, PHP CLI, curl and cron.
        2. Upload the CONTENTS of private/ to {p.PrivateDirectory}.
           This directory must be outside the public document root. Keep config.php private (chmod 600).
           State files use this same directory; a separate state folder is not required.
        3. Upload public/heartbeat.php to {p.PublicDirectory}/heartbeat.php.
           Public endpoint: {p.EndpointUrl}
        4. Use the PHP executable supplied by your hosting panel: {p.PhpExecutable}.
           Initialization: {Shell(p.PhpExecutable)} {Shell(p.PrivateDirectory+"/worker.php")} init
           Telegram test: {Shell(p.PhpExecutable)} {Shell(p.PrivateDirectory+"/worker.php")} test
        5. Add cron every five minutes (all fields */5 * * * *):
           {Shell(p.PhpExecutable)} {Shell(p.PrivateDirectory+"/worker.php")} check
           Check cron.log and test a missed heartbeat and recovery before relying on the setup.
        6. Enable heartbeats in Assistant using URL, server ID and shared secret from this package.
           The server sends a heartbeat every minute. Cron alerts roughly 5–10 minutes after the last one.
           Planned maintenance: set maintenance=true in private config.php, then restore false.

        This archive contains the shared secret and Telegram bot token. Never place the ZIP, config.php,
        instructions, worker or state files in the public web directory. The notification language is set
        from the server configuration at export time; regenerate the private config to change it.
        This watchdog cannot detect failure of its own hosting, cron or Telegram. Keep clocks accurate.
        """;
    public static byte[] Create(WatchdogDeployment p,string secret,string token,string chat,string notificationLanguage="ru")
    {
        Validate(p);ServerHeartbeatSettings.ValidateSecret(secret);
        if (!Regex.IsMatch(token,@"\A[0-9]+:[a-zA-Z0-9_-]+\z") || !Regex.IsMatch(chat,@"\A-?[0-9]+\z")) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сначала сохраните токен Telegram-бота и числовой Chat ID в настройках сервера"));
        using var stream=new MemoryStream();
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
        {
            void Add(string path,string value){var e=zip.CreateEntry(path,CompressionLevel.SmallestSize);e.ExternalAttributes=(Convert.ToInt32("100600",8)<<16);using var w=new StreamWriter(e.Open(),new UTF8Encoding(false));w.Write(value);}
            foreach(var name in new[]{"lib.php","receiver.php","worker.php"}){using var input=typeof(WatchdogPackage).Assembly.GetManifestResourceStream("Watchdog."+name)??throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("PHP-шаблоны отсутствуют в сборке сервера"));using var reader=new StreamReader(input);Add("private/"+name,reader.ReadToEnd());}
            Add("private/config.php","<?php\nreturn [\n    'server_id' => "+PhpString(p.ServerId)+",\n    'name' => "+PhpString(p.Name)+",\n    'language' => "+PhpString(SysAdminAssistant.Core.ProductText.Normalize(notificationLanguage))+",\n    'secret' => "+PhpString(secret)+",\n    'telegram_token' => "+PhpString(token)+",\n    'telegram_chat' => "+PhpString(chat)+",\n    'state_directory' => __DIR__,\n    'maintenance' => false,\n];\n");
            Add("public/heartbeat.php","<?php\nrequire "+PhpString(p.PrivateDirectory+"/receiver.php")+";\n");
            Add("START-HERE.txt",Instructions(p));
            Add("cron.txt","*/5 * * * * "+Shell(p.PhpExecutable)+" "+Shell(p.PrivateDirectory+"/worker.php")+" check >> "+Shell(p.PrivateDirectory+"/cron.log")+" 2>&1\n");
        }
        return stream.ToArray();
    }
}
