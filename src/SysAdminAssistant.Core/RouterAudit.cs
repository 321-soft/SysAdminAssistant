using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SysAdminAssistant.Core;
public sealed class RouterAuditConfig
{
 public DateTime CapturedUtc {get;set;}=DateTime.UtcNow;
 internal long Deadline;
 internal Dictionary<string,(AuditAddresses Set,bool Unknown)> AddressCache=[];
 public Dictionary<string,List<Dictionary<string,string>>> Tables {get;set;}=[];
 public Dictionary<string,string> Errors {get;set;}=[];
 public List<Dictionary<string,string>> Rows(string name)=>Tables.GetValueOrDefault(name)??[];
}
public enum FirewallVerdict { NotEvaluated,Blocked,Allowed,Restricted,Indeterminate }
public sealed record AuditSourceRange(string First,string Last);
public sealed class RouterAuditPath {
 public int Family {get;set;}public string Input {get;set;}="";public string Protocol {get;set;}="";public int ExternalPort {get;set;}public string OriginalDestination {get;set;}="";public int FinalPort {get;set;}public string FinalDestination {get;set;}="";public string Chain {get;set;}="";
 public FirewallVerdict Verdict {get;set;}public AuditSourceRange[] Confirmed {get;set;}=[];public AuditSourceRange[] Possible {get;set;}=[];public bool SourcesTruncated {get;set;}public string ConfirmedDigest {get;set;}="";public string PossibleDigest {get;set;}="";
 public string[] Stages {get;set;}=[];public string[] Conditions {get;set;}=[];public string[] UnknownReasons {get;set;}=[];
}
public sealed class RouterAuditFinding
{ 
 [System.Text.Json.Serialization.JsonIgnore] public List<RouterAuditFinding> Members {get;set;}=[];
 public List<RouterAuditPath> Paths {get;set;}=[];
 public string FirewallConclusion=>Paths.Count==0?"Не оценивалось":Paths.Any(p=>p.Verdict==FirewallVerdict.Allowed)?"Разрешено firewall":Paths.Any(p=>p.Verdict==FirewallVerdict.Indeterminate)?"Не удалось определить":Paths.Any(p=>p.Verdict==FirewallVerdict.Restricted)?"Доступ ограничен":"Закрыто для проверенного пути";
 internal string Proof="";
 public string Key {get;set;}="";public string Fingerprint {get;set;}="";public string Title {get;set;}="";public Severity Severity {get;set;}public string Status {get;set;}="";public string Evidence {get;set;}="";public string Recommendation {get;set;}="";public string Commands {get;set;}="";public bool Ignored {get;set;}public string IgnoredBy {get;set;}="";public DateTime? IgnoredUtc {get;set;}
 public string Level=>Ignored?"Игнорируется":Severity==Severity.Critical?"Критично":Severity==Severity.Warning?"Предупреждение":"Информация";
}
public sealed class RouterAuditResult
{
 public DateTime CheckedUtc {get;set;}public string[] WanInterfaces {get;set;}=[];public bool ManualWan {get;set;}public List<RouterAuditFinding> Findings {get;set;}=[];
 public string CoverageLabel=>Findings.Count==0?"Нет результатов":Findings.Any(f=>f.Status=="Нужна проверка"||f.Paths.Any(p=>p.Verdict==FirewallVerdict.Indeterminate))?"Анализ содержит неопределённые пути":"Проверенные пути вычислены";
 [System.Text.Json.Serialization.JsonIgnore] public IReadOnlyList<RouterAuditFinding> DisplayFindings=>RouterAuditPresentation.Group(Findings);
 public int Critical=>DisplayFindings.Count(f=>!f.Ignored&&f.Severity==Severity.Critical);public int Warnings=>DisplayFindings.Count(f=>!f.Ignored&&f.Severity==Severity.Warning);
}
// Exact finite unions of IP intervals; no sampling of just one outside address.
public sealed class AuditAddresses
{
 public sealed record Span(BigInteger Lo,BigInteger Hi);
 private readonly List<Span> spans;
 private string? digest;
 public int Bits {get;}
 public IReadOnlyList<Span> Spans {get;}
 public AuditAddresses(int bits,IEnumerable<Span>? ranges=null)
     : this(bits,Normalize(ranges??[]),true) {}

 private AuditAddresses(int bits,List<Span> normalized,bool _)
 {
  Bits=bits;
  spans=normalized;
  Spans=spans.AsReadOnly();
 }

 private static List<Span> Normalize(IEnumerable<Span> ranges)
 {
  var sorted=ranges.ToList();
  var ordered=true;
  for(var i=1;i<sorted.Count;i++)if(sorted[i-1].Lo>sorted[i].Lo){ordered=false;break;}
  if(!ordered)sorted.Sort(static (a,b)=>a.Lo.CompareTo(b.Lo));
  var count=0;
  for(var index=0;index<sorted.Count;index++)
  {
   var span=sorted[index];
   if(span.Hi<span.Lo)continue;
   if(count>0&&sorted[count-1].Hi+1>=span.Lo)
   {
    var previous=sorted[count-1];
    if(span.Hi>previous.Hi)sorted[count-1]=new(previous.Lo,span.Hi);
   }
   else sorted[count++]=span;
  }
  sorted.RemoveRange(count,sorted.Count-count);
  return sorted;
 }

