using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private async Task ShowDmarc()
    {
        await using var db = factory.Create();
        var reports = await db.Reports.AsNoTracking().Where(x => x.Provider == "DMARC" && !x.IsPending).OrderByDescending(x => x.ReceivedAt).Take(500).ToArrayAsync();
        var table = Table(reports.Select(x => new HistoryRow(x)).ToArray(), ("Домен", "Task", null), ("Дата", "Date", null), ("Результат", "Result", null), ("Количество", "Size", null));
        var tabs=new TabControl();
        tabs.Items.Add(new TabItem {Header="Отчёты DMARC",Content=Panel(MailPreview(table,x=>(x as HistoryRow)?.Entity),RowAction(table,"Открыть письмо",()=>ShowMail(Selected<HistoryRow>(table).Entity)),RowAction(table,"Понятный отчёт",()=>ShowReportDiagnosis(Selected<HistoryRow>(table).Entity)),ActionButton("Импорт XML / GZIP / ZIP",ImportDmarc),ActionButton("Почтовые ящики",ShowMailboxes),ActionButton("Проверить почту сейчас",RequestMailPoll),ActionButton("Диагностика почты",ShowMailDiagnostics))});
        var pending=await (from p in db.Pending.AsNoTracking() join e in db.Reports.AsNoTracking() on p.EventId equals e.Id where e.Provider=="DMARC" orderby e.ReceivedAt descending select new QueueRow(p.Subject,e)).Take(500).ToArrayAsync();
        var queue=Table(pending,("Получено","Received",null),("Тема","Subject",null),("Причина","Issue",null));
        tabs.Items.Add(new TabItem {Header="Ошибки DMARC · последние 500",Content=Panel(MailPreview(queue,x=>(x as QueueRow)?.Event),RowAction(queue,"Открыть письмо",()=>ShowMail(Selected<QueueRow>(queue).Event)),RowAction(queue,"Разбор отчёта",()=>ShowReportDiagnosis(Selected<QueueRow>(queue).Event)))});
        PageHost.Children.Add(tabs);
    }
    private async Task ImportDmarc()
    {
        var picker = new OpenFileDialog { Title = "Отчёты DMARC", Filter = "DMARC (*.xml;*.gz;*.zip)|*.xml;*.gz;*.zip", Multiselect = true };
        if (picker.ShowDialog(this) != true) return;
        var store = new ReportStore(factory, settings.CustomDiagnostics); int count = 0;
        foreach (var file in picker.FileNames)
        {
            if (new FileInfo(file).Length > DmarcParser.MaxXmlBytes) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("DMARC-файл превышает лимит 4 МБ"));
            await using var stream = File.OpenRead(file);
            var key = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, lifetime.Token)); stream.Position = 0;
            var message = new MimeMessage { Subject = "DMARC импорт " + Path.GetFileName(file), Date = DateTimeOffset.Now,
                Body = new MimePart("application", "octet-stream") { FileName = Path.GetFileName(file), Content = new MimeContent(stream) } };
            count += await store.IngestMessageAsync(MailPayloadReader.ReadSafe(message, "manual-dmarc", key, true), "manual-dmarc", key, lifetime.Token);
        }
        AppMessageBox.Show(this, $"Добавлено: {count}. Повреждённые вложения появятся в нераспознанной очереди.", "DMARC"); await ShowPage(10);
    }
}
