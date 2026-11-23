using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SysAdminAssistant.Core;

public static class AgentHttp
{
    public static async Task<T> SendAsync<T>(HttpClient http,string path,object request,CancellationToken token,TimeSpan? timeout=null)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout??TimeSpan.FromSeconds(20));token=deadline.Token;
        using var content=JsonContent.Create(request,options:AgentWire.Json);using var message=new HttpRequestMessage(HttpMethod.Post,path){Content=content};using var response=await http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode&&!((path=="agent/report"||path=="agent/report-v2"||path=="agent/report-v3")&&response.StatusCode==HttpStatusCode.Conflict))throw new HttpRequestException(global::SysAdminAssistant.Core.ProductText.Get("Сервер не принял запрос"),null,response.StatusCode);
        if(response.Content.Headers.ContentLength>200000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Ответ слишком велик"));
        await using var stream=await response.Content.ReadAsStreamAsync(token);using var data=new MemoryStream();var bytes=new byte[8192];int count;
        while((count=await stream.ReadAsync(bytes,token))>0){if(data.Length+count>200000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Ответ слишком велик"));data.Write(bytes,0,count);}
        if(response.StatusCode==HttpStatusCode.Conflict){var notice=JsonSerializer.Deserialize<AgentSequenceNotice>(data.ToArray(),AgentWire.Json)??throw new InvalidDataException();if(notice.NextSequence is <1 or >9000000000000)throw new InvalidDataException();throw new AgentSequenceException(notice.NextSequence);}
        return JsonSerializer.Deserialize<T>(data.ToArray(),AgentWire.Json)??throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Пустой ответ"));
    }
}
