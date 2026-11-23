using System.Text.Json;
using System.Windows;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow {
 private async Task EditDailySummary(){var s=settings.DailySummary;var names=new[]{"Понедельник","Вторник","Среда","Четверг","Пятница","Суббота","Воскресенье"};var labels=new[]{"Почта и события","Резервное копирование","Счета","Доступность LAN/WAN/служб","MikroTik и сеть","Агенты и журналы Windows"};
  var form=new FormDialog(this,"Ежедневная сводка Telegram",760).Toggle("enabled","Отправлять ежедневную сводку",s.Enabled).Note("Сначала настройте Telegram в настройках уведомлений. Часовой пояс: "+settings.TimeZoneId+". Пропущенные дни не рассылаются. Первый снимок — начальное состояние; новые и решённые считаются с начала наблюдения, далее — между успешными сводками.");
  for(var i=0;i<7;i++)form.Toggle("day"+i,names[i],s.Days[i].Enabled).Text("time"+i,"Время · HH:mm",s.Days[i].Time);
  for(var i=0;i<6;i++)form.Toggle("category"+i,labels[i],s.Categories.Contains(DailySummarySettings.Known[i]));
  if(form.ShowDialog()!=true)return;var next=new DailySummarySettings{Enabled=form.Checked("enabled"),Days=Enumerable.Range(0,7).Select(i=>new DailySummaryDay{Enabled=form.Checked("day"+i),Time=form.Value("time"+i).Trim()}).ToArray(),Categories=Enumerable.Range(0,6).Where(i=>form.Checked("category"+i)).Select(i=>DailySummarySettings.Known[i]).ToArray()};DailySummarySettings.Validate(next);await UpdateSettings(s=>s.DailySummary=next);await ShowPage(11);
 }
 private async Task PreviewDailySummary(){var reply=await remote.CommandAsync<JsonElement>("daily-summary-preview",new{},lifetime.Token);var text=reply.GetProperty("Text").GetString()??"";var status=reply.GetProperty("Status").GetString()??"";new FormDialog(this,"Предпросмотр сводки",900).Note("Последняя отправка: "+status).Text("preview","Сводка · без отправки",text,true,true).TextHeight("preview",470).ShowDialog();}
}
