using System.Windows.Data;
namespace SysAdminAssistant.Desktop;
public sealed class InverseBooleanConverter:IValueConverter {
 public object Convert(object value,System.Type targetType,object parameter,System.Globalization.CultureInfo culture)=>value is bool cancelled&&!cancelled;
 public object ConvertBack(object value,System.Type targetType,object parameter,System.Globalization.CultureInfo culture)=>throw new System.NotSupportedException();
}

public sealed class ReceiptMoneyConverter:IValueConverter {
 public object Convert(object value,System.Type targetType,object parameter,System.Globalization.CultureInfo culture)=>value is long amount?(amount/100m).ToString("N2"):"Не указана";
 public object ConvertBack(object value,System.Type targetType,object parameter,System.Globalization.CultureInfo culture)=>throw new System.NotSupportedException();
}
