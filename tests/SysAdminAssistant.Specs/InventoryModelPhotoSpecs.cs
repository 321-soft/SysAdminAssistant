using System.Buffers.Binary;
using System.IO.Compression;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

internal static class InventoryModelPhotoSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var rgb=new byte[]{255,0,0,0,255,0};
        var png=InventoryModelPhoto.Encode(2,1,rgb);
        int i=8;
        var chunks=new List<string>();
        byte[]? pixels=null;
        while(i+12<=png.Length)
        {
            var length=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(i));
            var name=System.Text.Encoding.ASCII.GetString(png,i+4,4);
            chunks.Add(name);
            if(name=="IDAT")
            {
                using var compressed=new MemoryStream(png,i+8,length);
                using var source=new ZLibStream(compressed,CompressionMode.Decompress);
                using var decoded=new MemoryStream();
                source.CopyTo(decoded);pixels=decoded.ToArray();
            }
            i+=12+length;
        }
        check("Model photo is a fresh PNG with exactly its bounded pixels and no metadata",
            i==png.Length&&chunks.SequenceEqual(["IHDR","IDAT","IEND"])&&pixels is not null&&pixels.SequenceEqual(new byte[]{0}.Concat(rgb)));
        bool Reject(Action a){try{a();return false;}catch(ArgumentException){return true;}}
        check("Photo rejects over-limit dimensions and inconsistent RGB",
            Reject(()=>InventoryModelPhoto.Encode(481,1,new byte[481*3]))&&Reject(()=>InventoryModelPhoto.Encode(2,1,[1])));
        check("Model photo key normalizes case and whitespace and distinguishes categories",
            InventoryModelPhoto.Key(new("Мышь"," Logitech ","G502"))==InventoryModelPhoto.Key(new("мышь","logitech","g502"))&&
            InventoryModelPhoto.Key(new("Клавиатура","Logitech","G502"))!=InventoryModelPhoto.Key(new("Мышь","Logitech","G502")));
        var viewer=new UserView("1","viewer",false,true,1,new(){{"inventory",1}});
        var editor=viewer with {Permissions=new(){{"inventory",2}}};
        AccessRules.Command(viewer,"inventory-photo-read");
        AccessRules.Command(editor,"inventory-photo-write");
        bool refused=false;
        try {AccessRules.Command(viewer,"inventory-photo-write");}
        catch(UnauthorizedAccessException){refused=true;}
        check("Inventory viewers can read photos but cannot replace them",refused);
        var catalog=new UsbCatalog(["046d  Logitech","\tc08b  G502 SE HERO Gaming Mouse","\tc077  Mouse","\tc52b  Unifying Receiver"]);
        StationPeripheral Device(string code,string name="HID-совместимая мышь")=>new("Mouse",name,"External","USB","",code);
        check("USB catalog fills generic names and preserves named hardware",
            catalog.Resolve(Device("VID_046D&PID_C08B")).Name=="Logitech G502 SE HERO Gaming Mouse"&&
            catalog.Resolve(Device("VID_046D&PID_C08B","Original product name")).Name=="Original product name");
        check("USB catalog distinguishes shared mouse IDs and wireless receivers",
            catalog.Resolve(Device("VID_046D&PID_C077")).Name.Contains("046D:C077")&&
            catalog.Resolve(Device("VID_046D&PID_C52B")).Name.Contains("через Logitech Unifying Receiver"));
    }
}
