using System.Security.Cryptography;

namespace SysAdminAssistant.Core;

public static class AgentDesktopRuntime
{
 public const string Version = "10.0.14";
 public const string FileName = "windowsdesktop-runtime-" + Version + "-win-x64.exe";
 public const string Sha512 = "337dacab1636219147b3fe75e451bf5b1c17bca2c86328266d40c4ad1ff0a1e8391c4746ca7a5b702b65b61e8bfe834566cfc1b548cc10aef90b441357b10b69";
 public const long MaxBytes = 100_000_000;

 public static bool MatchesMicrosoftPackage(byte[] bytes) =>
     bytes.LongLength is >0 and <=MaxBytes &&
     CryptographicOperations.FixedTimeEquals(SHA512.HashData(bytes), Convert.FromHexString(Sha512));

 public static bool HasRequiredFramework(string frameworkDirectory,string primaryAssembly) =>
     Directory.Exists(frameworkDirectory) && Directory.EnumerateDirectories(frameworkDirectory).Any(path =>
         System.Version.TryParse(Path.GetFileName(path),out var version) && version.Major==10 && version.Minor==0 &&
         version>=System.Version.Parse(Version) && File.Exists(Path.Combine(path,primaryAssembly)));
}
