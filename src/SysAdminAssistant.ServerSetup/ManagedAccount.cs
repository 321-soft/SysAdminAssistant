using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
namespace SysAdminAssistant.ServerSetup;
internal sealed class ManagedAccount
{
    public const string UserName = "SysAdminSvc06";
    private const string Marker = "SysAdmin Assistant managed service account";
    public string Name => Environment.MachineName + "\\" + UserName;
    public string Sid { get; private set; } = "";
    public string Password { get; private set; } = "";
    public bool Created { get; private set; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct UserInfo
    {
        public string Name, Password;
        public uint Age, Privilege;
        public string? Home, Comment;
        public uint Flags;
        public string? Script;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct PasswordInfo { public string Password; }
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] private static extern uint NetUserAdd(string? server, uint level, ref UserInfo user, out uint error);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] private static extern uint NetUserGetInfo(string? server, string user, uint level, out IntPtr buffer);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] private static extern uint NetUserSetInfo(string? server, string user, uint level, ref PasswordInfo info, out uint error);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] private static extern uint NetUserDel(string? server, string user);
    [DllImport("netapi32.dll")] private static extern uint NetApiBufferFree(IntPtr buffer);
    private static void Check(uint status) { if (status != 0) throw new Win32Exception((int)status); }
    public void CreateOrResume(SetupConfig? config)
    {
        Password = "Aa9!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(36));
        if (config != null)
        {
            if (!config.MachineSecrets || config.ServiceAccountName != Name) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Для повторной попытки нужны данные этой служебной учётной записи. Для прежней установки выберите новый каталог данных."));
            Check(NetUserGetInfo(null, UserName, 1, out var buffer));
            try
            {
                var info = Marshal.PtrToStructure<UserInfo>(buffer);
                Sid = ((SecurityIdentifier)new NTAccount(Name).Translate(typeof(SecurityIdentifier))).Value;
                if (info.Comment != Marker || Sid != config.OwnerSid) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Существующая учётная запись не принадлежит этой установке."));
            }
            finally { NetApiBufferFree(buffer); }
            var password = new PasswordInfo { Password = Password }; Check(NetUserSetInfo(null, UserName, 1003, ref password, out _));
        }
        else
        {
            var user = new UserInfo { Name = UserName, Password = Password, Privilege = 1, Comment = Marker, Flags = 0x10201 };
            var result = NetUserAdd(null, 1, ref user, out _);
            if (result == 2224) throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Учётная запись SysAdminSvc06 уже существует. Удалите прежнюю установку её деинсталлятором либо выберите LocalService; существующая учётная запись не изменяется."));
            Check(result); Created = true;
            Sid = ((SecurityIdentifier)new NTAccount(Name).Translate(typeof(SecurityIdentifier))).Value;
        }
        SysAdminInstaller.ServiceAccount.ServiceRights(Sid);
    }
    public void Remove()
    {
        if (!Created) return;
        if (Sid.Length > 0) SysAdminInstaller.ServiceAccount.RemoveManagedRights(Sid);
        Check(NetUserDel(null, UserName)); Created = false;
    }
    public void ClearPassword() => Password = "";
    public static void GrantProgramAccess(string folder, string sid)
    {
        var directory = new DirectoryInfo(folder);
        var acl = directory.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(acl);
    }
}
