namespace SysAdminAssistant.Core;
public sealed class InvoiceRecord:Entity
{
    public string Key{get;set;}="";public string BusinessKey{get;set;}="";public string Digest{get;set;}="";public string Json{get;set;}="";public string Status{get;set;}="Review";public int? PaymentId{get;set;}public DateTime CreatedUtc{get;set;}=DateTime.UtcNow;public int Revision{get;set;}=1;
}
public sealed class InvoiceAttachment:Entity
{
    public int DocumentId{get;set;}public string Name{get;set;}="";public string Digest{get;set;}="";public string MediaType{get;set;}="";public byte[] Data{get;set;}=[];
}
public sealed class InvoiceData
{
    public int? DuplicateOfId{get;set;}
    public float OcrConfidence{get;set;}=1;public string ExtractionError{get;set;}="";
    public bool SenderAuthenticated{get;set;}
    public string Supplier{get;set;}="";public string TaxId{get;set;}="";public string Kpp{get;set;}="";public string Number{get;set;}="";public DateTime? InvoiceDate{get;set;}public DateTime? DueDate{get;set;}public bool CalculatedDue{get;set;}public long? AmountCents{get;set;}public string Currency{get;set;}="";public string BankAccount{get;set;}="";public string BankCode{get;set;}="";public string Correspondent{get;set;}="";public string Purpose{get;set;}="";public string Sender{get;set;}="";public string Subject{get;set;}="";public DateTime ReceivedUtc{get;set;}public DateTime? SenderDateUtc{get;set;}public string MailBody{get;set;}="";public string RawText{get;set;}="";public string Extraction{get;set;}="";public string[] Issues{get;set;}=[];public string TemplateId{get;set;}="";public int? RecurringCandidateId{get;set;}public DateTime? LinkedPeriod{get;set;}
}
public sealed class InvoiceTemplate
{
    public string Id{get;set;}=Guid.NewGuid().ToString("N");public string Name{get;set;}="";public string Sender{get;set;}="";public string SubjectContains{get;set;}="";public string Supplier{get;set;}="";public string TaxId{get;set;}="";public string BankAccount{get;set;}="";public string BankCode{get;set;}="";public string Currency{get;set;}="RUB";public bool AutoCreate{get;set;}public int? DueDays{get;set;}public bool WorkingDays{get;set;}=true;public int? RecurringPaymentId{get;set;}public bool AutoLink{get;set;}public Dictionary<string,string> Anchors{get;set;}=new();
}
public sealed record InvoiceFile(int Id,string Name,string MediaType,int Bytes);
public sealed record InvoiceView(int Id,int Revision,string Status,int? PaymentId,InvoiceData Data,InvoiceFile[] Files);
public sealed record InvoiceApproval(int Id,int Revision,InvoiceData Data,int? LinkPaymentId=null,DateTime? Period=null);
public sealed record InvoiceExtract(string Text,string Method,float Confidence,string Error="");
public sealed record DeviceUpdateStamp(int DeviceId,string OsUpdatesJson,string MailUpdatesJson,string? AgentVersion=null);

public sealed record InvoicePaymentLink(int PaymentId,bool HasDocument);
