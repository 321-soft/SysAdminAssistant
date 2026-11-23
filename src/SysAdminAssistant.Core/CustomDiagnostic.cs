namespace SysAdminAssistant.Core;

public sealed class CustomDiagnostic
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Provider { get; set; } = "Veeam";
    public string Title { get; set; } = "";
    public string Phrase { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Impact { get; set; } = "";
    public string Steps { get; set; } = "";
    public string Source { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Provider) || Provider.Length > 100) return "Выберите источник или «Любой»";
        if (Title.Trim().Length == 0 || Phrase.Trim().Length < 3 || Meaning.Trim().Length == 0 || Steps.Trim().Length == 0) return "Заполните название, фразу (от 3 символов), объяснение и рекомендации";
        if (Phrase.Length > 500 || new[] { Title, Meaning, Impact, Steps }.Any(x => x.Length > 8000)) return "Слишком длинная статья или фраза";
        if (Source.Length > 0 && (!Uri.TryCreate(Source, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0)) return "Источник: укажите HTTPS-ссылку или оставьте поле пустым";
        return null;
    }
    public DiagnosticEntry ToEntry() => new()
    {
        Id = "custom." + Id, Providers = [Provider], Title = Title + (Enabled ? "" : " · отключена"),
        Patterns = [System.Text.RegularExpressions.Regex.Escape(System.Text.RegularExpressions.Regex.Replace(Phrase.Trim(), @"\s+", " "))], Meaning = Meaning, Impact = Impact,
        Steps = Steps.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), Source = Source,
        Scope = "Пользовательская статья; точное вхождение фразы без учёта регистра. Проверьте применимость к текущему сообщению.",
        Confidence = "Рекомендация пользователя", Reviewed = "—", IsCustom = true
    };
}
