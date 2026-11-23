using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
static class ServiceVaultSpecs
{
 public static async Task Run(string folder,Action<string,bool> check){
 var factory=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"service-vault.db")});await factory.InitializeAsync();
 await using(var db=factory.Create()){
 var service=new ServiceEntry{Id=400,Name="Availability fixture",Type="TCP",Address="127.0.0.1:1"};db.Services.Add(service);var credential=new CredentialEntry{Name="Existing encrypted entry",Ciphertext=Convert.ToBase64String(new byte[64])};db.Credentials.Add(credential);db.Vault.Add(new(){Salt=Convert.ToBase64String(new byte[32]),Proof=Convert.ToBase64String(new byte[28])});await db.SaveChangesAsync();
 await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_CredentialEntry DROP COLUMN ServiceId");await BindingSchema.EnsureAsync(db);await BindingSchema.EnsureAsync(db);db.ChangeTracker.Clear();var old=await db.Credentials.SingleAsync();check("Additive service link migration preserves old ciphertext and null relation",old.Name=="Existing encrypted entry"&&old.ServiceId==null&&old.Ciphertext==credential.Ciphertext);old.ServiceId=service.Id;await db.SaveChangesAsync();try{await CatalogDeletion.EnsureAllowedAsync(db,service);check("Linked service cannot orphan password entry",false);}catch(ArgumentException){check("Linked service cannot orphan password entry",true);}
 }
 var path=Path.Combine(folder,"service-vault.saa");await new DatabaseTransfer(factory).ExportAsync(path);var target=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"service-vault-copy.db")});await target.InitializeAsync();await new DatabaseTransfer(target).ImportAsync(path);await using(var copy=target.Create()){var entry=await copy.Credentials.SingleAsync();var service=await copy.Services.SingleAsync();check("Portable service password relation remaps IDs and keeps ciphertext",entry.ServiceId==service.Id&&service.Id!=400&&entry.Ciphertext==Convert.ToBase64String(new byte[64]));entry.ServiceId=null;await copy.SaveChangesAsync();await CatalogDeletion.EnsureAllowedAsync(copy,service);check("Unbound service can be deleted without deleting password",await copy.Credentials.CountAsync()==1);}
 }
}
