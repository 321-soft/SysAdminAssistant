namespace SysAdminAssistant.Infrastructure;
public static class AccessRules
{
    public static readonly string[] Modules = ["overview","devices","services","payments","reports","jobs","routers","vault","settings","inventory","dmarc","workstations"];
    public static readonly string[] Labels = ["Обзор","Устройства","Сервисы","Счета","Отчёты","Задания бэкапов","Коммутаторы / сеть","Пароли","Настройки","Инвентаризация","DMARC","Рабочие станции"];
    public static string PageModule(int page) => page == 11 ? "reports" : page == 4 ? "reports" : page switch { 0=>"overview",1=>"devices",2=>"services",3=>"payments",5=>"jobs",6=>"routers",7=>"vault",8=>"settings",9=>"inventory",10=>"dmarc",12=>"ksc",13=>"workstations",_=>"" };
    public static int Level(UserView user,string module) => user.Administrator ? 2 : user.Permissions?.GetValueOrDefault(module,0) ?? 0;
    public static void Require(UserView user,string module,int level=1) { if(Level(user,module)<level) throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Нет права на раздел «")+module+global::SysAdminAssistant.Core.ProductText.Get("» (нужен уровень ")+level+")"); }
    public static Dictionary<string,int> Validate(Dictionary<string,int>? values)
    {
        values ??= new();
        if(values.Keys.Any(x=>!Modules.Contains(x)) || values.Values.Any(x=>x is <0 or >2)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверные права разделов"));
        return Modules.ToDictionary(x=>x,x=>values.GetValueOrDefault(x));
    }
    public static string ResourceModule(string resource) => resource switch {
        "devices"=>"devices", "services" or "connectivity"=>"services", "payments"=>"payments", "jobs"=>"jobs",
        "rules" or "profiles" or "incidents"=>"reports", "employees" or "assets" or "acts" or "actLines"=>"inventory", _=>"" };
    public static void Command(UserView user,string command)
    {
        if(user.Administrator)return;
        if(command is "workstations-list" or "workstations-config"){Require(user,"workstations");return;}
        if(command.StartsWith("agent-trust-",StringComparison.Ordinal))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Только главный администратор управляет доверием HTTPS"));
        if(command.StartsWith("agent-",StringComparison.Ordinal)){Require(user,"devices",command is "agent-view" or "agent-events" or "agent-metric-history" or "agent-journal-history"?1:2);return;}
        if(command=="payment-history"){Require(user,"payments");return;}
        if(command is "payment-pay" or "payment-cancel"){Require(user,"payments",2);return;}
        if(command is "mail-poll" or "mail-status") { if(Level(user,"reports")<1 && Level(user,"dmarc")<1 && Level(user,"settings")<1)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Нет доступа к почте"));return; }
        if(command is "mail-bind-preview" or "mail-bind"){Require(user,"reports",2);return;}
        if(command is "device-updates" or "device-update-installed"){if(Level(user,"devices")<1&&Level(user,"routers")<1)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Нет доступа к устройствам"));return;}
        if(command=="device-problems"){Require(user,"devices");return;}
        if(command=="device-resolve"){Require(user,"reports",2);Require(user,"devices");return;}
        if(command=="device-reanalyze"){Require(user,"reports",2);Require(user,"devices",2);return;}
        if(command=="job-reanalyze"){Require(user,"reports",2);Require(user,"jobs",2);return;}
        if(command=="mail-view")return; // Report category checked before reading.
        if(command=="mail-receipt-repair"){Require(user,"settings",2);return;}
        if(command=="mail")return; // Payload purpose is checked before ingestion.
        if(command is "mail-reprocess-selected" or "mail-recheck"){Require(user,"reports",2);return;}
        if(command=="reprocess") {if(Level(user,"reports")<2 && Level(user,"dmarc")<2)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Нет права изменения отчётов"));return;}
        if(command=="hardware-history"){if(Level(user,"devices")<1&&Level(user,"routers")<1&&Level(user,"workstations")<1)throw new UnauthorizedAccessException("Нет доступа к мониторингу");return;}
        if(command=="inventory-hardware-history"){Require(user,"inventory");return;}
        if(command=="inventory-photo-read") { Require(user,"inventory");return; }
        if(command.StartsWith("inventory-",StringComparison.Ordinal)) { Require(user,"inventory",2);return; }
        if(command is "router-audit-ignore" or "router-audit-policy" or "router-connection-save" or "router-problem-resolve"){Require(user,"routers",2);return;}
        if(command.StartsWith("router-",StringComparison.Ordinal)) { if(command is not ("router-audit-view" or "router-audit-refresh" or "router-targets" or "router-snapshot" or "router-firewall-list" or "router-monitor-list" or "router-monitor-refresh" or "router-connection" or "router-dictionaries" or "router-events"))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("MikroTik доступен только для мониторинга"));Require(user,"routers");return; }
        if(command is "telegram-pin-status" or "server-heartbeat-status"){Require(user,"settings",1);return;}
        if(command is "mail-test" or "telegram-test" or "sendmail" or "dmarc-senders") { Require(user,"settings",2);return; }
        throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Операция доступна главному администратору"));
    }
}
