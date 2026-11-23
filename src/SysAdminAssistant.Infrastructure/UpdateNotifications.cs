namespace SysAdminAssistant.Infrastructure;
public static class UpdateNotifications
{
 public static bool IsAvailability(string key)=>key.StartsWith("os-updates:",StringComparison.Ordinal)||key.StartsWith("mail-updates:",StringComparison.Ordinal);
 public static void Suppress(NetworkState state){foreach(var n in state.Notifications.Where(n=>n.HandledAt==null&&IsAvailability(n.EntityKey))){n.HandledAt=DateTime.UtcNow;n.Result="Наличие обновлений отображается только в карточке устройства";}}
}
