namespace SysAdminAssistant.Core;
public sealed record DictionaryImport(string Json,string? ExpectedRevision=null);
public sealed record DictionaryChanges(string Revision,string[] Changes,int Changed);
public sealed record MonitoringDictionaryCatalog(NetworkDictionary[] Network,KscRuleView Kaspersky);
public static class MonitoringDictionaryFormat
{
 public static bool IsKaspersky(string json)
 {
  if(string.IsNullOrWhiteSpace(json)||json.Length>2_000_000)throw new ArgumentException("Файл словарей пуст или слишком большой.");
  using var document=System.Text.Json.JsonDocument.Parse(json,new System.Text.Json.JsonDocumentOptions{MaxDepth=32});
  var root=document.RootElement;
  if(root.ValueKind!=System.Text.Json.JsonValueKind.Object)throw new ArgumentException("Ожидается объект JSON со словарём.");
  if(!root.TryGetProperty("Product",out var product))return false;
  if(product.ValueKind==System.Text.Json.JsonValueKind.String&&product.GetString()=="KSC")return true;
  throw new ArgumentException("Неизвестный тип словаря мониторинга.");
 }
}
