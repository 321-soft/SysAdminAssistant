using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public static class InvoiceRecognition
{
    static readonly TimeSpan Timeout=TimeSpan.FromMilliseconds(150);
    static MatchCollection Matches(string s,string pattern)=>Regex.Matches(s,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,Timeout);
    static string Unique(IEnumerable<string> values){var a=values.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();return a.Length==1?a[0]:"";}
    public static string After(string text,string label){if(label.Length==0)return "";var rows=text.Replace('\r','\n').Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);var found=new List<string>();for(int i=0;i<rows.Length;i++){int at=rows[i].IndexOf(label,StringComparison.OrdinalIgnoreCase);if(at<0)continue;var s=rows[i][(at+label.Length)..].Trim(' ',':','|','\t');if(s.Length==0&&i+1<rows.Length)s=rows[i+1];found.Add(s);}return Unique(found);}
    public static long? Amount(string value){value=value.Replace('\u00a0',' ').Trim();if(Matches(value,@"[−-]\s*\d").Count>0)return null;var m=Matches(value,@"(?<![\d\p{L}])\d{1,3}(?:[ ,.]\d{3})+(?:[.,]\d{2})?(?!\d)|(?<![\d\p{L}])\d+(?:[.,]\d{2})?(?!\d)");if(m.Count!=1)return null;var v=m[0].Value.Replace(" ","");var sep=Math.Max(v.LastIndexOf('.'),v.LastIndexOf(','));var decimalSep=sep>=0&&v.Length-sep==3;var normalized=decimalSep?new string(v[..sep].Where(char.IsDigit).ToArray())+"."+v[(sep+1)..]:new string(v.Where(char.IsDigit).ToArray());return decimal.TryParse(normalized,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var a)&&a>0&&a<=999999999999.99m?(long)(a*100):null;}
    public static DateTime? Date(string s)
    {
        var numeric=Matches(s,@"(?<!\d)(?:\d{2}[./-]\d{2}[./-]\d{4}|\d{4}-\d{2}-\d{2})(?!\d)").Select(m=>m.Value);
        var named=Matches(s,@"(?<![\d\p{L}])(?:\d{1,2}\s+[\p{L}.]+\s+\d{4}|[\p{L}.]+\s+\d{1,2},?\s+\d{4})(?!\d)").Select(m=>m.Value);
        var values=new List<DateTime>();foreach(var v in numeric.Concat(named)){DateTime d;if(DateTime.TryParseExact(v,["dd.MM.yyyy","dd/MM/yyyy","dd-MM-yyyy","yyyy-MM-dd","d MMM yyyy","d MMMM yyyy","MMM d, yyyy","MMMM d, yyyy","MMM d yyyy","MMMM d yyyy"],CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces,out d)||DateTime.TryParseExact(v,["d MMMM yyyy","d MMM yyyy"],CultureInfo.GetCultureInfo("ru-RU"),DateTimeStyles.AllowWhiteSpaces,out d))values.Add(DateTime.SpecifyKind(d.Date,DateTimeKind.Unspecified));}var unique=values.Distinct().Take(2).ToArray();return unique.Length==1?unique[0]:null;
    }
    public static bool Inn(string s){if(s.Length is not(10 or 12)||!s.All(char.IsAsciiDigit))return false;int Check(int[] w){int sum=0;for(int i=0;i<w.Length;i++)sum+=(s[i]-'0')*w[i];return sum%11%10;}return s.Length==10?Check([2,4,10,3,5,9,4,6,8])==s[9]-'0':Check([7,2,4,10,3,5,9,4,6,8])==s[10]-'0'&&Check([3,7,2,4,10,3,5,9,4,6,8])==s[11]-'0';}
    public static bool Bank(string account,string bik){if(account.Length!=20||bik.Length!=9||!account.All(char.IsAsciiDigit)||!bik.All(char.IsAsciiDigit))return false;var s=bik[^3..]+account;int sum=0;int[] w=[7,1,3];for(int i=0;i<s.Length;i++)sum+=(s[i]-'0')*w[i%3];return sum%10==0;}
    public static string Currency(string s){var values=Matches(s,@"(?<![A-Z])(?:RUB|RUR|USD|EUR|AED|CNY|KZT|BYN|GBP)(?![A-Z])|₽|руб(?:\.|лей)?|\$|€").Select(m=>m.Value.ToUpperInvariant() switch{"RUR" or "₽" or "РУБ" or "РУБ." or "РУБЛЕЙ"=>"RUB","$"=>"USD","€"=>"EUR",var x=>x});return Unique(values);}
    public static InvoiceData Parse(InvoiceExtract extraction,string sender,string subject,DateTime received,InvoiceTemplate? template,int weekendMask)
    {
        var text=extraction.Text.Replace('\u00a0',' ');if(text.Length>100000)text=text[..100000];var issues=new List<string>();
        string Field(string name,params string[] defaults){if(template?.Anchors.TryGetValue(name,out var anchor)==true){var split=anchor.Split("=>",2,StringSplitOptions.TrimEntries);var value=After(text,split[0]);if(split.Length==2&&split[1].Length>0){var at=value.IndexOf(split[1],StringComparison.OrdinalIgnoreCase);if(at>=0)value=value[..at].Trim();}return value;}return Unique(defaults.Select(a=>After(text,a)));}
        string Digits(string value,int min,int max)=>Unique(Matches(value.Replace(" ",""),$@"(?<!\d)\d{{{min},{max}}}(?!\d)").Select(m=>m.Value));
        var amountText=Field("Amount","Всего к оплате","Итого к оплате","Total due","Amount due","Grand total");
        var invoiceHeader=Field("Number","Счет на оплату №","Счёт на оплату №","Счет №","Счёт №","Invoice #","Invoice No:");
        var number=invoiceHeader.Length==0?"":Regex.Split(invoiceHeader,@"\s+(?:от|dated|date)\b",RegexOptions.IgnoreCase,Timeout)[0].Trim();
        var supplier=Field("Supplier","Поставщик:","Получатель:","Продавец:","Supplier:","Beneficiary:");supplier=Regex.Split(supplier,@"\s+(?:ИНН|КПП)\b",RegexOptions.IgnoreCase,Timeout)[0];
        // Order-invoice headings may place the issue date on the following line.
        // Do not use a payment-purpose reference as an alternative heading.
        var orderHeaders=Matches(text,@"(?m)^\s*Сч[её]т[ -]заказ\s*№\s*([A-Za-zА-Яа-я0-9/-]+)[^\r\n]*(?:\r?\n\s*от\s*(\d{2}[./-]\d{2}[./-]\d{4}))?");
        var orderNumber=Unique(orderHeaders.Select(m=>m.Groups[1].Value));
        var orderDate=Date(Unique(orderHeaders.Select(m=>m.Groups[2].Value)));
        if(orderDate==null&&orderNumber.Length>0){
            var dateLines=Matches(text,@"(?im)^\s*от\s+(\d{2}[./-]\d{2}[./-]\d{4})\s*\|\s*оплатить\s+до").Select(m=>m.Groups[1].Value);
            orderDate=Date(Unique(dateLines));
        }
        if(invoiceHeader.Length==0&&orderNumber.Length>0)number=orderNumber;
        // A payment-order table describes the recipient, not the payer named above.
        // Restrict fallback to an explicitly labelled payment-order section.
        var paymentSection=Matches(text,@"(?is)образец\s+заполнения\s+плат[её]жного\s+поручения(?<body>.{0,5000})");
        var paymentText=paymentSection.Count==1?paymentSection[0].Groups["body"].Value:"";
        string PaymentDigits(string label,int count)=>Unique(Matches(paymentText,$@"{label}\s*[:№]?\s*(\d{{{count}}})(?!\d)").Select(m=>m.Groups[1].Value));
        var paymentInn=PaymentDigits("ИНН",10);
        var paymentKpp=PaymentDigits("КПП",9);
        var paymentBik=PaymentDigits("БИК",9);
        var accounts=Matches(paymentText,@"(?<!\d)(?:301|40[0-9])\d{17}(?!\d)").Select(m=>m.Value).Distinct().ToArray();
        var paymentAccount=Unique(accounts.Where(a=>a.StartsWith("40",StringComparison.Ordinal)));
        var paymentCorrespondent=Unique(accounts.Where(a=>a.StartsWith("301",StringComparison.Ordinal)));
        var paymentRecipient=Unique(Matches(paymentText,@"(?im)^\s*((?:ООО|000)\s*[«\x22“][^\r\n»\x22”]{1,100}[»\x22”])\s*\r?\n\s*Получатель\b").Select(m=>Regex.Replace(m.Groups[1].Value,@"^000\b","ООО",RegexOptions.None,Timeout)));
        if(paymentRecipient.Length>0&&template?.Anchors.ContainsKey("Supplier")!=true)supplier=paymentRecipient;
        var d=new InvoiceData{OcrConfidence=extraction.Confidence,ExtractionError=extraction.Error,Supplier=supplier,TaxId=Digits(Field("TaxId","ИНН"),10,12),Kpp=Digits(Field("Kpp","КПП"),9,9),Number=number,InvoiceDate=Date(Field("InvoiceDate","Дата счета:","Дата счёта:","Invoice date:","Дата выставления:"))??Date(invoiceHeader)??orderDate,DueDate=Date(Field("DueDate","Оплатить до","Срок оплаты:","Due date:","Pay by:")),AmountCents=Amount(amountText),Currency=Currency(amountText),BankAccount=Digits(Field("BankAccount","Расчетный счет:","Расчётный счёт:","Р/с:","Р/с "),20,20),BankCode=Digits(Field("BankCode","БИК"),9,9),Correspondent=Digits(Field("Correspondent","К/с:","Корреспондентский счет:"),20,20),Sender=sender,Subject=subject,ReceivedUtc=received,RawText=text,Extraction=extraction.Method,TemplateId=template?.Id??"",RecurringCandidateId=template?.RecurringPaymentId};
        // Only use an unambiguous and internally valid set from the recipient table.
        // When fields disagree, leave a review requirement rather than silently choosing.
        if(paymentInn.Length>0&&paymentBik.Length>0&&paymentAccount.Length>0&&Inn(paymentInn)&&Bank(paymentAccount,paymentBik)){
            if(template?.Anchors.ContainsKey("TaxId")!=true)d.TaxId=paymentInn;
            if(template?.Anchors.ContainsKey("Kpp")!=true&&paymentKpp.Length>0)d.Kpp=paymentKpp;
            if(template?.Anchors.ContainsKey("BankCode")!=true)d.BankCode=paymentBik;
            if(template?.Anchors.ContainsKey("BankAccount")!=true)d.BankAccount=paymentAccount;
            if(template?.Anchors.ContainsKey("Correspondent")!=true&&paymentCorrespondent.Length>0)d.Correspondent=paymentCorrespondent;
        }else if(paymentText.Length>0){
            if(template?.Anchors.ContainsKey("TaxId")!=true)d.TaxId="";
            if(template?.Anchors.ContainsKey("Kpp")!=true)d.Kpp="";
            issues.Add("Неоднозначные реквизиты получателя в платёжной таблице");
        }
        if(d.Currency=="")d.Currency=Currency(Field("Currency","Валюта:","Currency:"));if(d.Currency=="")d.Currency=Currency(text);
        if(d.Currency!="RUB"){var account=Field("BankAccount","Account number:","Account No:","IBAN:");var bank=Field("BankCode","SWIFT:","BIC:","Bank code:");if(account.Length>0)d.BankAccount=Unique(Matches(account,@"[A-Z]{2}\d{2}[A-Z0-9 ]{10,32}|(?<!\d)\d{6,34}(?!\d)").Select(m=>m.Value.Replace(" ","")));if(bank.Length>0)d.BankCode=bank.Trim();}
        if(template!=null){if(d.Supplier.Length>0&&template.Supplier.Length>0&&new string(d.Supplier.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray())!=new string(template.Supplier.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray()))issues.Add("Поставщик отличается от подтверждённого");if(template.Currency!=""&&d.Currency!=template.Currency)issues.Add("Валюта отличается от подтверждённой");if(d.Supplier=="")d.Supplier=template.Supplier;if(d.TaxId==""&&paymentText.Length==0&&Inn(template.TaxId)&&text.Contains(template.TaxId,StringComparison.Ordinal))d.TaxId=template.TaxId;if(d.Currency=="")issues.Add("Валюта не найдена в документе; шаблон не подменяет её");if(d.TaxId!=template.TaxId&&template.TaxId!="")issues.Add("ИНН отличается от подтверждённого");if(d.BankAccount!=template.BankAccount||d.BankCode!=template.BankCode)issues.Add("Новые или изменённые банковские реквизиты");}
        if(d.DueDate==null&&d.InvoiceDate!=null&&template?.DueDays is int days){var date=d.InvoiceDate.Value;for(int i=0;i<days;){date=date.AddDays(1);if(!template.WorkingDays||PaymentCalendar.EffectiveDueDate(date,weekendMask)==date)i++;}d.DueDate=date;d.CalculatedDue=true;}
        if(Matches(subject+"\n"+text,@"credit note|корректировочн|кредит-нот|возврат средств").Count>0)issues.Add("Корректировка / возврат: проверьте вручную");
        var paidText=Regex.Replace(subject+"\n"+text,@"\bне\s+оплачен\w*\b","",RegexOptions.IgnoreCase,Timeout);if(Matches(paidText,@"\b(?:оплачен[ао]?|paid in full|payment received|invoice paid)\b").Count>0)issues.Add("Есть отметка об оплате: проверьте, нужен ли новый платёж");
        if(d.Supplier.Length is <1 or >200)issues.Add("Не найден однозначный поставщик");if(d.Number.Length is <1 or >100)issues.Add("Не найден номер счёта");if(d.AmountCents==null)issues.Add("Не найдена однозначная итоговая сумма");if(d.Currency=="")issues.Add("Не найдена однозначная валюта");if(d.InvoiceDate==null)issues.Add("Не найдена дата выставления");if(d.DueDate==null)issues.Add("Нет срока оплаты или правила его расчёта");if(d.DueDate<d.InvoiceDate)issues.Add("Срок оплаты раньше даты счёта");if(d.Currency=="RUB"&&(!Inn(d.TaxId)||!Bank(d.BankAccount,d.BankCode)))issues.Add("Не прошла проверка ИНН / банковского счёта");if(d.BankAccount.Length==0||d.BankCode.Length==0)issues.Add("Не найдены банковские реквизиты");
        if(template==null||!template.AutoCreate)issues.Add("Нет подтверждённого шаблона автоматического добавления");if(extraction.Error!="")issues.Add(extraction.Error);if(extraction.Confidence<.9f)issues.Add("Распознавание скана требует проверки");d.Issues=issues.Distinct().ToArray();return d;
    }
    public static bool IsCandidate(string subject,string text)=>Matches(subject+"\n"+text,@"\b(?:invoice|сч[её]т|bill)\b").Count>0;
    public static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    public static string BusinessKey(InvoiceData d)=>d.Supplier.Length>0&&d.Number.Length>0&&d.InvoiceDate!=null&&d.AmountCents!=null&&d.Currency!=""?Hash((d.TaxId.Length>0?d.TaxId:d.Supplier.ToUpperInvariant())+"|"+d.Number.ToUpperInvariant()+"|"+d.InvoiceDate.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"|"+d.AmountCents+"|"+d.Currency):"";
}
