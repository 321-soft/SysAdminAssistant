using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<string, PageState> pageStates = new();
    private string currentUiKey = "";
    private sealed record ColumnState(int Order,DataGridLength Width);
    private sealed record GridState(string Columns, string[] Selected, SortDescription[] Sorts,ColumnState[] Layout);
    private sealed record PageState(string[] Searches, object?[] Filters, GridState[] Grids, double? Scroll);
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Children<T>(child)) yield return nested;
    }
    private static string RowKey(object row)
    {
        if (row is Entity entity) return entity.GetType().Name + ":" + entity.Id;
        var reference = row.GetType().GetProperties().FirstOrDefault(p => typeof(Entity).IsAssignableFrom(p.PropertyType))?.GetValue(row);
        if (reference is Entity linked) return linked.GetType().Name + ":" + linked.Id;
        return row.GetType().Name + ":" + (row.GetType().GetProperty("Name")?.GetValue(row) ?? row.ToString());
    }
    private static string ColumnKey(DataGrid table) => string.Join("|", table.Columns.OfType<DataGridTextColumn>().Select(c => (c.Binding as Binding)?.Path?.Path));
    private void CapturePageState()
    {
        if (currentUiKey.Length == 0) return;
        var searches = Children<TextBox>(PageHost).Where(t => t.Tag is string tag && tag.StartsWith("ui-search")).Select(t => t.Text).ToArray();
        var filters = Children<ComboBox>(PageHost).Where(c => Equals(c.Tag, "ui-filter")).Select(c => (c.SelectedItem as Choice)?.Key).ToArray();
        var grids = Children<DataGrid>(PageHost).Select(g => new GridState(ColumnKey(g), g.SelectedItems.Cast<object>().Select(RowKey).ToArray(), TableSorting.Capture(g),g.Columns.Select(c=>new ColumnState(c.DisplayIndex,c.Width)).ToArray())).ToArray();
        pageStates[currentUiKey] = new(searches, filters, grids, Children<ScrollViewer>(PageHost).FirstOrDefault(s=>!Equals(s.Tag,"workspace-scroll"))?.VerticalOffset);
    }
    private void RestorePageState(string key)
    {
        currentUiKey = key; if (!pageStates.TryGetValue(key, out var state)) return;
        if(state.Scroll is double offset && Children<ScrollViewer>(PageHost).FirstOrDefault(s=>!Equals(s.Tag,"workspace-scroll")) is ScrollViewer scroll)
            scroll.Loaded += (_,_) => scroll.ScrollToVerticalOffset(offset);
        var searches = Children<TextBox>(PageHost).Where(t => t.Tag is string tag && tag.StartsWith("ui-search")).ToArray();
        for (int i = 0; i < Math.Min(searches.Length, state.Searches.Length); i++) searches[i].Text = state.Searches[i];
        var filters = Children<ComboBox>(PageHost).Where(c => Equals(c.Tag, "ui-filter")).ToArray();
        for (int i = 0; i < Math.Min(filters.Length, state.Filters.Length); i++) filters[i].SelectedItem = filters[i].Items.OfType<Choice>().FirstOrDefault(c => Equals(c.Key, state.Filters[i]));
        var grids = Children<DataGrid>(PageHost).ToArray();
        for (int i = 0; i < Math.Min(grids.Length, state.Grids.Length); i++)
        {
            var grid = grids[i]; var saved = state.Grids[i]; if (saved.Columns != ColumnKey(grid)) continue;
            if(saved.Layout.Length==grid.Columns.Count){for(var c=0;c<grid.Columns.Count;c++){grid.Columns[c].Width=saved.Layout[c].Width;grid.Columns[c].DisplayIndex=saved.Layout[c].Order;}}
            TableSorting.Apply(grid,saved.Sorts);
            foreach (var item in grid.Items.Cast<object>().Where(r => saved.Selected.Contains(RowKey(r))))
            {
                if (grid.SelectionMode == DataGridSelectionMode.Single) { grid.SelectedItem = item; break; }
                grid.SelectedItems.Add(item);
            }
        }
    }
}
