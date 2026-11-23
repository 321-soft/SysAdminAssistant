using System.Text;
using System.Text.RegularExpressions;
namespace SysAdminAssistant.Infrastructure;
public sealed class MailAuthDecision
{
    public bool Allowed {get;set;}
    public string Reason {get;set;}="";
    public string AuthservId {get;set;}="";
    public string FromDomain {get;set;}="";
    public string Spf {get;set;}="нет результата";
    public string Dkim {get;set;}="нет результата";
    public string Describe()=>$"{(Allowed?"Принято":"Отклонено")}: {Reason}\nПринимающий узел: {AuthservId}\nДомен From: {FromDomain}\nSPF: {Spf}\nDKIM: {Dkim}";
}
public static class MailAuthentication
{
    private static readonly TimeSpan Timeout=TimeSpan.FromMilliseconds(100);
    private static bool IsId(string id)=>id.Length<=253&&Regex.IsMatch(id,@"\A[A-Za-z0-9][A-Za-z0-9.-]*\z",RegexOptions.CultureInvariant,Timeout);
    public static string[] TrustedIds(MailSettings settings)=>(settings.TrustedAuthservIds??[]).Concat(string.IsNullOrWhiteSpace(settings.TrustedAuthservId)?[]:new[]{settings.TrustedAuthservId}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static void Validate(MailSettings settings)
    {
        if(!settings.RequireSpfDkim)return;
        if(!Enum.IsDefined(settings.AuthReceiver)||!Enum.IsDefined(settings.AuthPolicy))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестный режим проверки почты"));
        if(settings.TrustedAuthservIds==null||settings.TrustedAuthservId==null||settings.TrustedAuthservIds.Count>32)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Список доверенных узлов не задан"));
        var ids=TrustedIds(settings);
        if(ids.Length>32||ids.Any(id=>!IsId(id)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите до 32 точных идентификаторов принимающего сервера, по одному на строку, без масок"));
        if(settings.AuthReceiver==MailAuthReceiver.Custom&&ids.Length==0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выберите профиль Яндекса или укажите доверенные узлы принимающего сервера"));
        if(!settings.AuthHeadersSanitized)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Нужно подтвердить, что принимающая система удаляет поддельные Authentication-Results со своими идентификаторами"));
    }
    private static bool Trusted(MailSettings settings,string id)=>TrustedIds(settings).Contains(id,StringComparer.OrdinalIgnoreCase)||settings.AuthReceiver==MailAuthReceiver.YandexMxFront&&Regex.IsMatch(id,@"\Amail-nwsmtp-mxfront-production-main-[0-9]{1,6}\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,Timeout);
    private sealed record Method(string Name,string Result,string Domain,string Property,bool Aligned)
    {
        public string Display=>$"{Result}; {(Domain.Length==0?"домен не указан":Property+"="+Domain)}; {(Aligned?"совпадает с From":"нет подтверждённого совпадения с From")}";
    }
    public static bool Allows(MailSettings settings,IEnumerable<string> headers,string? sender)=>Evaluate(settings,headers,sender).Allowed;
    public static MailAuthDecision Evaluate(MailSettings settings,IEnumerable<string> headers,string? sender)
    {
        var decision=new MailAuthDecision();
        MailAuthDecision Reject(string why){decision.Reason=why;return decision;}
        if(!settings.RequireSpfDkim){decision.Allowed=true;decision.Reason="Проверка подлинности отключена настройкой ящика";return decision;}
        try{
            Validate(settings);
            if(sender==null)return Reject("Нет единственного корректного адреса From");
            decision.FromDomain=MailAdmission.NormalizeAddress(sender).Split('@').Last();
            var values=headers.Take(33).ToArray();if(values.Length>32||values.Any(x=>x==null)||values.Sum(x=>x.Length)>32000)return Reject("Превышен предел заголовков проверки подлинности");
            var trusted=new List<(string Id,string[] Clauses)>();
            foreach(var raw in values){
                var value=StripComments(raw);if(value==null)return Reject("Некорректная структура Authentication-Results");
                var clauses=value.Split(';');var authority=Regex.Match(clauses[0].Trim(),@"\A([A-Za-z0-9][A-Za-z0-9.-]{0,252})(?:[ \t]+1)?\z",RegexOptions.CultureInvariant,Timeout);
                if(authority.Success&&Trusted(settings,authority.Groups[1].Value))trusted.Add((authority.Groups[1].Value,clauses));
            }
            if(trusted.Count==0)return Reject("Нет результата от доверенного принимающего узла; проверьте профиль или список идентификаторов");
            if(trusted.Count!=1)return Reject("Несколько доверенных Authentication-Results: результаты разных заголовков не объединяются");
            decision.AuthservId=trusted[0].Id;var methods=new List<Method>();
            foreach(var clause in trusted[0].Clauses.Skip(1)){
                var method=Regex.Match(clause,@"^\s*(spf|dkim)\s*=\s*([A-Za-z]+)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,Timeout);if(!method.Success)continue;
                var name=method.Groups[1].Value.ToLowerInvariant();var result=method.Groups[2].Value.ToLowerInvariant();
                if(result is not("pass" or "fail" or "softfail" or "neutral" or "none" or "temperror" or "permerror" or "policy"))return Reject("Неизвестный результат SPF/DKIM");
                var fields=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var rest=clause[method.Length..];int position=0;
                while(position<rest.Length&&rest[position..].Trim().Length>0){
                    var field=Regex.Match(rest[position..],@"^\s+([A-Za-z][A-Za-z0-9_.-]*)\s*=\s*(?:""([^""\r\n\\]*)""|([^\s;""]+))(?=\s|$)",RegexOptions.CultureInvariant,Timeout);
                    if(!field.Success||!fields.TryAdd(field.Groups[1].Value,field.Groups[2].Success?field.Groups[2].Value:field.Groups[3].Value))return Reject("Неоднозначные или повреждённые свойства SPF/DKIM");position+=field.Length;
                }
                string domain="",property="";
                if(name=="spf"){
                    if(fields.TryGetValue("smtp.mailfrom",out var a)&&fields.TryGetValue("smtp.mail",out var b)&&!a.Equals(b,StringComparison.OrdinalIgnoreCase))return Reject("Противоречивые smtp.mailfrom и smtp.mail");
                    property=fields.ContainsKey("smtp.mailfrom")?"smtp.mailfrom":fields.ContainsKey("smtp.mail")?"smtp.mail":"";
                    if(property!="")domain=AddressDomain(fields[property]);
                }else if(fields.TryGetValue("header.d",out var d)){property="header.d";domain=MailAdmission.NormalizeDomain(d);}
                else if(fields.TryGetValue("header.i",out var i)){property="header.i (AUID)";domain=i.StartsWith('@')?MailAdmission.NormalizeDomain(i[1..]):MailAdmission.NormalizeAddress(i).Split('@').Last();}
                var aligned=domain.Equals(decision.FromDomain,StringComparison.OrdinalIgnoreCase)&&domain.Length>0;
                if(name=="dkim"&&property!="header.d"&&settings.AuthPolicy==MailAuthPolicy.BothAligned)aligned=false;
                methods.Add(new(name,result,domain,property,aligned));
            }
            var spf=methods.Where(x=>x.Name=="spf").ToArray();var dkim=methods.Where(x=>x.Name=="dkim").ToArray();
            decision.Spf=spf.Length==0?"нет результата":string.Join(" | ",spf.Select(x=>x.Display));decision.Dkim=dkim.Length==0?"нет результата":string.Join(" | ",dkim.Select(x=>x.Display));
            if(spf.Length>1)return Reject("Несколько результатов SPF в доверенном заголовке: требуется проверка");
            if(!spf.Any(x=>x.Result=="pass"))return Reject("Требуется SPF pass; "+(spf.Length==0?"результат отсутствует":"получено "+spf[0].Result));
            if(!dkim.Any(x=>x.Result=="pass"))return Reject("Требуется DKIM pass; "+(dkim.Length==0?"результат отсутствует":"получено "+string.Join(", ",dkim.Select(x=>x.Result))));
            bool spfAligned=spf.Any(x=>x.Result=="pass"&&x.Aligned),dkimAligned=dkim.Any(x=>x.Result=="pass"&&x.Aligned);
            if(settings.AuthPolicy==MailAuthPolicy.BothAligned?!(spfAligned&&dkimAligned):!(spfAligned||dkimAligned))return Reject(settings.AuthPolicy==MailAuthPolicy.BothAligned?"Строгий режим требует совпадения From с SPF и header.d DKIM":"SPF/DKIM прошли, но ни одна подтверждённая идентичность не совпадает с доменом From");
            decision.Allowed=true;decision.Reason="Оба результата pass; выбранное требование совпадения домена From выполнено";return decision;
        }catch(Exception e)when(e is ArgumentException or FormatException or RegexMatchTimeoutException){return Reject(e is RegexMatchTimeoutException?"Превышено время разбора заголовков":e.Message);}
    }
    private static string AddressDomain(string value)=>value.Contains('@')?MailAdmission.NormalizeAddress(value).Split('@').Last():MailAdmission.NormalizeDomain(value);
    public static string[] PastedHeaders(string text)
    {
        if(text.Length>32000)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Заголовки должны занимать не более 32 КБ"));
        var values=new List<string>();string? current=null;
        foreach(var line in text.Replace("\r\n","\n").Split('\n')){
            if(line.StartsWith("Authentication-Results:",StringComparison.OrdinalIgnoreCase)){if(current!=null)values.Add(current);current=line[23..].Trim();}
            else if(line.Length>0&&char.IsWhiteSpace(line[0])&&current!=null)current+=" "+line.Trim();
            else if(string.IsNullOrWhiteSpace(line))continue;
            else throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Вставьте только строки Authentication-Results; каждый заголовок с новой строки"));
        }
        if(current!=null)values.Add(current);if(values.Count==0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Не найден заголовок Authentication-Results"));return values.ToArray();
    }
    private static string? StripComments(string input)
    {
        if (input.Length > 16000) return null;
        var text = new StringBuilder(); int depth = 0; bool quote = false, escape = false;
        foreach (var c in input)
        {
            if (escape) { if (depth == 0) text.Append(c); escape = false; continue; }
            if (c == '\\') { escape = true; if (depth == 0) text.Append(c); continue; }
            if (depth == 0 && c == '"') { quote = !quote; text.Append(c); continue; }
            if (!quote && c == '(') { if (++depth > 16) return null; if (depth == 1) text.Append(' '); continue; }
            if (!quote && c == ')') { if (--depth < 0) return null; continue; }
            if (depth == 0) text.Append(c);
        }
        return depth == 0 && !quote && !escape ? text.ToString() : null;
    }
}
