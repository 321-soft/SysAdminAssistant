using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    // Both panes get bounded space. Neither the description nor a long report can cover the list.
    private sealed class ListInspector : Grid
    {
        public ListInspector(DataGrid table,UIElement details,string title="Список",string emptyTitle="Нет записей",string emptyHint="Измените поиск или выберите другой раздел.")
        {
            ClipToBounds=true;
            var list=new DockPanel();var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);list.Children.Add(heading);
            heading.Children.Add(new TextBlock{Text=title,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,8)});
            heading.Children.Add(TableSearch(table));list.Children.Add(TableSurface(table,emptyTitle,emptyHint));
            var detail=new DockPanel();var navigation=new WrapPanel{Margin=new(0,0,0,10)};DockPanel.SetDock(navigation,Dock.Top);detail.Children.Add(navigation);
            var previous=UiButton("← Предыдущее");var next=UiButton("Следующее →");var buttons=new WrapPanel{MaxWidth=310};buttons.Children.Add(previous);buttons.Children.Add(next);navigation.Children.Add(buttons);
            var position=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brush("#64748B"),VerticalAlignment=VerticalAlignment.Center};navigation.Children.Add(position);detail.Children.Add(details);
            var card=new Border{Background=DesktopTheme.Brush("#FFFFFF"),BorderBrush=Brush("#DFE7F1"),BorderThickness=new(1),CornerRadius=new(10),Padding=new(16),Child=detail};
            var splitter=new GridSplitter{Background=Brush("#E3EAF3"),ShowsPreview=true,ResizeBehavior=GridResizeBehavior.PreviousAndNext,ToolTip="Перетащите границу, чтобы изменить размеры списка и описания"};
            Children.Add(list);Children.Add(splitter);Children.Add(card);
            bool? stacked=null;
            void Reflow(){var vertical=ActualWidth<900;if(stacked==vertical)return;stacked=vertical;RowDefinitions.Clear();ColumnDefinitions.Clear();
                if(vertical){ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star),MinHeight=0});RowDefinitions.Add(new(){Height=new(10)});RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star),MinHeight=0});SetColumn(list,0);SetColumn(splitter,0);SetColumn(card,0);SetRow(list,0);SetRow(splitter,1);SetRow(card,2);splitter.Height=6;splitter.Width=double.NaN;splitter.HorizontalAlignment=HorizontalAlignment.Stretch;splitter.VerticalAlignment=VerticalAlignment.Center;splitter.ResizeDirection=GridResizeDirection.Rows;splitter.Cursor=System.Windows.Input.Cursors.SizeNS;}
                else{RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star),MinWidth=300});ColumnDefinitions.Add(new(){Width=new(12)});ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star),MinWidth=340});SetRow(list,0);SetRow(splitter,0);SetRow(card,0);SetColumn(list,0);SetColumn(splitter,1);SetColumn(card,2);splitter.Width=6;splitter.Height=double.NaN;splitter.HorizontalAlignment=HorizontalAlignment.Center;splitter.VerticalAlignment=VerticalAlignment.Stretch;splitter.ResizeDirection=GridResizeDirection.Columns;splitter.Cursor=System.Windows.Input.Cursors.SizeWE;}
            }
            bool reflowQueued=false;
            void ScheduleReflow(){
                if(reflowQueued)return;
                reflowQueued=true;
                _=Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>{reflowQueued=false;if(IsLoaded)Reflow();}));
            }
            SizeChanged+=(_,_)=>ScheduleReflow();Loaded+=(_,_)=>ScheduleReflow();
            void Update(){var index=table.SelectedItem==null?-1:table.Items.IndexOf(table.SelectedItem);previous.IsEnabled=index>0;next.IsEnabled=index>=0&&index<table.Items.Count-1;position.Text=index<0?$"Записей: {table.Items.Count}":$"{index+1} из {table.Items.Count}";}
            void Move(int step){var i=table.SelectedItem==null?-1:table.Items.IndexOf(table.SelectedItem);var n=i+step;if(n<0||n>=table.Items.Count)return;table.SelectedItem=table.Items[n];table.ScrollIntoView(table.SelectedItem);}
            previous.Click+=(_,_)=>Move(-1);next.Click+=(_,_)=>Move(1);table.SelectionChanged+=(_,_)=>Update();((INotifyCollectionChanged)table.Items).CollectionChanged+=(_,_)=>Update();Update();
        }
    }
    private static void InspectorRows<T>(DataGrid table,IEnumerable<T> rows,Func<T,string> key) where T:class
    {
        var selected=table.SelectedItem is T value?key(value):null;var index=table.SelectedIndex;var sorts=TableSorting.Capture(table);
        table.ItemsSource=rows.ToArray();TableSorting.Apply(table,sorts);
        var visible=table.Items.Cast<T>().ToArray();table.SelectedItem=visible.FirstOrDefault(x=>key(x)==selected)??visible.ElementAtOrDefault(Math.Clamp(index,0,Math.Max(0,visible.Length-1)));
    }
    private static TextBox DetailText(string initial="Выберите строку в списке слева или сверху.")=>new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Text=initial,MinHeight=0,Margin=new(0),Padding=new(10),BorderThickness=new(0),Background=DesktopTheme.Brush("#FFFFFF")};
    private Window ReaderWindow(string title,UIElement content,double width=1200,double height=780,Window? owner=null)=>new DialogWindow(){Owner=owner??this,Title=title,Width=Math.Min(width,SystemParameters.WorkArea.Width-32),Height=Math.Min(height,SystemParameters.WorkArea.Height-32),MinWidth=Math.Min(760,SystemParameters.WorkArea.Width-32),MinHeight=Math.Min(540,SystemParameters.WorkArea.Height-32),Content=content,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.CanResizeWithGrip};
}
