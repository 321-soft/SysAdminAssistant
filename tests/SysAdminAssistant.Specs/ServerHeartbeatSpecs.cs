using System.Net;
using System.Text.Json;
using SysAdminAssistant.Infrastructure;
internal static class ServerHeartbeatSpecs
{
 const string Secret="000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
 sealed class Handler:HttpMessageHandler{
  public bool WrongSignature,WrongNonce;public HttpStatusCode Status=HttpStatusCode.OK;public int Calls;public bool SecretAbsent;public HttpMethod? Method;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;Method=request.Method;var raw=await request.Content!.ReadAsStringAsync(token);SecretAbsent=!raw.Contains(Secret);var p=JsonSerializer.Deserialize<HeartbeatPacket>(raw,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;var body=JsonSerializer.Serialize(new{ok=true,nonce=WrongNonce?"bad":p.Nonce,signature=WrongSignature?new string('0',64):ServerHeartbeat.Ack(Secret,p)});return new(Status){Content=new StringContent(body)};}
 }
 public static async Task Run(Action<string,bool> check){
  const string url="https://monitor.example.org/heartbeat.php";
  check("Own PHP heartbeat accepts site HTTPS endpoint",ServerHeartbeatSettings.ValidateUrl(url).Host=="monitor.example.org");
  foreach(var bad in new[]{"http://monitor.example.org/heartbeat.php","https://user@monitor.example.org/h","https://localhost/h","https://127.0.0.1/h",url+"?key=1",url+"#x","https://monitor.example.org:444/h"}){bool rejected=false;try{ServerHeartbeatSettings.ValidateUrl(bad);}catch(ArgumentException){rejected=true;}check("PHP heartbeat unsafe URL rejected "+bad,rejected);}
  var fixture=ServerHeartbeat.Create("assistant-server",Secret,1700000000,"00000000000000000000000000000000");check("C# HMAC matches independent shared PHP protocol vector",fixture.Signature=="08da38b51dd171c449f319e119f33a3283f12dac5067717cab72280ee3ab9a05"&&ServerHeartbeat.Ack(Secret,fixture)=="2e826a2b799c5c7a559f7510d9d21ebdcc2206386e375d343b260be6dfb79349");
  foreach(var id in new[]{"","a\nb","../../server",new string('a',65)}){bool rejected=false;try{ServerHeartbeatSettings.ValidateIdentity(id);}catch(ArgumentException){rejected=true;}check("Heartbeat server identity rejects invalid value",rejected);}
  using var handler=new Handler();using var http=new HttpClient(handler);await ServerHeartbeat.SendAsync(url,"assistant-server",Secret,default,http);check("Heartbeat sends signed POST without shared secret",handler.Calls==1&&handler.SecretAbsent&&handler.Method==HttpMethod.Post);
  handler.WrongSignature=true;bool wrong=false;try{await ServerHeartbeat.SendAsync(url,"assistant-server",Secret,default,http);}catch(InvalidOperationException){wrong=true;}check("Unsigned or forged heartbeat ACK rejected",wrong);
  handler.WrongSignature=false;handler.WrongNonce=true;wrong=false;try{await ServerHeartbeat.SendAsync(url,"assistant-server",Secret,default,http);}catch(InvalidOperationException){wrong=true;}check("Heartbeat ACK from another request rejected",wrong);
  handler.WrongNonce=false;handler.Status=HttpStatusCode.Redirect;wrong=false;try{await ServerHeartbeat.SendAsync(url,"assistant-server",Secret,default,http);}catch(InvalidOperationException){wrong=true;}check("Heartbeat redirect cannot report success",wrong);
  int sends=0;check("Failed database probe never sends false heartbeat",!await ServerHeartbeat.ProbeAndSendAsync(_=>Task.FromResult(false),_=>{sends++;return Task.CompletedTask;},default)&&sends==0);
  check("Healthy database probe allows one signed heartbeat",await ServerHeartbeat.ProbeAndSendAsync(_=>Task.FromResult(true),_=>{sends++;return Task.CompletedTask;},default)&&sends==1);
  var original=new AppSettings{ServerHeartbeat=new(){Enabled=true,EndpointUrl=url,ProtectedSecret="encrypted-secret"}};var visible=RemoteData.PublicSettings(original);check("Heartbeat key redacted from client settings",visible.ServerHeartbeat.ProtectedSecret=="REMOTE"&&original.ServerHeartbeat.ProtectedSecret=="encrypted-secret");
 }
}
