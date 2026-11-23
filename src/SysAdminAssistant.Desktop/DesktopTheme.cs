using System.IO;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows;
using System.Windows.Media;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Desktop;

internal static class DesktopTheme
{
    private sealed class ThemeColor(Color initial) : INotifyPropertyChanged
    {
        public Color Value { get; private set; }=initial;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Set(Color color)
        {
            if(Value==color)return;
            Value=color;
            PropertyChanged?.Invoke(this,new(nameof(Value)));
        }
    }
    private static readonly Dictionary<string,(SolidColorBrush Brush,ThemeColor Source)> brushes=new(StringComparer.OrdinalIgnoreCase);
    private static readonly string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SysAdminAssistantClient","theme.txt");
    public static bool Dark {get;private set;}
    public static void Initialize()
    {
        try{Dark=File.Exists(path)&&File.ReadAllText(path).Trim()=="dark";}catch(IOException){}catch(UnauthorizedAccessException){}
        var resources=Application.Current.Resources;
        foreach(var key in (string[])resources["ThemeColors"])resources["SaaTheme"+key]=Brush("#"+key);
        resources["SaaTheme60738F"]=Brush("#60738F");
        resources["SaaThemeA45A06"]=Brush("#A45A06");
        resources["SaaScrollTrack"]=Brush("#E9EEF5");
        resources["SaaScrollThumb"]=Brush("#7389A7");
        resources["SaaScrollThumbActive"]=Brush("#215CD9");
        resources["SaaThemePrimary"]=Brush("#215CD8");
        resources[SystemColors.WindowBrushKey]=Brush("#FFFFFF");
        resources[SystemColors.WindowTextBrushKey]=Brush("#17243C");
        resources[SystemColors.ControlBrushKey]=Brush("#EEF3F9");
        resources[SystemColors.ControlTextBrushKey]=Brush("#17243C");
        resources[SystemColors.HighlightBrushKey]=Brush("#215CD8");
        resources[SystemColors.HighlightTextBrushKey]=Brushes.White;
    }
    public static SolidColorBrush Brush(string hex)
    {
        if(brushes.TryGetValue(hex,out var entry))return entry.Brush;
        var source=new ThemeColor(Resolve(hex));
        var brush=new SolidColorBrush();
        // A bound color keeps WPF from freezing a theme brush when a style is sealed.
        BindingOperations.SetBinding(brush,SolidColorBrush.ColorProperty,new Binding(nameof(ThemeColor.Value)){Source=source,Mode=BindingMode.OneWay});
        brushes[hex]=(brush,source);
        return brush;
    }
    public static void Toggle()
    {
        bool next=!Dark;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,next?"dark":"light");File.Move(temp,path,true);}finally{File.Delete(temp);}
        Dark=next;
        foreach(var (hex,entry) in brushes.ToArray())entry.Source.Set(Resolve(hex));
    }
    private static Color Resolve(string hex)
    {
        var c=(Color)ColorConverter.ConvertFromString(hex);
        if(c.A==0)return c;
        if(!Dark){
            var light=hex.ToUpperInvariant() switch{
            "#71829D" or "#7C8DA6" or "#7B8CA3"=>"#536781",
            "#16A34A"=>"#18743D","#D97706"=>"#B54F00","#94A3B8"=>"#536781",_=>hex
            };
            return light==hex?c:(Color)ColorConverter.ConvertFromString(light);
        }
        var max=Math.Max(c.R,Math.Max(c.G,c.B));var min=Math.Min(c.R,Math.Min(c.G,c.B));
        string? replacement=hex.ToUpperInvariant() switch{
            "#FFFFFF"=>"#202A39","#F5F8FE"=>"#151D2A","#F4F7FB"=>"#151D2A","#FAFCFF"=>"#222D3D",
            "#215CD8"=>"#315BA8","#17243C"=>"#F1F6FF",
            "#215CD9" or "#1948AE" or "#1D4ED8" or "#173F76"=>"#A9CBFF",
            "#24364D" or "#293B5B" or "#1E375A"=>"#E0EBFC",
            "#53647C" or "#536781" or "#60738F" or "#64748B" or "#71829D" or "#7C8DA6" or "#7B8CA3"=>"#BFCDE2",
            "#94A3B8"=>"#AEBFD7","#E9EEF5"=>"#28364A","#7389A7"=>"#8299B8","#DC2626"=>"#FF838D","#D97706" or "#CC7000" or "#A45A06"=>"#F9BB69","#16A34A"=>"#63D895",
            "#E2EBFF" or "#E4EDFC" or "#E5EEFC"=>"#283E60","#EAF0F7" or "#EEF3F9"=>"#263449",
            "#DFE7F1" or "#D6DFEB" or "#CDD8E7" or "#E1E8F3" or "#EDF1F7"=>"#3D4C63",
            "#B42335"=>"#FF8C98","#FFF1F2"=>"#432835","#F2CAD0"=>"#744350",_=>null};
        if(replacement!=null)return (Color)ColorConverter.ConvertFromString(replacement);
        if(min>=210&&max-min<45)return Color.FromRgb(46,59,78);
        if(max<=195&&max-min<105)return Color.FromRgb(185,201,224);
        return c;
    }
}
public partial class MainWindow
{
    private void UpdateThemeButton()
    {
        ThemeButton.Content=DesktopTheme.Dark?"☀":"☾";
        var caption=ProductText.Get(DesktopTheme.Dark?"Переключить на светлую тему":"Переключить на тёмную тему");
        ThemeButton.ToolTip=caption;
        System.Windows.Automation.AutomationProperties.SetName(ThemeButton,caption);
    }
    private async void Theme_Click(object sender,RoutedEventArgs e)=>await Run(()=>{DesktopTheme.Toggle();UpdateThemeButton();return Task.CompletedTask;});
}
