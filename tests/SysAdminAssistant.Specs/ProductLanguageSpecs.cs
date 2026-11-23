using System.Text.Json;
using System.Text.RegularExpressions;
using SysAdminAssistant.Core;
internal static class ProductLanguageSpecs
{
 public static async Task Run(Action<string,bool> check)
 {
  using(ProductText.Scope("en")){
   var metricReport=new AgentReport{Os="Linux Ubuntu",MachineName="Успешно",CapturedUtc=DateTime.UtcNow,CpuPercent=99,CpuCount=4};
   var advice=AgentIssueAdvice.Metric(new AgentFinding("cpu","CPU",Severity.Warning,2),metricReport,new AgentPolicy{CpuPercent=80});
   check("Agent CPU advice localizes thresholds and remediation",advice.Contains("threshold")&&advice.Contains("top/htop")&&!advice.Contains("Что проверить"));
   var originalEvent=new AgentJournalEvent("raw","System","fixture",1,2,DateTime.UtcNow,"Успешно: исходная строка");
   var explanation=AgentIssueAdvice.Journal("custom","Успешно",1,DateTime.UtcNow,null,new[]{originalEvent},0);
   check("English journal advice preserves original message",explanation.Contains(originalEvent.Message));
   check("English product labels load",ProductText.Get("Резервное копирование")=="Backups");
   check("Translation preserves captured entity names",ProductText.Get("Устройство · Ошибка")=="Device · Ошибка");
   check("Translation preserves raw journal evidence",ProductText.Get("Ошибка\nПоследняя запись: Успешно\nТема")=="Error\nLast record: Успешно\nТема");
   check("Unknown text remains unchanged",ProductText.Get("Неизвестное имя пользователя 123")=="Неизвестное имя пользователя 123");
   check("Built-in network rule title translates without mutation",ProductLabels.NetworkRule(new NetworkRule{Id="wan",Name="WAN: доступ в интернет"})=="WAN: internet access");
   check("Custom network rule title remains verbatim",ProductLabels.NetworkRule(new NetworkRule{Id="custom",Name="WAN: доступ в интернет"})=="WAN: доступ в интернет");
   var originalRule=AgentEvents.Standard().First();var before=JsonSerializer.Serialize(originalRule);_=ProductLabels.AgentRule(originalRule);
   check("Displaying agent rule never changes matching policy",JsonSerializer.Serialize(originalRule)==before);
   check("Backup enum presentation follows client language",Display.Result(BackupResult.Success)=="Successful");
   check("Numbered translation template retains bounds",ProductText.Get("Число должно быть от 9 до 256")=="The number must be between 9 and 256");
   var longName=new string('Я',22000)+" · Ошибка";
   check("Long template captures remain unchanged",ProductText.Get("Устройство · "+longName)=="Device · "+longName);
   check("CPU template preserves numeric fields",ProductText.Get("CPU: 99,0%; порог ≥ 80%. Ядер: 4.")=="CPU: 99,0%; threshold ≥ 80%. Cores: 4.");
   using(ProductText.Scope("ru"))check("Nested notification scope uses Russian",ProductText.Get("Резервное копирование")=="Резервное копирование");
   check("Nested scope restores client language",ProductText.Language=="en");
   string serverLabel;using(ProductText.Scope("ru"))serverLabel=ProductText.Get("Выбери значение из списка");
   check("Formal Russian server messages translate in English client",ProductText.Get(serverLabel)=="Select a value from the list");
   var parallel=await Task.WhenAll(Task.Run(async()=>{using var scope=ProductText.Scope("ru");await Task.Yield();return ProductText.Language;}),Task.Run(async()=>{using var scope=ProductText.Scope("en");await Task.Yield();return ProductText.Language;}));
   check("Parallel notification languages do not leak",parallel[0]=="ru"&&parallel[1]=="en"&&ProductText.Language=="en");
  }
  using(ProductText.Scope("ru")){
   check("Russian UI uses professional imperative",ProductText.Get("Выбери значение из списка")=="Выберите значение из списка");
   check("English update diagnostics have Russian text",ProductText.Get("Release signature is invalid; installation blocked.").Contains("подпись"));
  }
  check("User guide includes operational workflows and troubleshooting",new[]{"first-start","dashboard-actions","device-agent-guide","backup-walkthrough","bill-workflow","router-audit-guide","telegram-guide","watchdog-guide","access-and-safety","troubleshooting-guide"}.All(id=>ProductHelp.All.Any(a=>a.Id==id&&a.BodyRu.Length>1200&&a.BodyEn.Length>1200)));
  check("Bilingual offline help covers all sections",ProductHelp.All.Count>=26&&ProductHelp.All.Select(a=>a.Id).Distinct().Count()==ProductHelp.All.Count&&ProductHelp.All.All(a=>a.BodyRu.Length>250&&a.BodyEn.Length>250&&a.TitleRu.Length>0&&a.TitleEn.Length>0));
  using(ProductText.Scope("en"))check("Offline help searches current language",ProductHelp.Search("Authenticode").Any(a=>a.Id=="updates"));
  using var stream=typeof(ProductText).Assembly.GetManifestResourceStream("SAA.Text.en")!;var catalog=JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
  string[] Slots(string value)=>Regex.Matches(value,@"⟪\d+⟫").Select(m=>m.Value).Order().ToArray();
  check("Translations retain every template placeholder",catalog.All(p=>Slots(p.Key).SequenceEqual(Slots(p.Value))));
 }
}
