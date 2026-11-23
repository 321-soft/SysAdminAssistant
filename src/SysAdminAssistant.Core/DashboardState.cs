namespace SysAdminAssistant.Core;

public sealed record DeviceHealth(int Priority, LampState Lamp, string Status, string Detail);
public static class DashboardState
{
    public static DeviceHealth Device(Device d, IEnumerable<ServiceEntry> services, DateTime now, int pollSeconds, bool monitoring)
    {
        if (!d.Enabled) return new(5, LampState.Gray, "Архив", "Автоматические проверки отключены");
        if (d.MaintenanceUntil > now) return new(4, LampState.Gray, "Обслуживание", "До " + d.MaintenanceUntil.Value.ToString("dd.MM HH:mm"));
        if (!monitoring) return new(2, LampState.Gray, "Проверки отключены", "Включите мониторинг в настройках");
        if(d.AgentHealth=="MISSING")return new(2,LampState.Amber,"Нет данных агента",d.AgentSummary);
        if(d.AgentLastSeen is DateTime seen && seen<=now && now-seen<TimeSpan.FromMinutes(6) && d.AgentHealth!="")return d.AgentHealth switch {
            "CRITICAL"=>new(0,LampState.Red,"Проблема · агент",d.AgentSummary),
            "WARNING"=>new(1,LampState.Amber,"Предупреждение · агент",d.AgentSummary),
            "OK"=>new(3,LampState.Green,"Работает · агент",d.AgentSummary),
            _=>new(2,LampState.Gray,"Состояние агента неизвестно",d.AgentSummary)};
        var cutoff = now.AddSeconds(-Math.Max(120, pollSeconds * 3));
        if (d.CheckedAt == null || d.CheckedAt < cutoff) return new(2, LampState.Gray, d.CheckedAt == null ? "Не проверено" : "Данные устарели", d.Host.Length == 0 ? "Не указан адрес" : "Ожидается проверка сервером");
        if (d.Status == "OFFLINE") return new(0, LampState.Red, "Недоступно", (d.StatusSince is DateTime since ? "С " + since.ToString("dd.MM HH:mm") + " · " : "") + d.LastError);
        if (d.Status != "ONLINE") return new(2, LampState.Gray, "Не определено", d.LastError);
        var linked = services.Where(s => s.DeviceId == d.Id).ToArray();
        var failed = linked.Where(s => s.CheckedAt >= cutoff && s.Status != "ONLINE" && s.Status != "UNKNOWN").ToArray();
        if (failed.Length > 0) return new(1, LampState.Amber, "Проблема сервиса", string.Join(", ", failed.Select(s => s.Name)));
        if (linked.Any(s => s.CheckedAt == null || s.CheckedAt < cutoff || s.Status == "UNKNOWN")) return new(2, LampState.Gray, "Сервисы не проверены", "Устройство отвечает; состояние сервисов неизвестно");
        return new(3, LampState.Green, "Работает", linked.Length == 0 ? "ICMP: ответ получен" : "ICMP и связанные сервисы работают");
    }
    public static DeviceHealth WithProblems(Device device,DeviceHealth health,IEnumerable<Incident> problems,DateTime now,KscCounts? ksc=null)
    {
        if(!device.Enabled || device.MaintenanceUntil>now)return health;
        var open=problems.Where(i=>!i.Closed).ToArray();
        var critical=open.Count(i=>i.Severity==Severity.Critical)+(ksc?.Critical??0);var warnings=open.Count(i=>i.Severity==Severity.Warning)+(ksc?.Warnings??0);
        var counts=$"Открыто: критических {critical}, предупреждений {warnings}";
        if(critical>0)return new(0,LampState.Red,$"Критических событий: {critical}",counts+" · "+health.Status);
        if(warnings>0 && health.Lamp!=LampState.Red)return new(1,LampState.Amber,$"Предупреждений: {warnings}",counts+" · "+health.Status);
        return health;
    }
    public static (int Priority, LampState Lamp, string Status) Payment(Payment p, DateTime today, int weekendMask, int soonDays)
    {
        if (p.Paid) return (4, LampState.Green, "Оплачен");
        var due = PaymentCalendar.EffectiveDueDate(p.DueDate, weekendMask).Date;
        if (due < today.Date) return (0, LampState.Red, "Просрочен");
        if (due == today.Date) return (1, LampState.Amber, "Оплатить сегодня");
        if (PaymentCalendar.WorkingDaysUntil(today,due,weekendMask) <= Math.Clamp(soonDays, 1, 90)) return (2, LampState.Amber, "Скоро · через " + (due - today.Date).Days + " дн.");
        return (3, LampState.Green, "До срока оплаты ещё есть время");
    }
}

public sealed record KscCounts(int Critical,int Warnings);
