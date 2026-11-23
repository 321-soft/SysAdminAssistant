using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record ExcelRequest(byte[] File,bool UpdateExisting=false);
public sealed record ExcelPreviewRow(int Row,string Name,string Operation,string Error);
public sealed record ExcelPreview(ExcelPreviewRow[] Rows) { public bool Valid=>Rows.Length>0&&Rows.All(x=>x.Error.Length==0); }
public sealed record ExcelFile(byte[] File,string Name);
public sealed record ExcelLine(int Row,string[] Values,bool LegacyVault=false);
public static class ExcelExchange
{
    public const int MaxBytes=8_000_000,MaxRows=2000;
    public static string Module(string kind)=>kind switch{"devices"=>"devices","employees"=>"inventory","vault"=>"vault",_=>throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестный раздел Excel"))};
    public static string[] Headers(string kind)=>kind switch {
        "devices"=>["ID","Версия","Имя","Тип","Адрес","Площадка","Роль","Активно"],
        "employees"=>["ID","Версия","ФИО","Табельный номер","Отдел","Должность","Контакт","Работает"],
        "vault"=>["ID","Версия","Название","Логин","Пароль","Примечание","Устройство ID","Сервис ID"],
        _=>throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестный раздел Excel"))};
    public static string DeviceVersion(Device d)=>Convert.ToHexString(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{d.IsKscServer,d.ConnectionsJson,d.Name,d.Type,d.Host,d.Site,d.Role,d.Enabled,d.EquipmentFamily,d.MaintenanceUntil,d.MailSystem})));
    public static int Id(string value,bool blank=true)=>value.Length==0&&blank?0:int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var id)&&id>0?id:throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("ID / версия должны быть положительным целым числом"));
    public static int Revision(string value)=>int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var v)&&v>=0?v:throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите версию из выгрузки"));
    public static bool Flag(string value)=>value.Trim().ToLowerInvariant() switch{"" or "да" or "true" or "1"=>true,"нет" or "false" or "0"=>false,_=>throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("В логическом поле укажите Да или Нет"))};
    public static string Text(string s,int max,bool required=false){if(s.Length>max||required&&string.IsNullOrWhiteSpace(s))throw new ArgumentException(required?"Обязательное поле пусто или слишком длинное":"Превышена длина поля");return s;}
    private static string Decode(string s)=>System.Text.RegularExpressions.Regex.Replace(s,@"_x([0-9A-Fa-f]{4})_",m=>((char)int.Parse(m.Groups[1].Value,NumberStyles.HexNumber,CultureInfo.InvariantCulture)).ToString());
    private static string Encode(string s){s=System.Text.RegularExpressions.Regex.Replace(s,@"_x[0-9A-Fa-f]{4}_",m=>"_x005F_"+m.Value[1..]);return string.Concat(s.Select(c=>XmlConvert.IsXmlChar(c)&&c!='\r'?c.ToString():"_x"+((int)c).ToString("X4",CultureInfo.InvariantCulture)+"_"));}
    private static readonly XNamespace Ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static ExcelLine[] Read(byte[] bytes,string kind)
    {
        try{return ReadCore(bytes,kind);}
        catch(Exception e)when(e is XmlException or InvalidDataException or OverflowException){throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось прочитать XLSX. Скачайте шаблон и сохраните книгу без формул и макросов"),e);}
    }
    private static ExcelLine[] ReadCore(byte[] bytes,string kind)
    {
        if(bytes is null||bytes.Length==0||bytes.Length>MaxBytes)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Размер XLSX: от 1 байта до 8 МБ"));
        using var stream=new MemoryStream(bytes,false);using var zip=new ZipArchive(stream,ZipArchiveMode.Read);
        if(zip.Entries.Count>256||zip.Entries.Sum(x=>x.Length)>48_000_000||zip.Entries.Select(x=>x.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=zip.Entries.Count)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой или неоднозначный XLSX"));
        if(zip.Entries.Any(x=>x.FullName.Contains("vbaProject",StringComparison.OrdinalIgnoreCase)||x.FullName.Contains("externalLinks/",StringComparison.OrdinalIgnoreCase)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Используйте XLSX без макросов и внешних ссылок"));
        XDocument Xml(string name){var e=zip.GetEntry(name)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Повреждён XLSX: нет обязательного компонента"));using var data=e.Open();using var xml=XmlReader.Create(data,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=24_000_000});return XDocument.Load(xml);}
        var workbook=Xml("xl/workbook.xml");var sheet=workbook.Descendants(Ns+"sheet").FirstOrDefault(x=>(string?)x.Attribute("name")=="Данные")??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Нет листа «Данные». Скачайте шаблон в программе"));
        XNamespace relNs="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var rel=Xml("xl/_rels/workbook.xml.rels").Root!.Elements().SingleOrDefault(x=>(string?)x.Attribute("Id")== (string?)sheet.Attribute(relNs+"id"))??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Повреждена ссылка на лист"));
        var target=(string?)rel.Attribute("Target")??"";
        if(rel.Attribute("TargetMode")!=null||target.Contains("..")||target.Contains('\\')||target.Contains(':'))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Недопустимая ссылка XLSX"));
        var path=target.StartsWith('/')?target.TrimStart('/'):("xl/"+target);
        if(!path.StartsWith("xl/worksheets/",StringComparison.Ordinal))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Недопустимый лист XLSX"));
        string[] strings=zip.GetEntry("xl/sharedStrings.xml")==null?[]:Xml("xl/sharedStrings.xml").Descendants(Ns+"si").Select(x=>string.Concat(x.Descendants(Ns+"t").Select(t=>t.Value))).ToArray();
        bool legacyVault=false;var headers=Headers(kind);var dataRows=new List<ExcelLine>();var rowIds=new HashSet<int>();
        foreach(var row in Xml(path).Descendants(Ns+"sheetData").Elements(Ns+"row")){
            if(!int.TryParse((string?)row.Attribute("r"),out var index)||index<1||index>MaxRows+1||!rowIds.Add(index))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Не более 2000 строк данных; номера строк не должны повторяться"));
            var values=new string[headers.Length];Array.Fill(values,"");var cells=new HashSet<int>();
            foreach(var c in row.Elements(Ns+"c")){
                var addr=(string?)c.Attribute("r")??"";var letters=new string(addr.TakeWhile(char.IsAsciiLetter).ToArray()).ToUpperInvariant();int col=0;foreach(var ch in letters){if(col>1000)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Слишком много столбцов"));col=col*26+ch-'A'+1;}
                if(letters.Length==0||addr[letters.Length..]!=index.ToString(CultureInfo.InvariantCulture)||!cells.Add(col))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Повреждены адреса ячеек"));
                if(c.Element(Ns+"f")!=null)throw new ArgumentException($"Строка {index}: формулы запрещены; вставьте значения");
                var type=(string?)c.Attribute("t");var value=type=="inlineStr"?string.Concat(c.Element(Ns+"is")?.Descendants(Ns+"t").Select(x=>x.Value)??[]):c.Element(Ns+"v")?.Value??"";
                if(type=="s"){if(!int.TryParse(value,out var si)||si<0||si>=strings.Length)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Повреждены строки XLSX"));value=strings[si];}
                if(type=="e")throw new ArgumentException($"Строка {index}: ячейка содержит ошибку Excel");
                value=Decode(value);
                if(value.Length>10000)throw new ArgumentException($"Строка {index}: слишком длинная ячейка");
                if(col>headers.Length){if(value.Length>0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Лишние столбцы. Используйте скачанный шаблон"));continue;}
                values[col-1]=value;
            }
            if(index==1){legacyVault=kind=="vault"&&values[^1]==""&&values.Take(headers.Length-1).SequenceEqual(headers.Take(headers.Length-1));if(!legacyVault&&!values.SequenceEqual(headers))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Заголовки не совпадают с шаблоном этого раздела"));}
            else if(values.Any(x=>x.Length>0))dataRows.Add(new(index,values,legacyVault));
        }
        if(!rowIds.Contains(1))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Нет заголовков шаблона"));return dataRows.OrderBy(x=>x.Row).ToArray();
    }
    // Explicit inline strings, including leading '=' and whitespace: Excel never executes exported data.
    public static byte[] Write(string kind,IEnumerable<string[]> rows)
    {
        var all=rows.ToArray();if(all.Length>MaxRows)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("В одной выгрузке не более 2000 записей"));
        using var ms=new MemoryStream();using(var zip=new ZipArchive(ms,ZipArchiveMode.Create,true)){
            void Add(string name,XElement xml){using var entry=zip.CreateEntry(name).Open();xml.Save(entry);}
            XNamespace ct="http://schemas.openxmlformats.org/package/2006/content-types",pr="http://schemas.openxmlformats.org/package/2006/relationships",rr="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Add("[Content_Types].xml",new XElement(ct+"Types",new XElement(ct+"Default",new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(ct+"Default",new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")),new XElement(ct+"Override",new XAttribute("PartName","/xl/workbook.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),new XElement(ct+"Override",new XAttribute("PartName","/xl/worksheets/sheet1.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
            XElement Rel(string id,string type,string target)=>new(pr+"Relationship",new XAttribute("Id",id),new XAttribute("Type","http://schemas.openxmlformats.org/officeDocument/2006/relationships/"+type),new XAttribute("Target",target));
            Add("_rels/.rels",new XElement(pr+"Relationships",Rel("rId1","officeDocument","xl/workbook.xml")));
            Add("xl/workbook.xml",new XElement(Ns+"workbook",new XAttribute(XNamespace.Xmlns+"r",rr),new XElement(Ns+"sheets",new XElement(Ns+"sheet",new XAttribute("name","Данные"),new XAttribute("sheetId",1),new XAttribute(rr+"id","rId1")))));
            Add("xl/_rels/workbook.xml.rels",new XElement(pr+"Relationships",Rel("rId1","worksheet","worksheets/sheet1.xml")));
            var matrix=new[]{Headers(kind)}.Concat(all).ToArray();
            Add("xl/worksheets/sheet1.xml",new XElement(Ns+"worksheet",new XElement(Ns+"sheetViews",new XElement(Ns+"sheetView",new XAttribute("workbookViewId",0),new XElement(Ns+"pane",new XAttribute("ySplit",1),new XAttribute("topLeftCell","A2"),new XAttribute("state","frozen")))),new XElement(Ns+"cols",Enumerable.Range(1,Headers(kind).Length).Select(i=>new XElement(Ns+"col",new XAttribute("min",i),new XAttribute("max",i),new XAttribute("width",i==2?18:26),new XAttribute("customWidth",1)))),new XElement(Ns+"sheetData",matrix.Select((row,i)=>new XElement(Ns+"row",new XAttribute("r",i+1),row.Select((v,j)=>new XElement(Ns+"c",new XAttribute("r",((char)('A'+j)).ToString()+(i+1)),new XAttribute("t","inlineStr"),new XElement(Ns+"is",new XElement(Ns+"t",new XAttribute(XNamespace.Xml+"space","preserve"),Encode(v)))))))),new XElement(Ns+"autoFilter",new XAttribute("ref","A1:"+(char)('A'+Headers(kind).Length-1)+(matrix.Length)))));
        }var bytes=ms.ToArray();if(bytes.Length>MaxBytes)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выгрузка превышает 8 МБ; сократите записи"));return bytes;
    }
    public static byte[] Template(string kind){_=Module(kind);using var s=typeof(ExcelExchange).Assembly.GetManifestResourceStream("Excel."+kind+".xlsx")??throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Шаблон не включён в сборку"));using var ms=new MemoryStream();s.CopyTo(ms);return ms.ToArray();}
}
