using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public static class AvailabilityIssueAdvice
{
    public static string Probe(ConnectivityProbe probe)=>global::SysAdminAssistant.Core.ProductText.Get($"Проверка с сервера Assistant: {probe.Group} · {probe.Host}; результат {probe.Result}; время {probe.CheckedAt:dd.MM.yyyy HH:mm:ss}.\nЧто проверить: ")+Steps(probe.Group)+global::SysAdminAssistant.Core.ProductText.Get("\nОтсутствие ICMP-ответа не доказывает отказ узла или интернета. Повторная успешная проверка восстанавливает состояние.");
    private static string Steps(string group)=>group switch{
        "LAN"=>global::SysAdminAssistant.Core.ProductText.Get("Сравните доступность шлюза и нескольких локальных узлов, маршрут, VLAN/туннель и правила ICMP. Если не отвечает один адрес, проверьте его отдельно; не меняйте всю сеть по одной проверке."),
        "WAN"=>global::SysAdminAssistant.Core.ProductText.Get("Сначала проверьте LAN и шлюз, затем несколько независимых внешних адресов, маршрут/провайдера и правила ICMP. Отказ одного адреса не равен падению всего интернета."),
        "DNS"=>global::SysAdminAssistant.Core.ProductText.Get("Проверьте разрешение указанного имени с сервера Assistant, доступность настроенного DNS и его ответы. Раздели ошибку DNS и недоступность узла; не заменяй DNS без проверки доменной конфигурации."),
        _=>global::SysAdminAssistant.Core.ProductText.Get("Сверьте адрес и тип проверки, доступность с сервера Assistant и ограничения сети. Успешный ping не подтверждает работу приложения.")};
    public static string Group(string group,ConnectivityRecord record,int delay)
    {
        ConnectivityProbe[] rows=[];try{rows=JsonSerializer.Deserialize<ConnectivityProbe[]>(record.ProbeResultsJson,RemoteData.Json)??[];}catch(JsonException){}
        var matching=rows.Where(p=>p.Group==group).ToArray();var result=group=="LAN"?record.Lan:group=="WAN"?record.Ping:record.Dns;
        return global::SysAdminAssistant.Core.ProductText.Get($"Результат {result}; проверено с сервера {record.CheckedAt:dd.MM.yyyy HH:mm:ss}; задержка уведомления {delay} мин.\n")+(matching.Length==0?global::SysAdminAssistant.Core.ProductText.Get("Поадресные результаты не сохранены.\n"):string.Join("\n",matching.Take(6).Select(p=>p.Host+": "+p.Result))+(matching.Length>6?global::SysAdminAssistant.Core.ProductText.Get($"\nЕщё {matching.Length-6} результатов — в окне LAN / WAN / DNS."):"")+"\n")+global::SysAdminAssistant.Core.ProductText.Get("Что проверить: ")+Steps(group)+global::SysAdminAssistant.Core.ProductText.Get("\nЗакроется после свежих успешных проверок. Отсутствие ICMP-ответа не устанавливает причину.");
    }
    public static string Device(Device d,int delay)=>global::SysAdminAssistant.Core.ProductText.Get($"Адрес {d.Host}; ICMP-статус {d.Status}; проверено {d.CheckedAt:dd.MM.yyyy HH:mm:ss}; задержка {delay} мин.\nЧто проверить: питание узла, маршрут и VLAN/туннель с сервера Assistant, правила ICMP и работу нужного приложения отдельным TCP/HTTP-запросом. Отсутствие ping-ответа не доказывает отказ сервера. Закроется после свежего успешного опроса/отчёта агента.");
    public static string Service(ServiceEntry service,int delay)
    {
        var target=global::SysAdminAssistant.Core.ProductText.Get("адрес из настроек");if(Uri.TryCreate(service.Type=="TCP"?"tcp://"+service.Address:service.Address,UriKind.Absolute,out var uri))target=uri.Host+":"+uri.Port;
        return global::SysAdminAssistant.Core.ProductText.Get($"Тип {service.Type}; узел {target}; результат {service.Status}; проверено {service.CheckedAt:dd.MM.yyyy HH:mm:ss}; задержка {delay} мин.\nЧто проверить: доступ к порту с сервера Assistant, состояние приложения, DNS, TLS и журналы службы. HTTP 4xx/5xx и недоступный TCP — разные причины; проверьте точный код. Не отключайте TLS ради успешной проверки. Закроется после успешного запроса. Параметры URL и учётные данные в уведомление не включаются.");
    }
}
