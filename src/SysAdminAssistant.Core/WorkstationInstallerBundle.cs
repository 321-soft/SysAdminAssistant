using System.IO.Compression;
namespace SysAdminAssistant.Core;
public static class WorkstationInstallerBundle
{
    public const string PackageName="SysAdminAssistant_Workstation_x64.msi";
    public static void Build(string launcher,string msi,string output)
    {
        var temporary=output+"."+Guid.NewGuid().ToString("N");var zip=temporary+".zip";
        try{
            using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))archive.CreateEntryFromFile(msi,PackageName,CompressionLevel.Optimal);
            ProductSetupBundle.Pack(launcher,zip,temporary);
            File.Move(temporary,output,true);
        }finally{if(File.Exists(temporary))File.Delete(temporary);if(File.Exists(zip))File.Delete(zip);}
    }
}
