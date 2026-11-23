using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Server;
public static class AgentEndpoints
{
    public static async Task<T> ReadAsync<T>(HttpContext context,CancellationToken token)
    {
        if(context.Request.ContentType?.Split(';')[0].Trim()!="application/json")throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Нужен application/json"));
        const int max=600000;if(context.Request.ContentLength>max)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой запрос агента"));
        using var buffer=new MemoryStream();var bytes=new byte[8192];int count;
        while((count=await context.Request.Body.ReadAsync(bytes,token))>0){if(buffer.Length+count>max)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой запрос агента"));buffer.Write(bytes,0,count);}
        return JsonSerializer.Deserialize<T>(buffer.ToArray(),AgentWire.Json)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пустой запрос агента"));
    }
}
