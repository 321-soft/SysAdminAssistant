using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private async Task ShowDiagnosticGuide()
    {
        var entries = DiagnosticGuide.Entries.Concat(settings.CustomDiagnostics.Select(x => x.ToEntry())).Select(x => new DiagnosticRow(x)).ToArray();
        var table = Table(entries, ("Продукт", "Provider", null), ("Предупреждение", "Title", null), ("Точность", "Confidence", null));
        var window = new DialogWindow { Owner = this, Title = $"Справочник предупреждений · {entries.Length} статей", Width = 1120, Height = 760, MinWidth = 640, MinHeight = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(8) };
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top);
        top.Children.Add(new TextBlock { Text = "Встроенные статьи по официальным источникам и свои рекомендации · проверено 09.10.2026 · работает без интернета. Список не исчерпывающий. Рекомендации не меняют статус события и не выполняют команды.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        var filters = new Grid();
        filters.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MaxWidth=245});
        filters.ColumnDefinitions.Add(new(){Width=new GridLength(2,GridUnitType.Star)});
        var provider = new ComboBox { Margin = new(0,0,8,8), ItemsSource = new[] { "Все продукты" }.Concat(entries.SelectMany(x => x.Entry.Providers).Where(x => x != "Любой").Distinct().Order()), SelectedIndex = 0 };
        var search = new TextBox { Margin = new(0,0,0,8), ToolTip = "Поиск по коду, сообщению, объяснению и рекомендациям" };
        filters.Children.Add(provider); Grid.SetColumn(search,1); filters.Children.Add(search); top.Children.Add(filters); root.Children.Add(top);
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Bottom);
        CustomDiagnostic? edited = null; bool add = false, remove = false, mailSources = false;
        var source = new Button { Content = "Открыть ссылку на статью", IsEnabled = false };
        source.Click += (_, _) => { try { if (table.SelectedItem is DiagnosticRow row) OpenGuideSource(row.Entry.Source, row.Entry.IsCustom); } catch (Exception e) { AppMessageBox.Show(window, e.Message, "Открытие статьи"); } };
        var close = new Button { Content = "Закрыть", IsCancel = true }; actions.Children.Add(source); actions.Children.Add(close); var create = new Button { Content = "Добавить статью" }; create.Click += (_, _) => { add = true; window.DialogResult = true; };
        var edit = new Button { Content = "Изменить свою", IsEnabled = false }; edit.Click += (_, _) => { if (table.SelectedItem is DiagnosticRow row) edited = settings.CustomDiagnostics.Single(x => "custom." + x.Id == row.Entry.Id); window.DialogResult = true; };
        var delete = new Button { Content = "Удалить свою", IsEnabled = false }; delete.Click += (_, _) => { if (table.SelectedItem is DiagnosticRow row) edited = settings.CustomDiagnostics.Single(x => "custom." + x.Id == row.Entry.Id); remove = true; window.DialogResult = true; };
        if(AccessRules.Level(remote.CurrentUser,"reports")>=2){var manage=new Button{Content="Источники почты"};manage.Click+=(_,_)=>{mailSources=true;window.DialogResult=true;};actions.Children.Add(manage);}
        actions.Children.Add(create); actions.Children.Add(edit); actions.Children.Add(delete);
        root.Children.Add(actions);
        var detail = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin=new(0), VerticalContentAlignment=VerticalAlignment.Top };
        var body=new Grid();
        body.RowDefinitions.Add(new(){Height=new GridLength(3,GridUnitType.Star),MinHeight=112});
        body.RowDefinitions.Add(new(){Height=new GridLength(8)});
        body.RowDefinitions.Add(new(){Height=new GridLength(2,GridUnitType.Star),MinHeight=64});
        var splitter=new GridSplitter{Height=8,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Center,Background=Brush("#DFE7F1"),ResizeDirection=GridResizeDirection.Rows,ResizeBehavior=GridResizeBehavior.PreviousAndNext,ToolTip="Перетащите, чтобы изменить высоту списка"};
        Grid.SetRow(splitter,1);Grid.SetRow(detail,2);
        table.Columns[0].Width=new DataGridLength(1,DataGridLengthUnitType.Star);
        table.Columns[1].Width=new DataGridLength(3,DataGridLengthUnitType.Star);
        table.Columns[2].Width=new DataGridLength(1,DataGridLengthUnitType.Star);
        body.Children.Add(table);body.Children.Add(splitter);body.Children.Add(detail);root.Children.Add(body);window.Content=root;
        table.SelectionChanged += (_, _) => { var row = table.SelectedItem as DiagnosticRow; source.IsEnabled = row != null && row.Entry.Source.Length > 0; edit.IsEnabled = delete.IsEnabled = row?.Entry.IsCustom == true; detail.Text = row == null ? "Выберите предупреждение" : row.Entry.Format(); };
        void Filter()
        {
            var query = search.Text.Trim(); var selected = provider.SelectedItem as string;
            var previous=table.SelectedItem as DiagnosticRow;
            var visible = entries.Where(x => (selected == "Все продукты" || x.Entry.Providers.Contains(selected ?? "") || x.Entry.Providers.Contains("Любой")) &&
                (query.Length == 0 || (x.Entry.Format() + string.Join(" ", x.Entry.Patterns)).Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
            table.ItemsSource=visible;
            table.SelectedItem=visible.FirstOrDefault(x=>x.Entry.Id==previous?.Entry.Id)??visible.FirstOrDefault();
            if(visible.Length==0)detail.Text="Предупреждения не найдены.";
        }
        provider.SelectionChanged += (_, _) => Filter(); search.TextChanged += (_, _) => Filter(); Filter(); if (window.ShowDialog() == true)
        {
            if(mailSources){await ShowMailSources();return;}
            if (remove && edited != null) { if (AppMessageBox.Show(this, "Удалить свою статью? Сохранённые события не изменятся.", "Справочник", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) await UpdateSettings(x => x.CustomDiagnostics.RemoveAll(y => y.Id == edited.Id)); }
            else if (add || edited != null) await EditCustomDiagnostic(edited ?? new());
            await ShowDiagnosticGuide();
        }
    }
    private Task ShowReportDiagnosis(ReportEvent report) => ShowStoredDiagnosis("Разбор отчёта", $"{report.Provider} · {Display.Stamp(report.OccurredAt ?? report.ReceivedAt)} · {Display.Result(report.Result)}", report.Description);
    private Task ShowStoredDiagnosis(string title, string subtitle, string description)
    {
        var form = new FormDialog(this, title, 820).Note(subtitle).Text("description", "Сохранённый отчёт", ProductText.Get(description), true, true).TextHeight("description", 420);
        foreach (var url in description.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.StartsWith("Официальная статья: ", StringComparison.Ordinal)).Select(x => x["Официальная статья: ".Length..].Trim()).Distinct())
            if (DiagnosticGuide.OfficialSource(url)) form.Action("Открыть: " + new Uri(url).Host, () => OpenOfficialSource(url));
        foreach (var url in settings.CustomDiagnostics.Where(x => x.Validate() == null && x.Source.Length > 0).Select(x => x.Source).Distinct())
            if (description.Contains("Источник пользователя: " + url, StringComparison.Ordinal)) form.Action("Ссылка на свою статью: " + new Uri(url).Host, () => OpenGuideSource(url, true));
        form.ViewOnly().ShowDialog(); return Task.CompletedTask;
    }
    private static void OpenGuideSource(string url, bool custom)
    {
        if (!custom) { OpenOfficialSource(url); return; }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Неверная ссылка статьи"));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    private static void OpenOfficialSource(string url)
    {
        if (!DiagnosticGuide.OfficialSource(url)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Ссылка не относится к официальному справочнику"));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    private sealed class DiagnosticRow(DiagnosticEntry entry)
    {
        public DiagnosticEntry Entry => entry;
        public string Provider => string.Join(", ", entry.Providers);
        public string Title => entry.IsCustom?entry.Title:ProductText.Get(entry.Title);
        public string Confidence => entry.IsCustom?entry.Confidence:ProductText.Get(entry.Confidence);
    }
}
