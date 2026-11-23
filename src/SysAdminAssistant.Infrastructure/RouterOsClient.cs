using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Net.Sockets;
using System.Text;

namespace SysAdminAssistant.Infrastructure;

public sealed record RouterSnapshot(Dictionary<string, string> Identity, Dictionary<string, string> Resource,
    Dictionary<string, string> Board, IReadOnlyList<Dictionary<string, string>> Interfaces,IReadOnlyList<Dictionary<string,string>>? Logs=null,string LogError="",SysAdminAssistant.Core.RouterAuditConfig? AuditConfig=null,Dictionary<string,string>? Clock=null);
// RouterOS API-SSL, with standard certificate validation. RouterOS >= 6.43 login protocol.
public sealed class RouterOsClient(RemoteRepository? remote = null) : IRouterReadSession
{
    public bool IncludeAuditInSnapshot {get;init;}=true;
    public bool IncludeClockInSnapshot {get;init;}
    private readonly TcpClient client = new();
    private SslStream? stream;
    private string targetHost = "";
    private SysAdminAssistant.Core.RouterAuditConfig? auditCache;
    private readonly SemaphoreSlim gate = new(1);
    public async Task ConnectAsync(RouterSettings settings, CancellationToken token = default)
    {
        if (remote != null) { targetHost = settings.Host; return; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        bool certificateRejected=false,tlsReady=false;
        try {
            if(string.IsNullOrWhiteSpace(settings.Host)||string.IsNullOrWhiteSpace(settings.User))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите адрес и учётную запись MikroTik в настройках подключения"));
            var pin=NormalizePin(settings.CertificateSha256);
            client.Client.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.KeepAlive,true);
            await client.ConnectAsync(settings.Host, settings.Port, timeout.Token);
            stream = new SslStream(client.GetStream(), false,(_,certificate,_,errors)=>{var accepted=certificate!=null && (pin.Length>0?Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()))==pin:errors==SslPolicyErrors.None);certificateRejected=!accepted;return accepted;});
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = settings.Host }, timeout.Token);
            tlsReady=true;
            await CommandAsync(["/login", "=name=" + settings.User, "=password=" + LocalSecret.Unprotect(settings.ProtectedPassword)], timeout.Token);
        }
        catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new RouterConnectionException(global::SysAdminAssistant.Core.ProductText.Get("MikroTik: превышено время подключения. Проверьте адрес, порт API-SSL и доступ от сервера приложения."));}
        catch(Exception e)when(e is AuthenticationException or IOException && !tlsReady) {
            if(certificateRejected)throw new RouterConnectionException(global::SysAdminAssistant.Core.ProductText.Get("MikroTik: TLS-сертификат не прошёл проверку. Используйте доверенный сертификат с подходящим DNS-именем либо укажите проверенный SHA-256 сертификата."),RouterFailure.Certificate,e);
            throw new RouterConnectionException(global::SysAdminAssistant.Core.ProductText.Get("MikroTik: TLS-соединение не установлено. Проверьте API-SSL, сертификат с закрытым ключом и поддерживаемые протоколы. HTTPS REST использует другой протокол."),RouterFailure.Tls,e);
        }
        catch(SocketException e){throw new RouterConnectionException("MikroTik: "+(e.SocketErrorCode==SocketError.HostNotFound?"DNS-имя не найдено.":e.SocketErrorCode==SocketError.ConnectionRefused?"порт API-SSL недоступен или служба выключена.":"сетевая ошибка подключения ("+e.SocketErrorCode+")."));}
        catch(CryptographicException){throw new RouterConnectionException(global::SysAdminAssistant.Core.ProductText.Get("MikroTik: сохранённый пароль недоступен службе. Сохраните пароль повторно в настройках подключения."));}
        catch(IOException){throw new RouterConnectionException(global::SysAdminAssistant.Core.ProductText.Get("MikroTik закрыл соединение. Проверьте API-SSL, сертификат и порт; HTTPS REST использует другой протокол."));}
        catch(InvalidOperationException e)when(e is not RouterConnectionException){throw new RouterConnectionException(global::SysAdminAssistant.Core.ProductText.Get("MikroTik: вход не выполнен. Проверьте логин, пароль и разрешение api у учётной записи. Ответ: ")+e.Message);}
    }
    public Task<SysAdminAssistant.Core.RouterAuditConfig> AuditAsync(CancellationToken token)=>RouterAuditReader.ReadAsync(this,token);
    public static string NormalizePin(string? value)
    {
        var pin=(value??"").Replace(":","").Replace(" ","").Trim().ToUpperInvariant();
        if(pin.Length>0 && (pin.Length!=64||!pin.All(Uri.IsHexDigit)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("SHA-256 сертификата MikroTik должен содержать 64 шестнадцатеричных символа"));
        return pin;
    }
    public static void RequireReadCommand(string[] words)
    {
        if(words.Length>0&&RouterAuditReader.Commands.Values.Contains(words[0])&&words.Skip(1).All(w=>w.StartsWith("=.proplist=",StringComparison.Ordinal)||(words[0] is "/ip/firewall/address-list/print" or "/ipv6/firewall/address-list/print")&&w.StartsWith("?list=",StringComparison.Ordinal)))return;
        if(words.Skip(1).Any(w=>!w.StartsWith("=",StringComparison.Ordinal)||w.Length<2)||words.Length==0 || words[0] is not ("/login" or "/system/identity/print" or "/system/clock/print" or "/system/resource/print" or "/system/routerboard/print" or "/interface/print" or "/ip/ipsec/policy/print" or "/ip/firewall/filter/print" or "/log/print"))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("MikroTik: доступны только команды чтения состояния"));
    }
    private static bool IsInterface(Dictionary<string,string> row)=>!string.IsNullOrWhiteSpace(row.GetValueOrDefault("name"));
    public async Task<RouterSnapshot> SnapshotAsync(CancellationToken token = default)
    {
        if (remote != null) return await remote.CommandAsync<RouterSnapshot>("router-snapshot", new { Host = targetHost }, token);
        var identity = (await CommandAsync(["/system/identity/print"], token)).FirstOrDefault() ?? new();
        var resource = (await CommandAsync(["/system/resource/print"], token)).FirstOrDefault() ?? new();
        Dictionary<string, string> board;
        try { board = (await CommandAsync(["/system/routerboard/print"], token)).FirstOrDefault() ?? new(); }
        catch (InvalidOperationException e) when(e is not RouterConnectionException) { board = new(); } // CHR has no RouterBOARD firmware.
        const string interfaceFields="=.proplist=.id,name,type,running,disabled,mtu,rx-byte,tx-byte,comment";
        Dictionary<string,string>[] interfaces;
        try { interfaces=(await CommandAsync(["/interface/print","=stats=",interfaceFields],token)).Where(IsInterface).ToArray(); }
        catch(InvalidOperationException e) when(e is not RouterConnectionException) { interfaces=[]; }
        if(interfaces.Length==0)
            interfaces=(await CommandAsync(["/interface/print",interfaceFields],token)).Where(IsInterface).ToArray();
        // An empty reply is not proof that every port disappeared. Keep the previous
        // snapshot and let the monitor record the unsuccessful poll.
        if(interfaces.Length==0)throw new RouterConnectionException("MikroTik: API-SSL не вернул список интерфейсов ни со статистикой, ни без неё.");
        IReadOnlyList<Dictionary<string,string>> logs=[];string logError="";
        try{logs=await CommandAsync(["/log/print","=.proplist=.id,time,topics,message"],token);}catch(InvalidOperationException e)when(e is not RouterConnectionException){logError=e.Message;}
        Dictionary<string,string>? clock=null;
        if(IncludeClockInSnapshot)try{clock=(await CommandAsync(["/system/clock/print","=.proplist=date,time,gmt-offset"],token)).FirstOrDefault();}catch(InvalidOperationException e)when(e is not RouterConnectionException){}
        if(IncludeAuditInSnapshot&&(auditCache==null||DateTime.UtcNow-auditCache.CapturedUtc>TimeSpan.FromMinutes(15)))auditCache=await RouterAuditReader.ReadAsync(this,token);
        return new(identity, resource, board, interfaces,logs,logError,IncludeAuditInSnapshot?auditCache:null,clock);
    }
    public async Task<IReadOnlyList<Dictionary<string, string>>> GetFirewallAsync(CancellationToken token = default) =>
        remote != null
            ? await remote.CommandAsync<Dictionary<string, string>[]>("router-firewall-list", new { Host = targetHost }, token)
            : await CommandAsync(["/ip/firewall/filter/print", "=.proplist=.id,chain,action,src-address,dst-address,protocol,dst-port,disabled,comment,packets,bytes"], token);
    public Task<IReadOnlyList<Dictionary<string,string>>> CommandAsync(string[] words,CancellationToken token=default)=>ExecuteAsync(words,token,null,8000000,100000);
    public Task<IReadOnlyList<Dictionary<string,string>>> ReadAddressListAsync(string command,string name,Action<Dictionary<string,string>> consume,CancellationToken token)=>command is "/ip/firewall/address-list/print" or "/ipv6/firewall/address-list/print"?ExecuteAsync([command,"=.proplist=list,address,dynamic,disabled,timeout","?list="+name],token,consume,64000000,500000):throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Только чтение address-list"));
    private async Task<IReadOnlyList<Dictionary<string,string>>> ExecuteAsync(string[] words,CancellationToken token,Action<Dictionary<string,string>>? consume,long byteLimit,int rowLimit)
    {
        RequireReadCommand(words);
        if (stream == null) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нет соединения RouterOS"));
        await gate.WaitAsync(token);
        var callerToken=token;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30)); token = timeout.Token;
        try
        {
            foreach (var word in words)
            {
                var bytes = Encoding.UTF8.GetBytes(word);
                await stream.WriteAsync(EncodeLength(bytes.Length), token); await stream.WriteAsync(bytes, token);
            }
            await stream.WriteAsync(new byte[] { 0 }, token); await stream.FlushAsync(token);
            var rows = new List<Dictionary<string, string>>(); string? error = null;long responseBytes=0;int rowCount=0;
            while (true)
            {
                var sentence = new List<string>();
                while (true)
                {
                    var length = await ReadLengthAsync(token); if (length == 0) break;
                    responseBytes+=length;if(responseBytes>byteLimit||sentence.Count>4096)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Превышен предел ответа RouterOS"));
                    if (length > 4_000_000) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой ответ RouterOS"));
                    var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token); sentence.Add(Encoding.UTF8.GetString(bytes));
                }
                if (sentence.Count == 0) continue;
                var row = new Dictionary<string, string>();
                foreach (var word in sentence.Skip(1).Where(x => x.StartsWith('=')))
                { var separator = word.IndexOf('=', 1); if (separator > 0) row[word[1..separator]] = word[(separator + 1)..]; }
                if (sentence[0] == "!fatal") throw new IOException(row.GetValueOrDefault("message", "RouterOS закрыл соединение"));
                if (sentence[0] == "!trap") error = row.GetValueOrDefault("message", "Ошибка RouterOS");
                if(sentence[0]=="!re"){if(++rowCount>rowLimit)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком много строк RouterOS"));if(consume==null)rows.Add(row);else consume(row);}
                if (sentence[0] == "!done") { if (error != null) throw new InvalidOperationException(error); if (row.Count > 0) rows.Add(row); return rows; }
            }
        }
        catch(OperationCanceledException)when(!callerToken.IsCancellationRequested){throw new RouterConnectionException(global::SysAdminAssistant.Core.ProductText.Get("MikroTik: превышено время чтения состояния"));}
        catch(IOException){throw new RouterConnectionException(global::SysAdminAssistant.Core.ProductText.Get("MikroTik: соединение оборвано при чтении состояния"));}
        finally { gate.Release(); }
    }
    public static byte[] EncodeLength(int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        uint value = (uint)length;
        if (value < 0x80) return [(byte)value];
        if (value < 0x4000) { value |= 0x8000; return [(byte)(value >> 8), (byte)value]; }
        if (value < 0x200000) { value |= 0xC00000; return [(byte)(value >> 16), (byte)(value >> 8), (byte)value]; }
        if (value < 0x10000000) { value |= 0xE0000000; return [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]; }
        return [0xF0, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }
    private async Task<int> ReadLengthAsync(CancellationToken token)
    {
        var first = await ByteAsync(token);
        uint value;
        int remaining;
        if (first < 0x80) return first;
        if (first < 0xC0) { value = (uint)(first & 0x3F); remaining = 1; }
        else if (first < 0xE0) { value = (uint)(first & 0x1F); remaining = 2; }
        else if (first < 0xF0) { value = (uint)(first & 0x0F); remaining = 3; }
        else if (first == 0xF0) { value = 0; remaining = 4; }
        else throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестный управляющий байт RouterOS"));
        for (int i = 0; i < remaining; i++) value = (value << 8) | (uint)await ByteAsync(token);
        if (value > int.MaxValue) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неверная длина RouterOS")); return (int)value;
    }
    private async Task<int> ByteAsync(CancellationToken token) { var b = new byte[1]; await stream!.ReadExactlyAsync(b, token); return b[0]; }
    public async ValueTask DisposeAsync() { if (stream != null) await stream.DisposeAsync(); client.Dispose(); gate.Dispose(); }
}

public enum RouterFailure { Protocol, Certificate, Tls }
public sealed class RouterConnectionException(string message,RouterFailure failure=RouterFailure.Protocol,Exception? inner=null):InvalidOperationException(message,inner)
{
    public RouterFailure Failure {get;}=failure;
}
