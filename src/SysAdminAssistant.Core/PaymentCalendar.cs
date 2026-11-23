namespace SysAdminAssistant.Core;

public static class PaymentCalendar
{
    public const int DefaultWeekendMask = (1 << 5) | (1 << 6);
    public static void Validate(int weekendMask)
    {
        if (weekendMask < 0 || weekendMask >= 127) throw new ArgumentOutOfRangeException(nameof(weekendMask), global::SysAdminAssistant.Core.ProductText.Get("Оставьте хотя бы один рабочий день"));
    }
    public static bool IsWorkingDay(DateTime date, int weekendMask)
    {
        Validate(weekendMask);
        return (weekendMask & (1 << (((int)date.DayOfWeek + 6) % 7))) == 0;
    }
    public static DateTime EffectiveDueDate(DateTime scheduled, int weekendMask)
    {
        Validate(weekendMask); var date = scheduled.Date;
        for (var i = 0; i < 7; i++, date = date.AddDays(1)) if (IsWorkingDay(date, weekendMask)) return date;
        throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Не найден рабочий день"));
    }
    public static int WorkingDaysUntil(DateTime today,DateTime due,int weekendMask)
    {
        if(due.Date<=today.Date)return 0;var count=0;for(var date=today.Date.AddDays(1);date<=due.Date;date=date.AddDays(1)){if(IsWorkingDay(date,weekendMask))count++;if(count>90)return count;}return count;
    }
    public static bool NeedsReminder(Payment payment, DateTime today, int weekendMask) =>
        !payment.Paid && IsWorkingDay(today, weekendMask) && EffectiveDueDate(payment.DueDate, weekendMask) <= today.Date;
}
