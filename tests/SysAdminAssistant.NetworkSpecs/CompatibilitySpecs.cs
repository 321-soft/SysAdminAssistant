using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
using SysAdminAssistant.Server;

internal static class CompatibilitySpecs
{
    public static async Task Run(string root,Action<string,bool> check)
    {
        var folder=Path.Combine(root,"compatibility");Directory.CreateDirectory(folder);
        var scope=LocalSecret.MachineScope;LocalSecret.MachineScope=true;
        try {
            var hash=ServerPassword.Create("Legacy-Admin-Password");
            var legacyConfig=new ServerConfiguration {InstallFormat=4,MachineSecrets=true,BindIp="127.0.0.1",Port=7443,Networks=["192.168.1.0/24"],User="admin",Salt=hash.Salt,PasswordHash=hash.Hash};
            legacyConfig.Save(folder);
            bool rejected=false;
            try {ServerConfiguration.Load(folder);} catch(InvalidDataException){rejected=true;}
            check("Clean release rejects 0.4 installed configuration",rejected);
            UserDirectory.Bootstrap(folder,legacyConfig);
            var original=new UserDirectory(folder);
            var owner=original.Create(new("owner","Legacy-Owner-Password",new(){["vault"]=2}));
            var viewer=original.Create(new("viewer","Legacy-Viewer-Password",new(){["vault"]=2}));
            var path=Path.Combine(folder,"users.json");
            var accounts=JsonNode.Parse(File.ReadAllText(path))!.AsArray();
            var ownerKey=accounts.Single(x=>x!["Id"]!.GetValue<string>()==owner.Id)!["ProtectedPrivateKey"]!.GetValue<string>();
            foreach(var account in accounts) {account!.AsObject().Remove("Permissions");account["ReadInfrastructure"]=account["Id"]!.GetValue<string>()==viewer.Id;}
            File.WriteAllText(path,accounts.ToJsonString());
            var migrated=new UserDirectory(folder);owner=migrated.Authenticate("owner","Legacy-Owner-Password")!;
            viewer=migrated.Authenticate("viewer","Legacy-Viewer-Password")!;
            check("0.4 update preserves password hashes and account IDs",owner.Id==original.List().Single(x=>x.Login=="owner").Id);
            check("0.4 hidden infrastructure stays hidden",AccessRules.Level(owner,"devices")==0 && AccessRules.Level(owner,"vault")==2);
            check("0.4 infrastructure view becomes read permission",AccessRules.Level(viewer,"devices")==1 && AccessRules.Level(viewer,"reports")==1 && AccessRules.Level(viewer,"settings")==0);
            var after=JsonNode.Parse(File.ReadAllText(path))!.AsArray();
            check("0.4 update preserves private encryption key",after.Single(x=>x!["Id"]!.GetValue<string>()==owner.Id)!["ProtectedPrivateKey"]!.GetValue<string>()==ownerKey);
            SettingsFile.Save(new AppSettings {SqlitePath=Path.Combine(folder,"server.db"),MonitoringEnabled=false},Path.Combine(folder,"settings.json"));
            var state=new ServerState(folder);await state.InitializeAsync(CancellationToken.None);
            var entry=new CredentialEntry {OwnerId=owner.Id,Name="Legacy secret"};
            var key=RandomNumberGenerator.GetBytes(32);var plain=JsonSerializer.SerializeToUtf8Bytes(new CredentialPayload("legacy","Legacy-Entry-Secret","kept"));var bytes=new byte[28+plain.Length];RandomNumberGenerator.Fill(bytes.AsSpan(0,12));
            try {
                using var aes=new AesGcm(key,16);aes.Encrypt(bytes.AsSpan(0,12),plain,bytes.AsSpan(28),bytes.AsSpan(12,16),Encoding.UTF8.GetBytes("SysAdminAssistant.0.4/Vault/"+owner.Id));
                using var userRsa=migrated.Key(owner.Id);using var adminRsa=migrated.Key(migrated.AdministratorId);
                entry.OwnerKey=Convert.ToBase64String(userRsa.Encrypt(key,RSAEncryptionPadding.OaepSHA256));entry.AdministratorKey=Convert.ToBase64String(adminRsa.Encrypt(key,RSAEncryptionPadding.OaepSHA256));entry.Ciphertext=Convert.ToBase64String(bytes);
            }finally{CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(plain);}
            await using(var db=state.Factory.Create()){db.Credentials.Add(entry);await db.SaveChangesAsync();}
            var vault=new OrganizationVault(state,migrated);var target=new VaultTarget(entry.Id,entry.Revision);
            check("0.4 Vault decrypts after update for owner",(await vault.Read(owner,target,CancellationToken.None)).Password=="Legacy-Entry-Secret");
            var admin=migrated.Authenticate("admin","Legacy-Admin-Password")!;
            check("0.4 Vault decrypts after update for administrator",(await vault.Read(admin,target,CancellationToken.None)).Password=="Legacy-Entry-Secret");
            await vault.Save(owner,new(entry.Id,entry.Revision,entry.Name,null,new("legacy","Rewritten-Secret","kept")),CancellationToken.None);
            check("0.4 Vault can be edited without a duplicate",(await vault.Read(owner,target,CancellationToken.None)).Password=="Rewritten-Secret" && (await vault.List(owner,CancellationToken.None)).Length==1);
        }finally{LocalSecret.MachineScope=scope;Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();}
    }
}
