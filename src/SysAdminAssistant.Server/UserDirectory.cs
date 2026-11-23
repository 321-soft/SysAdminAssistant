using System.Security.Cryptography;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;
public sealed class UserDirectory
{
    public sealed class Account
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Login { get; set; } = "";
        public bool Administrator { get; set; }
        public bool Enabled { get; set; } = true;
        public Dictionary<string,int> Permissions { get; set; } = new();
        public int Revision { get; set; } = 1;
        public string Salt { get; set; } = "";
        public string Hash { get; set; } = "";
        public string ProtectedPrivateKey { get; set; } = "";
        public UserView View() => new(Id, Login, Administrator, Enabled, Revision, new(Permissions));
    }
    private readonly object sync = new();
    private readonly string path;
    private List<Account> accounts;
    public UserDirectory(string directory)
    {
        path = Path.Combine(directory, "users.json");
        var json=File.ReadAllText(path);
        accounts = JsonSerializer.Deserialize<List<Account>>(json) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Пустой список пользователей"));
        if (accounts.Count(x => x.Administrator) != 1 || accounts.Select(x => x.Login).Distinct(StringComparer.OrdinalIgnoreCase).Count() != accounts.Count) throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неверная база пользователей"));
        var original=JsonSerializer.Deserialize<JsonElement[]>(json)!;bool converted=false;
        for(var i=0;i<accounts.Count;i++) {
            if(original[i].TryGetProperty("Permissions",out _))continue;
            // Preserve old account IDs, hashes and RSA keys; convert only the old permission flag.
            var read=!original[i].TryGetProperty("ReadInfrastructure",out var legacy) || legacy.GetBoolean();
            accounts[i].Permissions=AccessRules.Modules.ToDictionary(x=>x,x=>x=="vault"?2:read && x is not ("settings" or "routers")?1:0);
            accounts[i].Revision++;converted=true;
        }
        if(converted)Save();
    }
    public static void Bootstrap(string directory, ServerConfiguration config)
    {
        var path = Path.Combine(directory, "users.json");
        if (File.Exists(path)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Пользователи уже созданы; нужна чистая установка"));
        var account = NewAccount(config.User); account.Administrator = true; account.Salt = config.Salt; account.Hash = config.PasswordHash;
        File.WriteAllText(path, JsonSerializer.Serialize(new[] { account }));
    }
    private static Account NewAccount(string login)
    {
        if (login.Length is < 1 or > 100 || login != login.Trim() || login.Any(char.IsControl)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Логин: 1–100 символов без крайних пробелов и управляющих символов"));
        using var rsa = RSA.Create(3072);
        return new Account { Login = login, ProtectedPrivateKey = LocalSecret.Protect(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey())) };
    }
    private void Save()
    {
        var temp = path + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(accounts)); File.Move(temp, path, true);
    }
    private T Mutate<T>(Func<T> edit)
    {
        var backup = JsonSerializer.Serialize(accounts);
        try { var result = edit(); Save(); return result; }
        catch { accounts = JsonSerializer.Deserialize<List<Account>>(backup)!; throw; }
    }
    internal string? FindAccountId(string login)
    { lock(sync) return accounts.SingleOrDefault(x=>x.Login.Equals(login,StringComparison.OrdinalIgnoreCase))?.Id; }
    public UserView? Authenticate(string login, string password)
    {
        lock (sync)
        {
            var found = accounts.SingleOrDefault(x => x.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
            var probe = found ?? accounts.Single(x => x.Administrator);
            var valid = ServerPassword.Verify(password, probe.Salt, probe.Hash);
            return valid && found?.Enabled == true ? found.View() : null;
        }
    }
    public UserView? Current(string id, int revision)
    { lock (sync) { var a = accounts.SingleOrDefault(x => x.Id == id && x.Enabled && x.Revision == revision); return a?.View(); } }
    public UserView[] List() { lock (sync) return accounts.Select(x => x.View()).ToArray(); }
    public string AdministratorId { get { lock (sync) return accounts.Single(x => x.Administrator).Id; } }
    public UserView Create(UserCreate request)
    {
        lock (sync) return Mutate(() => {
            if (accounts.Count >= 200 || accounts.Any(x => x.Login.Equals(request.Login, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Логин занят или достигнут лимит 200 пользователей"));
            var a = NewAccount(request.Login); (a.Salt, a.Hash) = ServerPassword.Create(request.Password); a.Permissions=AccessRules.Validate(request.Permissions); accounts.Add(a); return a.View();
        });
    }
    public UserView Update(UserUpdate request)
    {
        lock (sync) return Mutate(() => {
            var a = accounts.SingleOrDefault(x => x.Id == request.Id) ?? throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пользователь не найден"));
            if (a.Administrator) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Главного администратора нельзя блокировать или менять через эту операцию"));
            if (a.Revision != request.Revision) throw new ConflictException(global::SysAdminAssistant.Core.ProductText.Get("Пользователь изменён; обновите список"));
            if (request.NewPassword.Length > 0) (a.Salt, a.Hash) = ServerPassword.Create(request.NewPassword);
            a.Enabled = request.Enabled; a.Permissions=AccessRules.Validate(request.Permissions); a.Revision++; return a.View();
        });
    }
    public void ChangePassword(UserView actor, PasswordChange request)
    {
        lock (sync) Mutate(() => {
            var a = accounts.Single(x => x.Id == actor.Id);
            if (!ServerPassword.Verify(request.CurrentPassword, a.Salt, a.Hash)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Текущий пароль неверен"));
            (a.Salt, a.Hash) = ServerPassword.Create(request.NewPassword); a.Revision++; return true;
        });
    }
    public void ResetAdministrator(string password)
    { lock (sync) Mutate(() => { var a = accounts.Single(x => x.Administrator); (a.Salt, a.Hash) = ServerPassword.Create(password); a.Revision++; return true; }); }
    public RSA Key(string id)
    {
        lock (sync)
        {
            var a = accounts.Single(x => x.Id == id); var bytes = Convert.FromBase64String(LocalSecret.Unprotect(a.ProtectedPrivateKey));
            try { var rsa = RSA.Create(); rsa.ImportPkcs8PrivateKey(bytes, out _); return rsa; } finally { CryptographicOperations.ZeroMemory(bytes); }
        }
    }
}
