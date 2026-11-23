using System.Text.Json;
namespace SysAdminAssistant.Core;
public sealed record HelpArticle(string Id,string TitleRu,string TitleEn,string BodyRu,string BodyEn)
{
 public string Title=>ProductText.Language=="en"?TitleEn:TitleRu;
 public string Body=>ProductText.Language=="en"?BodyEn:BodyRu;
}
public static class ProductHelp
{
 private static readonly Lazy<HelpArticle[]> Articles=new(()=>{
  using var stream=typeof(ProductHelp).Assembly.GetManifestResourceStream("SAA.Help")!;
  return JsonSerializer.Deserialize<HelpArticle[]>(stream)??[];
 });
 public static IReadOnlyList<HelpArticle> All=>Articles.Value;
 public static IEnumerable<HelpArticle> Search(string? text)=>All.Where(a=>string.IsNullOrWhiteSpace(text)||a.Title.Contains(text.Trim(),StringComparison.CurrentCultureIgnoreCase)||a.Body.Contains(text.Trim(),StringComparison.CurrentCultureIgnoreCase));
}
