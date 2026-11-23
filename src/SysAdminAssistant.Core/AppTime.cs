namespace SysAdminAssistant.Core;
public static class AppTime
{
 private static TimeZoneInfo zone=TimeZoneInfo.Local;private static readonly AsyncLocal<TimeZoneInfo?> scoped=new();private static TimeZoneInfo Current=>scoped.Value??Volatile.Read(ref zone);
 public static string ZoneId=>Current.Id;
 public static TimeZoneInfo Resolve(string id){if(string.IsNullOrWhiteSpace(id)||id.Length>150)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выберите часовой пояс сервера"));try{return TimeZoneInfo.FindSystemTimeZoneById(id);}catch(Exception e)when(e is TimeZoneNotFoundException or InvalidTimeZoneException){throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестный часовой пояс: ")+id);}}
 public static void Configure(string id)=>Volatile.Write(ref zone,Resolve(id));public static IDisposable Scope(string id){var old=scoped.Value;scoped.Value=Resolve(id);return new Restore(old);}private sealed class Restore(TimeZoneInfo? old):IDisposable{public void Dispose()=>scoped.Value=old;}
 public static DateTime Now=>FromUtc(DateTime.UtcNow);public static DateTime Today=>DateTime.SpecifyKind(Now.Date,DateTimeKind.Unspecified);
 public static DateTime InZone(DateTime utc,string id)=>TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc,DateTimeKind.Utc),Resolve(id));public static DateTime FromUtc(DateTime utc)=>TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc,DateTimeKind.Utc),Current);public static DateTime? FromUtc(DateTime? utc)=>utc.HasValue?FromUtc(utc.Value):null;
 public static DateTime FromOffset(DateTimeOffset value)=>FromUtc(value.UtcDateTime);public static DateTime? FromOffset(DateTimeOffset? value)=>value.HasValue?FromOffset(value.Value):null;
 public static DateTime ToUtc(DateTime value)=>ToUtc(value,Current);public static DateTime ToUtc(DateTime value,TimeZoneInfo source){if(value.Kind==DateTimeKind.Utc)return value;if(value.Kind==DateTimeKind.Local)return value.ToUniversalTime();if(source.IsInvalidTime(value))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Указанное время попадает в пропущенный час DST"));return TimeZoneInfo.ConvertTimeToUtc(value,source);}
 public static DateTime ToServerTime(this DateTime utc)=>FromUtc(utc.Kind==DateTimeKind.Local?utc.ToUniversalTime():utc);public static DateTimeOffset ToServerTime(this DateTimeOffset value)=>TimeZoneInfo.ConvertTime(value,Current);
}
