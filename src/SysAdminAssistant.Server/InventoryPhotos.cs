using SysAdminAssistant.Core;

namespace SysAdminAssistant.Server;

internal static class InventoryPhotos
{
    public static InventoryPhotoResult Read(string root,InventoryModelKey key)
    {
        var path=PathFor(root,key);
        if(!File.Exists(path))return new(null);
        using var file=File.OpenRead(path);
        if(file.Length is <=0 or >InventoryModelPhoto.MaxPngBytes)throw new InvalidDataException("Некорректное фото модели.");
        var bytes=new byte[(int)file.Length];file.ReadExactly(bytes);
        return new(bytes);
    }
    public static bool Save(string root,InventoryPhotoWrite photo)
    {
        var path=PathFor(root,photo.Key);
        var png=InventoryModelPhoto.Encode(photo.Width,photo.Height,photo.Rgb);
        var directory=Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        if(!File.Exists(path)&&Directory.EnumerateFiles(directory,"*.png").Take(2000).Count()>=2000)
            throw new InvalidOperationException("Достигнут лимит фотографий моделей.");
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllBytes(temp,png);File.Move(temp,path,true);}
        finally {if(File.Exists(temp))File.Delete(temp);}
        return true;
    }
    private static string PathFor(string root,InventoryModelKey key)=>Path.Combine(root,"inventory-model-photos",InventoryModelPhoto.Key(key)+".png");
}
