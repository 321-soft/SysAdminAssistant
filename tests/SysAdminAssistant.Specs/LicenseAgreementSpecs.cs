using SysAdminAssistant.Core;
internal static class LicenseAgreementSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SAA-Legal-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory,"acceptance.json");
        try
        {
            check("EULA missing record requires acceptance", !LicenseAgreement.IsAccepted(path));
            check("EULA both translations embedded", LicenseAgreement.Text("RU").Contains("Орлов Александр Юрьевич") && LicenseAgreement.Text("EN").Contains("support@321.su"));
            LicenseAgreement.Accept(path,"EN");
            check("EULA acceptance survives reload",LicenseAgreement.IsAccepted(path));
            var record=File.ReadAllText(path);
            File.WriteAllText(path,record.Replace("\"Version\":\""+LicenseAgreement.Version+"\"","\"Version\":\"1.0\""));
            check("EULA previous revision requires renewed acceptance",!LicenseAgreement.IsAccepted(path));
            File.WriteAllText(path,record);
            check("EULA embedded copies contain plain section headings and paragraphs",new[]{"RU","EN"}.All(language=>{
                var text=LicenseAgreement.CanonicalText(LicenseAgreement.Text(language));
                return !text.StartsWith('#')&&!text.Contains("**")&&text.Contains("\n\n1. ")&&text.Contains("\n\n12. ");
            }));
            File.WriteAllText(path,File.ReadAllText(path).Replace(LicenseAgreement.Hash,new string('0',64)));
            check("EULA changed text requires acceptance",!LicenseAgreement.IsAccepted(path));
            File.WriteAllText(path,"{broken");
            check("EULA malformed record fails closed",!LicenseAgreement.IsAccepted(path));
            LicenseAgreement.Accept(path,"RU");
            check("EULA damaged record can be replaced",LicenseAgreement.IsAccepted(path));
            check("EULA no abandoned temporary records",Directory.GetFiles(directory,"*.tmp").Length == 0);
        }
        finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
    }
}
