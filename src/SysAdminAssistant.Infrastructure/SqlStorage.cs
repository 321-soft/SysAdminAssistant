using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
namespace SysAdminAssistant.Infrastructure;
public static class SqlStorage
{
    public static string QuoteDatabase(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 123 || name.Any(char.IsControl) || new[] { "master", "model", "msdb", "tempdb" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите имя новой пользовательской базы: 1–123 символа, не имя системной базы."));
        return "[" + name.Replace("]", "]]") + "]";
    }
    public static void Configure(AppSettings settings, string host, string database, string user, string password, bool trustCertificate = false)
    {
        _ = QuoteDatabase(database);
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password)) throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Укажите SQL Server, логин и пароль SQL."));
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = host, InitialCatalog = database, UserID = user, IntegratedSecurity = false,
            Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = trustCertificate, PersistSecurityInfo = false, ConnectTimeout = 15
        };
        settings.DatabaseProvider = SysAdminAssistant.Core.DatabaseProvider.SqlServer;
        settings.SqlConnectionString = connection.ConnectionString;
        settings.ProtectedSqlPassword = LocalSecret.Protect(password);
    }
    public static string Connection(AppSettings settings)
    {
        var builder = new SqlConnectionStringBuilder(settings.SqlConnectionString);
        if (!string.IsNullOrEmpty(builder.Password)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Пароль SQL должен храниться зашифрованным, не в строке подключения."));
        if (!builder.IntegratedSecurity)
        {
            if (string.IsNullOrWhiteSpace(builder.UserID) || string.IsNullOrEmpty(settings.ProtectedSqlPassword)) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Не настроены SQL-логин или защищённый пароль."));
            builder.Password = LocalSecret.Unprotect(settings.ProtectedSqlPassword);
        }
        builder.PersistSecurityInfo = false;
        return builder.ConnectionString;
    }
    public static async Task CreateDatabaseAsync(AppSettings settings, CancellationToken token = default)
    {
        var target = new SqlConnectionStringBuilder(Connection(settings));
        var name = target.InitialCatalog;
        var identifier = QuoteDatabase(name);
        var master = new SqlConnectionStringBuilder(target.ConnectionString) { InitialCatalog = "master" };
        bool created = false;
        try
        {
            await using (var connection = new SqlConnection(master.ConnectionString))
            {
                await connection.OpenAsync(token);
                await using var probe = connection.CreateCommand(); probe.CommandText = "SELECT DB_ID(@name)";
                probe.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = name;
                if (await probe.ExecuteScalarAsync(token) is int) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("База с таким именем уже существует. Выберите новое имя; существующая база не изменена."));
                await using var create = connection.CreateCommand(); create.CommandText = "CREATE DATABASE " + identifier; create.CommandTimeout = 45;
                await create.ExecuteNonQueryAsync(token); created = true;
            }
            await using var db = new DatabaseFactory(settings).Create();
            await db.Database.EnsureCreatedAsync(token);
        }
        catch (SqlException e)
        {
            var reason = e.Number switch
            {
                -2146893019 => "Windows не доверяет сертификату SQL Server. Настройте доверенный сертификат или явно включите галочку доверия в мастере.",
                18456 => "SQL-логин или пароль не принят. Проверьте, что SQL-аутентификация разрешена на экземпляре.",
                262 or 15247 or 229 => "SQL-логину не хватает прав на создание базы или таблиц.",
                1801 => "База с таким именем уже существует; выберите новое имя.",
                _ => "Проверьте адрес SQL Server, доступность, доверие TLS-сертификату и права SQL-логина."
            };
            throw new InvalidOperationException($"SQL Server, код {e.Number}: {reason}" + (created ? " Новая база сохранена для диагностики; автоматически она не удаляется." : ""));
        }
    }
}
