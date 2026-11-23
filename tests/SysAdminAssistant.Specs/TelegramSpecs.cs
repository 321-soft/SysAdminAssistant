using System.Net;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Infrastructure;
internal static class TelegramSpecs
{
    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Reply = "{\"ok\":true}";
        public string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.Host != "api.telegram.org" || request.Method != HttpMethod.Post) throw new Exception("Invalid Telegram request");
            Body = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(Status) { Content = new StringContent(Reply, Encoding.UTF8, "application/json") };
        }
    }
    public static async Task RunAsync(Action<string, bool> check)
    {
        var handler = new Handler(); using var client = new HttpClient(handler);
        var settings = new TelegramSettings { Enabled = true, ChatId = "-123456", ProtectedToken = "fixture" };
        const string secret = "123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ12345";
        await TelegramSender.SendAsync(settings, "<device> & alert", CancellationToken.None, client, secret);
        using var json = JsonDocument.Parse(handler.Body);
        check("Telegram sends plain text and selected destination", json.RootElement.GetProperty("chat_id").GetString() == "-123456" && json.RootElement.GetProperty("text").GetString() == "<device> & alert" && !json.RootElement.TryGetProperty("parse_mode", out _));
        handler.Status = HttpStatusCode.Forbidden; bool denied = false;
        try { await TelegramSender.SendAsync(settings, "test", CancellationToken.None, client, secret); }
        catch (InvalidOperationException e) { denied = e.Message.Contains("403") && !e.Message.Contains(secret); }
        check("Telegram denial exposes code without token", denied);
        handler.Status = HttpStatusCode.OK; handler.Reply = "{\"ok\":false}"; bool rejected = false;
        try { await TelegramSender.SendAsync(settings, "test", CancellationToken.None, client, secret); }
        catch (InvalidOperationException) { rejected = true; }
        check("Telegram response must confirm delivery", rejected);
    }
}
