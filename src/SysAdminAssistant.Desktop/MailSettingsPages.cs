using System.Windows;
using Microsoft.EntityFrameworkCore;
using System.Windows.Controls;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private MailboxPurpose mailSettingsTab;
    private async Task ShowMailboxes()
    {
        settings.NormalizeMailboxes();
        var reportTable = Table(settings.Mailboxes.Where(x => x.Purpose == MailboxPurpose.Reports).ToArray(), ("Название", "Name", null), ("Сервер", "Host", null), ("Логин", "User", null), ("Папка", "Folder", null), ("Включён", "Enabled", null));
        var dmarcTable = Table(settings.Mailboxes.Where(x => x.Purpose == MailboxPurpose.Dmarc).ToArray(), ("Название", "Name", null), ("Сервер", "Host", null), ("Логин", "User", null), ("Папка", "Folder", null), ("Включён", "Enabled", null));
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Отчёты", Content = reportTable });
        tabs.Items.Add(new TabItem { Header = "DMARC", Content = dmarcTable });
        tabs.SelectedIndex = (int)mailSettingsTab;
        MailSettings? SelectedMailbox() => (tabs.SelectedIndex == 1 ? dmarcTable : reportTable).SelectedItem as MailSettings;
        var window = new DialogWindow { Owner = this, Title = "Почтовые ящики IMAP", Width = 1050, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        MailSettings? edit = null; bool add = false, remove = false, approve = false;
        var create = new Button { Content = "Добавить ящик" }; create.Click += (_, _) => { add = true; window.DialogResult = true; };
        var change = new Button { Content = "Изменить", IsEnabled = false }; change.Click += (_, _) => { edit = SelectedMailbox(); window.DialogResult = true; };
        var delete = new Button { Content = "Удалить", IsEnabled = false }; delete.Click += (_, _) => { edit = SelectedMailbox(); remove = true; window.DialogResult = true; };
        var trust = new Button { Content = "Подтвердить отправителя", IsEnabled = false }; trust.Click += (_, _) => { edit = SelectedMailbox(); approve = true; window.DialogResult = true; };
        void SelectionChanged() { mailSettingsTab = (MailboxPurpose)Math.Max(0, tabs.SelectedIndex); change.IsEnabled = delete.IsEnabled = SelectedMailbox() != null; trust.IsEnabled = SelectedMailbox()?.Purpose == MailboxPurpose.Dmarc; }
        reportTable.SelectionChanged += (_, _) => SelectionChanged(); dmarcTable.SelectionChanged += (_, _) => SelectionChanged(); tabs.SelectionChanged += (_, _) => SelectionChanged();
        var close = new Button { Content = "Закрыть", IsCancel = true }; window.Content = Panel(tabs, create, change, trust, delete, close);
        if (window.ShowDialog() != true) return;
        if (approve && edit != null) await ApproveDmarcSender(edit);
        else if (remove && edit != null)
        {
            if (AppMessageBox.Show(this, $"Удалить подключение «{edit}»? События этого ящика останутся в базе.", "IMAP", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                await UpdateSettings(x => x.Mailboxes.RemoveAll(m => m.Id == edit.Id));
        }
        else if (add || edit != null) await EditMailbox(edit ?? new() { Purpose = mailSettingsTab, UnseenOnly = mailSettingsTab != MailboxPurpose.Dmarc, AuthPolicy=MailAuthPolicy.BothPassOneIdentity });
        await ShowMailboxes();
    }
    private async Task EditMailbox(MailSettings source)
    {
        var m = Copy(source);
        var form = new FormDialog(this, "Ящик IMAP", 760).Tab("Подключение").Text("name", "Название ящика", m.Name)
            .Note("Назначение: " + m.PurposeName + ". Один IMAP-ящик можно подключить отдельно в обоих разделах.")
            .Toggle("enabled", "Принимать отчёты", m.Enabled).Text("host", "Сервер", m.Host).Text("port", "Порт TLS", m.Port.ToString())
            .Text("user", "Логин", m.User).Secret("password", "Новый пароль · пусто = оставить прежний").Text("folder", "Папка", m.Folder)
            .Select("mode", "Какие письма читать", [new Choice(true, "Только непрочитанные"), new Choice(false, "Все письма")], m.UnseenOnly)
            .Note("Чтение не меняет флаг прочтения; сертификат TLS проверяется. Вложения обычных отчётов не анализируются.");
        form.Tab("Допуск писем");
        if (m.Purpose == MailboxPurpose.Reports)
            form.Text("domains", "Разрешённые домены отправителей · по одному на строку", string.Join("\n", m.AllowedDomains), true)
                .Note("Пустой список блокирует обработку. Сравнивается точный домен From: example.com не разрешает sub.example.com. Адрес From можно подделать; это фильтр допуска, а не проверка подлинности.");
        else form.Text("trusted", "Подтверждённые отправители · полный email на строке", string.Join("\n", m.TrustedDmarcSenders), true)
            .Note("Тема обязательно содержит слово DMARC без учёта регистра. Пока отправитель не подтверждён, тело и вложения не загружаются. Для выбора по заголовкам используйте «Подтвердить отправителя» в списке ящиков.");
        form.Action("Проверить подключение к ящику",async()=>{
            var test=new MailSettings {Id=m.Id,Host=form.Value("host"),Port=form.Number("port",1,65535),User=form.Value("user"),Folder=form.Value("folder")};
            return await remote.CommandAsync<string>("mail-test",new MailConnectionTest(test,form.Value("password")),lifetime.Token);
        });
        form.Tab("Подлинность")
            .Toggle("auth","Проверять подлинность входящих писем",m.RequireSpfDkim)
            .Select("receiver","Принимающая почтовая система",new[]{new Choice(MailAuthReceiver.Custom,"Свой сервер · точный список узлов"),new Choice(MailAuthReceiver.YandexMxFront,"Яндекс · mxfront production main")},m.AuthReceiver)
            .Text("authserv","Доверенные узлы · по одному на строку · для Яндекса необязательно",string.Join("\n",MailAuthentication.TrustedIds(m)),true)
            .Note("Профиль Яндекса принимает семейство mail-nwsmtp-mxfront-production-main-<число>, включая узлы 60 и 78. Список выше дополняет профиль точными значениями. Маски и произвольные регулярные выражения не используются.")
            .Select("authPolicy","Требования к результатам",new[]{new Choice(MailAuthPolicy.BothPassOneIdentity,"Оба pass; From совпадает хотя бы с одной идентичностью"),new Choice(MailAuthPolicy.BothAligned,"Строго: оба pass и оба домена совпадают с From · прежний режим")},m.AuthPolicy)
            .Toggle("sanitized","Принимающая система удаляет поддельные результаты проверки со своими идентификаторами",m.AuthHeadersSanitized)
            .Note("Идентификатор обозначает принимающий узел, а не отправителя. Доверие требует защиты заголовков на стороне почтовой системы; один лишь похожий идентификатор не доказывает источник. Профиль не включает это подтверждение автоматически. Оба режима требуют SPF pass и DKIM pass. В первом достаточно одного совпадения домена From; во втором нужны оба. Вложения отклонённого письма не загружаются.")
            .Note("В первом режиме при отсутствии header.d используется удостоверенная DKIM-идентичность header.i. Это локальное правило приёма по результатам сервера, не самостоятельный расчёт DMARC.");
        void ReadAuthentication(){
            m.RequireSpfDkim=form.Checked("auth");m.AuthReceiver=form.Selected<MailAuthReceiver>("receiver");m.AuthPolicy=form.Selected<MailAuthPolicy>("authPolicy");
            m.TrustedAuthservIds=form.Value("authserv").Split(['\r','\n'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            m.TrustedAuthservId=m.TrustedAuthservIds.FirstOrDefault()??"";m.AuthHeadersSanitized=form.Checked("sanitized");
        }
        form.ActionAsync("Проверить заголовки письма",()=>{
            ReadAuthentication();var proposed=Copy(m);proposed.RequireSpfDkim=true;
            var test=new FormDialog(form,"Проверка заголовков",900)
                .Text("from","Полный адрес From проверяемого письма")
                .Text("headers","Authentication-Results · каждый заголовок с новой строки","",true).TextHeight("headers",180)
                .Note("Локальная проверка разбора и выбранных правил. Она не подтверждает, что вставленный заголовок действительно добавлен принимающим сервером. Тело и вложения не нужны.")
                .Text("result","Результат","",true,true).TextHeight("result",210);
            test.Action("Проверить",()=>{var result=MailAuthentication.Evaluate(proposed,MailAuthentication.PastedHeaders(test.Value("headers")),test.Value("from").Trim());test.Set("result",result.Describe());return Task.FromResult(result.Allowed?"Выбранные требования выполнены":"Письмо будет отклонено; причина указана выше");}).ViewOnly().ShowDialog();return Task.CompletedTask;
        });
        form.Validate = () =>
        {
            ReadAuthentication();
            if (form.Checked("enabled")) { try { MailAuthentication.Validate(m); } catch (ArgumentException e) { return e.Message; } }
            _ = form.Number("port", 1, 65535);
            var required = Required(form.Value("host"), form.Value("user"), form.Value("folder")); if (required != null) return required;
            _ = MailAdmission.ParseList(form.Value(m.Purpose == MailboxPurpose.Reports ? "domains" : "trusted"), m.Purpose == MailboxPurpose.Dmarc);
            if (settings.Mailboxes.Any(x => x.Id != m.Id && x.Purpose == m.Purpose && x.Port == form.Number("port", 1, 65535) && x.Host.Equals(form.Value("host"), StringComparison.OrdinalIgnoreCase) && x.User == form.Value("user") && x.Folder == form.Value("folder"))) return "Такой ящик и папка уже подключены в этом разделе";
            return form.Checked("enabled") && m.ProtectedPassword.Length == 0 && form.Value("password").Length == 0 ? "Укажите пароль включённого ящика" : null;
        };
        if (form.ShowDialog() != true) return;
        if (m.Purpose == MailboxPurpose.Reports) m.AllowedDomains = MailAdmission.ParseList(form.Value("domains"), false).ToList();
        else
        {
            var senders = MailAdmission.ParseList(form.Value("trusted"), true).ToList();
            var added = senders.Except(m.TrustedDmarcSenders, StringComparer.OrdinalIgnoreCase).ToArray();
            if (added.Length > 0 && AppMessageBox.Show(this, "Разрешить разбор DMARC от:\n" + string.Join("\n", added) + "\n\nАдрес From можно подделать. Подтверждение не доказывает подлинность письма.", "Подтверждение отправителей", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            m.TrustedDmarcSenders = senders;
        }
        m.Name = form.Value("name"); m.Enabled = form.Checked("enabled"); m.Host = form.Value("host"); m.Port = form.Number("port", 1, 65535); m.User = form.Value("user"); m.Folder = form.Value("folder"); m.UnseenOnly = form.Selected<bool>("mode");
        if (form.Value("password").Length > 0) m.ProtectedPassword = LocalSecret.Protect(form.Value("password"));
        await UpdateSettings(x => { x.Mailboxes.RemoveAll(y => y.Id == m.Id); x.Mailboxes.Add(m); }); return;
    }
    private async Task ApproveDmarcSender(MailSettings mailbox)
    {
        StatusLine.Text = "Читаю заголовки DMARC · вложения не загружаются";
        var candidates = factory.Remote == null ? await MailAdmission.DiscoverAsync(mailbox, lifetime.Token)
            : await factory.Remote.CommandAsync<DmarcSenderCandidate[]>("dmarc-senders", new { MailboxId = mailbox.Id }, lifetime.Token);
        if (candidates.Length == 0) { AppMessageBox.Show(this, "В последних 200 письмах с DMARC в теме нет новых отправителей. Адрес можно также указать вручную в настройках ящика.", "DMARC"); return; }
        var form = new FormDialog(this, "Подтвердить отправителя DMARC", 800)
            .Select("sender", "Отправитель · тема последнего найденного письма", candidates.Select(x => new Choice(x.Address, x.Address + " · " + x.Date + " · " + x.Subject)), candidates[0].Address)
            .Note("Загружены только заголовки. Сверьте адрес с почтовым сервисом. From можно подделать: подтверждение разрешает обработку, но не доказывает подлинность. Для ранее прочитанных писем выберите «Все письма» в настройках ящика.")
            .Submit("Подтвердить отправителя");
        if (form.ShowDialog() != true) return;
        var sender = form.Selected<string>("sender");
        await UpdateSettings(x => { var target = x.Mailboxes.Single(m => m.Id == mailbox.Id); if (!target.TrustedDmarcSenders.Contains(sender, StringComparer.OrdinalIgnoreCase)) target.TrustedDmarcSenders.Add(sender); });
    }
    private async Task EditRetention()
    {
        var form = new FormDialog(this, "Хранение событий", 650).Select("days", "Срок хранения", [new Choice(0, "Бессрочно"), new Choice(30, "30 дней"), new Choice(90, "90 дней")], settings.EventRetentionDays)
            .Note("Срок считается от получения записи. Удаляются старые отчёты, очередь и события, включая DMARC. Устройства, настройки, задания, Vault и акты инвентаризации сохраняются. После подтверждения очистка продолжается автоматически.");
        if (form.ShowDialog() != true) return;
        var days = form.Selected<int>("days"); if (days == settings.EventRetentionDays) return;
        if (days > 0)
        {
            var count = await EventRetention.PreviewAsync(factory, days, AppTime.Now, lifetime.Token);
            if (AppMessageBox.Show(this, $"Хранить события {days} дней?\nСейчас под удаление попадают {count.Reports} отчётов и {count.Incidents} событий.\nУдаление необратимо. Нераспознанные письма в старой очереди также удаляются. Счётчик может измениться при фоновом приёме.\nХеши сообщений останутся, чтобы старые письма не загрузились снова.", "Очистка истории", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        }
        await UpdateSettings(x => x.EventRetentionDays = days);
        var removed = await EventRetention.ApplyAsync(factory, days, AppTime.Now, lifetime.Token);
        AppMessageBox.Show(this, days == 0 ? "Установлено бессрочное хранение. Ранее удалённые записи не восстанавливаются." : $"Удалено: {removed.Reports} отчётов, {removed.Incidents} событий.", "Хранение событий");
        ShowSettingsRefresh();
    }
    private async Task EditCustomDiagnostic(CustomDiagnostic source)
    {
        var c = Copy(source);
        await using var db = factory.Create();
        var providers = await db.Profiles.Select(x => x.Provider).ToArrayAsync();
        var form = new FormDialog(this, "Статья справочника", 780).Text("title", "Название", c.Title)
            .Select("provider", "Продукт", providers.Append(c.Provider).Distinct().Select(x => new Choice(x, x)), c.Provider)
            .Text("phrase", "Фраза в теле отчёта · точное вхождение", c.Phrase).Text("meaning", "Что означает", c.Meaning, true)
            .Text("impact", "Последствия", c.Impact, true).Text("steps", "Что проверить · каждый шаг с новой строки", c.Steps, true)
            .Text("source", "HTTPS-источник · необязательно", c.Source).Toggle("enabled", "Использовать при анализе", c.Enabled)
            .Note("Тема и имена заданий не служат основанием. Статья не меняет статус события. Встроенные статьи остаются отдельно; ваши статьи сохраняются в настройках этого приложения.");
        void Read() { c.Title = form.Value("title"); c.Provider = form.Selected<string>("provider"); c.Phrase = form.Value("phrase"); c.Meaning = form.Value("meaning"); c.Impact = form.Value("impact"); c.Steps = form.Value("steps"); c.Source = form.Value("source"); c.Enabled = form.Checked("enabled"); }
        form.Validate = () => { Read(); return c.Validate(); };
        if (form.ShowDialog() == true) { Read(); await UpdateSettings(x => { x.CustomDiagnostics.RemoveAll(y => y.Id == c.Id); x.CustomDiagnostics.Add(c); }); }
        return;
    }
}
