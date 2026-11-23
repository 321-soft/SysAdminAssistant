using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.ComponentModel;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;

internal static class LanguageUi
{
 private static readonly string PathName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SysAdminAssistantClient","language.json");
 public static readonly DependencyProperty PreserveProperty=DependencyProperty.RegisterAttached("Preserve",typeof(bool),typeof(LanguageUi),new PropertyMetadata(false));
 public static void Preserve(DependencyObject element)=>element.SetValue(PreserveProperty,true);
 private static bool Raw(DependencyObject item){for(DependencyObject? p=item;p!=null;p=System.Windows.Media.VisualTreeHelper.GetParent(p))if((bool)p.GetValue(PreserveProperty))return true;return false;}
 public static string Load()
 {
  foreach(var path in new[]{PathName,Path.Combine(AppContext.BaseDirectory,"language.json")})try{if(File.Exists(path)){using var doc=JsonDocument.Parse(File.ReadAllText(path));return ProductText.Normalize(doc.RootElement.ValueKind==JsonValueKind.String?doc.RootElement.GetString():doc.RootElement.GetProperty("Language").GetString());}}catch(Exception e)when(e is IOException or JsonException or InvalidOperationException){}
  return ProductText.Normalize(Environment.GetEnvironmentVariable("SAA_LANGUAGE")??(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="ru"?"ru":"en"));
 }
 public static void Save(string language){Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);var temp=PathName+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(new{Language=ProductText.Normalize(language)}));File.Move(temp,PathName,true);}
 public static void Initialize(string? selected=null)
 {
  ProductText.DefaultLanguage=selected??Load();
  EventManager.RegisterClassHandler(typeof(FrameworkElement),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>Apply((FrameworkElement)sender)));
 }
 private static readonly DependencyProperty WatchingProperty=DependencyProperty.RegisterAttached("Watching",typeof(bool),typeof(LanguageUi),new PropertyMetadata(false));
 public static void RefreshWindows()
 {
  foreach(Window window in Application.Current.Windows){
   var seen=new HashSet<DependencyObject>();
   void Visit(DependencyObject item){
    if(!seen.Add(item))return;
    if(item is FrameworkElement element)Apply(element);
    if(item is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
     for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(item);i++)Visit(System.Windows.Media.VisualTreeHelper.GetChild(item,i));
    foreach(var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())Visit(child);
   }
   Visit(window);
  }
 }
 private static bool TextIsProduct(TextBlock block)
 {
  var binding=BindingOperations.GetBindingExpression(block,TextBlock.TextProperty);
  if(binding==null)return true;
  // Whitelist generated presentation fields. Entity names, mail and original log records remain verbatim.
  var path=binding.ParentBinding.Path?.Path??"";return path is "Status" or "StatusText" or "StateText" or "SeverityText" or "PaymentStatus" or "AvailabilityText" or "HealthText";
 }
 private static void Apply(FrameworkElement element)
 {
  if(Raw(element))return;
  if(element is Window window){if(window.Icon==null)window.Icon=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/"+typeof(LanguageUi).Assembly.GetName().Name+";component/Assets/SysAdminAssistant.ico"));window.Title=ProductText.Get(window.Title);window.Language=System.Windows.Markup.XmlLanguage.GetLanguage(ProductText.Language=="en"?"en-GB":"ru-RU");}
  if(element.ToolTip is string tip)element.ToolTip=ProductText.Get(tip);
  if(element is TextBlock text&&TextIsProduct(text)){
   void Update(){var before=text.Text;var translated=ProductText.Get(before);if(before!=translated)text.SetCurrentValue(TextBlock.TextProperty,translated);}
   Update();if((bool)text.GetValue(WatchingProperty))return;text.SetValue(WatchingProperty,true);var descriptor=DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));
   EventHandler changed=(_,_)=>Update();descriptor.AddValueChanged(text,changed);text.Unloaded+=Unload;
   void Unload(object sender,RoutedEventArgs args){descriptor.RemoveValueChanged(text,changed);text.SetValue(WatchingProperty,false);text.Unloaded-=Unload;}
  }
  if(element is ContentControl content&&content.Content is string label&&!BindingOperations.IsDataBound(content,ContentControl.ContentProperty))content.SetCurrentValue(ContentControl.ContentProperty,ProductText.Get(label));
  if(element is HeaderedContentControl heading&&heading.Header is string header&&!BindingOperations.IsDataBound(heading,HeaderedContentControl.HeaderProperty))heading.SetCurrentValue(HeaderedContentControl.HeaderProperty,ProductText.Get(header));
  if(element is HeaderedItemsControl menu&&menu.Header is string itemHeader&&!BindingOperations.IsDataBound(menu,HeaderedItemsControl.HeaderProperty))menu.SetCurrentValue(HeaderedItemsControl.HeaderProperty,ProductText.Get(itemHeader));
  if(element is DataGrid table)foreach(var column in table.Columns)if(column.Header is string columnHeader)column.Header=ProductText.Get(columnHeader);
 }
}
