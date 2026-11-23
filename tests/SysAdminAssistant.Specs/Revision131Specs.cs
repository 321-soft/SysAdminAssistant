using SysAdminAssistant.Core;
internal static class Revision131Specs {
 public static void Run(Action<string,bool> check){
  var package=new Version(0,13,1,0);bool Accept(Version old,Version next){try{ServerUpgradeVersions.Validate(old,next,package);return true;}catch(InvalidOperationException){return false;}}
  check("Installer accepts 0.12 to current 0.13 package",Accept(new(0,12,9,0),package));
  check("Installer accepts 0.13.0 to 0.13.1",Accept(new(0,13,0,0),package));
  check("Installer accepts minimum supported 0.6 installation",Accept(new(0,6,0,0),package));
  check("Installer allows same version repair",Accept(package,package));
  check("Installer rejects downgrade",!Accept(new(0,13,2,0),package));
  check("Installer rejects mismatched server payload",!Accept(new(0,12,9,0),new(0,13,0,0)));
  check("Installer rejects unsupported pre 0.6 installation",!Accept(new(0,5,9,0),package));
 }
}
