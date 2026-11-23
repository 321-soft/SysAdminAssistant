using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private async Task PreviewInventoryAct(int actId, Window? owner = null)
    {
        await using var db = factory.Create(); var act = await db.Acts.AsNoTracking().SingleAsync(a => a.Id == actId);
        var lines = await db.ActLines.AsNoTracking().Where(l => l.ActId == actId).OrderBy(l => l.Id).ToArrayAsync();
        var window = new DialogWindow { Owner = owner ?? this, Title = "Акт " + act.Number + " · просмотр и печать", Width = 1000, Height = Math.Min(900, SystemParameters.WorkArea.Height - 50), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var document = InventoryPrint.Build(act, lines);
        var reader = new FlowDocumentReader { Document = document, ViewingMode = FlowDocumentReaderViewingMode.Page, IsFindEnabled = true, IsPrintEnabled = false };
        var button = new Button { Content = "Печать / PDF" };
        button.Click += (_, _) =>
        {
            try
            {
                var dialog = new PrintDialog(); if (dialog.ShowDialog() != true) return;
                // Create a fresh document: printing cannot change the saved act or the preview.
                var printed = InventoryPrint.Build(act, lines);
                if (dialog.PrintableAreaWidth > 0) printed.PageWidth = dialog.PrintableAreaWidth;
                if (dialog.PrintableAreaHeight > 0) printed.PageHeight = dialog.PrintableAreaHeight;
                printed.ColumnWidth = Math.Max(100, printed.PageWidth - printed.PagePadding.Left - printed.PagePadding.Right);
                dialog.PrintDocument(((IDocumentPaginatorSource)printed).DocumentPaginator, "Акт " + act.Number);
            }
            catch (Exception e) { AppMessageBox.Show(window, "Не удалось напечатать: " + e.Message, "Печать", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        var panel = new DockPanel(); var top = new WrapPanel { Margin = new(12) }; top.Children.Add(button); top.Children.Add(new TextBlock { Text = "Для PDF выберите Microsoft Print to PDF. Акт хранится в базе без изменений.", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top); panel.Children.Add(reader); window.Content = panel; window.ShowDialog();
    }
}
public static class InventoryPrint
{
    public static FlowDocument Build(InventoryAct act, IReadOnlyList<InventoryActLine> lines)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Times New Roman"), FontSize = 16, Foreground = Brushes.Black, Background = Brushes.White, PageWidth = 793.7, PageHeight = 1122.5, PagePadding = new Thickness(48), ColumnWidth = 697.7, IsHyphenationEnabled = false };
        void Text(string value, bool bold = false, TextAlignment alignment = TextAlignment.Left, bool next = false)
        {
            doc.Blocks.Add(new Paragraph(new Run(value)) { TextAlignment = alignment, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, KeepWithNext = next, Margin = new Thickness(0, 0, 0, 10) });
        }
        string Person(string name, string position) => string.Join(", ", new[] { position, name }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var representative = Person(act.Representative, act.RepresentativePosition);
        var employee = Person(act.EmployeeName, act.Position) + (act.Department.Length > 0 ? ", " + act.Department : "");
        var dismissal = act.Operation == InventoryOperation.Return && act.DismissalDate != null;
        Text("АКТ", true, TextAlignment.Center, true);
        Text(act.Operation switch
        {
            InventoryOperation.Issue => "приема-передачи материальных ценностей работнику",
            InventoryOperation.Return => dismissal ? "приема-передачи материальных ценностей при увольнении работника" : "возврата материальных ценностей работником",
            _ => "списания материальных ценностей"
        }, true, TextAlignment.Center, true);
        Text("№ " + act.Number, false, TextAlignment.Center, true);
        Text((act.Place.Length > 0 ? act.Place + "                                    " : "") + Display.Date(act.Date), next: true);
        if (act.Operation == InventoryOperation.WriteOff)
        {
            Text(act.Organization + ". Ответственный представитель: " + representative + ".");
            if (act.Commission.Length > 0) Text("Комиссия в составе: " + act.Commission + ".");
            Text("На основании: " + act.Reason + ", составлен настоящий акт о списании следующих материальных ценностей:", next: true);
        }
        else
        {
            Text(act.Organization + " в лице представителя работодателя: " + representative + " (далее — Представитель работодателя), с одной стороны, и работник: " + employee + " (далее — Работник), с другой стороны, составили настоящий акт приема-передачи.");
            if (act.Operation == InventoryOperation.Issue)
                Text("Основание: " + act.Reason + ". " + Display.Date(act.Date) + " Представитель работодателя передал, а Работник принял следующие материальные ценности:", next: true);
            else if (dismissal)
                Text("В связи с увольнением Работника " + Display.Date(act.DismissalDate) + " Работник передал, а Представитель работодателя принял следующие материальные ценности. Основание: " + act.Reason + ".", next: true);
            else Text("Основание: " + act.Reason + ". Работник передал, а Представитель работодателя принял следующие материальные ценности:", next: true);
        }
        var table = new Table { CellSpacing = 0, FontSize = 13.3, Margin = new Thickness(0, 4, 0, 14) };
        foreach (var width in new[] { 0.45, 3.6, 0.6, 0.65, 1.3, 1.4 }) table.Columns.Add(new TableColumn { Width = new GridLength(width, GridUnitType.Star) });
        var group = new TableRowGroup(); table.RowGroups.Add(group);
        void Row(string[] values, bool header)
        {
            var row = new TableRow();
            foreach (var value in values) row.Cells.Add(new TableCell(new Paragraph(new Run(value)) { Margin = new Thickness(0), FontWeight = header ? FontWeights.Bold : FontWeights.Normal }) { Padding = new Thickness(5), BorderBrush = Brushes.Black, BorderThickness = new Thickness(0.5) });
            group.Rows.Add(row);
        }
        Row(["№ п/п", "Наименование материальных ценностей", "Ед. изм.", "Кол-во", "Стоимость, руб.", "Состояние"], true);
        foreach (var (line, index) in lines.Select((l, i) => (l, i)))
        {
            var name = line.Name + "\nИнв. № " + line.InventoryNumber;
            if (line.WindowsName.Length > 0) name += "\nWIN: " + line.WindowsName;
            if (line.SerialNumber.Length > 0) name += "\nSN: " + line.SerialNumber;
            if (line.Specifications.Length > 0) name += "\n" + line.Specifications;
            Row([(index + 1).ToString(), name, "шт.", "1", InventoryMoney.Print(line.ValueKopecks), line.Condition], false);
        }
        doc.Blocks.Add(table);
        Text($"Всего материальных ценностей: {lines.Count} шт.");
        var priced = lines.Where(l => l.ValueKopecks != null).ToArray();
        if (priced.Length > 0) Text((priced.Length == lines.Count ? "Общая стоимость: " : "Стоимость строк с заполненной ценой: ") + InventoryMoney.Print(priced.Sum(l => l.ValueKopecks!.Value)) + " руб.");
        if (priced.Length != lines.Count) Text("Прочерк в графе стоимости означает, что стоимость в карточке не указана.");
        if (act.Operation == InventoryOperation.WriteOff)
        {
            Text("Заключение комиссии: материальные ценности, перечисленные в настоящем акте, подлежат списанию. Основание: " + act.Reason + ".");
            Text("Ответственный представитель: ____________________ / " + representative, next: true);
            if (act.Commission.Length > 0)
                foreach (var member in act.Commission.Split(['\r', '\n', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) Text("Член комиссии: ____________________ / " + member);
            else Text("Подписи членов комиссии: __________________________________________________");
        }
        else
        {
            Text("Материальные ценности переданы и приняты в состоянии и комплектности, указанных в таблице.");
            Text("Настоящий акт составлен в двух экземплярах, по одному для Работодателя и Работника, при этом оба экземпляра имеют равную юридическую силу.", next: true);
            var signatures = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 0) };
            signatures.Columns.Add(new TableColumn()); signatures.Columns.Add(new TableColumn());
            var signatureGroup = new TableRowGroup(); var signatureRow = new TableRow();
            foreach (var value in new[] { "Представитель работодателя:\n" + act.Organization + "\n" + representative + "\n\n____________________\nПодпись", "Работник:\n" + employee + "\n\n____________________\nПодпись" })
                signatureRow.Cells.Add(new TableCell(new Paragraph(new Run(value)) { Margin = new Thickness(0), KeepTogether = true }) { Padding = new Thickness(0, 0, 12, 0) });
            signatureGroup.Rows.Add(signatureRow); signatures.RowGroups.Add(signatureGroup); doc.Blocks.Add(signatures);
        }
        Text("Дата подписания: " + Display.Date(act.Date));
        return doc;
    }
}
