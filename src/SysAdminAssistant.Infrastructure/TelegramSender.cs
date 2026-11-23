using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed class TelegramSettings
{
    public int DelayMinutes {get;set;}=3;
    public static readonly int[] DelayOptions=[0,1,3,5,10];
    public Dictionary<string,bool> Topics {get;set;}=[];
    public bool TopicEnabled(string id)=>id=="updates.available"?false:Topics.TryGetValue(id,out var value)?value:TelegramTopics.All.FirstOrDefault(t=>t.Id==id)?.Default??true;
    public bool AcceptsEvent(Severity severity,string topic,bool recovered=false)=>Accepts(severity)&&TopicEnabled(topic)&&(!recovered||TopicEnabled("recovery"));
    public bool PinActiveProblems {get;set;}=true;
    public bool Enabled { get; set; }
    public string ChatId { get; set; } = "";
    public string ProtectedToken { get; set; } = "";
    public bool Critical { get; set; } = true;
    public bool Warning { get; set; }
    public bool Info { get; set; }
    public bool Accepts(Severity severity) => severity switch { Severity.Critical => Critical, Severity.Warning => Warning, Severity.Info => Info, _ => false };
    public void Validate()
    {
        if(!DelayOptions.Contains(DelayMinutes))throw new ArgumentException("Недопустимая задержка Telegram");
        if(Topics==null||Topics.Count>100||Topics.Keys.Any(k=>!TelegramTopics.Known(k)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестная категория Telegram"));
        if (ChatId.Length > 100 || ChatId.Length > 0 && !Regex.IsMatch(ChatId, @"^(?:-?[0-9]+|@[A-Za-z][A-Za-z0-9_]{3,})$", RegexOptions.CultureInvariant)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Chat ID: число или @имя канала"));
        if (Enabled && (ChatId.Length == 0 || ProtectedToken.Length == 0)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Для Telegram нужны Chat ID и токен бота"));
        if (Enabled && !(Critical || Warning || Info)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выберите хотя бы один уровень уведомлений"));
    }
}
public static class TelegramSender
{
    public static Func<int>? ServerDelayMinutes {get;set;}
    internal static Task WaitBeforeSendAsync(CancellationToken token)=>Task.Delay(TimeSpan.FromMinutes(ServerDelayMinutes?.Invoke()??0),token);
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    public static string Format(Incident incident,string? subject=null)
    {
        string Cut(string text, int limit) => text.Length <= limit ? text : text[..limit] + "…";
        var level = incident.Severity switch { Severity.Critical => "КРИТИЧНО", Severity.Warning => "ПРЕДУПРЕЖДЕНИЕ", _ => "ИНФОРМАЦИЯ" };
        return $"{TelegramTopics.Emoji(incident.Severity)} SysAdmin Assistant · {ProductText.Get(level)}\n{Cut(ProductText.Get(incident.Source), 300)}\n{(string.IsNullOrWhiteSpace(subject)?"":Cut(ProductText.Get(subject),500)+"\n")}{Cut(ProductText.Language=="en"?ProductText.Get(incident.Title):incident.Title, 1000)}\n{incident.CreatedAt:dd.MM.yyyy HH:mm:ss} · событие #{incident.Id}";
    }
    public static async Task SendAsync(TelegramSettings settings,string text,CancellationToken token,HttpClient? client=null,string? resolvedToken=null,int? messageThreadId=null)=>_ =await SendMessageAsync(settings,text,token,client,resolvedToken,requireMessageId:false,messageThreadId:messageThreadId);
    public static async Task<int> SendMessageAsync(TelegramSettings settings,string text,CancellationToken token,HttpClient? client=null,string? resolvedToken=null,int? replyToMessageId=null,bool requireMessageId=true,int? messageThreadId=null)
    {
        settings.Validate();
        if(settings.ChatId.Length==0||settings.ProtectedToken.Length==0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Сначала сохраните Chat ID и токен Telegram"));
        var secret=resolvedToken??LocalSecret.Unprotect(settings.ProtectedToken);
        if(!Regex.IsMatch(secret,@"^[0-9]+:[A-Za-z0-9_-]{20,200}$",RegexOptions.CultureInvariant))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный формат токена Telegram"));
        try{
            var payload=new Dictionary<string,object>{{"chat_id",settings.ChatId},{"text",ProductText.Language=="en"?ProductText.Get(text):text},{"link_preview_options",new{is_disabled=true}}};
            if(messageThreadId is >0)payload["message_thread_id"]=messageThreadId.Value;
            if(replyToMessageId is >0)payload["reply_parameters"]=new{message_id=replyToMessageId.Value,allow_sending_without_reply=true};
            await WaitBeforeSendAsync(token);
            using var response=await(client??Client).PostAsJsonAsync("https://api.telegram.org/bot"+secret+"/sendMessage",payload,token);
            if(!response.IsSuccessStatusCode){
                if(response.StatusCode==System.Net.HttpStatusCode.BadRequest&&replyToMessageId is >0){
                    var raw=await response.Content.ReadAsStringAsync(token);if(raw.Length<20000){using var error=JsonDocument.Parse(raw);var description=error.RootElement.TryGetProperty("description",out var d)?d.GetString()??"":"";if(description.Contains("message to be replied",StringComparison.OrdinalIgnoreCase)||description.Contains("REPLY_MESSAGE_ID_INVALID",StringComparison.OrdinalIgnoreCase))return await SendMessageAsync(settings,text,token,client,secret,null,requireMessageId);}
                }
                throw new InvalidOperationException($"Telegram: отказ отправки (HTTP {(int)response.StatusCode}). Проверьте токен, Chat ID и доступ бота; при 429 повторная попытка будет позже.");
            }
            var body=await response.Content.ReadFromJsonAsync<JsonElement>(token);
            if(body.ValueKind!=JsonValueKind.Object||!body.TryGetProperty("ok",out var ok)||ok.ValueKind!=JsonValueKind.True)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Telegram не подтвердил отправку сообщения"));
            if(body.TryGetProperty("result",out var result)&&result.TryGetProperty("message_id",out var id)&&id.TryGetInt32(out var value)&&value>0)return value;
            if(requireMessageId)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Telegram не вернул ID сообщения"));return 0;
        }
        catch(JsonException){throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Telegram вернул некорректный ответ"));}
        catch(HttpRequestException){throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Telegram недоступен: проверьте интернет на сервере"));}
        catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Telegram не ответил вовремя"));}
    }
}
