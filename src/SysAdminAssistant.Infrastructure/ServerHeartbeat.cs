using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SysAdminAssistant.Infrastructure;
public sealed class ServerHeartbeatSettings
{
 public bool Enabled {get;set;}
 public string EndpointUrl {get;set;}="";
 public string ServerId {get;set;}="assistant-server";
 public string ProtectedSecret {get;set;}="";
 public static Uri ValidateUrl(string value){
  if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.HostNameType!=UriHostNameType.Dns||uri.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase)||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||uri.Port!=443||value.Length>2048)
   throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите HTTPS-адрес PHP-приёмника на своём сайте, без параметров и логина"));
  return uri;
 }
 public static void ValidateIdentity(string id){if(!Regex.IsMatch(id,@"\A[a-zA-Z0-9_-]{1,64}\z"))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("ID сервера: 1–64 латинских букв, цифр, _ или -"));}
 public static void ValidateSecret(string secret){if(!Regex.IsMatch(secret,@"\A[0-9a-fA-F]{64}\z"))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Общий ключ должен содержать 64 шестнадцатеричных символа"));}
}
public sealed record ServerHeartbeatStatus(bool Enabled,DateTime? LastAttemptUtc,DateTime? LastSuccessUtc,string Message);
public sealed record HeartbeatPacket(int Version,string ServerId,long Timestamp,string Nonce,string Signature);
public static class ServerHeartbeat
{
 private static readonly HttpClient Http=new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(10)};
 private static string Mac(string secret,string value)=>Convert.ToHexString(HMACSHA256.HashData(Convert.FromHexString(secret),Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
 public static HeartbeatPacket Create(string id,string secret,long timestamp,string nonce){ServerHeartbeatSettings.ValidateIdentity(id);ServerHeartbeatSettings.ValidateSecret(secret);if(!Regex.IsMatch(nonce,@"\A[0-9a-f]{32}\z"))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный nonce"));return new(1,id,timestamp,nonce,Mac(secret,$"saa-heartbeat-v1\n{id}\n{timestamp}\n{nonce}"));}
 public static string Ack(string secret,HeartbeatPacket p)=>Mac(secret,$"saa-ack-v1\n{p.ServerId}\n{p.Timestamp}\n{p.Nonce}");
 public static async Task SendAsync(string url,string id,string secret,CancellationToken token,HttpClient? client=null){
  var uri=ServerHeartbeatSettings.ValidateUrl(url);var p=Create(id,secret,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
  using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(10));
  try{
   using var request=new HttpRequestMessage(HttpMethod.Post,uri){Content=JsonContent.Create(p,options:new JsonSerializerOptions(JsonSerializerDefaults.Web))};
   using var response=await (client??Http).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,budget.Token);
   if(response.StatusCode!=HttpStatusCode.OK)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("PHP-наблюдатель отклонил сигнал жизни"));
   using var stream=await response.Content.ReadAsStreamAsync(budget.Token);var buffer=new byte[1025];var used=0;int read;
   while(used<buffer.Length&&(read=await stream.ReadAsync(buffer.AsMemory(used),budget.Token))>0)used+=read;
   if(used>1024)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой ответ наблюдателя"));
   using var json=JsonDocument.Parse(buffer.AsMemory(0,used));var root=json.RootElement;
   if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("ok",out var ok)||ok.ValueKind!=JsonValueKind.True||!root.TryGetProperty("nonce",out var nonce)||nonce.GetString()!=p.Nonce||!root.TryGetProperty("signature",out var signature)||signature.ValueKind!=JsonValueKind.String||!Regex.IsMatch(signature.GetString()??"",@"\A[0-9a-f]{64}\z")||!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature.GetString()!),Convert.FromHexString(Ack(secret,p))))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Подпись подтверждения PHP-наблюдателя не прошла проверку"));
  }catch(HttpRequestException){throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нет связи с PHP-наблюдателем"));}
   catch(JsonException){throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Некорректный ответ PHP-наблюдателя"));}
   catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("PHP-наблюдатель не ответил вовремя"));}
 }
 public static async Task<bool> ProbeAndSendAsync(Func<CancellationToken,Task<bool>> probe,Func<CancellationToken,Task> send,CancellationToken token){if(!await probe(token))return false;await send(token);return true;}
}
