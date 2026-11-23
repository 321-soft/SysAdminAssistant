using SysAdminAssistant.Core;
internal static class DashboardSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var now=new DateTime(2026,10,16,12,0,0);
        var device=new Device{Id=17,Name="Server",Host="host",Enabled=true,Status="ONLINE",CheckedAt=now};
        check("Dashboard online device is green",DashboardState.Device(device,[],now,60,true).Lamp==LampState.Green);
        var service=new ServiceEntry{DeviceId=17,Name="HTTPS",Status="OFFLINE",CheckedAt=now};
        check("Dashboard linked service warning precedes healthy device",DashboardState.Device(device,[service],now,60,true).Priority==1);
        service.DeviceId=18;
        check("Dashboard unrelated service does not affect device",DashboardState.Device(device,[service],now,60,true).Lamp==LampState.Green);
        device.Status="OFFLINE";
        check("Dashboard offline device first",DashboardState.Device(device,[],now,60,true).Priority==0);
        device.CheckedAt=now.AddMinutes(-10);
        check("Dashboard old failure becomes unknown",DashboardState.Device(device,[],now,60,true).Lamp==LampState.Gray);
        device.MaintenanceUntil=now.AddHours(1);
        check("Dashboard maintenance suppresses alarm",DashboardState.Device(device,[],now,60,true).Priority==4);
        device.MaintenanceUntil=now.AddSeconds(-1);device.CheckedAt=now;
        check("Dashboard expired maintenance restores alarm",DashboardState.Device(device,[],now,60,true).Priority==0);
        check("Dashboard disabled monitoring is unknown",DashboardState.Device(device,[],now,60,false).Lamp==LampState.Gray);
        device.Enabled=false;
        check("Dashboard archived device retained at bottom",DashboardState.Device(device,[],now,60,true).Priority==5);
        var bill=new Payment{DueDate=now.Date.AddDays(-1)};
        check("Dashboard overdue bill",DashboardState.Payment(bill,now,PaymentCalendar.DefaultWeekendMask,7).Priority==0);
        bill.DueDate=now.Date;
        check("Dashboard due today",DashboardState.Payment(bill,now,PaymentCalendar.DefaultWeekendMask,7).Priority==1);
        bill.DueDate=now.Date.AddDays(1);
        check("Dashboard weekend bill shifts to working day",PaymentCalendar.EffectiveDueDate(bill.DueDate,PaymentCalendar.DefaultWeekendMask)==new DateTime(2026,10,19));
        check("Dashboard upcoming bill",DashboardState.Payment(bill,now,PaymentCalendar.DefaultWeekendMask,7).Priority==2);
        bill.DueDate=now.Date.AddDays(20);
        check("Dashboard future bill is planned",DashboardState.Payment(bill,now,PaymentCalendar.DefaultWeekendMask,7).Priority==3);
        bill.Paid=true;bill.DueDate=now.Date.AddYears(-1);
        check("Dashboard paid bill never overdue",DashboardState.Payment(bill,now,PaymentCalendar.DefaultWeekendMask,7).Priority==4);
    }
}
