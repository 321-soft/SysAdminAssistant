using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;
internal static class TableSorting
{
    private static bool initialized;
    private static readonly DependencyProperty SortsProperty=DependencyProperty.RegisterAttached("Sorts",typeof(SortDescription[]),typeof(TableSorting));
    private static readonly DependencyProperty InstalledProperty=DependencyProperty.RegisterAttached("Installed",typeof(bool),typeof(TableSorting),new PropertyMetadata(false));
    public static void Initialize(){if(initialized)return;initialized=true;EventManager.RegisterClassHandler(typeof(DataGrid),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>Install((DataGrid)sender)));}
    public static void Install(DataGrid table){if(table is TypedDataGrid||(bool)table.GetValue(InstalledProperty))return;table.SetValue(InstalledProperty,true);table.Sorting+=Sorting;}
    public static SortDescription[] Capture(DataGrid table)=>(SortDescription[]?)table.GetValue(SortsProperty)??table.Items.SortDescriptions.ToArray();
    private static string Path(DataGridColumn column)=>column.SortMemberPath.Length>0?column.SortMemberPath:column is DataGridBoundColumn{Binding:Binding binding}?binding.Path?.Path??"":"";
    internal static void Sorting(object sender,DataGridSortingEventArgs e)
    {
        if(sender is not DataGrid table||!table.CanUserSortColumns||!e.Column.CanUserSort)return;
        var path=Path(e.Column);if(path.Length==0)return;
        e.Handled=true;
        var direction=e.Column.SortDirection==ListSortDirection.Ascending?ListSortDirection.Descending:ListSortDirection.Ascending;
        var sorts=(Keyboard.Modifiers&ModifierKeys.Shift)!=0?Capture(table).Where(s=>s.PropertyName!=path).ToList():[];
        sorts.Add(new(path,direction));Apply(table,sorts.ToArray());
    }
    public static void Apply(DataGrid table,SortDescription[] sorts)
    {
        var view=table.ItemsSource==null?table.Items:CollectionViewSource.GetDefaultView(table.ItemsSource);
        if(view is not ListCollectionView list){if(view.CanSort){view.SortDescriptions.Clear();foreach(var sort in sorts)view.SortDescriptions.Add(sort);}return;}
        list.CustomSort=sorts.Length==0?null:new Rows(sorts);
        table.SetValue(SortsProperty,sorts);
        foreach(var column in table.Columns){var match=sorts.FirstOrDefault(s=>s.PropertyName==Path(column));column.SortDirection=match.PropertyName==null?null:match.Direction;}
    }
    private sealed class Rows(SortDescription[] sorts):IComparer
    {
        public int Compare(object? x,object? y)
        {
            if(x==null||y==null)return x==null?(y==null?0:1):-1;
            foreach(var sort in sorts){var a=TableValues.Read(x,sort.PropertyName);var b=TableValues.Read(y,sort.PropertyName);
                var result=TableValues.Compare(a,b,sort.PropertyName);if(result==0)continue;
                // Missing readings remain last in both directions.
                return TableValues.Missing(a)||TableValues.Missing(b)||sort.Direction==ListSortDirection.Ascending?result:result>0?-1:1;
            }
            return 0;
        }
    }
}

// Attach the ordering to the control itself: it must work before Loaded and
// cannot depend on a process-wide Loaded handler running in a particular host.
internal class TypedDataGrid : DataGrid
{
    public TypedDataGrid()=>SetResourceReference(StyleProperty,typeof(DataGrid));
    protected override void OnSorting(DataGridSortingEventArgs e)
    {
        TableSorting.Sorting(this,e);
        base.OnSorting(e);
    }
}
