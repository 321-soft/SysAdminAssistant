using System.IO.Compression;
using System.Text;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
static class ExcelExchangeSpecs
{
    public static void Run(Action<string,bool> check)
    {
        bool Reject(Action action){try{action();return false;}catch(Exception e)when(e is ArgumentException or InvalidDataException or System.Xml.XmlException){return true;}}
        byte[] Mutate(byte[] bytes,Action<ZipArchive> action){using var ms=new MemoryStream();ms.Write(bytes);using(var z=new ZipArchive(ms,ZipArchiveMode.Update,true))action(z);return ms.ToArray();}
        void Replace(ZipArchive z,string name,Func<string,string> change){var e=z.GetEntry(name)!;string s;using(var reader=new StreamReader(e.Open()))s=reader.ReadToEnd();e.Delete();using var w=new StreamWriter(z.CreateEntry(name).Open());w.Write(change(s));}
        foreach(var kind in new[]{"devices","employees","vault"}){
            check("Excel embedded template readable "+kind,ExcelExchange.Read(ExcelExchange.Template(kind),kind).Length==0);
            check("Excel empty export readable "+kind,ExcelExchange.Read(ExcelExchange.Write(kind,[]),kind).Length==0);
            check("Excel correct permission module "+kind,ExcelExchange.Module(kind)==(kind=="employees"?"inventory":kind));
        }
        string[] row=["","","Тест & <имя>"," 00123 "," =HYPERLINK(\"https://example.invalid\") ","Строка 1\nСтрока 2","12","42"];
        var vault=ExcelExchange.Write("vault",[row]);var read=ExcelExchange.Read(vault,"vault");
        check("Excel exact password whitespace and leading equals roundtrip",read.Single().Values.SequenceEqual(row));
        check("Excel vault carries service ID",read[0].Values[7]=="42");var legacy=Mutate(vault,z=>Replace(z,"xl/worksheets/sheet1.xml",s=>{var xml=System.Xml.Linq.XDocument.Parse(s);System.Xml.Linq.XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";foreach(var c in xml.Descendants(ns+"c").Where(c=>((string?)c.Attribute("r"))?.StartsWith("H")==true).ToArray())c.Remove();return xml.ToString();}));check("Old seven-column vault Excel remains supported without clearing service link",ExcelExchange.Read(legacy,"vault")[0].LegacyVault&&ExcelExchange.Read(legacy,"vault")[0].Values[7]=="");
        var special=(string[])row.Clone();special[4]="_x0041_\r\n\t"+(char)1+" 🔑";var observed=ExcelExchange.Read(ExcelExchange.Write("vault",[special]),"vault")[0].Values[4];check("Excel escaped literals and control characters roundtrip",observed==special[4]);
        check("Excel row numbers retained",read[0].Row==2);
        using(var z=new ZipArchive(new MemoryStream(vault))){using var reader=new StreamReader(z.GetEntry("xl/worksheets/sheet1.xml")!.Open());var xml=reader.ReadToEnd();check("Excel exports literal strings not formulas",xml.Contains("inlineStr")&&!xml.Contains("<f>"));}
        check("Excel wrong section template rejected",Reject(()=>ExcelExchange.Read(vault,"devices")));
        check("Excel upload size bounded",Reject(()=>ExcelExchange.Read(new byte[ExcelExchange.MaxBytes+1],"vault")));
        check("Excel malformed ZIP rejected",Reject(()=>ExcelExchange.Read([1,2,3],"vault")));
        var formula=Mutate(vault,z=>Replace(z,"xl/worksheets/sheet1.xml",s=>s.Replace("<is>","<f>1+1</f><is>")));check("Excel rejects formulas without evaluation",Reject(()=>ExcelExchange.Read(formula,"vault")));
        var duplicate=Mutate(vault,z=>{var e=z.CreateEntry("xl/workbook.xml");using var w=new StreamWriter(e.Open());w.Write("duplicate");});check("Excel duplicate ZIP paths rejected",Reject(()=>ExcelExchange.Read(duplicate,"vault")));
        var external=Mutate(vault,z=>z.CreateEntry("xl/externalLinks/externalLink1.xml"));check("Excel external links rejected",Reject(()=>ExcelExchange.Read(external,"vault")));
        var macros=Mutate(vault,z=>z.CreateEntry("xl/vbaProject.bin"));check("Excel VBA rejected",Reject(()=>ExcelExchange.Read(macros,"vault")));
        var dtd=Mutate(vault,z=>Replace(z,"xl/workbook.xml",s=>s.Insert(s.IndexOf("?>")+2,"<!DOCTYPE workbook [<!ENTITY x SYSTEM 'file:///etc/passwd'>]>")));check("Excel DTD prohibited",Reject(()=>ExcelExchange.Read(dtd,"vault")));
        var badheader=Mutate(vault,z=>Replace(z,"xl/worksheets/sheet1.xml",s=>s.Replace(">Название<",">Неверно<")));check("Excel header mismatch rejected",Reject(()=>ExcelExchange.Read(badheader,"vault")));
        var shared=Mutate(vault,z=>{Replace(z,"xl/worksheets/sheet1.xml",s=>{var xml=System.Xml.Linq.XDocument.Parse(s);System.Xml.Linq.XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";var c=xml.Descendants(ns+"c").Single(x=>(string?)x.Attribute("r")=="C2");c.SetAttributeValue("t","s");c.RemoveNodes();c.Add(new System.Xml.Linq.XElement(ns+"v",0));return xml.ToString();});using var w=new StreamWriter(z.CreateEntry("xl/sharedStrings.xml").Open());w.Write("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>Тест &amp; &lt;имя&gt;</t></si></sst>");});check("Excel shared string reader supported",ExcelExchange.Read(shared,"vault")[0].Values[2]==row[2]);
        check("Excel 2000 row export limit",Reject(()=>ExcelExchange.Write("vault",Enumerable.Repeat(row,2001))));
        check("Excel blank boolean defaults active",ExcelExchange.Flag(""));check("Excel localized false",!ExcelExchange.Flag("Нет"));check("Excel bad boolean rejected",Reject(()=>ExcelExchange.Flag("может быть")));
        check("Excel zero revision supported",ExcelExchange.Revision("0")==0);check("Excel negative IDs rejected",Reject(()=>ExcelExchange.Id("-1")));
        var d=new Device{Name="test",Host="host"};var stamp=ExcelExchange.DeviceVersion(d);d.Status="ONLINE";d.AgentHealth="critical";check("Excel version ignores monitoring results",ExcelExchange.DeviceVersion(d)==stamp);d.Role="changed";check("Excel version detects metadata edit",ExcelExchange.DeviceVersion(d)!=stamp);
        check("Excel mandatory text check",Reject(()=>ExcelExchange.Text("  ",200,true)));check("Excel cell length bounded",Reject(()=>ExcelExchange.Text(new string('a',4001),4000)));
        var user=new UserView("u","test",false,true,1,new(){["vault"]=1});check("Excel read role cannot import",RejectPermission(()=>AccessRules.Require(user,ExcelExchange.Module("vault"),2)));
    }
    static bool RejectPermission(Action action){try{action();return false;}catch(UnauthorizedAccessException){return true;}}
}
