using System.Globalization;
using System.Text.RegularExpressions;

namespace SysAdminAssistant.Core;

public sealed class UsbCatalog
{
    private readonly Dictionary<string,string> vendors=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,string> products=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex UsbId=new(@"\AVID_([0-9a-f]{4})&PID_([0-9a-f]{4})\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    public UsbCatalog(IEnumerable<string> lines)
    {
        string vendor="";
        foreach(var line in lines)
        {
            if(line.Length<6||line[0]=='#')continue;
            bool device=line[0]=='\t';
            if(device&&line[1]=='\t')continue;
            int start=device?1:0;
            if(line.Length<=start+4||!char.IsWhiteSpace(line[start+4])||
               !ushort.TryParse(line.AsSpan(start,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out _))
            {
                if(!device)vendor="";
                continue;
            }
            var name=WindowsUpdateResults.Clean(line[(start+4)..].Trim(),120);
            if(name.Length==0)continue;
            var code=line.Substring(start,4);
            if(!device){vendor=code;vendors[code]=name;}
            else if(vendor.Length>0)products[vendor+code]=name;
        }
    }
    public StationPeripheral Resolve(StationPeripheral item)
    {
        var match=UsbId.Match(item.ModelCode);
        if(!match.Success)return item;
        var vid=match.Groups[1].Value;var pid=match.Groups[2].Value;
        var vendor=vendors.GetValueOrDefault(vid);
        var product=products.GetValueOrDefault(vid+pid);
        if(vendor==null)return item;
        vendor=WindowsUpdateResults.Clean(vendor,50);
        // Prefer the device's named descriptor over a database alias.
        var generic=item.Name.Contains("HID",StringComparison.OrdinalIgnoreCase)||
            item.Name.Contains("совместим",StringComparison.OrdinalIgnoreCase)||item.Name.Contains("generic",StringComparison.OrdinalIgnoreCase)||
            item.Name is "USB Input Device" or "Устройство ввода USB" or "USB Composite Device" or "Составное USB устройство" or "USB Device";
        var manufacturer=string.IsNullOrEmpty(item.Manufacturer)||generic?vendor:item.Manufacturer;
        if(!generic||string.IsNullOrWhiteSpace(product))return item with {Manufacturer=manufacturer};
        var code=$"{vid.ToUpperInvariant()}:{pid.ToUpperInvariant()}";
        string name;
        if(product.Contains("Receiver",StringComparison.OrdinalIgnoreCase)&&item.Kind is ("Mouse" or "Keyboard" or "Touch"))
            name=$"{item.Name} · через {vendor} {product}";
        else if(product is "Mouse" or "Keyboard" or "USB Device" or "Optical Mouse" or "HID device")
            name=$"{vendor} · {item.Name} · {code}";
        else name=product.StartsWith(vendor,StringComparison.OrdinalIgnoreCase)?product:vendor+" "+product;
        return item with {Name=WindowsUpdateResults.Clean(name,120),Manufacturer=manufacturer};
    }
}
