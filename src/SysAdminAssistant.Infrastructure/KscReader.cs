using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed class KscConnectionException(string message):IOException(message);
public static class KscReader
{
 // OpenAPI names are case-sensitive, including Name and Asc inside the sort container.
 private static readonly JsonSerializerOptions RequestJson = new();
 private static KscConnectionException ApiError(string name,JsonElement error)
 {
    static int? ReadCode(JsonElement error,string field)=>
        error.ValueKind==JsonValueKind.Object&&error.TryGetProperty(field,out var value)&&
        value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var number)?number:null;
    var code=ReadCode(error,"code");
    var subcode=ReadCode(error,"subcode");
    var advice=code switch{
        1184 when name=="Session.StartSession"&&subcode==5=>"KSC не принял учётные данные.",
        1184 or 1193=>"Доступ запрещён. Проверьте права учётной записи KSC для этой операции.",
        1125 or 1192=>"KSC не принял параметры или формат запроса.",
        1194=>"Метод не поддерживается этой версией KSC.",
        1195=>"Ресурс KSC временно недоступен.",
        _=>"Проверьте параметры, права учётной записи и версию KSC."
    };
    // Never copy the upstream message, arguments or paths into client-visible errors.
    var suffix=code.HasValue?$" (код {code}"+(subcode.HasValue?$", подкод {subcode}":"")+")":"";
    return new($"KSC отклонил {name}{suffix}. {advice}");
 }
 public static async Task<int> ProbeAsync(KscConnection target,CancellationToken token)=>(await Read(target,null,token)).Count;
 public static Task<KscBatch> Read(KscConnection target,long? cursor,CancellationToken token)=>ReadCore(target,cursor,token,null,null,0,null);
 internal static Task<KscBatch> ReadForTest(KscConnection target,long? cursor,CancellationToken token,HttpMessageHandler handler,IReadOnlyList<string>? hosts=null)=>ReadCore(target,cursor,token,handler,hosts,0,null);
 public static Task<KscBatch> ReadWithNames(KscConnection target,long? cursor,IReadOnlyList<string> hosts,int offset,Dictionary<string,string>? known,CancellationToken token)
    =>ReadCore(target,cursor,token,null,hosts,offset,known);
 public static Task<KscFindingList> ReadFindings(KscConnection target,string host,string code,long first,long last,long through,CancellationToken token)
    =>ReadFindingsCore(target,host,code,first,last,through,token,null);
 internal static async Task<KscFindingList> ReadFindingsCore(KscConnection target,string host,string code,long first,long last,long through,CancellationToken token,HttpMessageHandler? handler)
 {
    KscFindingList? result=null;
    await ReadCore(target,null,token,handler,null,0,null,async(count,range)=>{
        result=await FindEvents(count,range,host,code,first,last,through);
    });
    return result!;
 }
 private static readonly string[] BaseFields=["event_type","hostname","event_db_id","GNRL_EA_SEVERITY"];
 private static readonly string[] DetailFields=[..BaseFields,"event_type_display_name","rise_time","product_name","GNRL_EA_DESCRIPTION_DISP_NAME","GNRL_EA_DESCRIPTION","GNRL_EA_PARAM_1","GNRL_EA_PARAM_2","GNRL_EA_PARAM_5","GNRL_EA_PARAM_6"];
 private static readonly HashSet<string> ProcessingEvents=new(StringComparer.Ordinal){
    "GNRL_EV_OBJECT_CURED","GNRL_EV_OBJECT_DELETED","GNRL_EV_OBJECT_QUARANTINED","GNRL_EV_OBJECT_NOTCURED","GNRL_EV_OBJECT_BLOCKED","GNRL_EV_OBJECT_REPORTED","GNRL_EV_OBJECT_PASSED"
 };
 private static JsonElement V(JsonElement e)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty("value",out var v)?v:e;
 private static string Field(JsonElement row,string key,int max=1000){
    if(!row.TryGetProperty(key,out var value))return "";
    var v=V(value);
    var s=v.ValueKind is JsonValueKind.String or JsonValueKind.Number?v.ToString().Trim():"";
    return s.Length>max?s[..max]:s;
 }
 private static string Description(JsonElement row){var text=Field(row,"GNRL_EA_DESCRIPTION_DISP_NAME");return text.Length>0?text:Field(row,"GNRL_EA_DESCRIPTION");}
 private static bool HasThreatName(string code) => code is "GNRL_EV_VIRUS_FOUND" or "GNRL_EV_VIRUS_FOUND_BY_KSN" or "GNRL_EV_SUSPICIOUS_OBJECT_FOUND";
 private static KscEvent DescribeEvent(KscEvent e,JsonElement row)
 {
    var title=Field(row,"event_type_display_name",160);
    var description=Description(row);
    var threat=HasThreatName(e.Kind)?Field(row,"GNRL_EA_PARAM_5",512):"";
    var item=e with{EventTitle=title,Description=description,Product=Field(row,"product_name",100),Published=Field(row,"rise_time",100),
        Threat=threat,ObjectName=HasThreatName(e.Kind)||ProcessingEvents.Contains(e.Kind)?Field(row,"GNRL_EA_PARAM_2",512):""};
    return KscNetworkAttack.Enrich(item,title,description,threat);
 }
 private static long EventId(JsonElement row)=>long.TryParse(Field(row,"event_db_id"),out var id)&&id>0?id:throw new InvalidDataException("KSC: неверный номер события.");
 private static async Task<KscFindingList> FindEvents(int count,Func<int,int,Task<JsonElement[]>> range,string host,string code,long first,long last,long through)
 {
    if(count<0)throw new InvalidDataException("KSC: неверное количество событий.");
    int lo=0,hi=count;
    while(lo<hi){
        int mid=lo+(hi-lo)/2;
        var one=await range(mid,mid);
        if(one.Length!=1)return new([],true);
        if(EventId(one[0])<first)lo=mid+1;else hi=mid;
    }
    int remaining=Math.Min(count-lo,1024);
    bool limited=count-lo>remaining;
    var findings=new List<(KscFinding Event,Guid? Uid)>();
    var blocks=new List<(long Id,string Ip)>();
    var actions=new List<(KscFindingAction Event,Guid Uid,string ObjectName)>();
    long previous=first-1;
    while(remaining>0){
        int n=Math.Min(remaining,64);
        var rows=await range(lo,lo+n-1);
        if(rows.Length==0){limited=true;break;}
        if(rows.Length>n)throw new InvalidDataException("KSC: неверный размер страницы событий.");
        foreach(var row in rows){
            long id=EventId(row);
            if(id<=previous)throw new InvalidDataException("KSC: нарушен порядок событий.");
            previous=id;
            if(id>through){remaining=0;limited=false;break;}
            if(Field(row,"hostname",256)!=host)continue;
            var kind=Field(row,"event_type",256);
            var title=Field(row,"event_type_display_name");
            var published=Field(row,"rise_time",100);
            var objectName=Field(row,"GNRL_EA_PARAM_2");
            // Some versions use parameter 6 for the user name. Only correlate nonempty GUIDs.
            Guid? uid=Guid.TryParse(Field(row,"GNRL_EA_PARAM_6"),out var key)&&key!=Guid.Empty?key:null;
            if(ProcessingEvents.Contains(kind)&&uid is Guid actionUid)actions.Add((new(id,kind,title,published),actionUid,objectName));
            var description=Description(row);
            var attack=DescribeEvent(new(id,host,kind,0),row);
            if(attack.IpBlocked&&attack.AttackerIp is {} blockedIp)blocks.Add((id,blockedIp));
            if(kind!=code||id>last)continue;
            if(findings.Count==128){limited=true;continue;}
            bool detection=kind is "GNRL_EV_VIRUS_FOUND" or "GNRL_EV_VIRUS_FOUND_BY_KSN" or "GNRL_EV_SUSPICIOUS_OBJECT_FOUND";
            var hash=kind is "GNRL_EV_VIRUS_FOUND" or "GNRL_EV_VIRUS_FOUND_BY_KSN"?Field(row,"GNRL_EA_PARAM_1"):"";
            findings.Add((new KscFinding(id,kind,title,published,Field(row,"product_name"),detection?Field(row,"GNRL_EA_PARAM_5"):"",detection||ProcessingEvents.Contains(kind)?objectName:"",
                hash.Length==64&&hash.All(Uri.IsHexDigit)?hash:"",description,[]){RdpBruteForce=attack.RdpBruteForce,AttackerIp=attack.AttackerIp,IpBlocked=attack.IpBlocked},detection?uid:null));
        }
        lo+=rows.Length;remaining-=rows.Length;
    }
    return new(findings.Select(f=>f.Event with{IpBlocked=f.Event.IpBlocked||(f.Event.RdpBruteForce&&f.Event.AttackerIp!=null&&blocks.Any(b=>b.Id>f.Event.Id&&b.Ip==f.Event.AttackerIp)),Actions=actions.Where(a=>f.Uid.HasValue&&a.Uid==f.Uid&&a.Event.Id>f.Event.Id&&
        (a.ObjectName.Length==0||f.Event.ObjectName.Length==0||a.ObjectName==f.Event.ObjectName)).Select(a=>a.Event).Take(8).ToArray()}).ToArray(),limited);
 }
 internal static string? CertificateError(Uri expected,Uri? actual,byte[] pin,X509Certificate2? certificate,SslPolicyErrors errors,X509ChainStatusFlags chainStatus,DateTime utcNow)
 {
    if(actual==null||actual.Scheme!=Uri.UriSchemeHttps||actual.Port!=expected.Port||
        !string.Equals(actual.IdnHost,expected.IdnHost,StringComparison.OrdinalIgnoreCase))
        return "Адрес подключения не совпадает с адресом OpenAPI, для которого задан отпечаток.";
    if(certificate==null||(errors&SslPolicyErrors.RemoteCertificateNotAvailable)!=0)
        return "KSC не предоставил сертификат TLS.";
    if((errors&SslPolicyErrors.RemoteCertificateNameMismatch)!=0)
        return "Имя сервера OpenAPI не совпадает с именем в сертификате KSC.";
    if(utcNow<certificate.NotBefore.ToUniversalTime()||utcNow>certificate.NotAfter.ToUniversalTime())
        return "Сертификат KSC просрочен или ещё не вступил в силу.";
    if(!CryptographicOperations.FixedTimeEquals(pin,certificate.GetCertHash(HashAlgorithmName.SHA256)))
        return "Отпечаток SHA-256 сертификата KSC не совпадает. Проверьте сертификат на сервере KSC и подтвердите новый отпечаток в настройках.";
    const X509ChainStatusFlags permitted=X509ChainStatusFlags.UntrustedRoot|X509ChainStatusFlags.PartialChain;
    if(errors==SslPolicyErrors.None ||
        (errors==SslPolicyErrors.RemoteCertificateChainErrors&&chainStatus!=X509ChainStatusFlags.NoError&&(chainStatus&~permitted)==0))
        return null;
    return "Сертификат KSC не прошёл проверку TLS: "+chainStatus+".";
 }
 private static async Task<KscBatch> ReadCore(KscConnection target,long? cursor,CancellationToken token,HttpMessageHandler? handler,IReadOnlyList<string>? hosts,int offset,Dictionary<string,string>? known,Func<int,Func<int,int,Task<JsonElement[]>>,Task>? details=null)
 {
    target=target.Normalize();
    if(string.IsNullOrEmpty(target.Password))throw new ArgumentException("Введите пароль учётной записи KSC.");
    var uri=new Uri(target.Address);
    string stage="Session.StartSession";
    string? certificateError=null;
    if(handler==null){
        var transport=new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,UseProxy=false};
        if(target.CertificateSha256.Length>0){
            var pin=Convert.FromHexString(target.CertificateSha256);
            transport.ServerCertificateCustomValidationCallback=(request,certificate,chain,errors)=>{
                var flags=chain?.ChainStatus.Aggregate(X509ChainStatusFlags.NoError,(all,item)=>all|item.Status)??X509ChainStatusFlags.NoError;
                var error=CertificateError(uri,request.RequestUri,pin,certificate,errors,flags,DateTime.UtcNow);
                Volatile.Write(ref certificateError,error);
                return error==null;
            };
        }
        handler=transport;
    }
    using var http=new HttpClient(handler){BaseAddress=new Uri(uri,"api/v1.0/"),Timeout=TimeSpan.FromSeconds(12),MaxResponseContentBufferSize=1048576};
    string? session=null,iterator=null;static string B(string s)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    async Task<JsonElement> Call(string name,object args,CancellationToken? requestToken=null){
        var ct=requestToken??token;
        stage=name;
        using var req=new HttpRequestMessage(HttpMethod.Post,name){Content=JsonContent.Create(args,options:RequestJson)};
        if(session==null){var auth=$"KSCBasic user=\"{B(target.Login)}\", pass=\"{B(target.Password)}\", internal=\"{(target.Internal?1:0)}\"";if(!target.Internal&&target.Domain.Length>0)auth+=$", domain=\"{B(target.Domain)}\"";req.Headers.Add("Authorization",auth);}
        else req.Headers.Add("X-KSC-Session",session);
        Volatile.Write(ref certificateError,null);
        using var res=await http.SendAsync(req,ct);
        if(!res.IsSuccessStatusCode){
            var advice=(int)res.StatusCode switch {
                401=>session==null?"Проверьте учётные данные KSC.":"Сессия KSC отклонена; следующий опрос создаст новую сессию.",
                403=>"Нет доступа: проверьте права учётной записи на чтение событий KSC.",
                404=>"Метод API недоступен: проверьте адрес, порт и поддержку OpenAPI этой версией KSC.",
                429=>"KSC ограничил частоту запросов; повтор при следующем опросе.",
                >=500=>"Ошибка сервера KSC; проверьте его состояние и журнал.",
                _=>"Проверьте адрес и настройки OpenAPI KSC."
            };
            throw new KscConnectionException($"KSC {name}: HTTP {(int)res.StatusCode}. {advice}");
        }
        using var doc=JsonDocument.Parse(await res.Content.ReadAsByteArrayAsync(ct));if(doc.RootElement.TryGetProperty("PxgError",out var error))throw ApiError(name,error);return doc.RootElement.Clone();
    }
    try{
        session=(await Call("Session.StartSession",new{})).GetProperty("PxgRetVal").GetString();if(string.IsNullOrEmpty(session))throw new KscConnectionException("KSC session is missing.");
        async Task<string> Create(string[] fields)=>(await Call("EventProcessingFactory.CreateEventProcessing",new{
            vecFieldsToReturn=fields,vecFieldsToOrder=new[]{new{type="params",value=new{Name="event_db_id",Asc=true}}},lifetimeSec=30
        })).GetProperty("strIteratorId").GetString()??"";
        try{iterator=await Create(details!=null||hosts!=null?DetailFields:BaseFields);}
        catch(KscConnectionException)when(details==null&&hosts!=null){iterator=await Create(BaseFields);}
        if(string.IsNullOrEmpty(iterator))throw new KscConnectionException("KSC event iterator is missing.");
        var count=(await Call("EventProcessing.GetRecordCount",new{strIteratorId=iterator})).GetProperty("PxgRetVal").GetInt32();
        async Task<JsonElement[]> Fetch(int start,int end){
            var reply=await Call("EventProcessing.GetRecordRange",new{strIteratorId=iterator,nStart=start,nEnd=end});
            return V(reply.GetProperty("pParamsEvents")).GetProperty("KLEVP_EVENT_RANGE_ARRAY").EnumerateArray().Select(V).ToArray();
        }
        // Some OpenAPI versions return a half-open interval: a singleton (i,i)
        // is empty. Negotiate on this iterator, never by resetting the durable cursor.
        bool exclusiveEnd=false;
        if(count>0){
            var probe=await Fetch(0,0);
            if(probe.Length==0){
                var singleton=await Fetch(0,1);
                if(singleton.Length!=1)throw new InvalidDataException("KSC: не удалось определить границы диапазона событий. Позиция чтения сохранена.");
                exclusiveEnd=true;
            }else if(probe.Length!=1)throw new InvalidDataException("KSC: неожиданный ответ на запрос одной записи. Позиция чтения сохранена.");
        }
        async Task<JsonElement[]> Range(int start,int end){
            var rows=await Fetch(start,exclusiveEnd?checked(end+1):end);
            if(details==null&&rows.Length<end-start+1&&end>start){
                int mid=start+(end-start)/2;
                return [..await Range(start,mid),..await Range(mid+1,end)];
            }
            return rows;
        }
        if(details!=null){await details(count,Range);return new(0,false,count,[]);}
        var batch=await KscCursor.Read(count,cursor,async(start,end)=>(await Range(start,end)).Select(row=>{
              string F(string k)=>row.TryGetProperty(k,out var v)?V(v).ToString():"";
              if(!long.TryParse(F("event_db_id"),out var id)||!int.TryParse(F("GNRL_EA_SEVERITY"),out var level)||F("hostname").Length>256||F("event_type").Length is 0 or >256)throw new InvalidDataException("Unsupported KSC event schema");
              return DescribeEvent(new(id,F("hostname"),F("event_type"),level),row);
        }).ToArray());
        if(hosts==null)return batch;
        var missing=hosts.Concat(batch.Rows.Select(e=>e.Host)).Where(h=>h.Length is >0 and <=256&&known?.ContainsKey(h)!=true)
            .Distinct(StringComparer.Ordinal).ToArray();
        if(missing.Length==0)return batch;
        using var lookup=CancellationTokenSource.CreateLinkedTokenSource(token);
        lookup.CancelAfter(TimeSpan.FromSeconds(4));
        var names=new Dictionary<string,string>(StringComparer.Ordinal);
        for(int i=0;i<Math.Min(8,missing.Length)&&!lookup.IsCancellationRequested;i++){
            var host=missing[(offset%missing.Length+i)%missing.Length];
            try{
                var reply=await Call("HostGroup.GetHostInfo",new{strHostName=host,pFields2Return=new[]{"KLHST_WKS_DN","KLHST_WKS_DNSNAME","KLHST_WKS_WINHOSTNAME"}},lookup.Token);
                if(!reply.TryGetProperty("PxgRetVal",out var value)||V(value).ValueKind!=JsonValueKind.Object)continue;
                var attrs=V(value);
                foreach(var field in new[]{"KLHST_WKS_DN","KLHST_WKS_DNSNAME","KLHST_WKS_WINHOSTNAME"}){
                    if(!attrs.TryGetProperty(field,out var attr)||V(attr).ValueKind!=JsonValueKind.String)continue;
                    var name=V(attr).GetString()?.Trim();
                    if(name is {Length:>0 and <=256}&&name!=host&&!name.Any(char.IsControl)){names[host]=name;break;}
                }
            }catch(Exception e)when(!token.IsCancellationRequested&&(e is HttpRequestException or IOException or JsonException or OperationCanceledException)){}
        }
        token.ThrowIfCancellationRequested();
        return batch with{ComputerNames=names};
    }catch(HttpRequestException e){
        var reason=e.HttpRequestError switch{
            HttpRequestError.SecureConnectionError=>Volatile.Read(ref certificateError)??"Не удалось установить TLS. Проверьте имя в сертификате KSC и доверие к нему на сервере Assistant.",
            HttpRequestError.NameResolutionError=>"Имя сервера KSC не разрешается на сервере Assistant.",
            HttpRequestError.ConnectionError=>"Сервер Assistant не смог подключиться к KSC. Проверьте адрес, порт OpenAPI и брандмауэр.",
            _=>"Ответ KSC не получен или соединение прервано. Проверьте доступность OpenAPI."
        };
        throw new KscConnectionException($"KSC {stage}: {reason}");
    }catch(OperationCanceledException)when(!token.IsCancellationRequested){
        throw new KscConnectionException($"KSC {stage}: истекло время ожидания ответа.");
    }catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){
        throw new InvalidDataException($"KSC {stage}: неожиданный формат ответа OpenAPI. Проверьте версию KSC и права чтения журнала.",e);
    }finally{
        if(iterator!=null)try{await Call("EventProcessing.ReleaseIterator",new{strIteratorId=iterator});}catch(Exception){}
        if(session!=null)try{await Call("Session.EndSession",new{});}catch(Exception){}
    }
 }
}
