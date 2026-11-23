// Shared with the .NET Framework launcher; deliberately compatible with C# 5.
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
namespace SysAdminAssistant.Core
{
 public static class ProductSetupBundle
 {
  const string Magic="SAA_PRODUCT_EX1!";
  const long Limit=1024L*1024*1024;
  private static bool Reserved(string name){foreach(char c in name)if(c<32||"<>\"|?*".IndexOf(c)>=0)return true;var stem=name.Split('.')[0].ToUpperInvariant();return stem=="CON"||stem=="PRN"||stem=="AUX"||stem=="NUL"||stem.Length==4&&(stem.StartsWith("COM")||stem.StartsWith("LPT"))&&stem[3]>='1'&&stem[3]<='9';}
  public static bool HasPayload(string executable)
  {
   using(var input=File.OpenRead(executable)){
    var magic=Encoding.ASCII.GetBytes(Magic);if(input.Length<16+32+magic.Length)return false;
    input.Position=input.Length-magic.Length;for(int i=0;i<magic.Length;i++)if(input.ReadByte()!=magic[i])return false;
    return true;
   }
  }
  public static void Pack(string launcher,string archive,string output)
  {
   using(var target=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
    using(var stub=File.OpenRead(launcher))stub.CopyTo(target);
    var start=target.Position;byte[] hash;
    using(var zip=File.OpenRead(archive)){if(zip.Length>Limit)throw new InvalidDataException("Слишком большой пакет");using(var sha=SHA256.Create())hash=sha.ComputeHash(zip);zip.Position=0;zip.CopyTo(target);}
    using(var writer=new BinaryWriter(target,Encoding.UTF8,true)){writer.Write(start);writer.Write(target.Position-start-8);writer.Write(hash);writer.Write(Encoding.ASCII.GetBytes(Magic));}
   }
  }
  public static void Extract(string executable,string destination,Action<int,string> progress)
  {
   using(var input=File.OpenRead(executable)){
    var footer=16+32+Magic.Length;if(input.Length<footer)throw new InvalidDataException("Пакет повреждён");input.Position=input.Length-footer;
    long start,length;byte[] expected;using(var reader=new BinaryReader(input,Encoding.UTF8,true)){start=reader.ReadInt64();length=reader.ReadInt64();expected=reader.ReadBytes(32);if(Encoding.ASCII.GetString(reader.ReadBytes(Magic.Length))!=Magic)throw new InvalidDataException("Нет установочного пакета");}
    if(start<2||length<22||length>Limit||start>input.Length-footer||length!=input.Length-footer-start)throw new InvalidDataException("Неверные границы пакета");
    input.Position=start;using(var memory=new MemoryStream()){
     var buffer=new byte[81920];long left=length;while(left>0){int n=input.Read(buffer,0,(int)Math.Min(left,buffer.Length));if(n==0)throw new EndOfStreamException();memory.Write(buffer,0,n);left-=n;}
     memory.Position=0;using(var sha=SHA256.Create()){var actual=sha.ComputeHash(memory);int mismatch=0;for(int i=0;i<32;i++)mismatch|=actual[i]^expected[i];if(mismatch!=0)throw new InvalidDataException("Контрольная сумма пакета не совпадает");}
     memory.Position=0;using(var zip=new ZipArchive(memory,ZipArchiveMode.Read)){
      if(zip.Entries.Count>5000)throw new InvalidDataException("Слишком много файлов");long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar;
      // Validate the entire directory before creating anything; reject aliases and Windows path escapes.
      foreach(var e in zip.Entries){var name=e.FullName.Replace('\\','/');if(name.StartsWith("/")||name.Contains(":"))throw new InvalidDataException("Недопустимый путь");foreach(var part in name.Split('/')){if(part==".."||part=="."||part.EndsWith(" ")||part.EndsWith(".")||Reserved(part))throw new InvalidDataException("Недопустимый путь");}var p=Path.GetFullPath(Path.Combine(destination,name.Replace('/',Path.DirectorySeparatorChar)));if(!p.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!names.Add(p))throw new InvalidDataException("Повтор или выход за каталог");total+=e.Length;if(e.Length>512L*1024*1024||total>Limit)throw new InvalidDataException("Превышен размер распаковки");}
      int i=0;foreach(var e in zip.Entries){var p=Path.Combine(destination,e.FullName.Replace('\\','/').Replace('/',Path.DirectorySeparatorChar));if(e.FullName.EndsWith("/")){Directory.CreateDirectory(p);continue;}Directory.CreateDirectory(Path.GetDirectoryName(p)??destination);using(var source=e.Open())using(var target=new FileStream(p,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var block=new byte[81920];long written=0;int read;while((read=source.Read(block,0,block.Length))!=0){written+=read;if(written>e.Length||written>512L*1024*1024)throw new InvalidDataException("Превышен размер файла");target.Write(block,0,read);}if(written!=e.Length)throw new InvalidDataException("Неверный размер файла");}if(progress!=null)progress(++i*100/zip.Entries.Count,e.FullName);}
     }
    }
   }
  }
 }
}
