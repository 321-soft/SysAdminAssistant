using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SysAdminAssistant.Core;

/// <summary>Presentation-only localization. Never changes persisted keys, matching rules or raw evidence.</summary>
public static class ProductText
{
 private static readonly AsyncLocal<string?> Current=new();
 private static string fallback="ru";
 private static readonly Lazy<Dictionary<string,string>> English=new(()=>Read("SAA.Text.en"));
 private static readonly Lazy<Dictionary<string,string>> Russian=new(()=>English.Value.GroupBy(p=>p.Value,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First().Key,StringComparer.Ordinal));
 private static readonly Lazy<Dictionary<string,string>> Professional=new(()=>English.Value.Keys.Where(k=>!k.Contains("⟪")).ToDictionary(k=>k,k=>Polite(k),StringComparer.Ordinal));
 private static readonly Dictionary<string,string> FormalForms=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){["выбери"]="выберите",["укажи"]="укажите",["нажми"]="нажмите",["проверь"]="проверьте",["создай"]="создайте",["заполни"]="заполните",["повтори"]="повторите",["дождись"]="дождитесь",["открой"]="откройте",["подключись"]="подключитесь",["замени"]="замените",["измени"]="измените",["оставь"]="оставьте",["перетащи"]="перетащите",["подтверди"]="подтвердите",["настрой"]="настройте",["уточни"]="уточните",["отзови"]="отзовите",["распакуй"]="распакуйте",["сохрани"]="сохраните",["удали"]="удалите",["сверь"]="сверьте",["обнови"]="обновите",["покажи"]="покажите",["скачай"]="скачайте",["вставь"]="вставьте",["верни"]="верните",["перенеси"]="перенесите",["выполни"]="выполните",["запусти"]="запустите",["перезапусти"]="перезапустите",["оцени"]="оцените",["посмотри"]="посмотрите",["составь"]="составьте",["сократи"]="сократите",["согласуй"]="согласуйте",["найди"]="найдите",["сравни"]="сравните",["выдай"]="выдайте",["включи"]="включите",["отключи"]="отключите",["исправь"]="исправьте",["не принимай"]="не принимайте",["не удаляй"]="не удаляйте",["не запускай"]="не запускайте",["не перезапускай"]="не перезапускайте",["не меняй"]="не меняйте",["не считай"]="не считайте",["не форматируй"]="не форматируйте",["не устанавливай"]="не устанавливайте",["не очищай"]="не очищайте",["не отключай"]="не отключайте",["не отправляй"]="не отправляйте",["твой"]="ваш",["твоей"]="вашей",["твоих"]="ваших",["тебе"]="вам",["сделай"]="сделайте",["зайди"]="зайдите",["закрой"]="закройте",["запиши"]="запишите",["прочитай"]="прочитайте",["введи"]="введите",["подожди"]="подождите",["убери"]="уберите",["сними"]="снимите",["установи"]="установите",["добавь"]="добавьте",["адаптируй"]="адаптируйте",["учитывай"]="учитывайте",["отметь"]="отметьте",["нажимай"]="нажимайте",["переходи"]="переходите",["отключись"]="отключитесь",["войди"]="войдите",["выйди"]="выйдите",["перезагрузи"]="перезагрузите",["просмотри"]="просмотрите",["воспользуйся"]="воспользуйтесь",["убедись"]="убедитесь",["задай"]="задайте",["отфильтруй"]="отфильтруйте",["скорректируй"]="скорректируйте",["путай"]="путайте",["полагайся"]="полагайтесь",["используй"]="используйте",["добавляй"]="добавляйте",["снимай"]="снимайте",["закрывай"]="закрывайте",["соглашайся"]="соглашайтесь",["указывай"]="указывайте",["вводи"]="вводите",["учти"]="учтите",["исключи"]="исключите",["разреши"]="разрешите",["запрети"]="запретите",["попробуй"]="попробуйте",["отправь"]="отправьте",["повторяй"]="повторяйте",["разбери"]="разберите",["перечитай"]="перечитайте",["сохранись"]="сохранитесь",["посчитай"]="посчитайте",["импортируй"]="импортируйте",["экспортируй"]="экспортируйте",["привяжи"]="привяжите",["отвяжи"]="отвяжите",["очисти"]="очистите",["загрузи"]="загрузите",["перепроверь"]="перепроверьте",["включай"]="включайте",["отключай"]="отключайте",["убирай"]="убирайте",["скопируй"]="скопируйте",["читай"]="читайте",["тебя"]="вас",["тобой"]="вами",["твою"]="вашу",["твоего"]="вашего",["твоему"]="вашему",["твоим"]="вашим",["твоими"]="вашими",["твои"]="ваши",["твоя"]="ваша",["перейди"]="перейдите",["смотри"]="смотрите",["напиши"]="напишите",["поставь"]="поставьте",["запомни"]="запомните",["прочти"]="прочтите",["спроси"]="спросите",["подключи"]="подключите",["выключи"]="выключите",["кликни"]="нажмите",["удостоверься"]="удостоверьтесь",["забудь"]="забудьте"};
 private static readonly Regex FormalPattern=new(@"(?<![\p{L}\p{N}_])(?:"+string.Join("|",FormalForms.Keys.OrderByDescending(k=>k.Length).Select(Regex.Escape))+@")(?![\p{L}\p{N}_])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,Regex.InfiniteMatchTimeout);
 private static string Polite(string value)=>FormalPattern.Replace(value,match=>{var replacement=FormalForms[match.Value];return char.IsUpper(match.Value[0])?char.ToUpperInvariant(replacement[0])+replacement[1..]:replacement;});

 private static readonly Lazy<KeyValuePair<string,string>[]> EnPrefixes=new(()=>Prefixes(English.Value));
 private static readonly Lazy<KeyValuePair<string,string>[]> RuPrefixes=new(()=>Prefixes(Russian.Value));
 private static KeyValuePair<string,string>[] Prefixes(Dictionary<string,string> dictionary)=>dictionary.Where(p=>!p.Key.Contains('\n')&&(p.Key.EndsWith(": ",StringComparison.Ordinal)||p.Key.EndsWith(" · ",StringComparison.Ordinal)||p.Key.EndsWith(" — ",StringComparison.Ordinal))).OrderByDescending(p=>p.Key.Length).ToArray();
 private static readonly Regex SlotPattern=new(@"(⟪[0-9]+⟫)",RegexOptions.CultureInvariant,Regex.InfiniteMatchTimeout);
 private sealed record Template(string[] Source,string[] Result,string Anchor,bool Multiline)
 {
  public string? Translate(string value)
  {
   var start=Source[0].Length;var end=value.Length-Source[^1].Length;
   if(start>end||!value.StartsWith(Source[0],StringComparison.Ordinal)||!value.EndsWith(Source[^1],StringComparison.Ordinal))return null;
   if(!Multiline&&value.AsSpan().IndexOfAny('\r','\n')>=0)return null;
   var slots=new Dictionary<string,(int Start,int Length)>(StringComparer.Ordinal);
   for(int i=1;i<Source.Length;i+=2){
    var separator=Source[i+1];
    var next=i+1==Source.Length-1?end:value.IndexOf(separator,start,end-start,StringComparison.Ordinal);
    if(next<0)return null;
    slots[Source[i]]=(start,next-start);
    start=next+separator.Length;
   }
   var translated=new StringBuilder();
   for(int i=0;i<Result.Length;i++){
    if(i%2==0)translated.Append(Result[i]);
    else if(slots.TryGetValue(Result[i],out var slot))translated.Append(value,slot.Start,slot.Length);
   }
   return translated.ToString();
  }
 }
 private static readonly Lazy<Template[]> EnTemplates=new(()=>Templates(English.Value));
 private static readonly Lazy<Template[]> RuTemplates=new(()=>Templates(Russian.Value.ToDictionary(p=>p.Key,p=>Polite(p.Value),StringComparer.Ordinal)));
 public static string Language=>Current.Value??fallback;
 public static string DefaultLanguage{get=>fallback;set=>fallback=Normalize(value);}
 public static string Normalize(string? value)=>value?.StartsWith("en",StringComparison.OrdinalIgnoreCase)==true?"en":"ru";
 public static IDisposable Scope(string? language){var previous=Current.Value;Current.Value=Normalize(language);return new Restore(previous);}
 private sealed class Restore(string? previous):IDisposable{public void Dispose()=>Current.Value=previous;}
 private static Dictionary<string,string> Read(string name){using var stream=typeof(ProductText).Assembly.GetManifestResourceStream(name)??throw new InvalidDataException("Language catalog missing: "+name);var result=JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??[];foreach(var key in result.Keys.ToArray()){var formal=Polite(key);if(formal!=key)result.TryAdd(formal,result[key]);}return result;}

 // Catalog templates contain only literal text and numbered slots. Find literal
 // separators directly instead of executing a timed regex on user-visible text.
 private static Template[] Templates(Dictionary<string,string> source)=>source.Where(p=>p.Key.Contains("⟪0⟫")).Select(p=>{
  var parts=SlotPattern.Split(p.Key);
  var anchor=parts.Where((_,index)=>index%2==0).OrderByDescending(part=>part.Length).First();
  return new Template(parts,SlotPattern.Split(p.Value),anchor,p.Key.Contains('\n'));
 }).OrderByDescending(t=>t.Anchor.Length).ToArray();
 public static string Get(string? value)
 {
  if(string.IsNullOrEmpty(value))return value??"";if(value.Length>24000)return value;
  var dictionary=Language=="en"?English.Value:Russian.Value;
  if(Language=="ru"&&Professional.Value.TryGetValue(value,out var professional))return professional;
  if(dictionary.TryGetValue(value,out var exact))return Language=="ru"?Polite(exact):exact;
  var trimmed=value.Trim();if(dictionary.TryGetValue(trimmed,out exact))return value[..(value.Length-value.TrimStart().Length)]+(Language=="ru"?Polite(exact):exact)+value[value.TrimEnd().Length..];
  foreach(var template in (Language=="en"?EnTemplates.Value:RuTemplates.Value)){
   if(template.Anchor.Length<4||!value.Contains(template.Anchor,StringComparison.Ordinal))continue;
   // Preserve captured names, paths, addresses and evidence verbatim.
   if(template.Translate(value) is {} translated)return translated;
  }
  // A generated multiline message may contain raw journal/mail evidence. Leave the evidence untouched.
  foreach(var marker in new[]{"Последняя запись:","Исходное сообщение:","Исходные строки:","Last record:","Original message:","Source lines:"}){
   var index=value.IndexOf(marker,StringComparison.Ordinal);if(index>=0)return Lines(value[..index])+Get(marker)+value[(index+marker.Length)..];
  }
  if(!value.Contains('\n'))foreach(var prefix in (Language=="en"?EnPrefixes.Value:RuPrefixes.Value))if(value.StartsWith(prefix.Key,StringComparison.Ordinal))return (Language=="ru"?Polite(prefix.Value):prefix.Value)+value[prefix.Key.Length..];
  return value.Contains('\n')?Lines(value):value;
 }
 private static string Lines(string value)=>string.Join("\n",value.Split('\n').Select(Get));
}
