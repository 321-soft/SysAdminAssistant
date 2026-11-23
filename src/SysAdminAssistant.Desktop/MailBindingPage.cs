using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private async Task BindMail(ReportEvent selected,bool asUpdate=false)
    {
        if(AccessRules.Level(remote.CurrentUser,"reports")<2)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нужно право изменения отчётов"));
        await remote.RefreshAsync(lifetime.Token);settings=remote.Settings;
        await using var db=factory.Create();
        var report=await db.Reports.AsNoTracking().SingleAsync(e=>e.Id==selected.Id,lifetime.Token);
        var mail=await remote.CommandAsync<MailView>("mail-view",new MailViewRequest(report.Id),lifetime.Token);
        var devices=(CanRead("devices")||CanRead("routers"))?await db.Devices.AsNoTracking().Where(d=>d.Enabled).OrderBy(d=>d.Name).ToArrayAsync():[];
        var jobs=CanRead("jobs")?await db.Jobs.AsNoTracking().OrderBy(j=>j.Title).ToArrayAsync():[];
        var targets=devices.Select(d=>new Choice("D:"+d.Id,"Устройство · "+d.Name)).Concat(jobs.Select(j=>new Choice("J:"+j.Id,"Задание · "+j.Title))).ToArray();
        var target=report.JobId is int jobId?"J:"+jobId:report.DeviceId is int deviceId?"D:"+deviceId:null;
        var form=new FormDialog(this,"Привязать письмо",950);
        MailBindPreview? preview=null;string signature="";
        form.Tab("Письмо и строки")
            .Note("От: "+mail.Sender+"\nТема: "+mail.Subject+"\nПоступило: "+Display.Stamp(mail.ImapReceivedAt))
            .Text("originalSubject","Тема письма · выдели постоянную часть",mail.Subject,false,true)
            .Action("Добавить выделенное из темы",()=>{var added=form.Selection("originalSubject").Trim();if(added.Length>0)form.Set("subjectLines",string.Join("\n",RuleMatcher.BodyLines(form.Value("subjectLines")+"\n"+added)));})
            .Text("subjectLines","Тема содержит · каждое условие с новой строки","",true)
            .Select("subjectAll","Совпадение строк темы",new[]{new Choice(true,"Все строки (И)"),new Choice(false,"Хотя бы одна строка (ИЛИ)")},true)
            .Text("original","Текст письма · выделяй постоянные строки",mail.Body,true,true).TextHeight("original",260)
            .Action("Добавить выделенное в условия",()=>{
                var added=form.Selection("original").Trim();if(added.Length==0)return;
                form.Set("lines",string.Join("\n",RuleMatcher.BodyLines(form.Value("lines")+"\n"+added)));
            })
            .Text("lines","Тело содержит · каждая строка — отдельное буквальное условие","",true)
            .Select("all","Совпадение строк",new[]{new Choice(true,"Все строки (И)"),new Choice(false,"Хотя бы одна строка (ИЛИ)")},true)
            .Select("fieldsAll","Если заполнены тема и тело",new[]{new Choice(true,"Совпасть должны тема И тело"),new Choice(false,"Достаточно темы ИЛИ тела")},true)
            .Note("Можно заполнить только тему, только тело или оба поля. В каждом поле — до 32 буквальных условий, без учёта регистра. Пустые поля не проверяются. Символы * и ; — обычный текст.");
        form.Tab("Цель и применение")
            .Select("category","Назначение письма",new[]{new Choice("Report","Отчёт / событие"),new Choice("Update","Уведомление об обновлении")},asUpdate||report.MailCategory=="Update"?"Update":"Report")
            .Text("product","Продукт для извлечения версии · например DSM, QTS, QuTS hero",report.UpdateProduct!=""?report.UpdateProduct:report.Provider=="QNAP"?"QTS":report.Provider=="DSM"?"DSM":"")
            .Select("target","Привязать к",targets,target)
            .Select("slot","Запуск только для выбранного письма",new[]{new Choice(null,"Автоматически по времени отчёта")}.Concat(jobs.SelectMany(j=>BackupSchedule.Deadlines(j)).Distinct().OrderBy(x=>x).Select(t=>new Choice(t,TimeSpan.FromMinutes(t).ToString(@"hh\:mm")))),report.ScheduledSlotMinutes)
            .Toggle("rule","Автоматически привязывать следующие письма по этим строкам",false)
            .Toggle("past","Также применить к уже полученным совпавшим письмам",true)
            .Toggle("replace","Разрешить замену существующих привязок у совпавших старых писем",false)
            .Note("Без автоматического правила изменится только выбранное письмо. При создании правила сначала покажите совпадения. Другие письма распределяются между запусками автоматически.")
            .Action("Показать совпадения",async()=>{
                var request=Request();
                preview=await remote.CommandAsync<MailBindPreview>("mail-bind-preview",request,lifetime.Token);
                signature=RemoteData.Hash(request with {PreviewToken=""});
                form.Set("preview",$"Совпало: {preview.Matched}; будет привязано: {preview.Applicable}; сейчас привязаны к другой цели: {preview.OtherBindings}; без сохранённого текста (пропущены): {preview.MissingText}.\n"+
                    (preview.ConflictingRules.Length==0?"Конфликтов правил нет.":"Конфликт с правилами: "+string.Join(", ",preview.ConflictingRules))+
                    "\n\n"+string.Join("\n",preview.Samples.Select(s=>$"#{s.Id} · {Display.Stamp(s.ReceivedAt)} · {s.Sender} · {s.Subject} · сейчас: {s.Target}")));
                return "Предварительный просмотр готов";
            })
            .Text("preview","Совпадения · первые 30","Нажмите «Показать совпадения»",true,true).TextHeight("preview",210).Submit("Привязать");
        MailBindRequest Request() {
            var choice=form.Selected<string>("target").Split(':');
            return new(report.Id,choice[0]=="J"?TargetKind.SqlJob:TargetKind.Device,int.Parse(choice[1]),form.Value("lines"),form.Selected<bool>("all"),
                form.Checked("rule"),form.Checked("past"),form.Checked("replace"),form.Selected<string>("category")=="Update"?null:form.Selected("slot") as int?,RemoteData.EntityStamp(report),preview?.Token??"",form.Value("subjectLines"),form.Selected<bool>("subjectAll"),form.Selected<bool>("fieldsAll"),form.Selected<string>("category"),form.Value("product"));
        }
        void ChangeCategory(){var updates=form.Selected<string>("category")=="Update";form.Choices("target",updates?targets.Where(x=>((string)x.Key!).StartsWith("D:")):targets,form.Selected("target"));form.FieldEnabled("slot",!updates);form.FieldEnabled("product",updates);}
        form.SelectionChanged("category",ChangeCategory);ChangeCategory();
        form.Validate=()=>{
            if(!form.HasSelection("target"))return "Выберите устройство или задание";
            var request=Request();
            if(request.MailCategory=="Update"&&string.IsNullOrWhiteSpace(request.UpdateProduct))return "Укажите продукт для извлечения версии";
            if(report.MailCategory=="Update"&&request.MailCategory!="Update")return "Выберите уведомление об обновлении";
            if(request.TargetKind==TargetKind.Device&&request.ScheduledSlotMinutes!=null)return "Для устройства выберите автоматический запуск";
            if(request.TargetKind==TargetKind.SqlJob&&request.ScheduledSlotMinutes is int time&&!BackupSchedule.Deadlines(jobs.Single(j=>j.Id==request.TargetId)).Contains(time))return "Этого времени нет в расписании выбранного задания";
            if(request.CreateRule&&(preview==null||signature!=RemoteData.Hash(request with {PreviewToken=""})))return "Покажите совпадения после изменения условий";
            if(request.CreateRule&&preview!.ConflictingRules.Length>0)return "Уточните условия или исправьте конфликтующие правила";
            return null;
        };
        if(form.ShowDialog()!=true)return;
        var result=await remote.CommandAsync<MailBindResult>("mail-bind",Request(),lifetime.Token);
        AppMessageBox.Show(this,$"Привязано писем: {result.Bound}"+(result.NeedsReview>0?$"\nУ {result.NeedsReview} писем цель задана, но результат/уровень ещё требует разбора.":"")+(result.RuleId is int id?$"\nПравило для следующих писем: #{id}":"\nАвтоматическое правило не создавалось."),"Привязка");
        await LoadPendingSnapshot(true);await ShowPage(page);
    }
}
