using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
namespace SysAdminAssistant.Core;
public static class AgentFailure
{
 // Never copy exception messages, URLs, payloads or stack traces into status.
 public static string Describe(string stage,Exception error){
 var kind=error switch{HttpRequestException=>"HTTP",AuthenticationException=>"TLS",CryptographicException=>"Криптография",ArgumentException=>"Проверка данных",System.Text.Json.JsonException=>"JSON",OperationCanceledException=>"Тайм-аут",IOException=>"Ввод-вывод",_=>"Обработка данных"};
 var detail="";if(error is HttpRequestException http){detail="; HttpRequestError="+http.HttpRequestError;if(http.StatusCode is {} status)detail+="; HTTP="+(int)status;}
 for(var inner=error;inner!=null;inner=inner.InnerException){if(inner is SocketException socket){detail+="; Socket="+socket.SocketErrorCode;break;}if(inner is AuthenticationException){detail+="; TLS handshake";break;}}
 return "Ошибка: "+kind+"; этап: "+stage+"; код="+error.GetType().Name+"; HRESULT=0x"+error.HResult.ToString("X8")+detail+"; повтор через 30 секунд";
 }
}
