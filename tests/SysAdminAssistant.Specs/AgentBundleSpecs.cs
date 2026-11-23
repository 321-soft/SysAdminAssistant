using System.IO.Compression;
using SysAdminAssistant.Core;
internal static class AgentBundleSpecs
{
 public static void Run(string folder,Action<string,bool> check){
  var root=Path.Combine(folder,"bundle210");Directory.CreateDirectory(root);var stub=Path.Combine(root,"stub.exe");File.WriteAllBytes(stub,[77,90,0,1,2,3]);
  string Build(string name,params (string Name,string Text)[] files){var zip=Path.Combine(root,name+".zip");using(var z=ZipFile.Open(zip,ZipArchiveMode.Create))foreach(var f in files){using var w=new StreamWriter(z.CreateEntry(f.Name).Open());w.Write(f.Text);}var output=Path.Combine(root,name+".exe");AgentSetupBundle.Pack(stub,zip,output);return output;}
  bool Reject(string exe,string dir){try{AgentSetupBundle.Extract(exe,dir,(_,_)=>{});return false;}catch(InvalidDataException){return true;}}
  var exe=Build("normal",("bootstrap.json","signed fixture"),("Agent/agent.dll","binary fixture"));var output=Path.Combine(root,"out");int progress=0;AgentSetupBundle.Extract(exe,output,(n,_)=>progress=n);
  check("Single EXE package preserves bootstrap and nested agent bytes",File.ReadAllText(Path.Combine(output,"bootstrap.json"))=="signed fixture"&&File.ReadAllText(Path.Combine(output,"Agent/agent.dll"))=="binary fixture"&&progress==100);
  var tampered=Path.Combine(root,"tamper.exe");var bytes=File.ReadAllBytes(exe);bytes[10]^=1;File.WriteAllBytes(tampered,bytes);check("Single EXE rejects changed payload before extraction",Reject(tampered,Path.Combine(root,"bad"))&&!Directory.Exists(Path.Combine(root,"bad")));
  foreach(var path in new[]{"../escape","/absolute","C:/escape","Agent/../escape","Agent\\..\\escape","Agent/file.","Agent/file ","Agent/file:stream","Agent/CON.txt","Agent/NUL","Agent/file?"}){var invalid=Build("unsafe"+Guid.NewGuid().ToString("N"),(path,"unsafe"));check("Single EXE rejects unsafe entry "+path,Reject(invalid,Path.Combine(root,Guid.NewGuid().ToString("N"))));}
  var duplicate=Build("duplicate",("Agent/a.dll","1"),("agent/A.dll","2"));check("Single EXE rejects Windows case aliases",Reject(duplicate,Path.Combine(root,"aliases")));
  var truncated=Path.Combine(root,"short.exe");File.WriteAllBytes(truncated,File.ReadAllBytes(exe)[..^1]);check("Single EXE rejects truncated footer",Reject(truncated,Path.Combine(root,"short")));
  var bounds=File.ReadAllBytes(exe);Array.Copy(BitConverter.GetBytes(long.MaxValue),0,bounds,bounds.Length-(16+32+System.Text.Encoding.ASCII.GetByteCount("SAA_AGENT_EXE1!")),8);var badBounds=Path.Combine(root,"bounds.exe");File.WriteAllBytes(badBounds,bounds);check("Single EXE rejects forged payload boundaries",Reject(badBounds,Path.Combine(root,"bounds")));
  var limitZip=Path.Combine(root,"limit.zip");using(var z=ZipFile.Open(limitZip,ZipArchiveMode.Create)){using var e=z.CreateEntry("Agent/too-large",CompressionLevel.SmallestSize).Open();var block=new byte[1024*1024];for(int n=0;n<151;n++)e.Write(block);}
  var limitExe=Path.Combine(root,"limit.exe");AgentSetupBundle.Pack(stub,limitZip,limitExe);check("Single EXE rejects oversized decompressed entry",Reject(limitExe,Path.Combine(root,"limit")));
 }
}
