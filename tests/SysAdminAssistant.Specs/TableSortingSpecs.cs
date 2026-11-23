using SysAdminAssistant.Core;
internal static class TableSortingSpecs
{
    private sealed record Row(string Uptime,long UptimeSort);
    public static void Run(Action<string,bool> check)
    {
        foreach(var (name,a,b,path) in new (string,object,object,string)[]{
            ("numbers 11 below 20","11","20","Count"),("numbers 9 below 50","9","50","Count"),
            ("Russian days","9 дней","50 дней","Uptime"),("English days","9 days","50 days","Uptime"),
            ("hours below days","23 hours","2 days","Uptime"),("compound duration","1 д. 23 ч. 59 мин.","2 д. 00 ч. 00 мин.","Uptime"),
            ("megabytes below gigabytes","900 МБ","2 ГБ","Capacity"),("English binary capacity","900 MiB","2 GiB","Capacity"),
            ("gigabytes below terabytes","900 GB","2 TB","Capacity"),("decimal percentages","9,5%","11,2%","Percent"),
            ("negative money","-20 ₽","-9 ₽","Amount"),("large numbers","99999999999999999999999999","100000000000000000000000000","Count"),
            ("natural device names","ПК-2","ПК-11","Name"),("device names containing units","NAS-2TB","NAS-900GB","Name"),("semantic versions","0.46.9","0.46.14","Agent"),("two-component versions","7.9","7.25","Version"),("grouped capacity","900 GB","1 024 GB","Capacity"),
            ("IPv4 bytes","192.168.1.9","192.168.1.20","Ip"),("IPv6 bytes","2001:db8::f","2001:db8::10","Address"),
            ("Russian displayed dates","31.12.2025 12:00","01.01.2026 11:00","Seen"),
            ("typed seconds",9L*86400,50L*86400,"Uptime"),("typed bytes",900000000L,2000000000L,"Size")
        })check("Table sort orders "+name+" in both directions",TableValues.Compare(a,b,path)<0&&TableValues.Compare(b,a,path)>0);
        var raw=new Row("not available",9*86400);
        check("Table sorting prefers a raw reading over presentation text",Equals(TableValues.Read(raw,"Uptime"),9L*86400));
        check("Table sorting safely handles a missing nested reading",TableValues.Read(new{Station=(object?)null},"Station.Sample.Uptime")==null);
        var values=new[]{"50","11","9","20","2"};Array.Sort(values,(a,b)=>TableValues.Compare(a,b));
        check("Numeric table ordering is coherent across the whole list",values.SequenceEqual(new[]{"2","9","11","20","50"}));
        var screenshot=new[]{"9 д. 12 ч. 17 мин.","8 д. 09 ч. 10 мин.","70 д. 02 ч. 50 мин.","7 д. 03 ч. 42 мин.","7 д. 00 ч. 59 мин."};
        var expected=new[]{screenshot[4],screenshot[3],screenshot[1],screenshot[0],screenshot[2]};
        Array.Sort(screenshot,(a,b)=>TableValues.Compare(a,b,"Uptime"));
        check("Screenshot uptime sequence sorts by duration",screenshot.SequenceEqual(expected));
        var versions=new[]{"0.46.9.0","0.46.14.0","0.46.2.0","0.46.13.0"};
        Array.Sort(versions,(a,b)=>TableValues.Compare(a,b,"Agent"));
        check("Agent release versions sort by numeric components",versions.SequenceEqual(new[]{"0.46.2.0","0.46.9.0","0.46.13.0","0.46.14.0"}));
        check("Unavailable readings do not compare as zero",TableValues.Missing("—")&&TableValues.Missing(null)&&!TableValues.Missing(0));
    }
}
