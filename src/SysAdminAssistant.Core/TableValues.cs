using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SysAdminAssistant.Core;

/// <summary>Ordering of product values; never reorders or rewrites persisted data.</summary>
public static class TableValues
{
    private static readonly ConcurrentDictionary<(Type,string),PropertyInfo?> Properties=new();
    private static readonly Regex Quantity=new(@"(?<n>[+-]?\d(?:[\d ]*\d)?(?:[.,]\d+)?)\s*(?<u>tib|gib|mib|kib|tb|gb|mb|kb|тб|гб|мб|кб|bytes?|байт(?:ов|а)?|б|%|days?|дн(?:ей|я)?|д\.?|hours?|hrs?|ч(?:ас(?:ов|а)?)?\.?|h|minutes?|mins?|мин\.?|seconds?|secs?|сек\.?|с\.?|d|m|s)(?![\p{L}])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    public static object? Read(object row,string path)
    {
        // Rows can expose a raw key while displaying a formatted value.
        foreach(var alias in new[]{path+"Sort","Sort"+path})
            if(!path.Contains('.')&&Property(row,alias) is { } raw)return raw.GetValue(row);
        object? value=row;
        foreach(var part in path.Split('.')){if(value==null)return null;var property=Property(value,part);if(property==null)return null;value=property.GetValue(value);}
        return value;
    }
    private static PropertyInfo? Property(object value,string name)
        =>Properties.GetOrAdd((value.GetType(),name),key=>key.Item1.GetProperty(key.Item2,BindingFlags.Instance|BindingFlags.Public));
    public static bool Missing(object? value)=>value==null||value is string text&&(string.IsNullOrWhiteSpace(text)||text.Trim() is "—" or "–" or "-");
    public static int Compare(object? a,object? b,string path="")
    {
        if(ReferenceEquals(a,b))return 0;
        if(Missing(a))return Missing(b)?0:1;if(Missing(b))return -1;
        if(a is DateTime dateA&&b is DateTime dateB)return dateA.CompareTo(dateB);
        if(a is DateTimeOffset offsetA&&b is DateTimeOffset offsetB)return offsetA.CompareTo(offsetB);
        if(a is TimeSpan spanA&&b is TimeSpan spanB)return spanA.CompareTo(spanB);
        if(a is Version versionA&&b is Version versionB)return versionA.CompareTo(versionB);
        if(a is not string&&b is not string&&a is IComparable comparable&&a.GetType()==b!.GetType())return comparable.CompareTo(b);
        var x=Convert.ToString(a,CultureInfo.InvariantCulture)??"";var y=Convert.ToString(b,CultureInfo.InvariantCulture)??"";
        if((path.Contains("Ip",StringComparison.OrdinalIgnoreCase)||path.Contains("Address",StringComparison.OrdinalIgnoreCase))&&IPAddress.TryParse(x,out var ipA)&&IPAddress.TryParse(y,out var ipB)){
            var aa=ipA.GetAddressBytes();var bb=ipB.GetAddressBytes();var length=aa.Length.CompareTo(bb.Length);return length!=0?length:aa.AsSpan().SequenceCompareTo(bb);
        }
        if(path.EndsWith("Name",StringComparison.OrdinalIgnoreCase)||path is "Title" or "Computer" or "Host" or "Serial" or "Model")return Natural(x,y);
        if(DateTime.TryParseExact(x,["dd.MM.yyyy HH:mm:ss","dd.MM.yyyy HH:mm","dd.MM.yyyy","yyyy-MM-dd HH:mm:ss","yyyy-MM-dd","O"],CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var dx)
            &&DateTime.TryParseExact(y,["dd.MM.yyyy HH:mm:ss","dd.MM.yyyy HH:mm","dd.MM.yyyy","yyyy-MM-dd HH:mm:ss","yyyy-MM-dd","O"],CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var dy))return dx.CompareTo(dy);
        if((path.Contains("Version",StringComparison.OrdinalIgnoreCase)||path.Contains("Agent",StringComparison.OrdinalIgnoreCase)||path.Contains("RouterOs",StringComparison.OrdinalIgnoreCase)||path.Contains("Firmware",StringComparison.OrdinalIgnoreCase))&&Version.TryParse(x.TrimStart('v','V'),out var vx)&&Version.TryParse(y.TrimStart('v','V'),out var vy))return vx.CompareTo(vy);
        if(Measure(x,out var mx,out var kindX)&&Measure(y,out var my,out var kindY)&&kindX==kindY)return mx.CompareTo(my);
        return Natural(x,y);
    }
    private static bool Measure(string value,out decimal number,out int kind)
    {
        number=0;kind=0;
        var text=value.Replace("\u00a0","").Replace("\u202f","");
        var matches=Quantity.Matches(text);
        foreach(Match match in matches){
            var unit=match.Groups["u"].Value.ToLowerInvariant().TrimEnd('.');
            decimal factor;int category;
            if(unit is "tb" or "тб" or "tib"){factor=unit=="tib"?1099511627776m:1000000000000m;category=1;}
            else if(unit is "gb" or "гб" or "gib"){factor=unit=="gib"?1073741824m:1000000000m;category=1;}
            else if(unit is "mb" or "мб" or "mib"){factor=unit=="mib"?1048576m:1000000m;category=1;}
            else if(unit is "kb" or "кб" or "kib"){factor=unit=="kib"?1024m:1000m;category=1;}
            else if(unit is "d" or "day" or "days" or "д" or "дня" or "дней" or "дн"){factor=86400;category=2;}
            else if(unit is "h" or "hour" or "hours" or "hr" or "hrs" or "ч" or "час" or "часа" or "часов"){factor=3600;category=2;}
            else if(unit is "m" or "min" or "mins" or "minute" or "minutes" or "мин"){factor=60;category=2;}
            else if(unit is "s" or "sec" or "secs" or "second" or "seconds" or "сек" or "с"){factor=1;category=2;}
            else{factor=1;category=unit=="%"?3:1;}
            if(!decimal.TryParse(match.Groups["n"].Value.Replace(" ","").Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount))continue;
            try{if(kind==0){number=amount*factor;kind=category;}else if(kind==2&&category==2)number+=amount*factor;}
            catch(OverflowException){number=0;kind=0;return false;}
        }
        if(kind!=0)return true;
        if(text.Contains(':')&&TimeSpan.TryParse(text,CultureInfo.InvariantCulture,out var duration)){kind=2;number=duration.Ticks/10000000m;return true;}
        foreach(var currency in new[]{"₽","$","€","руб.","руб","USD","EUR","RUB"})text=text.Replace(currency,"",StringComparison.OrdinalIgnoreCase);
        return decimal.TryParse(text.Replace(" ","").Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out number);
    }
    public static int Natural(string a,string b)
    {
        int i=0,j=0;
        while(i<a.Length&&j<b.Length){
            if(char.IsAsciiDigit(a[i])&&char.IsAsciiDigit(b[j])){
                int endA=i,endB=j;while(endA<a.Length&&char.IsAsciiDigit(a[endA]))endA++;while(endB<b.Length&&char.IsAsciiDigit(b[endB]))endB++;
                while(i<endA-1&&a[i]=='0')i++;while(j<endB-1&&b[j]=='0')j++;
                var length=(endA-i).CompareTo(endB-j);if(length!=0)return length;
                var number=a.AsSpan(i,endA-i).SequenceCompareTo(b.AsSpan(j,endB-j));if(number!=0)return number;
                i=endA;j=endB;
            }else{var character=char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));if(character!=0)return character;i++;j++;}
        }
        return (a.Length-i).CompareTo(b.Length-j);
    }
}
