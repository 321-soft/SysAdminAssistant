namespace SysAdminAssistant.Core;
public sealed class MailSource : Entity
{
    public int EventId {get;set;}
    public string Account {get;set;}="";
    public string MessageKey {get;set;}="";
    public string Sender {get;set;}="";
    public string Subject {get;set;}="";
    public DateTime? SentAt {get;set;}
    public DateTime? ImapReceivedAt {get;set;}
    public string Body {get;set;}="";
}
