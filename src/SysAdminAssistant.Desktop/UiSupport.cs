using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;

public sealed class UiValueConverter : IValueConverter
{
    public static readonly UiValueConverter Instance = new();
    private static readonly HashSet<string> ProductFields=new(StringComparer.Ordinal){"Status","StatusText","StateText","Result","Severity","HealthText","PaymentStatus","AvailabilityText","Kind","ReadState","State","Updates","Uptime"};
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DateTime d when d.Kind==DateTimeKind.Utc=>AppTime.FromUtc(d),
        Severity severity=>ProductText.Get(severity switch{Severity.Critical=>"Критическое",Severity.Warning=>"Предупреждение",Severity.Info=>"Информация",_=>"Не определён"}),
        TargetKind target=>ProductText.Get(target==TargetKind.Device?"Устройство":"Задание бэкапа"),
        bool flag=>ProductText.Get(flag?"Да":"Нет"),
        string text when parameter is string field&&ProductFields.Contains(field)=>ProductText.Get(text switch{"Critical"=>"Критическое","Warning"=>"Предупреждение","Info"=>"Информация","Unknown"=>"Не определён","ONLINE"=>"Доступно","OFFLINE"=>"Недоступно","UNKNOWN"=>"Не проверялось","ERROR"=>"Ошибка проверки",_=>text}),
        _=>value
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public partial class MainWindow
{
    private static Button UiButton(string title, string? style = null)
    {
        style ??= title.StartsWith("Добавить", StringComparison.Ordinal) ? "PrimaryButton" : title is "Удалить" or "Списать" ? "DangerButton" : null;
        var button = new Button { Content = ProductText.Get(title), ToolTip=ProductText.Get(title), MaxWidth=280 }; if (style != null) button.Style = (Style)Application.Current.FindResource(style); return button;
    }
    private Button RowAction(DataGrid table, string title, Func<Task> action, bool multiple = false,Func<bool>? applicable=null)
    {
        var button = ActionButton(title, action); button.SetValue(SelectionActionProperty,true);
        if(title is "Карточка" or "Просмотр / печать" or "Открыть письмо" or "Просмотр / печать акта" or "Подробности" or "Карточка / история")OpenOnDoubleClick(table,action);
        button.ToolTip = multiple ? "Выберите одну или несколько строк (Ctrl / Shift)" : "Выберите одну строку";
        void Update() => button.IsEnabled = button.Tag is true && (multiple ? table.SelectedItems.Count > 0 : table.SelectedItems.Count == 1) && (applicable?.Invoke()??true);
        table.SelectionChanged += (_, _) => Update(); Update(); return button;
    }
    private static void ActionContextMenu(DataGrid table,IEnumerable<Button> buttons,bool extended=true)
    {
        if(extended){table.SelectionMode=DataGridSelectionMode.Extended;table.SelectionUnit=DataGridSelectionUnit.FullRow;}
        var menu=new ContextMenu();
        var choices=new List<(Button Button,MenuItem Item)>();
        foreach(var button in buttons.Where(b=>b.GetValue(SelectionActionProperty) is true)){
            var item=new MenuItem{Header=button.Content,ToolTip=button.ToolTip};
            item.SetBinding(UIElement.IsEnabledProperty,new Binding("IsEnabled"){Source=button});
            item.Click+=(_,_)=>{if(button.IsEnabled)button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
            menu.Items.Add(item);choices.Add((button,item));
        }
        menu.Opened+=(_,_)=>{foreach(var (button,item) in choices)item.Visibility=button.IsEnabled?Visibility.Visible:Visibility.Collapsed;};
        table.ContextMenu=menu;
        table.PreviewMouseRightButtonDown+=(_,e)=>{
            var row=ItemsControl.ContainerFromElement(table,e.OriginalSource as DependencyObject) as DataGridRow;
            if(row==null)return;
            if(!row.IsSelected){table.SelectedItems.Clear();row.IsSelected=true;}
        };
    }
    private static UIElement TableSurface(UIElement content,string emptyTitle="Нет записей",string emptyHint="В списке пока нет записей или ничего не подходит под условия поиска.")
    {
        if (content is not DataGrid table) return content;
        var grid = new Grid(); grid.Children.Add(table);
        var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new(30) };
        empty.Children.Add(new TextBlock { Text = emptyTitle, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("#475569"), TextAlignment = TextAlignment.Center });
        empty.Children.Add(new TextBlock { Text = emptyHint, Foreground = Brush("#7B8CA3"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        empty.SetBinding(UIElement.VisibilityProperty, new Binding("HasItems") { Source = table, Converter = new EmptyVisibilityConverter() }); grid.Children.Add(empty);
        return new Border { Background = DesktopTheme.Brush("#FFFFFF"), BorderBrush = Brush("#DFE7F1"), BorderThickness = new(1), CornerRadius = new(8), Padding = new(1), Child = grid };
    }
    private static DockPanel ListSurface(UIElement content, IEnumerable<Button> buttons, UIElement? filter = null, bool search = false)
    {
        var root = new DockPanel();var top=new StackPanel();
        var allButtons=buttons.ToArray();
        var selected=allButtons.Where(button=>content is DataGrid&&button.GetValue(SelectionActionProperty) is true).ToArray();
        var common=allButtons.Except(selected).ToArray();
        if(content is DataGrid actionTable&&actionTable.ContextMenu==null)ActionContextMenu(actionTable,allButtons,false);
        if(allButtons.Length>0)top.Children.Add(SelectionActionBar.Create(content as DataGrid,common,selected));
        if(filter!=null)top.Children.Add(filter);
        if(search&&content is DataGrid table)top.Children.Add(TableSearch(table));
        DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        if(content is DataGrid list){var count=new TextBlock{FontSize=12,Foreground=Brush("#60738F"),Margin=new(12,9,12,0)};void Count()=>count.Text=$"Записей: {list.Items.Count}"+(list.SelectedItems.Count>0?$" · выбрано: {list.SelectedItems.Count}":"");list.SelectionChanged+=(_,_)=>Count();((System.Collections.Specialized.INotifyCollectionChanged)list.Items).CollectionChanged+=(_,_)=>Count();Count();DockPanel.SetDock(count,Dock.Bottom);root.Children.Add(count);}
        root.Children.Add(TableSurface(content));return root;
    }
    private static FrameworkElement TableSearch(DataGrid table)
    {
        var row = new DockPanel { Margin = new(0, 0, 0, 6) };
        var caption = new TextBlock { Text = ProductText.Get(Equals(table.Tag, "paged") ? "Поиск на странице" : "Поиск"), Foreground = Brush("#64748B"), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 8) }; DockPanel.SetDock(caption, Dock.Left); row.Children.Add(caption);
        var input = new TextBox { MinWidth = 80, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Поиск по видимым столбцам текущего списка", Tag = "ui-search-list" };
        var clear = UiButton("Сбросить"); DockPanel.SetDock(clear, Dock.Right); clear.Click += (_, _) => input.Clear(); row.Children.Add(clear);
        // Cache displayed text once per row and source. Do not retain a previous replica.
        object? source=null;
        var cache=new Dictionary<object,string[]>(ReferenceEqualityComparer.Instance);
        void Apply()
        {
            if (table.ItemsSource == null) return;
            if (!ReferenceEquals(source,table.ItemsSource)) { source=table.ItemsSource; cache.Clear(); }
            var view=CollectionViewSource.GetDefaultView(table.ItemsSource); if(!view.CanFilter)return;
            var columns=table.Columns.OfType<DataGridTextColumn>().Where(c=>c.Visibility==Visibility.Visible)
                .Select(c=>(c.Binding as Binding)?.Path?.Path).Where(p=>p!=null).ToArray();
            var query=input.Text.Trim();
            view.Filter=query.Length==0?null:item=>
            {
                if(!cache.TryGetValue(item,out var texts))
                {
                    texts=columns.Select(path=>(Path:path,Value:item.GetType().GetProperty(path!)?.GetValue(item)))
                        .Where(cell=>cell.Value!=null).Select(cell=>cell.Value is DateTime d?Display.Stamp(d):
                            System.Convert.ToString(UiValueConverter.Instance.Convert(cell.Value!,typeof(string),cell.Path!,CultureInfo.CurrentCulture),CultureInfo.CurrentCulture)??"").ToArray();
                    cache[item]=texts;
                }
                return texts.Any(text=>text.Contains(query,StringComparison.OrdinalIgnoreCase));
            };
        }
        var timer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(180) };
        timer.Tick+=(_,_)=>{timer.Stop();Apply();};
        input.TextChanged+=(_,_)=>{timer.Stop();if(input.Text.Length==0)Apply();else timer.Start();};
        var descriptor=DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty,typeof(DataGrid));
        EventHandler sourceChanged=(_,_)=>Apply();bool attached=false;
        row.Loaded+=(_,_)=>{if(!attached){descriptor.AddValueChanged(table,sourceChanged);attached=true;}Apply();};
        row.Unloaded+=(_,_)=>{timer.Stop();cache.Clear();source=null;if(attached){descriptor.RemoveValueChanged(table,sourceChanged);attached=false;}};
        row.Children.Add(input); return row;
    }
    private static T Copy<T>(T entity) where T : class => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(entity))!;
    private sealed class EmptyVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