 public static AuditAddresses All(int bits)=>new(bits,[new(0,(BigInteger.One<<bits)-1)],true);
 public bool Broad
 {
  get
  {
   var threshold=BigInteger.One<<(Bits-4);
   var total=BigInteger.Zero;
   foreach(var span in spans){total+=span.Hi-span.Lo+1;if(total>=threshold)return true;}
   return false;
  }
 }
 public bool Empty=>spans.Count==0;
 public bool Universal=>spans.Count==1&&spans[0].Lo==0&&spans[0].Hi==(BigInteger.One<<Bits)-1;
 public AuditAddresses Union(AuditAddresses b)
 {
  if(ReferenceEquals(this,b)||b.Empty||Universal)return this;
  if(Empty||b.Universal)return b;
  return new(Bits,spans.Concat(b.spans));
 }
 public AuditAddresses Intersect(AuditAddresses b){if(ReferenceEquals(this,b)||Empty||b.Universal)return this;if(b.Empty||Universal)return b;var rows=new List<Span>();int i=0,j=0;while(i<Spans.Count&&j<b.Spans.Count){var a=Spans[i];var x=b.Spans[j];var lo=BigInteger.Max(a.Lo,x.Lo);var hi=BigInteger.Min(a.Hi,x.Hi);if(lo<=hi)rows.Add(new(lo,hi));if(a.Hi<x.Hi)i++;else j++;}return new(Bits,rows,true);}
 public AuditAddresses Except(AuditAddresses b){if(Empty||b.Empty)return this;if(ReferenceEquals(this,b)||b.Universal)return new(Bits);var rows=new List<Span>();int j=0;foreach(var a in Spans){var cursor=a.Lo;while(j<b.Spans.Count&&b.Spans[j].Hi<cursor)j++;var k=j;while(k<b.Spans.Count&&b.Spans[k].Lo<=a.Hi){var x=b.Spans[k];if(x.Lo>cursor)rows.Add(new(cursor,BigInteger.Min(a.Hi,x.Lo-1)));cursor=BigInteger.Max(cursor,x.Hi+1);if(cursor>a.Hi)break;k++;}if(cursor<=a.Hi)rows.Add(new(cursor,a.Hi));}return new(Bits,rows,true);}
 public string Digest()=>digest??=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(";",spans.Select(s=>s.Lo+":"+s.Hi)))));
 public static bool Try(string text,int bits,out AuditAddresses set){set=new(bits);if(string.IsNullOrWhiteSpace(text))return false;foreach(var part in text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)){if(part.Contains('-')){var pair=part.Split('-');if(pair.Length!=2||!IPAddress.TryParse(pair[0],out var a)||!IPAddress.TryParse(pair[1],out var b)||a.AddressFamily!=b.AddressFamily)return false;var familyRange=a.AddressFamily==AddressFamily.InterNetwork?32:128;if(familyRange!=bits)continue;var loRange=new BigInteger(a.GetAddressBytes(),true,true);var hiRange=new BigInteger(b.GetAddressBytes(),true,true);if(loRange>hiRange)return false;set=set.Union(new(bits,[new(loRange,hiRange)]));continue;}var slash=part.Split('/');if(slash.Length>2||!IPAddress.TryParse(slash[0],out var ip))return false;var family=ip.AddressFamily==AddressFamily.InterNetwork?32:128;if(family!=bits)continue;var mask=bits;if(slash.Length==2&&(!int.TryParse(slash[1],out mask)||mask<0||mask>bits))return false;var n=new BigInteger(ip.GetAddressBytes(),true,true);var size=BigInteger.One<<(bits-mask);var lo=n/size*size;set=set.Union(new(bits,[new(lo,lo+size-1)]));}return true;}
 public static string Address(BigInteger n,int bits){var raw=n.ToByteArray(true,true);var data=new byte[bits/8];raw.CopyTo(data,data.Length-raw.Length);return new IPAddress(data).ToString();}
 public string Describe()=>Empty?"никто":Universal?"любые адреса":string.Join(", ",Spans.Take(30).Select(s=>Address(s.Lo,Bits)+(s.Lo==s.Hi?"":"–"+Address(s.Hi,Bits))))+(Spans.Count>30?" …":"");
}
public static class RouterAudit
{
 private sealed class AuditBudgetException:Exception {}
 private enum Match {No,Yes,Unknown}
 private static string V(Dictionary<string,string> r,string key)=>r.GetValueOrDefault(key,"");
 private static bool Yes(string s)=>s is "true" or "yes";
 private static bool Enabled(Dictionary<string,string> r)=>!Yes(V(r,"disabled"))&&!Yes(V(r,"invalid"));
 private static bool Id(string s)=>s.Length>1&&s[0]=='*'&&s[1..].All(Uri.IsHexDigit);
 private static string Q(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("$","\\$").Replace("\r","\\r").Replace("\n","\\n")+"\"";
 private static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
 private static string Semantic(Dictionary<string,string> r)=>string.Join(";",r.Where(p=>!new[]{"packets","bytes","comment","creation-time","timeout","last-link-up-time","last-link-down-time","dynamic"}.Contains(p.Key)).OrderBy(p=>p.Key).Select(p=>p.Key+"="+p.Value));
 private sealed record Probe(int Bits,string In,string Out,string Protocol,int Port,string Destination,bool Forward=false,bool Dnat=false,string State="new",bool NatUnknown=false);
 private sealed class Reach(int bits){public AuditAddresses Definite=new(bits),Possible=new(bits);public HashSet<string> Trace=[];public bool Limited;public bool Notrack;public AuditAddresses UntrackedDefinite=new(bits),UntrackedPossible=new(bits);}
 private sealed class Evaluator(RouterAuditConfig config,Probe probe,string table,string chain,string? natTarget=null,bool routingTarget=false,bool manglePass=false)
 {
  private int work;private Reach result=new(probe.Bits);private readonly Dictionary<string,Dictionary<string,string>[]> chains=config.Rows(table).Select((r,i)=>r.ContainsKey(".id")?r:new Dictionary<string,string>(r){[".id"]="analysis-"+i}).Where(Enabled).GroupBy(r=>V(r,"chain")).ToDictionary(g=>g.Key,g=>g.ToArray());
  public Match Interface(string list,string name,HashSet<string>? seen=null){if(list=="all")return Match.Yes;if(list=="none")return Match.No;if(name=="")return Match.Unknown;seen??=[];if(!seen.Add(list))return Match.Unknown;var definition=config.Rows("lists").FirstOrDefault(r=>V(r,"name")==list);if(definition==null)return Match.Unknown;var unknown=false;var included=false;foreach(var sub in V(definition,"include").Split(',',StringSplitOptions.RemoveEmptyEntries)){var m=Interface(sub,name,new(seen));included|=m==Match.Yes;unknown|=m==Match.Unknown;}foreach(var sub in V(definition,"exclude").Split(',',StringSplitOptions.RemoveEmptyEntries)){var m=Interface(sub,name,new(seen));if(m==Match.Yes)included=false;unknown|=m==Match.Unknown;}if(config.Rows("members").Any(r=>Enabled(r)&&V(r,"list")==list&&V(r,"interface")==name))return Match.Yes;return unknown?Match.Unknown:included?Match.Yes:Match.No;}
  private (AuditAddresses Set,bool Unknown) AddressList(string name){var table=probe.Bits==32?"address-lists4":"address-lists6";var key=table+":"+name;if(config.AddressCache.TryGetValue(key,out var cached))return cached;var spans=new List<AuditAddresses.Span>();bool unknown=config.Errors.ContainsKey(table)||config.Errors.ContainsKey(key);foreach(var r in config.Rows(table).Where(r=>Enabled(r)&&V(r,"list")==name)){if(Yes(V(r,"dynamic"))||(V(r,"timeout") is not("" or "0s" or "none-static" or "none-dynamic"))||!AuditAddresses.Try(V(r,"address"),probe.Bits,out var value)){unknown=true;continue;}spans.AddRange(value.Spans);}return config.AddressCache[key]=(new(probe.Bits,spans),unknown);}
  private Match Eq(string condition,string actual){bool not=condition.StartsWith('!');var value=not?condition[1..]:condition;if(actual=="")return Match.Unknown;var matches=value.Split(',').Contains(actual);return matches!=not?Match.Yes:Match.No;}
  private Match Port(string condition,int port){if(port<0)return Match.Unknown;bool not=condition.StartsWith('!');var value=not?condition[1..]:condition;bool matches=false;foreach(var p in value.Split(',')){var range=p.Split('-');if(!int.TryParse(range[0],out var lo)||range.Length>2||lo<0||lo>65535)return Match.Unknown;var hi=lo;if(range.Length==2&&(!int.TryParse(range[1],out hi)||hi<lo||hi>65535))return Match.Unknown;matches|=port>=lo&&port<=hi;}return matches!=not?Match.Yes:Match.No;}
  private (AuditAddresses Set,Match Match) Matches(Dictionary<string,string> r,AuditAddresses source){var match=Match.Yes;var selected=source;void Add(Match m){if(m==Match.No)match=Match.No;else if(m==Match.Unknown&&match!=Match.No){match=Match.Unknown;result.Limited=true;result.Trace.Add("Не вычислены все условия правила "+table+" "+V(r,".id")+" chain="+V(r,"chain"));}}
   foreach(var p in r){if(p.Value==""||new[]{".id","chain","action","jump-target","disabled","comment","packets","bytes","dynamic","log","log-prefix","reject-with","to-addresses","to-ports","address-list","address-list-timeout","hw-offload","new-routing-mark","new-connection-mark","new-packet-mark","passthrough","route-dst","new-dscp","new-priority","new-mss","new-ttl"}.Contains(p.Key))continue;
    switch(p.Key){case "invalid":if(Yes(p.Value))Add(Match.Unknown);break;
    case "src-address":case "src-address-list":{bool not=p.Value.StartsWith('!');var value=not?p.Value[1..]:p.Value;AuditAddresses addresses;bool unknown;if(p.Key=="src-address"){unknown=!AuditAddresses.Try(value,probe.Bits,out addresses);}else{(addresses,unknown)=AddressList(value);}if(unknown){Add(Match.Unknown);result.Trace.Add("Не вычислен полный состав "+p.Key+"="+value+" (динамические, DNS или непрочитанные записи)");}else selected=not?selected.Except(addresses):selected.Intersect(addresses);break;}
    case "dst-address":case "dst-address-list":{if(!AuditAddresses.Try(probe.Destination,probe.Bits,out var destination)){Add(Match.Unknown);break;}bool not=p.Value.StartsWith('!');var value=not?p.Value[1..]:p.Value;AuditAddresses addresses;bool unknown;if(p.Key=="dst-address")unknown=!AuditAddresses.Try(value,probe.Bits,out addresses);else(addresses,unknown)=AddressList(value);Add(unknown?Match.Unknown:(!destination.Intersect(addresses).Empty)!=not?Match.Yes:Match.No);break;}
    case "in-interface":Add(Eq(p.Value,probe.In));break;case "out-interface":Add(Eq(p.Value,probe.Out));break;
    case "in-interface-list":case "out-interface-list":{bool not=p.Value.StartsWith('!');var m=Interface(not?p.Value[1..]:p.Value,p.Key.StartsWith("in-")?probe.In:probe.Out);Add(m==Match.Unknown?m:not?(m==Match.Yes?Match.No:Match.Yes):m);break;}
    case "protocol":Add(Eq(p.Value is "6"?"tcp":p.Value is "17"?"udp":p.Value,probe.Protocol));break;case "dst-port":Add(Port(p.Value,probe.Port));break;
    case "ipsec-policy":{var pieces=p.Value.TrimStart('!').Split(',');var negated=p.Value.StartsWith('!');if(pieces.Length!=2||pieces[0] is not("in" or "out")||pieces[1] is not("ipsec" or "none")){Add(Match.Unknown);break;}var encrypted=pieces[1]=="ipsec";if(negated)encrypted=!encrypted;
        if(pieces[0]=="in"||!probe.Forward){Add(encrypted?Match.No:Match.Yes);break;}
        if(!config.Tables.ContainsKey("ipsec-policies")||config.Errors.ContainsKey("ipsec-policies")){Add(Match.Unknown);result.Trace.Add("Не прочитаны IPsec-политики для проверки out,ipsec; это не подтверждение открытого публичного порта");break;}
        var covered=new AuditAddresses(probe.Bits);bool unknown=false,unbounded=false;
        foreach(var policy in config.Rows("ipsec-policies").Where(x=>Enabled(x)&&!Yes(V(x,"template"))&&!Yes(V(x,"invalid"))&&(V(x,"action") is "" or "encrypt"))){
          if(!AuditAddresses.Try(V(policy,"src-address"),probe.Bits,out var src)){unknown=true;unbounded=true;continue;}
          var destinationMatch=Match.Yes;
          if(!AuditAddresses.Try(probe.Destination,probe.Bits,out var dest))destinationMatch=Match.Unknown;
          else if(!AuditAddresses.Try(V(policy,"dst-address"),probe.Bits,out var target))destinationMatch=Match.Unknown;
          else if(dest.Intersect(target).Empty)continue;
          var proto=V(policy,"protocol");if(proto!=""&&proto is not("all" or "255")){var m=Eq(proto,probe.Protocol);if(proto=="6")m=probe.Protocol=="tcp"?Match.Yes:Match.No;if(proto=="17")m=probe.Protocol=="udp"?Match.Yes:Match.No;if(m==Match.No)continue;if(m==Match.Unknown)unknown=true;}
          if(V(policy,"dst-port") is not("" or "any" or "0")&&Port(V(policy,"dst-port"),probe.Port)==Match.No)continue;
          covered=covered.Union(src);unknown|=destinationMatch==Match.Unknown||V(policy,"src-port") is not("" or "any" or "0");
        }
        if(unknown){Add(Match.Unknown);result.Trace.Add("Не вычислен полный IPsec-селектор; возможные источники не отбрасываются");}selected=encrypted?(unbounded?selected:selected.Intersect(covered)):unknown?selected:selected.Except(covered);break;
    }
    case "connection-state":Add(Eq(p.Value,probe.State));break;case "connection-nat-state":Add(probe.NatUnknown?Match.Unknown:Eq(p.Value,probe.Dnat?"dstnat":"none"));break;
    case "dst-address-type":Add(Eq(p.Value,probe.Forward?"unicast":"local"));break;
    case "tcp-flags":Add(probe.Protocol!="tcp"?Match.No:p.Value=="syn"?Match.Yes:Match.Unknown);break;
    default:Add(Match.Unknown);result.Trace.Add("Не вычислено условие: "+p.Key+"="+p.Value);break;
    }
   }if(selected.Empty)return(selected,Match.No);return(selected,match);
  }
  private void Accepted(AuditAddresses sources,bool uncertain){result.Possible=result.Possible.Union(sources);if(!uncertain)result.Definite=result.Definite.Union(sources);}
  private void Walk(string current,int index,AuditAddresses sources,List<(string Chain,int Index)> stack,bool uncertain){if(sources.Empty)return;if(System.Diagnostics.Stopwatch.GetTimestamp()>config.Deadline)throw new AuditBudgetException();if(++work>512||stack.Count>24){result.Limited=true;result.Trace.Add("Достигнут предел переходов / глубины цепочек");Accepted(sources,true);return;}var rows=chains.GetValueOrDefault(current)??[];if(index>=rows.Length){if(stack.Count>0){var back=stack[^1];Walk(back.Chain,back.Index,sources,stack.Take(stack.Count-1).ToList(),uncertain);}else if(natTarget==null)Accepted(sources,uncertain);return;}
   var r=rows[index];var action=V(r,"action");if(action is "log" or "passthrough"){Walk(current,index+1,sources,stack,uncertain);return;}var (selected,m)=Matches(r,sources);if(m==Match.No){Walk(current,index+1,sources,stack,uncertain);return;}result.Trace.Add(table+" "+Semantic(r));
   if(m==Match.Unknown){Walk(current,index+1,sources.Except(selected),stack,uncertain);Walk(current,index+1,selected,stack,true);uncertain=true;}else Walk(current,index+1,sources.Except(selected),stack,uncertain);
   switch(action){case "accept":case "fasttrack-connection":if(natTarget==null)Accepted(selected,uncertain);else if(V(r,".id")==natTarget)Accepted(selected,uncertain);break;
    case "drop":case "reject":break;
    case "jump":if(V(r,"jump-target")==""||!chains.ContainsKey(V(r,"jump-target"))){result.Limited=true;Accepted(selected,true);}else Walk(V(r,"jump-target"),0,selected,stack.Append((current,index+1)).ToList(),uncertain);break;
    case "return":if(stack.Count==0){if(natTarget==null)Accepted(selected,uncertain);}else{var back=stack[^1];Walk(back.Chain,back.Index,selected,stack.Take(stack.Count-1).ToList(),uncertain);}break;
    case "dst-nat":case "redirect":case "netmap":if(V(r,".id")==natTarget)Accepted(selected,uncertain);else if(natTarget==null){result.Limited=true;Accepted(selected,true);}break;
    case "mark-routing":case "route":
     if(manglePass){if(V(r,"passthrough") is "no" or "false")Accepted(selected,uncertain);else Walk(current,index+1,selected,stack,uncertain);}
     else if(routingTarget){if(V(r,".id")==natTarget)Accepted(selected,uncertain);else if(V(r,"passthrough") is not("no" or "false"))Walk(current,index+1,selected,stack,uncertain);}
     else{result.Limited=true;Accepted(selected,true);}break;
    case "mark-connection":case "mark-packet":case "change-mss":case "change-dscp":case "set-priority":case "change-ttl":case "clear-df":
     if(manglePass){if(V(r,"passthrough") is "no" or "false")Accepted(selected,uncertain);else Walk(current,index+1,selected,stack,uncertain);}
     else if(routingTarget){if(V(r,"passthrough") is not("no" or "false"))Walk(current,index+1,selected,stack,uncertain);}else{result.Limited=true;Accepted(selected,true);}break;
    case "notrack":if(table is "raw4" or "raw6"){result.Notrack=true;result.UntrackedPossible=result.UntrackedPossible.Union(selected);if(!uncertain)result.UntrackedDefinite=result.UntrackedDefinite.Union(selected);Accepted(selected,true);Walk(current,index+1,selected,stack,uncertain);}else{result.Limited=true;Accepted(selected,true);}break;
    case "add-src-to-address-list":case "add-dst-to-address-list":Walk(current,index+1,selected,stack,uncertain);break;
    default:result.Limited=true;Accepted(selected,true);break;
   }
  }
  public Reach Run(AuditAddresses? source=null){Walk(chain,0,source??AuditAddresses.All(probe.Bits),[],false);if(config.Errors.ContainsKey(table)){result.Trace.Add("Не прочитан раздел "+table);if(table is "raw4" or "raw6"){result.Notrack=true;result.UntrackedPossible=AuditAddresses.All(probe.Bits);}result.Limited=true;result.Possible=AuditAddresses.All(probe.Bits);result.Definite=new(probe.Bits);}return result;}
 }
 private static string Outgoing(RouterAuditConfig c,string address,int bits){if(!IPAddress.TryParse(address,out _))return "";var matches=new List<(int Length,string Name)>();foreach(var a in c.Rows(bits==32?"addresses4":"addresses6").Where(Enabled)){if(AuditAddresses.Try(V(a,"address"),bits,out var subnet)&&AuditAddresses.Try(address,bits,out var dest)&&!subnet.Intersect(dest).Empty){var parts=V(a,"address").Split('/');if(parts.Length==2&&int.TryParse(parts[1],out var n))matches.Add((n,V(a,"interface")));}}foreach(var route in c.Rows(bits==32?"routes4":"routes6").Where(r=>Enabled(r)&&Yes(V(r,"active"))&&(V(r,"routing-table") is "" or "main")&&V(r,"routing-mark")=="")){if(AuditAddresses.Try(V(route,"dst-address"),bits,out var subnet)&&AuditAddresses.Try(address,bits,out var dest)&&!subnet.Intersect(dest).Empty&&int.TryParse(V(route,"dst-address").Split('/').Last(),out var n)){var gw=V(route,"immediate-gw");if(gw.Contains('%'))matches.Add((n,gw.Split('%').Last()));else if(c.Rows("interfaces").Any(i=>V(i,"name")==V(route,"gateway")))matches.Add((n,V(route,"gateway")));}}var best=matches.OrderByDescending(x=>x.Length).ToArray();return best.Length==0?"":best.Where(x=>x.Length==best[0].Length).Select(x=>x.Name).Distinct().Count()==1?best[0].Name:"";}
 public static string[] DetectWan(RouterAuditConfig c){var result=new HashSet<string>();foreach(var row in c.Rows("members").Where(r=>Enabled(r)&&V(r,"list").Equals("WAN",StringComparison.OrdinalIgnoreCase)))result.Add(V(row,"interface"));foreach(var route in c.Rows("routes4").Concat(c.Rows("routes6")).Where(r=>Enabled(r)&&Yes(V(r,"active"))&&(V(r,"dst-address") is "0.0.0.0/0" or "::/0")&&(V(r,"routing-table") is "" or "main")&&V(r,"routing-mark")=="")){var gw=V(route,"immediate-gw");if(gw.Contains('%'))result.Add(gw.Split('%').Last());else if(c.Rows("interfaces").Any(i=>V(i,"name")==V(route,"gateway")))result.Add(V(route,"gateway"));}return result.Where(n=>n!="").Order().ToArray();}
 public static RouterAuditResult Analyze(RouterAuditConfig c,string[]? manualWan=null){var wan=manualWan?.Length>0?manualWan.Distinct().ToArray():DetectWan(c);var output=new RouterAuditResult{CheckedUtc=c.CapturedUtc,WanInterfaces=wan,ManualWan=manualWan?.Length>0};
  var evidenceBlocks=new Dictionary<string,SortedSet<string>>(StringComparer.Ordinal);
  var proofBlocks=new Dictionary<string,SortedSet<string>>(StringComparer.Ordinal);
  static int Rank(string status)=>status switch{"Разрешено firewall: любые источники"=>5,"Разрешено firewall: широкий доступ"=>4,"Нужна проверка"=>3,"Разрешено firewall: ограниченные источники"=>2,"Закрыто"=>1,_=>0};
  void Add(string key,string title,Severity severity,string status,string evidence,string recommendation,string commands="",string proof=""){
   if(!evidenceBlocks.TryGetValue(key,out var blocks)){blocks=new(StringComparer.Ordinal);evidenceBlocks[key]=blocks;proofBlocks[key]=new(StringComparer.Ordinal);}
   blocks.Add(evidence);if(proof!="")proofBlocks[key].Add(proof);
   var merged=string.Join("\n\n",blocks);var mergedProof=string.Join("\n",proofBlocks[key]);
   var existing=output.Findings.SingleOrDefault(f=>f.Key==key);
   if(existing!=null){
    if(severity>existing.Severity||severity==existing.Severity&&Rank(status)>Rank(existing.Status)){existing.Severity=severity;existing.Status=status;existing.Recommendation=recommendation;existing.Commands=commands;}
    existing.Evidence=merged;existing.Proof=mergedProof;existing.Fingerprint=Hash(key+"\n"+existing.Status+"\n"+merged+(mergedProof==""?"":"\n"+mergedProof));return;
   }
   if(commands=="")commands="# Диагностика; исправление зависит от условий, указанных выше\n/ip firewall filter print detail\n/ipv6 firewall filter print detail\n/ip firewall raw print detail\n/ip firewall nat print detail\n/ip service print detail";
   output.Findings.Add(new(){Key=key,Proof=mergedProof,Fingerprint=Hash(key+"\n"+status+"\n"+merged+(mergedProof==""?"":"\n"+mergedProof)),Title=title,Severity=severity,Status=status,Evidence=merged,Recommendation=recommendation,Commands=commands});
  }
  if(c.Tables.Where(t=>!t.Key.StartsWith("address-lists")).Sum(t=>t.Value.Count)>5000||c.Rows("address-lists4").Count>500000||c.Rows("address-lists6").Count>500000||c.Rows("services").Count>64||wan.Length>16){Add("limits","Превышен предел анализа",Severity.Warning,"Нужна проверка","Большой снимок конфигурации не интерпретируется частично как безопасный.","Проверьте конфигурацию вручную.");return output;}
  if(AuditPorts(c).Length>256)Add("port-coverage","Больше 256 проверяемых портов",Severity.Warning,"Нужна проверка","Предел списка портов: часть путей не вычислена.","Проверьте остальные публикации отдельно.");
  // Large, fragmented allowlists can exceed the normal deadline even with exact linear interval operations.
  var budgetSeconds=c.Rows("address-lists4").Count+c.Rows("address-lists6").Count>=50_000?15:5;
  c.AddressCache.Clear();
  c.Deadline=System.Diagnostics.Stopwatch.GetTimestamp()+budgetSeconds*System.Diagnostics.Stopwatch.Frequency;
  try{
  foreach(var required in new[]{"filter4","raw4","nat4","services","dns","addresses4","addresses6","filter6","raw6","address-lists4","address-lists6","interfaces","lists","members","routes4","routes6","ipv6-settings","mac","mac-winbox","neighbors","bridge-ports","switches","mangle4","mangle6"})if(!c.Tables.ContainsKey(required)&&!c.Errors.ContainsKey(required))c.Errors[required]="Раздел отсутствует в снимке";
  foreach(var error in c.Errors)Add("read:"+error.Key,"Не прочитан раздел "+error.Key,Severity.Warning,"Нужна проверка",error.Value,"Проверьте права read + api и доступность меню в этой версии RouterOS. Отсутствующие данные не считаются безопасной конфигурацией.");
  if(wan.Length==0){Add("wan","Внешние интерфейсы не определены",Severity.Warning,"Нужна проверка","Нет однозначного WAN в списках и активных маршрутах по умолчанию.","Укажите внешние интерфейсы в настройках анализа.");return output;}
  foreach(var port in c.Rows("bridge-ports").Where(p=>Enabled(p)&&wan.Contains(V(p,"interface"))))Add("bridge:"+V(port,"interface"),"WAN является портом моста",Severity.Warning,"Нужна проверка",Semantic(port),"IP input/forward не доказывает защиту прозрачного L2-транзита. Проверьте bridge firewall, VLAN и аппаратное переключение.","/interface bridge port print detail\n/interface bridge settings print\n/interface bridge filter print detail");
  foreach(var item in c.Rows("switches").Where(p=>Yes(V(p,"l3-hw-offloading"))))Add("l3-offload:"+V(item,".id"),"Включён аппаратный L3 offload",Severity.Warning,"Нужна проверка",Semantic(item),"Проверьте обход IP firewall на аппаратном уровне и нужные исключения offload.","/interface ethernet switch print detail\n/interface ethernet switch port print detail");
  foreach(var table in new[]{"mangle4","mangle6"})if(c.Rows(table).Any(p=>Enabled(p)&&V(p,"action") is "mark-routing" or "route"))Add("policy:"+table,"Маршрутизация изменяется правилами "+table,Severity.Info,"Учитывается по пути",string.Join("\n",c.Rows(table).Where(p=>Enabled(p)&&V(p,"action") is "mark-routing" or "route").Select(Semantic)),"Само наличие policy routing не означает открытый порт. Проверяются достижимость правила из prerouting, вход, исходные адрес и порт, источники и конечный firewall; неизвестные варианты отмечаются в соответствующих путях.",table=="mangle4"?"/ip firewall mangle print detail\n/ip route print detail":"/ipv6 firewall mangle print detail\n/ipv6 route print detail");
  foreach(var name in wan)if(!c.Rows("interfaces").Any(i=>V(i,"name")==name))Add("wan:"+name,"Внешний интерфейс отсутствует",Severity.Warning,"Нужна проверка",name,"Обновите выбор внешних интерфейсов.");
  Reach Ingress(Probe p,AuditAddresses? source=null){
   var raw=new Evaluator(c,p,p.Bits==32?"raw4":"raw6","prerouting").Run(source);
   var mangle=p.Bits==32?"mangle4":"mangle6";
   var tracked=new Evaluator(c,p,mangle,"prerouting",manglePass:true).Run(raw.Possible.Except(raw.UntrackedDefinite));
   var untracked=new Evaluator(c,p with{State="untracked"},mangle,"prerouting",manglePass:true).Run(raw.UntrackedPossible.Intersect(raw.Possible));
   raw.Possible=tracked.Possible.Union(untracked.Possible).Intersect(raw.Possible);
   raw.Definite=tracked.Definite.Except(raw.UntrackedPossible).Union(untracked.Definite.Intersect(raw.UntrackedDefinite)).Intersect(raw.Definite);
   raw.Trace.UnionWith(tracked.Trace);raw.Trace.UnionWith(untracked.Trace);raw.Limited|=tracked.Limited||untracked.Limited;
   return raw;
  }
  Reach Path(Probe p,string filter,AuditAddresses? source=null){var routeCandidates=source??AuditAddresses.All(p.Bits);var reach=new Evaluator(c,p,filter,p.Forward?"forward":"input").Run(source);var raw=Ingress(p,source);reach.Definite=reach.Definite.Intersect(raw.Definite);reach.Possible=reach.Possible.Intersect(raw.Possible);reach.Trace.UnionWith(raw.Trace);reach.Limited|=raw.Limited;if(raw.Notrack){var untracked=new Evaluator(c,p with{State="untracked"},filter,p.Forward?"forward":"input").Run(raw.UntrackedPossible.Intersect(raw.Possible));var normal=new Evaluator(c,p,filter,p.Forward?"forward":"input").Run((source??AuditAddresses.All(p.Bits)).Except(raw.UntrackedDefinite));reach.Possible=normal.Possible.Union(untracked.Possible).Intersect(raw.Possible);reach.Definite=normal.Definite.Except(raw.UntrackedPossible).Union(untracked.Definite.Intersect(raw.UntrackedDefinite)).Intersect(raw.Definite);reach.Trace.UnionWith(untracked.Trace);reach.Trace.Add("RAW notrack: отдельно проверены tracked/untracked источники; NAT для untracked не применяется");}
 if(p.Bits==32&&!p.Forward){foreach(var nat in c.Rows("nat4").Where(n=>Enabled(n)&&V(n,"action") is "dst-nat" or "redirect" or "netmap")){var translated=new Evaluator(c,p,"nat4","dstnat",V(nat,".id")).Run();translated.Definite=translated.Definite.Except(raw.UntrackedPossible);translated.Possible=translated.Possible.Except(raw.UntrackedDefinite);reach.Definite=reach.Definite.Except(translated.Possible);reach.Possible=reach.Possible.Except(translated.Definite);routeCandidates=routeCandidates.Except(translated.Definite);if(!translated.Possible.Empty){reach.Trace.UnionWith(translated.Trace);reach.Trace.Add("DST-NAT меняет назначение; доступ к перенаправленному узлу проверяется отдельно по NAT → маршрутизация → INPUT/FORWARD");}}}RoutingUncertainty(reach,raw,p,null,routeCandidates);return reach;}
  // Routing uncertainty is propagated through terminal firewall decisions, never converted to an unconditional allow.
  // Ingress is before DNAT; packet is after DNAT. Candidate sources keep NAT/RAW restrictions.
  void RoutingUncertainty(Reach reach,Reach raw,Probe packet,Probe? ingress=null,AuditAddresses? candidateSources=null){
   if(raw.Possible.Empty)return;
   var original=ingress??packet;var mangle=packet.Bits==32?"mangle4":"mangle6";
   var affected=new AuditAddresses(packet.Bits);var traces=new HashSet<string>();
   var ownershipUnknown=c.Errors.ContainsKey(packet.Bits==32?"addresses4":"addresses6");
   var missingMangle=c.Errors.ContainsKey(mangle);var missingNat=packet.Bits==32&&c.Errors.ContainsKey("nat4");
   if(missingMangle||missingNat||ownershipUnknown)affected=raw.Possible;
   else foreach(var item in c.Rows(mangle).Select((row,index)=>(row,index)).Where(x=>Enabled(x.row)&&V(x.row,"action") is "mark-routing" or "route")){
    var id=item.row.ContainsKey(".id")?V(item.row,".id"):"analysis-"+item.index;
    // Only rules reachable from ingress prerouting can affect this inbound packet. Output is not visited.
    var influence=new Evaluator(c,original,mangle,"prerouting",id,true).Run();
    if(influence.Possible.Empty)continue;affected=affected.Union(influence.Possible);traces.UnionWith(influence.Trace);
   }
   affected=affected.Intersect(raw.Possible).Intersect(candidateSources??raw.Possible);
   if(affected.Empty)return;
   var filter=packet.Bits==32?"filter4":"filter6";
   var uncertainPacket=missingNat?packet with{Out="",Destination="",Port=-1,NatUnknown=true}:packet with{Out=""};
   var downstream=new Reach(packet.Bits);
   // A route mark can divert local traffic to forwarding; it does not make a nonlocal destination a local address.
   foreach(var forward in missingNat||ownershipUnknown||!packet.Forward?new[]{false,true}:new[]{true}){
    var routed=uncertainPacket with{Forward=forward};
    var branch=new Evaluator(c,routed,filter,forward?"forward":"input").Run(affected);
    downstream.Possible=downstream.Possible.Union(branch.Possible);downstream.Trace.UnionWith(branch.Trace);
    if(raw.Notrack){var untracked=new Evaluator(c,routed with{State="untracked"},filter,forward?"forward":"input").Run(affected.Intersect(raw.UntrackedPossible));downstream.Possible=downstream.Possible.Union(untracked.Possible);downstream.Trace.UnionWith(untracked.Trace);}
   }
   reach.Definite=reach.Definite.Except(affected);
   reach.Possible=reach.Possible.Except(affected).Union(downstream.Possible.Intersect(affected));
   reach.Limited=true;reach.Trace.UnionWith(traces);reach.Trace.UnionWith(downstream.Trace);
   reach.Trace.Add("Не вычислен точный выбор маршрута для части источников; проверен последующий firewall с неизвестным выходом (для локального/неизвестного назначения также альтернативная цепочка). Mangle не обходит firewall.");
   if(downstream.Possible.Empty)reach.Trace.Add("Все рассмотренные варианты последующего firewall блокируют затронутые источники; неопределённость маршрута не отменяет запрет.");
  }
  void Exposure(string key,string title,Probe p,Reach reach,Severity severity,string recommendation,string commands,AuditAddresses? service=null,Probe? original=null){if(service!=null){reach.Definite=reach.Definite.Intersect(service);reach.Possible=reach.Possible.Intersect(service);}var uncertain=!reach.Possible.Except(reach.Definite).Empty;var broad=reach.Definite.Broad;var status=reach.Possible.Empty?"Закрыто":reach.Definite.Universal?"Разрешено firewall: любые источники":uncertain?"Нужна проверка":broad?"Разрешено firewall: широкий доступ":"Разрешено firewall: ограниченные источники";var level=status is "Разрешено firewall: любые источники" or "Разрешено firewall: широкий доступ"?severity:status=="Нужна проверка"?Severity.Warning:Severity.Info;var evidence=$"Результат пути: {status}\nIPv{(p.Bits==32?4:6)} · вход {p.In} · {p.Protocol}/{p.Port} → {p.Destination}\nПодтверждённые источники: {reach.Definite.Describe()}\nВозможные источники: {reach.Possible.Describe()}\n"+"Рассмотренные правила ветвей (не один линейный маршрут):\n"+string.Join("\n",reach.Trace.OrderBy(t=>t.StartsWith("raw")?0:t.StartsWith("nat")?1:2).ThenBy(t=>t,StringComparer.Ordinal));if(status=="Закрыто"){recommendation="По конфигурации этот путь не разрешает новое публичное подключение. Перенаправления и транзит проверены отдельно; наличие службы на конечном узле не сканируется.";commands="/ip firewall filter print detail\n/ip firewall nat print detail\n/ip firewall raw print detail";}else if(status=="Нужна проверка"){recommendation="Нет достаточного доказательства открытого порта: уточните указанные условия и путь пакета перед изменениями. "+recommendation;commands="/ip firewall filter print detail\n/ip firewall nat print detail\n/ip ipsec policy print detail";}if(reach.Limited)evidence+="\nЕсть невычисленные действия или предел ветвления; окончательный вывод учитывает последующие блокирующие этапы.";Add(key,title,level,status,evidence,recommendation,commands,reach.Definite.Spans.Count>30||reach.Possible.Spans.Count>30?reach.Definite.Digest()+":"+reach.Possible.Digest():"");
   var start=original??p;var verdict=reach.Possible.Empty?FirewallVerdict.Blocked:uncertain?FirewallVerdict.Indeterminate:reach.Definite.Universal||broad?FirewallVerdict.Allowed:FirewallVerdict.Restricted;
   static AuditSourceRange[] Ranges(AuditAddresses addresses){string Ip(System.Numerics.BigInteger value){var bytes=value.ToByteArray(true,true);var padded=new byte[addresses.Bits/8];bytes.CopyTo(padded,padded.Length-bytes.Length);return new IPAddress(padded).ToString();}return addresses.Spans.Take(16).Select(s=>new AuditSourceRange(Ip(s.Lo),Ip(s.Hi))).ToArray();}
   var path=new RouterAuditPath{Family=p.Bits==32?4:6,Input=p.In,Protocol=p.Protocol,ExternalPort=start.Port,OriginalDestination=start.Destination,FinalPort=p.Port,FinalDestination=p.Destination,Chain=p.Forward?"forward":"input",Verdict=verdict,Confirmed=Ranges(reach.Definite),Possible=Ranges(reach.Possible),ConfirmedDigest=reach.Definite.Digest(),PossibleDigest=reach.Possible.Digest(),SourcesTruncated=reach.Definite.Spans.Count>16||reach.Possible.Spans.Count>16,Stages=["RAW prerouting","MANGLE prerouting: влияние на выбранный пакет",p.Dnat?"DST-NAT: исходный → преобразованный адрес и порт":"Без выбранной трансляции DST-NAT",p.Forward?"Маршрутизация к конечному узлу":"Маршрутизация на сам MikroTik",p.Forward?"FILTER forward":"FILTER input"],Conditions=reach.Trace.OrderBy(t=>t.StartsWith("raw")?0:t.StartsWith("nat")?1:t.StartsWith("filter")?3:2).ThenBy(t=>t,StringComparer.Ordinal).ToArray(),UnknownReasons=reach.Trace.Where(t=>t.Contains("Не вычис")||t.Contains("Не прочитан")||t.Contains("предел")||t.Contains("не моделируется")).Order(StringComparer.Ordinal).ToArray()};
   var finding=output.Findings.Single(f=>f.Key==key);if(!finding.Paths.Any(old=>JsonSerializer.Serialize(old)==JsonSerializer.Serialize(path)))finding.Paths.Add(path);
  }
  foreach(var family in new[]{32,128}){var filter=family==32?"filter4":"filter6";if(family==128&&c.Rows("ipv6-settings").Any(r=>Yes(V(r,"disable-ipv6")))&&!c.Rows("addresses6").Any(Enabled))continue;var addresses=c.Rows(family==32?"addresses4":"addresses6").Where(Enabled).Select(r=>V(r,"address").Split('/')[0]).Distinct().ToArray();if(addresses.Length==0){if(c.Errors.ContainsKey(family==32?"addresses4":"addresses6"))continue;Add("no-address:"+family,"Адреса IPv"+(family==32?4:6)+" не обнаружены",Severity.Info,"Не применимо","В полученном снимке нет локальных адресов этого семейства.","При появлении адреса проверка будет выполнена снова.");continue;}if(addresses.Length>32){Add("address-limit:"+family,"Слишком много адресов для полного анализа",Severity.Warning,"Нужна проверка",addresses.Length.ToString(),"Проверяются первые 32 адреса; остальные требуют ручной проверки.");addresses=addresses.Take(32).ToArray();}
   foreach(var input in wan){foreach(var riskyPort in AuditPorts(c).Where(p=>p.Protocol=="tcp").Take(256).Select(p=>p.Port)){var transit=new Probe(family,input,"","tcp",riskyPort,"",true);Exposure("forward:"+family+":"+input+":"+riskyPort,"Транзит без DST-NAT · TCP/"+riskyPort,transit,Path(transit,filter),Severity.Warning,"Обычный новый незашифрованный пакет WAN без DST-NAT. Это оценка транзитной политики, а не доказательство публичной публикации порта. Проверяется разрешение нового транзита в firewall, а не наличие службы на конечном узле. Ограничь ненужный входящий forward; учитывайте нужные VPN, NAT и IPv6. Команды зависят от разрешённых сетей.","");}foreach(var port in AuditPorts(c).Where(p=>p.Protocol=="udp").Take(256)){var transit=new Probe(family,input,"","udp",port.Port,"",true);Exposure("forward:"+family+":"+input+":"+port.Port+":udp","Транзит без DST-NAT · UDP/"+port.Port,transit,Path(transit,filter),Severity.Warning,"Проверяется обычный незашифрованный транзит WAN без DST-NAT; это не доказательство наличия публичной службы.","");}foreach(var service in c.Rows("services").Where(Enabled)){var name=V(service,"name");if(!new[]{"ssh","telnet","ftp","www","www-ssl","api","api-ssl","winbox"}.Contains(name))continue;if(!int.TryParse(V(service,"port"),out var port)||port is <1 or >65535){Add("port:"+name,"Не определён порт "+name,Severity.Warning,"Нужна проверка",Semantic(service),"Проверьте настройки службы.");continue;}var allowed=AuditAddresses.All(family);bool valid=V(service,"address")==""||AuditAddresses.Try(V(service,"address"),family,out allowed);foreach(var address in addresses){var p=new Probe(family,input,"","tcp",port,address);var reach=Path(p,filter);if(!valid){reach.Definite=new(family);reach.Possible=AuditAddresses.All(family);reach.Limited=true;}Exposure("service:"+family+":"+input+":"+name,"Доступ к "+name,p,reach,Severity.Critical,"Если доступ нужен, задайте разрешённые адреса. Замените пример своими CIDR; проверьте текущий административный доступ и используйте Safe Mode.","# Заменить TRUSTED_CIDR до применения; команда с шаблоном не готова к запуску\n/ip service set [find where name="+Q(name)+"] address=\"TRUSTED_CIDR\"",allowed);}}
    foreach(var protocol in new[]{"udp","tcp"})foreach(var address in addresses){var p=new Probe(family,input,"",protocol,53,address);var path=family==32?"/ip firewall filter":"/ipv6 firewall filter";var add="chain=input in-interface="+Q(input)+" protocol="+protocol+" dst-port=53 action=drop comment=\"SAA: protect WAN DNS\"";Exposure("dns:"+family+":"+input,"DNS с внешнего интерфейса · TCP/UDP 53",p,Path(p,filter),Severity.Warning,"Если внешние DNS-клиенты не нужны, запретите TCP и UDP 53 на этом входе перед разрешающими input. Правило не ограничивает DNS на других интерфейсах.",string.Join("\n",new[]{"tcp","udp"}.Select(transport=>{var ruleAdd=add.Replace("protocol="+protocol+" ","protocol="+transport+" ",StringComparison.Ordinal);return "# Проверить выбранный WAN и необходимость внешнего DNS · "+transport+"\n:local first ["+path+" find where chain=input]; :if ([:len $first] > 0) do={ "+path+" add "+ruleAdd+" place-before=[:pick $first 0] } else={ "+path+" add "+ruleAdd+" }";})));}
   }
  }
  // DNS reachability is always visible, independently of whether the resolver currently listens.
  var dnsKnown=c.Tables.ContainsKey("dns")&&!c.Errors.ContainsKey("dns")&&c.Rows("dns").Any(r=>r.ContainsKey("allow-remote-requests"));
  var dnsEnabled=c.Rows("dns").Any(r=>Yes(V(r,"allow-remote-requests")));
  foreach(var f in output.Findings.Where(f=>f.Key.StartsWith("dns:"))){
   f.Evidence="DNS MikroTik: "+(dnsKnown?(dnsEnabled?"allow-remote-requests=yes":"allow-remote-requests=no"):"настройка не прочитана")+"\n"+f.Evidence;
   if(!dnsKnown){f.Status="Нужна проверка";f.Severity=Severity.Warning;f.Recommendation="Не прочитано состояние DNS-службы. Ни отключение DNS, ни защищённость резолвера не подтверждены. Проверьте настройку DNS и указанный путь firewall.";}
   else if(!dnsEnabled){f.Status="DNS-сервис отключён";f.Severity=Severity.Info;f.Recommendation="MikroTik не обслуживает внешние DNS-запросы: allow-remote-requests=no. Это не доказывает блокировку TCP/UDP 53 в firewall. Разрешённость пакетов показана отдельно в результатах пути; публикации DNS на другие узлы проверяются в NAT.";}
   if(!dnsKnown||!dnsEnabled)f.Commands="/ip dns print\n/ip firewall raw print detail\n/ip firewall filter print detail\n/ip firewall nat print detail";
   f.Fingerprint=Hash(f.Key+"\n"+f.Status+"\n"+f.Evidence+(f.Proof==""?"":"\n"+f.Proof));
  }
  // Trace actual publications for all configured ports, standard service ports and whole-address DNAT.
  foreach(var nat in c.Rows("nat4").Where(r=>Enabled(r)&&V(r,"chain")!="srcnat"&&V(r,"action") is "dst-nat" or "netmap" or "redirect")){
    var id=V(nat,".id");var target=V(nat,"to-addresses");var ports=V(nat,"to-ports");
    foreach(var input in wan){
      var protocols=V(nat,"protocol") is "tcp" or "6"?new[]{"tcp"}:V(nat,"protocol") is "udp" or "17"?new[]{"udp"}:V(nat,"protocol")==""?new[]{"tcp","udp"}:Array.Empty<string>();
      if(protocols.Length==0)continue;
      if(!Id(id)||V(nat,"dst-address")!=""&&(!IPAddress.TryParse(V(nat,"dst-address"),out _)&&!(V(nat,"dst-address").EndsWith("/32")&&IPAddress.TryParse(V(nat,"dst-address")[..^3],out _)))||V(nat,"action")=="netmap"||ports!=""&&!int.TryParse(ports,out _)||V(nat,"dst-port")!=""&&!int.TryParse(V(nat,"dst-port"),out _)||V(nat,"action")!="redirect"&&(!IPAddress.TryParse(target,out var parsed)||parsed.AddressFamily!=AddressFamily.InterNetwork)){
         Add("nat-unknown:"+id+":"+input,"Публикация NAT требует уточнения",Severity.Warning,"Нужна проверка",Semantic(nat),"Диапазон/сложная трансляция не вычислены полностью. Это не доказательство открытого порта. Проверьте NAT, RAW, маршрутизацию и завершающую цепочку.");continue;
      }
      foreach(var protocol in protocols){
       var externalPorts=int.TryParse(V(nat,"dst-port"),out var single)?new[]{single}:AuditPorts(c).Where(p=>p.Protocol==protocol).Take(256).Select(p=>p.Port).ToArray();
       foreach(var externalPort in externalPorts){
        var insidePort=int.TryParse(ports,out var remap)?remap:externalPort;
        var targets=V(nat,"action")=="redirect"?c.Rows("addresses4").Where(a=>Enabled(a)&&V(a,"interface")==input).Select(a=>V(a,"address").Split('/')[0]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray():new[]{target};
        if(targets.Length==0){Add("nat-unknown:"+id+":"+input,"Не определён локальный адрес redirect",Severity.Warning,"Нужна проверка",Semantic(nat),"Уточните адрес входного интерфейса.");continue;}
        foreach(var dst in targets){
         var publicTargets=V(nat,"dst-address")!=""?new[]{V(nat,"dst-address").Split('/')[0]}:c.Rows("addresses4").Where(a=>Enabled(a)&&V(a,"interface")==input).Select(a=>V(a,"address").Split('/')[0]).ToArray();
         publicTargets=publicTargets.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
         if(publicTargets.Length==0)publicTargets=[""];
         foreach(var publicTarget in publicTargets){
          var before=new Probe(32,input,"",protocol,externalPort,publicTarget);var candidates=new Evaluator(c,before,"nat4","dstnat",id).Run();
          var local=c.Rows("addresses4").Any(a=>Enabled(a)&&V(a,"address").Split('/')[0]==dst);
          var after=new Probe(32,input,local?"":Outgoing(c,dst,32),protocol,insidePort,dst,!local,true);
          var raw=Ingress(before);candidates.Possible=candidates.Possible.Except(raw.UntrackedDefinite);candidates.Definite=candidates.Definite.Except(raw.UntrackedPossible);var reach=new Evaluator(c,after,"filter4",local?"input":"forward").Run(candidates.Possible.Intersect(raw.Possible));
          reach.Definite=reach.Definite.Intersect(candidates.Definite).Intersect(raw.Definite);reach.Trace.UnionWith(candidates.Trace);reach.Trace.UnionWith(raw.Trace);
          RoutingUncertainty(reach,raw,after,before,candidates.Possible.Intersect(raw.Possible));reach.Trace.Add("Путь в конфигурации: вход "+input+" · "+(publicTarget==""?"адрес назначения не определён":publicTarget)+":"+externalPort+" → DST-NAT "+dst+":"+insidePort+" → "+(local?"INPUT (сам маршрутизатор)":"FORWARD (конечный узел)"));
          if(raw.Notrack)reach.Trace.Add("RAW notrack исключает соответствующие источники из DST-NAT");
          var key="nat:"+protocol+":"+externalPort+":"+dst+":"+insidePort;
          Exposure(key,"Публикация "+protocol+"/"+externalPort+" → "+insidePort,after,reach,insidePort is 3389 or 445 or 23 or 2375?Severity.Critical:Severity.Warning,"Доступность через провайдера, внешний NAT и служба конечного узла не проверялись. Оценивается путь в конфигурации этого MikroTik. Если публикация не нужна — отключите это правило. Иначе ограничь источники и проверьте "+(local?"INPUT":"FORWARD")+". INPUT другого адреса не защищает перенаправленный узел.","# Сверить все правила, перечисленные в доказательствах, и последствия отключения\n/ip firewall nat print detail",null,before);
         }
        }
       }
      }
    }
  }
  foreach(var table in new[]{"filter4","filter6","raw4","raw6","nat4"})foreach(var rule in c.Rows(table).Where(r=>!Yes(V(r,"disabled"))&&Yes(V(r,"invalid"))))Add("invalid:"+table+":"+V(rule,".id"),"Неработающее правило firewall",Severity.Warning,"Нужна проверка",Semantic(rule),"RouterOS пометил правило invalid. Оно не используется как доказательство защиты.");
  foreach(var rule in c.Rows("nat4").Where(r=>Enabled(r)&&V(r,"chain")=="input"))Add("nat-input:"+V(rule,".id"),"NAT input не моделируется полностью",Severity.Warning,"Нужна проверка",Semantic(rule),"Локальная обработка NAT input требует ручной проверки; общий вывод о конфигурации не подтверждён.");
  foreach(var table in new[]{"filter4","filter6"}){var terminals=new Dictionary<string,string>();foreach(var rule in c.Rows(table).Where(Enabled)){var chain=V(rule,"chain");if(terminals.TryGetValue(chain,out var blocker)){Add("shadow:"+table+":"+V(rule,".id"),"Правило после безусловного завершения цепочки",(V(rule,"action") is "drop" or "reject")&&blocker.Contains("action=accept;")?Severity.Warning:Severity.Info,"Не достигается",Semantic(rule)+"\nРанее: "+blocker,"Проверьте порядок; безусловный accept выше делает этот запрет неэффективным. Не перемещай правила без проверки административного доступа.");continue;}if(V(rule,"action") is "accept" or "drop" or "reject" or "return"&&rule.All(p=>new[]{".id","chain","action","disabled","comment","packets","bytes","dynamic","log","log-prefix"}.Contains(p.Key)||p.Value==""))terminals[chain]=Semantic(rule);}}
  foreach(var table in new[]{"mac","mac-winbox","neighbors"})foreach(var row in c.Rows(table)){var list=V(row,table=="neighbors"?"discover-interface-list":"allowed-interface-list");if(list=="none")continue;var memberships=wan.Select(w=>new Evaluator(c,new(32,w,"","tcp",0,""),"filter4","input").Interface(list,w)).ToArray();var exposed=memberships.Any(m=>m==Match.Yes);if(exposed)Add("l2:"+table,"L2-доступ / обнаружение на WAN: "+table,Severity.Warning,"Доступно на сегменте WAN",Semantic(row),"IP firewall не блокирует MAC-доступ. Выберите список только доверенных интерфейсов.",table=="neighbors"?"/ip neighbor discovery-settings set discover-interface-list=none":"/tool "+(table=="mac"?"mac-server":"mac-server mac-winbox")+" set allowed-interface-list=none");else if(list==""||memberships.Any(m=>m==Match.Unknown))Add("l2-unknown:"+table,"Не определён список интерфейсов: "+table,Severity.Warning,"Нужна проверка",Semantic(row),"Проверьте состав списка интерфейсов.");}
  return output;
  }catch(AuditBudgetException){Add("budget","Исчерпан бюджет анализа",Severity.Warning,"Нужна проверка",$"Сложный снимок не удалось полностью проверить за {budgetSeconds} секунд.","Часть результатов ниже не завершена; проверьте сложные цепочки вручную.");return output;}
 }
 private static (string Protocol,int Port)[] AuditPorts(RouterAuditConfig c){
  var ports=new HashSet<(string Protocol,int Port)>();foreach(var p in new[]{21,22,23,25,53,80,110,135,139,143,389,443,445,465,587,636,993,995,1433,1521,2049,2375,2376,3306,3389,5432,5672,5900,5985,5986,6379,8080,8443,8291,8728,8729,9200,11211,27017})ports.Add(("tcp",p));foreach(var p in new[]{53,123,161,389,1434,1900,5060,11211})ports.Add(("udp",p));
  foreach(var protocol in new[]{"tcp","udp"}){ports.Add((protocol,1));ports.Add((protocol,65535));}
  foreach(var row in c.Rows("nat4").Concat(c.Rows("filter4")).Concat(c.Rows("filter6")).Concat(c.Rows("ipsec-policies")).Where(Enabled))foreach(var key in new[]{"dst-port","to-ports"})foreach(var part in V(row,key).TrimStart('!').Split(',')){
   var pair=part.Split('-');if(pair.Length>2||!int.TryParse(pair[0],out var lo)||lo is <1 or >65535)continue;var hi=lo;if(pair.Length==2&&(!int.TryParse(pair[1],out hi)||hi<lo||hi>65535))continue;
   foreach(var p in new[]{lo,hi,Math.Max(1,lo-1),Math.Min(65535,hi+1)}){var protocol=V(row,"protocol");if(protocol is "" or "all" or "255" or "tcp" or "6")ports.Add(("tcp",p));if(protocol is "" or "all" or "255" or "udp" or "17")ports.Add(("udp",p));}
  }
  return ports.OrderBy(p=>p.Protocol).ThenBy(p=>p.Port).ToArray();
 }
 private static bool PortIncludes(string value,int port){if(value=="")return true;foreach(var part in value.Split(',')){var pair=part.Split('-');if(int.TryParse(pair[0],out var lo)){var hi=lo;if(pair.Length==2&&!int.TryParse(pair[1],out hi))return true;if(port>=lo&&port<=hi)return true;}else return true;}return false;}
}
