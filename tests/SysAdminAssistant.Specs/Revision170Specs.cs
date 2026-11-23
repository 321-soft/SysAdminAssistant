using System.Text.Json;
using SysAdminAssistant.Infrastructure;
internal static class Revision170Specs
{
 public static void Run(Action<string,bool> check){
  const string node="mail-nwsmtp-mxfront-production-main-60";
  var y=new MailSettings{AuthReceiver=MailAuthReceiver.YandexMxFront,AuthPolicy=MailAuthPolicy.BothPassOneIdentity,AuthHeadersSanitized=true};
  string Header(string id=node,string spf="pass",string dkim="pass",string mail="bounce@mailer.example",string identity="@vendor.example")=>$"{id}; spf={spf} ({id}: domain designates 198.51.100.10 as permitted sender) smtp.mail={mail}; dkim={dkim} header.i={identity}";
  foreach(var n in new[]{"60","78","1","999999"})check("Yandex numeric receiving node accepted "+n,MailAuthentication.Allows(y,[Header(id:"mail-nwsmtp-mxfront-production-main-"+n)],"report@vendor.example"));
  foreach(var bad in new[]{"evil-"+node,node+".evil.example",node+"x","mail-nwsmtp-mxfront-production-main-","mail-nwsmtp-mxfront-production-main-1234567","mx.yandex.evil","yandex",node+"/evil"})check("Lookalike receiver rejected "+bad,!MailAuthentication.Allows(y,[Header(id:bad)],"report@vendor.example"));
  check("Yandex profile does not waive sanitation confirmation",!MailAuthentication.Allows(new(){AuthReceiver=MailAuthReceiver.YandexMxFront,AuthPolicy=MailAuthPolicy.BothPassOneIdentity},[Header()],"report@vendor.example"));
  var custom=new MailSettings{TrustedAuthservIds=["mx.one.example","mx.two.example"],AuthHeadersSanitized=true,AuthPolicy=MailAuthPolicy.BothPassOneIdentity};
  check("Exact multiple receiver list accepts second node",MailAuthentication.Allows(custom,[Header(id:"mx.two.example")],"report@vendor.example"));
  custom.TrustedAuthservId="mx.legacy.example";check("Legacy exact receiver remains supported alongside list",MailAuthentication.Allows(custom,[Header(id:"mx.legacy.example")],"report@vendor.example"));
  check("Exact custom list does not imply provider family",!MailAuthentication.Allows(custom,[Header()],"report@vendor.example"));
  var duplicate=MailAuthentication.Evaluate(y,[Header(),Header(id:"mail-nwsmtp-mxfront-production-main-78")],"report@vendor.example");
  check("Different trusted Yandex headers remain ambiguous",!duplicate.Allowed&&duplicate.Reason.Contains("Несколько"));
  check("Untrusted results cannot supplement trusted header",!MailAuthentication.Allows(y,[node+"; spf=pass smtp.mail=report@vendor.example","evil.example; dkim=pass header.d=vendor.example"],"report@vendor.example"));
  check("Additional untrusted header does not override valid trusted result",MailAuthentication.Allows(y,[Header(),"evil.example; spf=fail; dkim=fail"],"report@vendor.example"));
  var failed=MailAuthentication.Evaluate(y,[Header(mail:"report@corp.example",dkim:"fail",identity:"@other.example")],"report@corp.example");
  check("SPF aligned pass cannot bypass required DKIM pass",!failed.Allowed&&failed.Reason.Contains("DKIM pass")&&failed.Dkim.Contains("fail"));
  check("DKIM aligned pass cannot bypass SPF fail",!MailAuthentication.Allows(y,[Header(spf:"fail")],"report@vendor.example"));
  check("Two pass results without From identity are rejected",!MailAuthentication.Allows(y,[Header()],"report@other.example"));
  check("Preferred signing domain cannot be overridden by aligned AUID",!MailAuthentication.Allows(y,[Header()+" header.d=other.example"],"report@vendor.example"));
  check("Authenticated header i with empty local part is accepted in identity mode",MailAuthentication.Allows(y,[Header(identity:"@vendor.example")],"report@vendor.example"));
  check("Bounce local part containing equals is parsed",MailAuthentication.Allows(y,[Header(mail:"bounce-123-a.user=corp.example@mailer.example")],"report@vendor.example"));
  check("Bare domain is not a valid DKIM AUID",!MailAuthentication.Allows(y,[Header(identity:"vendor.example")],"report@vendor.example"));
  check("Authenticated header i full address is accepted in identity mode",MailAuthentication.Allows(y,[Header(identity:"agent@vendor.example")],"report@vendor.example"));
  check("Strict legacy mode requires signing header d",!MailAuthentication.Allows(new(){TrustedAuthservId=node,AuthHeadersSanitized=true},[Header(mail:"report@vendor.example")],"report@vendor.example"));
  check("Standard smtp mailfrom and signing domain remain accepted",MailAuthentication.Allows(y,[node+"; spf=pass smtp.mailfrom=report@vendor.example; dkim=pass header.d=vendor.example"],"report@vendor.example"));
  check("Contradictory SPF identity aliases rejected",!MailAuthentication.Allows(y,[Header()+"; spf=pass smtp.mailfrom=report@vendor.example smtp.mail=report@other.example"],"report@vendor.example"));
  check("Two SPF methods cannot provide an arbitrary winning identity",!MailAuthentication.Allows(y,[Header()+"; spf=pass smtp.mail=report@vendor.example"],"report@vendor.example"));
  check("Multiple DKIM signatures allow an aligned successful result",MailAuthentication.Allows(y,[Header(dkim:"fail",identity:"@other.example")+"; dkim=pass header.d=vendor.example"],"report@vendor.example"));
  check("Failed aligned signature cannot align different successful signature",!MailAuthentication.Allows(y,[Header(identity:"@other.example")+"; dkim=fail header.d=vendor.example"],"report@vendor.example"));
  check("Comment cannot inject an aligned DKIM result",!MailAuthentication.Allows(y,[node+"; spf=pass smtp.mail=report@other.example (dkim=pass header.i=@vendor.example)"],"report@vendor.example"));
  check("Quoted reason cannot supply authenticated identity",!MailAuthentication.Allows(y,[node+"; spf=pass smtp.mail=report@other.example; dkim=pass reason=\"header.i=@vendor.example\""],"report@vendor.example"));
  check("Repeated authenticated property is rejected",!MailAuthentication.Allows(y,[Header()+" header.i=@vendor.example"],"report@vendor.example"));
  check("Malformed trusted property rejects whole result",!MailAuthentication.Allows(y,[Header()+" broken-property"],"report@vendor.example"));
  check("Unknown result cannot be parsed as pass",!MailAuthentication.Allows(y,[Header(dkim:"passx")],"report@vendor.example"));
  check("Authserv explicit version one supported",MailAuthentication.Allows(y,[Header(id:node+" 1")],"report@vendor.example"));
  check("Unknown authserv version rejected",!MailAuthentication.Allows(y,[Header(id:node+" 2")],"report@vendor.example"));
  check("Header count is bounded",!MailAuthentication.Allows(y,Enumerable.Repeat(Header(),33),"report@vendor.example"));
  check("Header size is bounded",!MailAuthentication.Allows(y,[new string('x',33000)],"report@vendor.example"));
  check("Missing From cannot pass authentication",!MailAuthentication.Allows(y,[Header()],null));
  check("Duplicate or invalid From cannot pass authentication",!MailAuthentication.Allows(y,[Header()],"a@vendor.example,b@vendor.example"));
  var legacy=JsonSerializer.Deserialize<MailSettings>("{\"TrustedAuthservId\":\"mx.legacy.example\",\"AuthHeadersSanitized\":true}")!;
  check("Old settings retain strict default without implicit provider trust",legacy.AuthReceiver==MailAuthReceiver.Custom&&legacy.AuthPolicy==MailAuthPolicy.BothAligned&&legacy.TrustedAuthservIds.Count==0);
  check("New receiving profile and list survive serialization",JsonSerializer.Deserialize<MailSettings>(JsonSerializer.Serialize(y))!.AuthReceiver==MailAuthReceiver.YandexMxFront);
  var pasted=MailAuthentication.PastedHeaders("Authentication-Results: "+node+";\r\n spf=pass smtp.mail=bounce@mailer.example;\r\n dkim=pass header.i=@vendor.example");
  check("Pasted folded Authentication Results parsed without body",MailAuthentication.Allows(y,pasted,"report@vendor.example"));
  check("Pasted duplicate headers stay distinct",MailAuthentication.PastedHeaders("Authentication-Results: "+Header()+"\nAuthentication-Results: "+Header()).Length==2);
  bool invalidPaste=false;try{MailAuthentication.PastedHeaders("From: report@vendor.example");}catch(ArgumentException){invalidPaste=true;}check("Pasted checker requires explicit Authentication Results",invalidPaste);
  var described=MailAuthentication.Evaluate(y,[Header()],"report@vendor.example").Describe();check("Decision explains receiver methods and From alignment",described.Contains(node)&&described.Contains("SPF: pass")&&described.Contains("DKIM: pass")&&described.Contains("совпадает с From"));
  check("Local policy does not advertise a calculated DMARC verdict",!described.Contains("DMARC pass"));
 }
}
