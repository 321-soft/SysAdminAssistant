using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;

// Disposable SQLite view in RAM only; the authoritative store and mutations live on the server.
public sealed class RemoteRepository : IDisposable
{
    private readonly HttpClient http;
    private readonly SessionRenewal renewal = new();
    private string? accessToken;
    private readonly ConnectionLease connectionLease=new();
    public bool RememberSession {get;set;}
    public bool InventoryOpen {get;set;}
    public bool RequiresSignIn=>string.IsNullOrEmpty(Volatile.Read(ref accessToken))||RememberSession&&connectionLease.Expired;
    public Func<CancellationToken, Task<bool>>? SessionExpired { get; set; }
    public void AllowSessionPrompt() => renewal.AllowPrompt();
    private readonly SemaphoreSlim gate = new(1);
    private readonly SqliteConnection replica = new("Data Source=:memory:;Foreign Keys=True");
    private Dictionary<string, string> hashes = new();
    private bool loaded;
    public RemoteSnapshot Snapshot { get; private set; } = null!;
    public long Revision => loaded ? Snapshot.Revision : 0;
    public AppSettings Settings => Snapshot.Settings;
    public DbContextOptions<AssistantDb> Options { get; }
    private ClientServerTrust? serverTrust;private string initialPin="",observedPin="";
    public string ServerFingerprint=>observedPin;
    private async Task LearnServerTrustAsync(CancellationToken token){var info=await GetAsync<ServerTrustInfo>("api/server-trust",token).ConfigureAwait(false);var next=ClientServerTrust.Learn(info,Address,initialPin,observedPin,serverTrust);next.Save(Address);serverTrust=next;}
    public UserView CurrentUser { get; private set; } = null!;
    public string Address { get; }
    public RemoteRepository(string address, string fingerprint)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Адрес: https://IP:порт"));
        if (!IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var ip) || !(LanNetwork.IsPrivate(ip) || IPAddress.IsLoopback(ip))) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Укажите частный IP сервера в локальной сети"));
        var pin = fingerprint.Replace(" ", "").Replace(":", "").ToUpperInvariant();
        if (pin.Length != 0 && (pin.Length != 64 || pin.Any(c => !Uri.IsHexDigit(c)))) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Отпечаток сертификата SHA-256: 64 шестнадцатеричных символа"));
        initialPin=pin;serverTrust=ClientServerTrust.Load(uri.ToString(),pin);
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) => {
            if(certificate==null)return false;var saved=serverTrust;
            var valid=ServerTrust.ValidLeaf(certificate,DateTime.UtcNow)&&(saved!=null?ServerTrust.Accept(certificate,uri.IdnHost,AgentTrust.Verify(saved.Trust,saved.AuthorityCertificate).Pins,saved.AuthorityCertificate):pin.Length==0?errors==SslPolicyErrors.None:CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pin),SHA256.HashData(certificate.RawData)));
            if(valid)observedPin=certificate.GetCertHashString(HashAlgorithmName.SHA256);return valid;
        };
        http = new(handler) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(90) }; Address = uri.ToString();
        replica.Open(); Options = new DbContextOptionsBuilder<AssistantDb>().UseSqlite(replica).Options;
        using var db = new AssistantDb(Options); db.Database.EnsureCreated();
    }
    public async Task LoginAsync(string user, string password, CancellationToken token = default)
    {
        await ReauthenticateAsync(user, password, token).ConfigureAwait(false);
        await RefreshAsync(token).ConfigureAwait(false);
    }
    // Renewal must not replace the replica or acquire its gate: a save may be awaiting this dialog.
    public async Task ReauthenticateAsync(string user, string password, CancellationToken token = default)
    {
        using(var trustResponse = await SendRawAsync(HttpMethod.Get,"api/server-trust",null,token,false).ConfigureAwait(false))
        {
            var info = await ReadResponse<ServerTrustInfo>(trustResponse, token).ConfigureAwait(false);
            var next = ClientServerTrust.Learn(info,Address,initialPin,observedPin,serverTrust);
            next.Save(Address); serverTrust=next;
        }
        using var response = await SendRawAsync(HttpMethod.Post,"api/login",JsonContent.Create(new RemoteLogin(user,password,RememberSession),options:RemoteData.Json),token,false).ConfigureAwait(false);
        var login = await ReadResponse<RemoteToken>(response,token).ConfigureAwait(false);
        Volatile.Write(ref accessToken,login.Token);connectionLease.Connected();
        try
        {
            using var me = await SendRawAsync(HttpMethod.Get,"api/me",null,token).ConfigureAwait(false);
            var current = await ReadResponse<UserView>(me,token).ConfigureAwait(false);
            if(CurrentUser != null && current.Id != CurrentUser.Id) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Повторный вход возможен только в текущую учётную запись."));
            CurrentUser=current;
        }
        catch { Volatile.Write(ref accessToken,null); throw; }
    }
    public AssistantDb Create() => new(Options) { Remote = this };
    private async Task<T> ReadResponse<T>(HttpResponseMessage response, CancellationToken token)
    {
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new InvalidOperationException(response.RequestMessage?.RequestUri?.AbsolutePath == "/api/login" ? "Неверный логин или пароль сервера" : "Сеанс сервера завершён. Подключитесь заново.");
            var error = await RemoteResponseBody.ReadError(response.Content,token).ConfigureAwait(false);
            throw new ServerResponseException(response.StatusCode,$"Сервер: {(int)response.StatusCode}. {error}");
        }
        var value = await RemoteResponseBody.ReadJson<T>(response.Content,token).ConfigureAwait(false);
        if (value is null) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Пустой ответ сервера"));
        return value;
    }
    public async Task<T> GetAsync<T>(string path, CancellationToken token = default)
    {
        return await SendAsync<T>(() => SendRawAsync(HttpMethod.Get,path,null,token),token).ConfigureAwait(false);
    }
    public Task<ServerTrustInfo> WorkstationTrustAsync(CancellationToken token)=>GetAsync<ServerTrustInfo>("api/server-trust",token);
    public async Task SaveWorkstationInstallerAsync(string destination,CancellationToken token)
    {
        const long limit=256L*1024*1024;
        using var response=await SessionRequests.SendAsync(
            ()=>SendRawAsync(HttpMethod.Get,"api/workstations/installer",null,token),renewal,SessionExpired,token).ConfigureAwait(false);
        if(!response.IsSuccessStatusCode){
            _=await ReadResponse<object>(response,token).ConfigureAwait(false);
            throw new InvalidDataException("Сервер не вернул установщик");
        }
        if(response.Content.Headers.ContentType?.MediaType!="application/x-msi"||
            response.Content.Headers.ContentLength is not long size||size is <512 or >limit)
            throw new InvalidDataException("Сервер вернул неверный тип или размер установщика");
        var temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            await using(var source=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){
                var header=new byte[8];
                await source.ReadExactlyAsync(header,token).ConfigureAwait(false);
                if(!header.SequenceEqual(new byte[]{0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1}))
                    throw new InvalidDataException("Файл не является пакетом Windows Installer");
                await file.WriteAsync(header,token).ConfigureAwait(false);
                var buffer=new byte[65536];long written=header.Length;
                int count;
                while((count=await source.ReadAsync(buffer,token).ConfigureAwait(false))>0){
                    written+=count;if(written>size)throw new InvalidDataException("Размер установщика не совпадает");
                    await file.WriteAsync(buffer.AsMemory(0,count),token).ConfigureAwait(false);
                }
                if(written!=size)throw new InvalidDataException("Установщик загружен не полностью");
            }
            token.ThrowIfCancellationRequested();
            File.Move(temp,destination,true);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }

    public sealed record ProductUpdateCheck(string Manifest,string ServerVersion,bool MaintenanceInstalled);

    public async Task<ProductUpdateCheck> CheckServerProductUpdateAsync(CancellationToken token)
    {
        // Background checks must never open the session renewal dialog.
        using var response=await SendRawAsync(HttpMethod.Get,"api/product-updates/check",null,token).ConfigureAwait(false);
        return await ReadResponse<ProductUpdateCheck>(response,token).ConfigureAwait(false);
    }

    public async Task<Dictionary<int,LampState>> NavigationHealthAsync(CancellationToken token)
    {
        using var response=await SendRawAsync(HttpMethod.Get,"api/personal/navigation-health",null,token).ConfigureAwait(false);
        return await ReadResponse<Dictionary<int,LampState>>(response,token).ConfigureAwait(false);
    }
    public async Task<T> PostAsync<TRequest, T>(string path, TRequest value, CancellationToken token = default)
    {
        return await SendAsync<T>(() => SendRawAsync(HttpMethod.Post,path,JsonContent.Create(value,options:RemoteData.Json),token),token).ConfigureAwait(false);
    }
    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method,string path,HttpContent? content,CancellationToken token,bool authenticated=true)
    {
        if(authenticated&&RememberSession&&connectionLease.Expired){Volatile.Write(ref accessToken,null);return new HttpResponseMessage(HttpStatusCode.Unauthorized);}
        using var request=new HttpRequestMessage(method,path){Content=content};
        var bearer=authenticated?Volatile.Read(ref accessToken):null;
        if(bearer!=null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
        var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
        if(authenticated&&bearer!=null&&response.StatusCode!=HttpStatusCode.Unauthorized)connectionLease.Connected();
        return response;
    }
    private async Task<T> SendAsync<T>(Func<Task<HttpResponseMessage>> send, CancellationToken token)
    {
        using var response=await SessionRequests.SendAsync(send,renewal,SessionExpired,token).ConfigureAwait(false);
        return await ReadResponse<T>(response,token).ConfigureAwait(false);
    }
    public Task<T> CommandAsync<T>(string command, object value, CancellationToken token = default) => PostAsync<object, T>("api/command/" + command, value, token);
    public async Task RefreshAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await LearnServerTrustAsync(token).ConfigureAwait(false);
            var snapshot = await GetAsync<RemoteSnapshot>(InventoryOpen?"api/state?inventory=true":"api/state", token).ConfigureAwait(false);
            if (snapshot.Rows.Count != RemoteData.Types.Count || RemoteData.Types.Keys.Any(k => !snapshot.Rows.ContainsKey(k))) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Несовместимая версия сервера"));
            AppTime.Configure(snapshot.Settings.TimeZoneId);
            var baseline = new Dictionary<string, string>();
            // Rebuild one consistent view in a transaction. No database file is written on the client.
            using var db = new AssistantDb(Options);
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=OFF;");
            try
            {
                using var tx = db.Database.BeginTransaction();
                foreach (var type in RemoteData.Types.Values.Reverse())
                {
                    // Closed list of model types; no user-controlled SQL identifier.
#pragma warning disable EF1002
                    db.Database.ExecuteSqlRaw($"DELETE FROM [sa2_{type.Name}];");
#pragma warning restore EF1002
                }
                foreach (var (resource, values) in snapshot.Rows)
                foreach (var value in values)
                {
                    var entity = (Entity)(value.Deserialize(RemoteData.Types[resource], RemoteData.Json) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Пустая запись")));
                    db.Add(entity); baseline[resource + "/" + entity.Id] = RemoteData.EntityStamp(entity);
                }
                db.SaveChanges(); tx.Commit(); hashes = baseline; Snapshot = snapshot; loaded = true;
            }
            finally { db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;"); }
        }
        finally { gate.Release(); }
    }
    public async Task PushAsync(AssistantDb db, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            db.ChangeTracker.DetectChanges();
            var entries = db.ChangeTracker.Entries<Entity>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
            if (entries.Length == 0) return;
            var changes = entries.Select(e =>
            {
                var resource = RemoteData.Types.Single(x => x.Value == e.Entity.GetType()).Key;
                return new RemoteChange(resource, e.State.ToString(), e.Entity.Id, hashes.GetValueOrDefault(resource + "/" + e.Entity.Id, ""), JsonSerializer.SerializeToElement(e.Entity, e.Entity.GetType(), RemoteData.Json));
            }).ToArray();
            var results = await PostAsync<RemoteChange[], RemoteChangeResult[]>("api/changes", changes, token).ConfigureAwait(false);
            if (results.Length != entries.Length) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неполный ответ записи сервера"));
            for (var i = 0; i < results.Length; i++)
            {
                var result = results[i]; var entry = entries[i];
                if (entry.State == EntityState.Deleted) hashes.Remove(result.Resource + "/" + result.OldId);
                else
                {
                    var returned = (Entity)result.Value.Deserialize(entry.Entity.GetType(), RemoteData.Json)!;
                    entry.CurrentValues.SetValues(returned); if (entry.State == EntityState.Added) entry.Property(nameof(Entity.Id)).IsTemporary = false; hashes[result.Resource + "/" + returned.Id] = RemoteData.EntityStamp(returned);
                }
            }
        }
        finally { gate.Release(); }
    }
    public async Task<AppSettings> SaveSettingsAsync(AppSettings proposed, CancellationToken token = default)
    {
        var secrets = new Dictionary<string, string>();
        void Add(string key, string value) { if (value.Length > 0 && value != "REMOTE") secrets[key] = LocalSecret.Unprotect(value); }
        foreach (var mailbox in proposed.Mailboxes) Add("mail/" + mailbox.Id, mailbox.ProtectedPassword);
        Add("telegram", proposed.Telegram.ProtectedToken);Add("server-heartbeat",proposed.ServerHeartbeat.ProtectedSecret);
        Add("smtp", proposed.Smtp.ProtectedPassword); Add("router", proposed.Router.ProtectedPassword);
        var result = await PostAsync<RemoteSettingsChange, RemoteSettingsResult>("api/settings", new(proposed, Snapshot.SettingsStamp, secrets), token).ConfigureAwait(false);
        Snapshot = Snapshot with { Settings = result.Settings, SettingsStamp = result.SettingsStamp };
        return result.Settings;
    }
    public void AcceptSettings(AppSettings value){AppTime.Configure(value.TimeZoneId);Snapshot = Snapshot with { Settings = value };}
    public async Task WatchAsync(Func<long,Task> changed, Func<Exception, Task> failed, CancellationToken token)
    {
        var observedRevision=Revision;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var revision = await GetAsync<long>("api/wait?after=" + observedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), token).ConfigureAwait(false);
                if (revision != observedRevision) { observedRevision=revision; await changed(revision).ConfigureAwait(false); }
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception e) { await failed(e).ConfigureAwait(false); try { await Task.Delay(5000, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; } }
        }
    }
    public async Task ClearInventoryAsync(CancellationToken token=default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try{
            using var db=new AssistantDb(Options);await db.ActLines.ExecuteDeleteAsync(token);
            await db.Acts.ExecuteDeleteAsync(token);await db.Assets.ExecuteDeleteAsync(token);await db.Employees.ExecuteDeleteAsync(token);
            if(loaded){var rows=new Dictionary<string,JsonElement[]>(Snapshot.Rows);foreach(var key in new[]{"employees","assets","acts","actLines"})rows[key]=[];Snapshot=Snapshot with{Rows=rows};}
        }finally{gate.Release();}
    }
    public async Task LogoutAsync(){try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));using var response=await SendRawAsync(HttpMethod.Post,"api/logout",null,timeout.Token).ConfigureAwait(false);}catch(Exception e)when(e is HttpRequestException or OperationCanceledException){}finally{Volatile.Write(ref accessToken,null);connectionLease.Clear();}}
    public void Dispose() { Volatile.Write(ref accessToken,null);connectionLease.Clear();SessionExpired=null;http.Dispose(); replica.Dispose(); gate.Dispose(); }
}

public sealed class ServerResponseException(System.Net.HttpStatusCode statusCode,string message):InvalidOperationException(message)
{
    public System.Net.HttpStatusCode StatusCode { get; }=statusCode;
}
