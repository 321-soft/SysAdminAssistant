namespace SysAdminAssistant.Core;
public sealed record KscConnection(string Address,string Login,string Password,string Domain,bool Internal)
{
 public int DeviceId {get;init;}
 public string CertificateSha256 {get;init;} = "";
 public static string NormalizeCertificateSha256(string? input)
 {
  if(string.IsNullOrWhiteSpace(input))return "";
  if(input.Length>256)throw new ArgumentException("Отпечаток сертификата KSC должен содержать 64 шестнадцатеричных символа (SHA-256).");
  var hex=string.Concat(input.Where(c=>!char.IsWhiteSpace(c)&&c is not (':' or '-')));
  if(hex.Length!=64||!hex.All(Uri.IsHexDigit))throw new ArgumentException("Отпечаток сертификата KSC должен содержать 64 шестнадцатеричных символа (SHA-256).");
  return hex.ToUpperInvariant();
 }
 public KscConnection Normalize()
 {
  if(string.IsNullOrWhiteSpace(Address)||Address.Length>512||!Uri.TryCreate(Address.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!="https"||
     uri.UserInfo!=""||uri.Query!=""||uri.Fragment!=""||uri.AbsolutePath!="/"||string.IsNullOrWhiteSpace(Login)||Login.Length>256||
     Password is null||Password.Length>1024||Domain is null||Domain.Length>256)
   throw new ArgumentException("Укажите HTTPS-адрес сервера KSC с портом OpenAPI и имя пользователя. Путь /api/v1.0 добавляется автоматически.");
  return this with{Address=uri.AbsoluteUri,Login=Login.Trim(),Domain=Internal?"":Domain.Trim(),CertificateSha256=NormalizeCertificateSha256(CertificateSha256)};
 }
 public bool SameAccount(KscConnection old)=>DeviceId==old.DeviceId&&Internal==old.Internal&&
  string.Equals(Address,old.Address,StringComparison.OrdinalIgnoreCase)&&Login==old.Login&&Domain==old.Domain;
 public KscConnectionView PublicView(bool draft)=>new(Address,Login,Domain,Internal,!string.IsNullOrEmpty(Password),draft){CertificateSha256=CertificateSha256};
 public override string ToString()=>"KSC connection (credentials omitted)";
}
public sealed record KscConnectionView(string Address,string Login,string Domain,bool Internal,bool PasswordSaved,bool Draft)
{
 public string CertificateSha256 {get;init;} = "";
}
public sealed record KscEvent(long Id, string Host, string Kind, int Severity)
{
    public string? ComputerName { get; init; }
    public string? EventTitle { get; init; }
    public string? Description { get; init; }
    public string? Product { get; init; }
    public string? Published { get; init; }
    public string? Threat { get; init; }
    public string? ObjectName { get; init; }
    public string? AttackerIp { get; init; }
    public bool RdpBruteForce { get; init; }
    public bool IpBlocked { get; init; }
    public string Computer => ComputerName ?? Host;
    public string EventName => RdpBruteForce ? "Подбор пароля RDP" :
        !string.IsNullOrWhiteSpace(EventTitle) ? EventTitle :
        Kind == "GNRL_EV_VIRUS_FOUND" ? "Срабатывание защиты" : Kind;
    private string? DescriptionPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Description) || !string.IsNullOrWhiteSpace(Threat)) return null;
            var line = Description.Split(['\r', '\n'], 2, StringSplitOptions.TrimEntries)[0];
            if (line == EventName) return null;
            return line.Length > 120 ? line[..120] + "…" : line;
        }
    }

    public string Summary => string.Join(" · ", new[] { EventName, Threat, DescriptionPreview }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
}
public sealed record KscBatch(long Cursor,bool More,int Count,KscEvent[] Rows){public Dictionary<string,string> ComputerNames {get;init;}=new(StringComparer.Ordinal);}
public sealed record KscStatus(bool Enabled,long? Cursor,long Total,DateTime? LastUtc,string Error,KscEvent[] Recent,Guid Source=default){ public int DeviceId {get;init;} public bool Configured {get;init;} }
public sealed record KscActivation(bool Active);
public sealed record KscTelegramOptions(bool Enabled=false,bool Critical=true,bool Warning=true,bool Recovery=true)
{
 public bool Allows(Severity severity,bool recovered)=>Enabled&&(!recovered||Recovery)&&(severity switch{
  Severity.Critical=>Critical,Severity.Warning=>Warning,_=>false
 });
}
public sealed record KscIssueResolution(string Key);
public static class KscDeviceRole {
 public static string? BlockReason(Device? device,int ownerId)=>ownerId<=0?"Подключение KSC не связано с устройством. Выберите сервер в карточке устройства.":device==null||device.Id!=ownerId?"Устройство сервера KSC удалено или недоступно.":!device.IsKscServer?"Роль сервера Kaspersky отключена в устройстве.":!device.Enabled?"Устройство сервера Kaspersky отключено от мониторинга.":device.Type is "MikroTik" or "Коммутатор"?"Для сетевого устройства роль сервера Kaspersky недоступна.":null;
}
public static class KscCursor{
 public static async Task<KscBatch> Read(int count,long? cursor,Func<int,int,Task<KscEvent[]>> range){
  if(count<0||cursor<0)throw new InvalidDataException("KSC: неверное количество записей или позиция чтения.");
  if(count==0){
   if(cursor>0)throw new InvalidDataException("KSC: ранее непустой журнал теперь пуст. Возможна очистка журнала или изменение доступа; проверьте журнал и права учётной записи. Прежняя позиция чтения сохранена.");
   return new(0,false,0,[]);
  }
  async Task<long> At(int i){var r=await range(i,i);if(r.Length!=1||r[0].Id<=0)throw new InvalidDataException("KSC: запись по запрошенному индексу не получена. Журнал мог измениться во время чтения; позиция сохранена, запрос будет повторён.");return r[0].Id;}
  var last=await At(count-1);if(cursor==null||cursor==last)return new(last,false,count,[]);
  if(last<cursor)throw new InvalidDataException("KSC: номера событий стали меньше сохранённой позиции. Возможен сброс или замена журнала; проверьте источник. Автоматический сброс позиции не выполнен.");
  int lo=0,hi=count;while(lo<hi){int m=lo+(hi-lo)/2;if(await At(m)<=cursor)lo=m+1;else hi=m;}
  int n=Math.Min(128,count-lo);if(n<=0)throw new InvalidDataException("KSC: диапазон событий изменился во время чтения. Позиция сохранена.");
  var rows=await range(lo,lo+n-1);long pos=cursor.Value;if(rows.Length!=n)throw new InvalidDataException("Incomplete KSC page");
  foreach(var row in rows){if(row.Id<=pos||row.Id>last)throw new InvalidDataException("KSC order changed");pos=row.Id;}return new(pos,lo+n<count,count,rows);
 }
}


public sealed record KscFinding(long Id,string Code,string Title,string Published,string Product,string Threat,string ObjectName,string Sha256,string Description,KscFindingAction[] Actions){public string? AttackerIp {get;init;} public bool RdpBruteForce {get;init;} public bool IpBlocked {get;init;}}
public sealed record KscFindingAction(long Id,string Code,string Title,string Published);
public sealed record KscFindingList(KscFinding[] Events,bool Limited);
