using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private int backupTab;
    private async Task ShowBackups()
    {
        await using var db = factory.Create(); var jobs = await db.Jobs.AsNoTracking().OrderBy(x => x.Title).ToArrayAsync();
        var now=remote.Snapshot.ServerTime;
        var reports=CanRead("reports")?await db.Reports.AsNoTracking().Where(e=>e.JobId!=null&&e.Provider!="DMARC").ToArrayAsync():[];
        var jobRows=jobs.SelectMany(j=>Infrastructure.BackupQueries.RunRows(j,reports,now,CanRead("reports"))).ToArray();
        var jobGrid = Table(jobRows, ("Задание", "Title", null), ("Система бэкапа", "BackupSystem", null), ("Сервер", "Host", null), ("Текущее состояние", "CurrentStatus", null), ("Дни", "Days", null), ("Завершение", "Deadline", null), ("Последний результат", "Result", null), ("Дата отчёта","LastReport","dd.MM.yyyy HH:mm")); jobGrid.Columns.Insert(0, LampColumn("DisplayState","Сейчас"));jobGrid.Columns.Add(LampColumn("State","Последний отчёт"));
        var jobPanel = Panel(jobGrid, ActionButton("Добавить задание", () => EditJob(new())), RowAction(jobGrid,"Удалить",()=>DeleteCatalogEntity(Selected<Infrastructure.BackupRunRow>(jobGrid).Entity,Selected<Infrastructure.BackupRunRow>(jobGrid).Title)), RowAction(jobGrid, "Изменить", () => EditJob(Selected<Infrastructure.BackupRunRow>(jobGrid).Entity)), RowAction(jobGrid, "История задания", () => ShowHistory(null, Selected<Infrastructure.BackupRunRow>(jobGrid).Entity.Id)), RowAction(jobGrid,"Разобрать письма",()=>ReanalyzeJob(Selected<Infrastructure.BackupRunRow>(jobGrid).Entity.Id)));
        PageHost.Children.Add(jobPanel);
    }
    private static DockPanel Panel(UIElement content, params Button[] buttons)
    {
        return ListSurface(content, buttons, search: true);
    }
    private async Task EditJob(SqlBackupJob j)
    {
        var previousSystem=j.BackupSystem;var existing=j.Id!=0;
        j = Copy(j);
        await using var db = factory.Create(); var devices = await db.Devices.AsNoTracking().Where(x => x.Enabled).OrderBy(x => x.Name).ToArrayAsync();
        var systems = (await db.Profiles.AsNoTracking().Select(p=>p.Provider).ToArrayAsync()).Concat(ReportAnalyzer.Providers).Append(j.BackupSystem).Where(p=>p!="Любой"&&p!="Неизвестно"&&p!="DMARC"&&!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p=>p).ToArray();
        var form = new FormDialog(this, "бэкап — мониторинг")
            .Select("system", "Система бэкапа", new[]{new Choice("", "Автоопределение")}.Concat(systems.Select(p=>new Choice(p,p))), j.BackupSystem)
            .Text("title", "Название задания *", j.Title).Text("host", "Устройство / сервер *", j.Host).Text("instance", "Экземпляр SQL · если применимо", j.Instance)
            .Select("device", "Связь с устройством (необязательно)", new[] { new Choice(null, "Без привязки") }.Concat(devices.Select(d => new Choice(d.Id, d.ToString()))), j.DeviceId)
            .Days("days", j.Days).Text("times", "Ожидаемые завершения · ЧЧ:ММ через ; · например 09:00; 21:00", string.Join("; ",BackupSchedule.Deadlines(j).Select(t=>TimeSpan.FromMinutes(t).ToString(@"hh\:mm"))))
            .Text("grace", "Допуск ± минут · 0–360", j.GraceMinutes.ToString()).Toggle("active", "Контролировать по расписанию", j.Active)
            .Note("В дни без запуска программа не требует нового отчёта. Здесь задаётся ожидаемое время завершения. Допуск ± N минут применяется до и после этого времени.");
        form.Validate = () => { _=BackupSchedule.Deadlines(new SqlBackupJob {CompletionTimes=form.Value("times"),EarlyMinutes=form.Number("grace",0,360),GraceMinutes=form.Number("grace",0,360)}); if (Infrastructure.AccessRules.Level(remote.CurrentUser,"devices")>=1 && !form.HasSelection("device")) return "Выберите устройство или «Без привязки»"; if (form.Checked("active") && form.Mask("days") == 0) return "Выберите хотя бы один день"; return Required(form.Value("title"), form.Value("host"),form.Value("times")); };
        if (form.ShowDialog() != true) return;
        if (j.Days != form.Mask("days") || j.CompletionTimes != form.Value("times") || j.GraceMinutes != form.Number("grace", 0, 360) || j.EarlyMinutes != form.Number("grace", 0, 360) || j.Active != form.Checked("active")) j.MonitoringSince = AppTime.Now;
        j.BackupSystem=form.Selected<string>("system"); j.Title = form.Value("title"); j.Host = form.Value("host"); j.Instance = form.Value("instance"); j.DeviceId = Infrastructure.AccessRules.Level(remote.CurrentUser,"devices")>=1 ? form.Selected("device") as int? : j.DeviceId;
        j.EarlyMinutes=form.Number("grace",0,360);j.Days = form.Mask("days"); j.CompletionTimes=form.Value("times"); j.DeadlineMinutes=BackupSchedule.Deadlines(new SqlBackupJob {CompletionTimes=j.CompletionTimes,EarlyMinutes=form.Number("grace",0,360),GraceMinutes=form.Number("grace",0,360)})[0]; j.GraceMinutes = form.Number("grace", 0, 360); j.Active = form.Checked("active"); await SaveEntity(j);
        if(existing && previousSystem!=j.BackupSystem && Infrastructure.AccessRules.Level(remote.CurrentUser,"reports")>=2)await ReanalyzeJob(j.Id);
    }
    private async Task ReanalyzeJob(int id)
    {
        var result=await remote.CommandAsync<Infrastructure.MailBindResult>("job-reanalyze",new{JobId=id},lifetime.Token);
        await remote.RefreshAsync(lifetime.Token);await ShowPage(page);
        AppMessageBox.Show(this,$"Разобрано писем: {result.Bound}\nРезультат требует уточнения: {result.NeedsReview}","Анализ отчётов");
    }
    private async Task ShowRules()
    {
        await using var db = factory.Create(); var rows = await db.Rules.AsNoTracking().OrderBy(x => x.Priority).ToArrayAsync();
        var devices = await db.Devices.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        var jobs = await db.Jobs.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Title);
        var view = rows.Select(r => new RuleRow(r, (r.TargetKind == TargetKind.Device ? devices : jobs).GetValueOrDefault(r.TargetId, "Цель отсутствует"))).ToArray();
        var table = Table(view, ("Назначение","Category",null), ("Почтовый профиль", "Provider", null), ("Фраза темы", "SubjectPhrase", null), ("Фраза тела", "BodyPhrase", null), ("Привязка", "Target", null), ("Приоритет", "Priority", null), ("Включено", "Active", null));
        var window = new DialogWindow { Owner = this, Title = "Правила привязки отчётов", Width = 1100, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var create = new Button { Content = "Добавить" }; create.Click += (_, _) => { window.DialogResult = true; };
        var edit = new Button { Content = "Изменить" }; BindingRule? selected = null;
        edit.Click += (_, _) => { if (table.SelectedItem is RuleRow r) { selected = r.Entity; window.DialogResult = true; } };
        edit.IsEnabled = false; table.SelectionChanged += (_, _) => edit.IsEnabled = table.SelectedItem != null;
        var close = new Button { Content = "Закрыть", IsCancel = true }; window.Content = Panel(table, create, edit, close);
        if (window.ShowDialog() == true) { await EditRule(selected ?? new()); await ShowPage(page); }
    }
    private async Task EditRule(BindingRule r, PendingReport? source = null)
    {
        r = Copy(r);
        await using var db = factory.Create(); var devices = await db.Devices.AsNoTracking().Where(x => x.Enabled).OrderBy(x => x.Name).ToArrayAsync(); var jobs = await db.Jobs.AsNoTracking().OrderBy(x => x.Title).ToArrayAsync();
        var targets = devices.Select(d => new Choice($"D:{d.Id}", "Устройство · " + d)).Concat(jobs.Select(j => new Choice($"J:{j.Id}", "Задание бэкапа · " + j))).ToArray();
        var key = r.TargetId == 0 ? null : (r.TargetKind == TargetKind.Device ? "D:" : "J:") + r.TargetId;
        var sourceProfiles = await db.Profiles.AsNoTracking().ToArrayAsync();
        var form = new FormDialog(this, "Правило привязки", 740);
        if (source != null)
        {
            form.Text("sourceSubject", "Исходная тема — выдели постоянную фразу", source.Subject, false, true)
                .Action("Выделенное из темы → условие", () => { var s = form.Selection("sourceSubject"); if (s.Length > 0) form.Set("subject", s); })
                .Text("sourceBody", "Исходное тело — выдели имя NAS / сервера / задания", source.Body, true, true)
                .Action("Выделенное из тела → условие", () => { var s = form.Selection("sourceBody"); if (s.Length > 0) form.Set("body", string.Join("\n",RuleMatcher.BodyLines(form.Value("body")+"\n"+s))); });
        }
        form.Select("category","Назначение",new[]{new Choice("Report","Отчёт / событие"),new Choice("Update","Уведомление об обновлении")},r.MailCategory)
            .Text("updateProduct","Продукт для версии · только для обновлений",r.UpdateProduct)
            .Select("provider", "Почтовый профиль", sourceProfiles.Select(x => new Choice(x.Provider, x.Provider)), r.Provider)
            .Text("sender", "Отправитель содержит", r.SenderPhrase).Text("subject", "Тема содержит · каждое условие с новой строки", r.SubjectPhrase,true).Text("body", "Тело содержит · каждое условие с новой строки", r.BodyPhrase,true)
            .Select("subjectMode","Совпадение строк темы",new[]{new Choice(true,"Все строки (И)"),new Choice(false,"Любая строка (ИЛИ)")},r.SubjectMatchAll)
            .Select("bodyMode","Совпадение строк тела",new[]{new Choice(true,"Все строки (И)"),new Choice(false,"Любая строка (ИЛИ)")},r.BodyMatchAll)
            .Select("mode", "Совпадение условий", new[] { new Choice(true, "Все указанные"), new Choice(false, "Любое из указанных") }, r.MatchAll)
            .Select("target", "Привязать к *", targets, key)
            .Select("result", "Результат по правилу (обычно «Не менять»)", new[] { new Choice(null, "Не менять"), new Choice(BackupResult.Success, "Успешно"), new Choice(BackupResult.Event, "Информационное событие"), new Choice(BackupResult.Warning, "Предупреждение"), new Choice(BackupResult.Failed, "Ошибка") }, r.ResultOverride).Text("priority", "Приоритет · меньше = важнее", r.Priority.ToString()).Toggle("active", "Правило включено", r.Active)
            .Note("Правило действует для следующих писем. Для выбора и предварительного просмотра прошлых писем используйте «Привязать» в списке писем. Совпадение адреса или имени устройства само по себе не создаёт привязку.");
        form.Validate = () =>
        {
            _ = form.Number("priority", 0, 100000); if (form.Selected("target") == null) return "Выберите цель";
            var provider = form.Selected<string>("provider");
            if (provider == "Любой" && source != null) provider = ReportAnalyzer.DetectProvider(source.Subject, source.Body, source.Sender, sourceProfiles);
            var sqlTarget = form.Selected<string>("target").StartsWith("J:", StringComparison.Ordinal);
            if(form.Selected<string>("category")=="Update"&&(sqlTarget||string.IsNullOrWhiteSpace(form.Value("updateProduct"))))return "Для обновления выберите устройство и укажите продукт";
            if (provider == "SQLBackupAndFTP" && !sqlTarget&&form.Selected<string>("category")!="Update") return "Для SQLBackupAndFTP выберите Задание бэкапа";
            return string.IsNullOrWhiteSpace(form.Value("sender") + form.Value("subject") + form.Value("body")) ? "Укажите хотя бы одну постоянную фразу" : null;
        };
        if (form.ShowDialog() != true) return;
        var target = form.Selected<string>("target").Split(':'); r.TargetKind = target[0] == "D" ? TargetKind.Device : TargetKind.SqlJob; r.TargetId = int.Parse(target[1]);
        r.MailCategory=form.Selected<string>("category");r.UpdateProduct=form.Value("updateProduct");r.Provider = form.Selected<string>("provider"); r.SenderPhrase = form.Value("sender"); r.SubjectPhrase = form.Value("subject"); r.BodyPhrase = form.Value("body"); r.SubjectMatchAll=form.Selected<bool>("subjectMode"); r.BodyMatchAll=form.Selected<bool>("bodyMode"); r.MatchAll = form.Selected<bool>("mode"); r.Priority = form.Number("priority", 0, 100000); r.Active = form.Checked("active"); r.ResultOverride = r.MailCategory=="Update"?null:form.Selected("result") as BackupResult?;
        await using var editDb = factory.Create(); if (r.Id == 0) editDb.Rules.Add(r); else editDb.Rules.Update(r); await editDb.SaveChangesAsync();
        AppMessageBox.Show(this,"Правило сохранено для следующих писем. Для прошлых выберите письмо и нажмите «Привязать».","Привязка"); await ShowPage(page);
    }
    private async Task EditKeywords()
    {
        await using var db = factory.Create();
        var allProfiles = await db.Profiles.AsNoTracking().OrderBy(x => x.Provider).ToArrayAsync();
        var pick = new FormDialog(this, "Словари разбора почты").Submit("Открыть словарь")
            .Select("provider", "Почтовый профиль", allProfiles.Select(x => new Choice(x.Provider, x.Provider)), "Любой")
            .Text("newProvider", "Новый источник · оставьте пустым для существующего", "");
        pick.Validate = () => { var name = pick.Value("newProvider"); return name.Length > 100 ? "Название: до 100 символов" : name.Length > 0 && (name.Equals("DMARC", StringComparison.OrdinalIgnoreCase) || name.Equals("Неизвестно", StringComparison.OrdinalIgnoreCase) || allProfiles.Any(x => x.Provider.Equals(name, StringComparison.OrdinalIgnoreCase))) ? "Название уже используется или зарезервировано" : null; };
        if (pick.ShowDialog() != true) return;
        var provider = pick.Value("newProvider");
        var isNew = provider.Length > 0;
        if (!isNew) provider = pick.Selected<string>("provider");
        var profile = isNew ? KeywordDefaults.For(provider) : allProfiles.Single(x => x.Provider == provider);
        if(profile.EventRulesJson.Length>0){await EditMailSource(profile,null,null);return;}
        var inherited = provider != "Любой" && string.IsNullOrWhiteSpace(profile.Critical + profile.Warning + profile.Info);
        if (inherited)
        {
            var common = allProfiles.Single(x => x.Provider == "Любой");
            profile.Critical = common.Critical; profile.Warning = common.Warning; profile.Info = common.Info; profile.Ignore = common.Ignore; profile.Scope = common.Scope;
        }
        var form = new FormDialog(this, "Правила разбора почты — " + provider, 760)
            .Note("Разделитель: ; или новая строка. Целые слова и фразы; ошиб* — начало слова. Ошибка важнее предупреждения, предупреждение важнее INFO. Нулевые счётчики ошибок исключаются.")
            .Tab("Уровни событий").Select("scope", "Искать уровни события", new[] { new Choice(SearchScope.Both, "Тема и тело"), new Choice(SearchScope.Subject, "Тема"), new Choice(SearchScope.Body, "Тело") }, profile.Scope)
            .Text("critical", "Критические события · CRITICAL", profile.Critical, true).Text("warning", "Предупреждения · WARNING", profile.Warning, true).Text("info", "Информация · INFO", profile.Info, true).Text("ignore", "Исключения перед анализом", profile.Ignore, true)
            .Tab("Результат").Note("INFO определяет уровень события, а эти слова — результат бэкапа. Результат читается из полей Status / Result и словаря в выбранной области письма. Регистр не учитывается: Success и success равнозначны. По умолчанию слова результата ищутся в теме. Имена заданий и on job добавлять не нужно. Поиск во всём теле может зацепить промежуточные операции; настраивай точные фразы.").Text("successResults", "Результат: успех", profile.SuccessResults).Text("warningResults", "Результат: предупреждение", profile.WarningResults).Text("failedResults", "Результат: ошибка", profile.FailedResults)
            .Tab("Проверка").Text("sender", "Тестовый отправитель", "").Text("subject", "Тестовая тема", provider == "Любой" ? "Report" : "[Success] " + provider + " report").Text("body", "Тестовое тело", "Job name: Daily\nStatus: Success\nErrors: 0", true);
        form.Tab("Обновления")
            .Text("updateProduct","Продукт / метка перед версией · DSM, QTS, QuTS hero",profile.UpdateProduct)
            .Text("updateSubject","Тема уведомления содержит · буквальные фразы через ;",profile.UpdateSubjectTerms,true)
            .Text("updateBody","Тело уведомления содержит · буквальные фразы через ;",profile.UpdateBodyTerms,true)
            .Select("updateAll","Совпадение заполненных полей",new[]{new Choice(false,"Тема ИЛИ тело"),new Choice(true,"Тема И тело")},profile.UpdateMatchAll)
            .Note("Внутри каждого поля достаточно одной фразы. Здесь нет результата бэкапа и цвета проблемы. Версия извлекается после метки продукта; при нескольких разных версиях её нужно проверить вручную. Для NAS потребуется привязка к устройству.")
            .Action("Типовые условия обновлений",()=>{var defaults=new KeywordProfile{Provider=provider};MailUpdates.Defaults(defaults);form.Set("updateProduct",defaults.UpdateProduct);form.Set("updateSubject",defaults.UpdateSubjectTerms);form.Set("updateBody",defaults.UpdateBodyTerms);});
        form.Tab("Условия распознавания").Text("sourceSender", "Отправитель содержит · слова/фразы через ;", profile.SenderTerms)
            .Text("sourceSubject", "Тема содержит · слова/фразы через ;", profile.SubjectTerms)
            .Text("sourceBody", "Тело содержит · слова/фразы через ;", profile.BodyTerms)
            .Select("sourceAll", "Совпадение заполненных полей", [new Choice(true, "Все поля"), new Choice(false, "Любое поле")], profile.MatchAllSourceFields)
            .Note("Внутри поля достаточно одной фразы. Пустые поля не участвуют. Несколько подходящих источников дают «Неизвестно», чтобы не выбрать случайный. Пользовательские условия проверяются раньше стандартного распознавания. Для нового источника заполните хотя бы одно поле.");
        form.Tab("Результат").Select("resultScope", "Где искать слова результата", [new Choice(SearchScope.Subject, "Тема"), new Choice(SearchScope.Body, "Тело"), new Choice(SearchScope.Both, "Тема и тело")], profile.ResultScope);
        if (inherited) form.Tab("Уровни событий").Note("Показаны действующие слова общего словаря. Сохранение создаст отдельный набор для этого источника.");
        form.Validate = () => KeywordDefaults.Validate(form.Value("critical"), form.Value("warning"), form.Value("info"), form.Value("ignore"), form.Value("successResults"), form.Value("warningResults"), form.Value("failedResults"), form.Value("sourceSender"), form.Value("sourceSubject"), form.Value("sourceBody")) ?? (isNew && string.IsNullOrWhiteSpace(form.Value("sourceSender") + form.Value("sourceSubject") + form.Value("sourceBody")) ? "Укажите условия распознавания нового источника" : null);
        Func<string> independent=provider=="Любой"?()=>"":AddIndependentRulesTab(form,profile.AdditionalEventRulesJson);
        form.Tab("Уровни событий").Action("Заполнить стандартными словами", () =>
        {
            var defaults = KeywordDefaults.For(provider);
            form.Set("critical", defaults.Critical); form.Set("warning", defaults.Warning); form.Set("info", defaults.Info); form.Set("ignore", defaults.Ignore);
            form.Set("successResults", defaults.SuccessResults); form.Set("warningResults", defaults.WarningResults); form.Set("failedResults", defaults.FailedResults);
        });
        form.Tab("Проверка").Action("Проверить анализ", () =>
        {
            var invalid = form.Validate?.Invoke(); if (invalid != null) { AppMessageBox.Show(form, invalid); return; }
            ReadProfile(); var profiles = allProfiles.Where(x => x.Provider != profile.Provider).Append(profile).ToArray();
            var testMail=new MailReport("test","test",form.Value("sender"),form.Value("subject"),form.Value("body"),AppTime.Now);
            var notice=profiles.FirstOrDefault(p=>MailUpdates.Matches(p,testMail));if(notice!=null){var found=MailUpdates.Extract(testMail,notice.Provider,notice.UpdateProduct);new FormDialog(form,"Уведомление об обновлении",780).Note("Категория: обновления\nПродукт: "+found.Product+"\nВерсия: "+(found.Version==""?"не определена":found.Version)+"\n"+found.Issue).ViewOnly().ShowDialog();return;}
            var analysis = ReportAnalyzer.Analyze(new("test", "test", form.Value("sender"), form.Value("subject"), form.Value("body"), AppTime.Now), profiles);
            new FormDialog(form, "Результат теста", 780).Note($"Почтовый профиль: {analysis.Provider}\nБэкап: {Display.Result(analysis.Result)}\nУровень: {analysis.Severity}\nОснование: {analysis.Evidence}").Text("advice", "Разбор предупреждений", DiagnosticGuide.Analyze(analysis.Provider, form.Value("body"), settings.CustomDiagnostics).Format(true), true, true).ViewOnly().ShowDialog();
        });
        if (form.ShowDialog() != true) return; ReadProfile(); if (isNew) db.Profiles.Add(profile); else db.Profiles.Update(profile); await db.SaveChangesAsync(); await ShowPage(page);
        void ReadProfile()
        {
            profile.AdditionalEventRulesJson=independent();
            profile.UpdateProduct=form.Value("updateProduct");profile.UpdateSubjectTerms=form.Value("updateSubject");profile.UpdateBodyTerms=form.Value("updateBody");profile.UpdateMatchAll=form.Selected<bool>("updateAll");
            profile.SenderTerms = form.Value("sourceSender"); profile.SubjectTerms = form.Value("sourceSubject"); profile.BodyTerms = form.Value("sourceBody"); profile.MatchAllSourceFields = form.Selected<bool>("sourceAll"); profile.ResultScope = form.Selected<SearchScope>("resultScope");
            profile.Scope = form.Selected<SearchScope>("scope"); profile.Critical = form.Value("critical"); profile.Warning = form.Value("warning"); profile.Info = form.Value("info"); profile.Ignore = form.Value("ignore"); profile.SuccessResults = form.Value("successResults"); profile.WarningResults = form.Value("warningResults"); profile.FailedResults = form.Value("failedResults");
        }
    }
    private sealed class RuleRow(BindingRule rule, string target)
    {
        public string Category=>rule.MailCategory=="Update"?"Обновления":"Отчёт / событие";public BindingRule Entity => rule; public string Provider => rule.Provider; public string SubjectPhrase => rule.SubjectPhrase; public string BodyPhrase => rule.BodyPhrase; public string Target => (rule.TargetKind == TargetKind.Device ? "Устройство · " : "Задание · ") + target; public int Priority => rule.Priority; public bool Active => rule.Active;
    }
    private sealed class QueueRow(string subject, ReportEvent report)
    {
        public ReportEvent Event => report; public DateTime SortDate => report.ReceivedAt; public string Received => Display.Stamp(SortDate); public string Provider => report.Provider;
        public string Severity => report.Severity.ToString(); public string Subject => MailDisplayLimit(subject,360); public string Issue => MailDisplayLimit(report.Issue,500);
    }
}
