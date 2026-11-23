namespace SysAdminAssistant.Infrastructure;

public static class StoredCredential
{
    public static bool SameHost(string? a,string? b)=>string.Equals(a?.Trim(),b?.Trim(),StringComparison.OrdinalIgnoreCase);
    public static bool SameEndpoint(string? host,int port,string? user,string? oldHost,int oldPort,string? oldUser)=>
        SameHost(host,oldHost)&&port==oldPort&&string.Equals(user,oldUser,StringComparison.Ordinal);

    public static string Reuse(string secret,bool sameEndpoint)
    {
        if(secret.Length!=0&&!sameEndpoint)
            throw new ArgumentException("Параметры подключения изменились. Введите пароль для нового подключения.");
        return secret;
    }

    public static string MailPassword(MailSettings target,MailSettings? old)=>old==null?"":
        Reuse(old.ProtectedPassword,SameEndpoint(target.Host,target.Port,target.User,old.Host,old.Port,old.User));
}
