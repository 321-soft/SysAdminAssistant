using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record TelegramTopic(string Id,string Group,string Name,bool Default=true);
public static class TelegramTopics
{
 public static readonly TelegramTopic[] All=[
 new("ksc.security","Kaspersky Security Center","Проблемы защиты и восстановление"),new("device.availability","Доступность","Устройства: потеря связи / восстановление"),new("service.availability","Доступность","HTTP/TCP-сервисы"),new("network.wan","Доступность","WAN / интернет"),new("network.lan","Доступность","LAN"),new("network.dns","Доступность","Проверки DNS"),
 new("router.link","MikroTik и коммутаторы","Интерфейсы, WAN, uplink, туннели"),new("router.security","MikroTik и коммутаторы","Неудачные входы и события безопасности"),new("router.reboot","MikroTik и коммутаторы","Перезагрузки"),new("router.logs","MikroTik и коммутаторы","Остальные журналы и свои правила"),new("router.collector","MikroTik и коммутаторы","Сбой API / получения журналов"),new("router.audit","MikroTik и коммутаторы","Находки проверки конфигурации"),
 new("agent.link","Агенты Windows / Linux","Нет свежих данных агента"),new("agent.cpu","Агенты Windows / Linux","Загрузка CPU"),new("agent.ram","Агенты Windows / Linux","Оперативная память"),new("agent.disk","Агенты Windows / Linux","Свободное место и состояние дисков"),new("agent.service","Агенты Windows / Linux","Контролируемые службы"),new("agent.metric","Агенты Windows / Linux","Другие показатели"),new("journal.security","Журналы Windows / Linux","Входы, учётные записи и безопасность"),new("journal.reboot","Журналы Windows / Linux","Перезагрузки / сбои системы"),new("journal.storage","Журналы Windows / Linux","Файловая система, диски, RAID"),new("journal.reader","Журналы Windows / Linux","Нет доступа к журналам / данные устарели"),new("journal.other","Журналы Windows / Linux","Остальные правила журналов"),
 new("backup.failure","Резервное копирование","Ошибки выполнения"),new("backup.warning","Резервное копирование","Предупреждения выполнения"),new("backup.missing","Резервное копирование","Не получен отчёт по расписанию"),new("mail.device","Почта и отчёты","События устройств из писем"),new("mail.intake","Почта и отчёты","Ошибки получения IMAP"),new("mail.dmarc","Почта и отчёты","SPF / DKIM / DMARC"),new("payment.soon","Счета","Скоро платить / сегодня"),new("payment.overdue","Счета","Просроченные платежи"),new("recovery","Восстановление","Отправлять сообщения о решении / восстановлении"),new("other","Прочее","События без категории")];
 public static bool Known(string id)=>All.Any(t=>t.Id==id);
 public static string Infer(string key,string source,string title,Severity severity){
  if(UpdateNotifications.IsAvailability(key))return "updates.available";
  if(key.StartsWith("router-audit:"))return "router.audit";
  if(key.StartsWith("agent-link:"))return "agent.link";
  if(key.StartsWith("agent:")){if(key.Contains(":journal-reader:"))return "journal.reader";if(key.Contains(":service:"))return "agent.service";if(key.EndsWith(":cpu"))return "agent.cpu";if(key.EndsWith(":ram"))return "agent.ram";if(key.Contains(":disk:"))return "agent.disk";if(key.EndsWith(":reboot"))return "journal.reboot";return "agent.metric";}
  if(key.StartsWith("journal:"))return "journal.other";
  if(key.StartsWith("availability:device:"))return "device.availability";if(key.StartsWith("availability:service:"))return "service.availability";
  if(key.StartsWith("availability:wan:"))return "network.wan";if(key.StartsWith("availability:lan:"))return "network.lan";if(key.StartsWith("availability:dns:"))return "network.dns";
  if(key.StartsWith("payment:"))return title.Contains("просрочен",StringComparison.OrdinalIgnoreCase)?"payment.overdue":"payment.soon";
  if(key.StartsWith("schedule:"))return "backup.missing";
  if(key=="mail-intake")return "mail.intake";
  if(source.StartsWith("Сеть ·")){var value=title.ToLowerInvariant();if(key.StartsWith("network-availability:"))return "router.link";if(value.Contains("перезагруз"))return "router.reboot";if(value.Contains("вход")||value.Contains("login")||value.Contains("парол"))return "router.security";if(value.Contains("api")||value.Contains("логов"))return "router.collector";return "router.logs";}
  if(source.Contains("DMARC",StringComparison.OrdinalIgnoreCase))return "mail.dmarc";
  if(source=="ICMP")return "device.availability";
  if(source.Contains("бэкап",StringComparison.OrdinalIgnoreCase)||source.Contains("backup",StringComparison.OrdinalIgnoreCase)||source.Contains("Veeam",StringComparison.OrdinalIgnoreCase)||source.Contains("SQL",StringComparison.OrdinalIgnoreCase))return severity==Severity.Critical?"backup.failure":"backup.warning";
  return "mail.device";
 }
 public static string Emoji(Severity level,bool recovered=false)=>recovered?"🟢":level==Severity.Critical?"🔴":level==Severity.Warning?"🟠":"🔵";
}
