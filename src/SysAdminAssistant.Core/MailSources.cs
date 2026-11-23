using System.Text.Json;
using System.Text.Json.Serialization;
namespace SysAdminAssistant.Core;

public sealed record MailSourceEvent(string Title,string Subject,string Body,bool MatchAll,Severity Severity,BackupResult Result,string Advice)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public MailNumericLevels? Numeric {get;init;}
    [JsonIgnore] public string NumericEvidence {get;init;}="";
}
public sealed record MailNumericLevels(MailNumericCondition[] Info,MailNumericCondition[] Warning,MailNumericCondition[] Critical)
{
    public IEnumerable<(Severity Level,MailNumericCondition[] Conditions)> Groups()
    {
        yield return (Severity.Critical,Critical);
        yield return (Severity.Warning,Warning);
        yield return (Severity.Info,Info);
    }
    [JsonIgnore] public bool HasConditions=>Groups().Any(g=>g.Conditions is {Length:>0});
}
public sealed record MailSourcePackage(int Format,string Product,string Name,string Sender,string Subject,string Body,bool MatchAll,MailSourceEvent[] Events);
public sealed record MailReprocessSelection(int[] ReportIds);

public sealed record MailRecheckResult(bool Applied,bool IsPending,string Provider,string Issue);

public static class MailSources
{
    private static readonly JsonSerializerOptions FormatOptions=new(){WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=12};
    public static MailSourceEvent[] ReadEvents(string? json)
    {
        if(json=="")return [];
        if(string.IsNullOrWhiteSpace(json)||json.Length>65_536)throw new ArgumentException("Неверный размер правил событий.");
        var events=JsonSerializer.Deserialize<MailSourceEvent[]>(json,FormatOptions)??throw new ArgumentException("Список событий отсутствует.");
        ValidateEvents(events);return events;
    }
    public static string WriteEvents(MailSourceEvent[] events){ValidateEvents(events);var json=JsonSerializer.Serialize(events,FormatOptions);if(json.Length>65_536)throw new ArgumentException("Объём правил событий превышает лимит.");return json;}
    public static void ValidateEvents(MailSourceEvent[] events)
    {
        if(events.Length>64)throw new ArgumentException("Не более 64 событий на источник.");
        var numericCount=0;
        var titles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var e in events){
            if(e==null||string.IsNullOrWhiteSpace(e.Title)||e.Title.Length>160||!titles.Add(e.Title.Trim())||
                e.Subject==null||e.Body==null||(string.IsNullOrWhiteSpace(e.Subject+e.Body)&&e.Numeric?.HasConditions!=true)||e.Advice==null||e.Advice.Length>1500||
                e.Severity is not (Severity.Info or Severity.Warning or Severity.Critical)||
                e.Result is not (BackupResult.Event or BackupResult.Success or BackupResult.Warning or BackupResult.Failed)||
                e.Numeric?.HasConditions!=true&&e.Result==BackupResult.Failed&&e.Severity!=Severity.Critical||
                e.Numeric?.HasConditions!=true&&e.Result==BackupResult.Warning&&e.Severity==Severity.Info||
                KeywordDefaults.Validate(e.Subject,e.Body)!=null)
                throw new ArgumentException("Укажите уникальное название события, условия, допустимый уровень и результат.");
            if(e.Numeric!=null)
                foreach(var (level,conditions) in e.Numeric.Groups())
                {
                    if(conditions==null||conditions.Length>12)throw new ArgumentException("Не более 12 числовых условий на уровень.");
                    numericCount+=conditions.Length;
                    if(numericCount>72)throw new ArgumentException("Не более 72 числовых условий на источник.");
                    if(conditions.Length>0&&(e.Result==BackupResult.Failed&&level!=Severity.Critical||e.Result==BackupResult.Warning&&level==Severity.Info))
                        throw new ArgumentException("Уровень числового условия не соответствует результату бэкапа.");
                    foreach(var condition in conditions)
                    {
                        if(condition==null)throw new ArgumentException("Числовое условие отсутствует.");
                        condition.Validate();
                    }
                }
        }
    }
    public static void ValidateProfile(KeywordProfile p)
    {
        var extra=ReadEvents(p.AdditionalEventRulesJson);
        if(extra.Length>0&&p.Provider is ("Любой" or "DMARC" or "Неизвестно"))throw new ArgumentException("Дополнительные правила требуют конкретного почтового профиля.");
        var events=ReadEvents(p.EventRulesJson);
        if(p.EventRulesJson=="" )return;
        Validate(new(1,"MailSource",p.Provider,p.SenderTerms,p.SubjectTerms,p.BodyTerms,p.MatchAllSourceFields,events));
    }
    public static MailSourcePackage Parse(string json)
    {
        if(string.IsNullOrWhiteSpace(json)||json.Length>100_000)throw new ArgumentException("Файл источника пуст или слишком большой.");
        var p=JsonSerializer.Deserialize<MailSourcePackage>(json,FormatOptions)??throw new ArgumentException("Источник отсутствует.");
        Validate(p);return p;
    }
    public static void Validate(MailSourcePackage p)
    {
        if(p.Format!=1||p.Product!="MailSource"||string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>100||
            new[]{"Любой","DMARC","Неизвестно"}.Contains(p.Name.Trim(),StringComparer.OrdinalIgnoreCase)||
            p.Sender==null||p.Subject==null||p.Body==null||p.Events==null||p.Events.Length==0||
            string.IsNullOrWhiteSpace(p.Sender+p.Subject+p.Body)||KeywordDefaults.Validate(p.Sender,p.Subject,p.Body)!=null)
            throw new ArgumentException("Неверный формат источника. Нужны название, условия распознавания и события.");
        ValidateEvents(p.Events);
    }
    public static string Export(MailSourcePackage p)=>JsonSerializer.Serialize(p,FormatOptions);
    public static MailSourcePackage From(KeywordProfile p)=>new(1,"MailSource",p.Provider,p.SenderTerms,p.SubjectTerms,p.BodyTerms,p.MatchAllSourceFields,ReadEvents(p.EventRulesJson));
    public static MailSourceEvent[] Match(KeywordProfile p,MailReport m)=>Evaluate(p.EventRulesJson,m.Subject,m.Body);
    public static MailSourceEvent[] MatchAdditional(KeywordProfile p,MailReport m)=>Evaluate(p.AdditionalEventRulesJson,m.Subject,m.Body);
    public static bool HasAdditional(KeywordProfile p,string subject,string body)=>p.AdditionalEventRulesJson.Length>0&&ReadEvents(p.AdditionalEventRulesJson).Any(e=>!string.IsNullOrWhiteSpace(e.Subject+e.Body)&&Evaluate(e,subject,body)!=null);
    private static MailSourceEvent[] Evaluate(string json,string subject,string body)=>
        ReadEvents(json).Select(e=>Evaluate(e,subject,body)).OfType<MailSourceEvent>().ToArray();

    private static MailSourceEvent? Evaluate(MailSourceEvent e,string subjectText,string bodyText)
    {
        var subject=ReportAnalyzer.Terms(e.Subject).ToArray();var body=ReportAnalyzer.Terms(e.Body).ToArray();
        var checks=new List<bool>(2);
        if(subject.Length>0)checks.Add(subject.Any(t=>subjectText.Contains(t,StringComparison.OrdinalIgnoreCase)));
        if(body.Length>0)checks.Add(body.Any(t=>bodyText.Contains(t,StringComparison.OrdinalIgnoreCase)));
        if(checks.Count>0&&!(e.MatchAll?checks.All(x=>x):checks.Any(x=>x)))return null;
        if(e.Numeric?.HasConditions!=true)return checks.Count>0?e:null;
        foreach(var (level,conditions) in e.Numeric.Groups())
            foreach(var condition in conditions)
            {
                var result=condition.Evaluate(bodyText);
                if(result.Match!=null)return e with{Severity=level,NumericEvidence=result.Evidence};
            }
        return null;
    }
}
