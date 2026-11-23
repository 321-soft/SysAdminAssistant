using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private static void FindModelPhoto(InventoryModelKey model,string serial="",string machine="")
    {
        _=InventoryModelPhoto.Key(model);
        var query=model.Manufacturer.Trim()+" "+model.Model.Trim();
        if((serial.Length>0&&query.Contains(serial,StringComparison.OrdinalIgnoreCase))||
           (machine.Length>0&&query.Contains(machine,StringComparison.OrdinalIgnoreCase))||
           System.Text.RegularExpressions.Regex.IsMatch(query,@"\b(?:\d{1,3}\.){3}\d{1,3}\b"))
            throw new ArgumentException("Укажите только производителя и модель, без серийного номера, имени ПК или IP.");
        using var process=Process.Start(new ProcessStartInfo("https://yandex.ru/images/search?text="+Uri.EscapeDataString(query)){UseShellExecute=true});
    }

    private void AddInventoryPhotoTab(FormDialog dialog,Func<InventoryModelKey> model,bool canEdit,
        Func<string> serial,Func<string> machine)
    {
        var preview=new Image {MaxWidth=480,MaxHeight=235,Stretch=Stretch.Uniform};
        var status=new TextBlock{Text="Нажмите «Показать сохранённое фото», чтобы проверить текущую модель.",TextWrapping=TextWrapping.Wrap};
        dialog.Tab("Фото модели").Note("Фото общее для имущества с той же категорией, производителем и моделью.")
            .Element(preview).Element(status);
        async Task RefreshPhoto()
        {
            preview.Source=null;
            var key=model();
            _=InventoryModelPhoto.Key(key);
            var result=await remote.CommandAsync<InventoryPhotoResult>("inventory-photo-read",key,lifetime.Token);
            if(result.Png is not {Length:>0 and <=InventoryModelPhoto.MaxPngBytes} png)
            {
                status.Text="Для этой модели нет фото.";
                return;
            }
            using var stream=new MemoryStream(png,false);
            var image=new BitmapImage();
            image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();
            image.Freeze();preview.Source=image;status.Text=key.Manufacturer+" "+key.Model;
        }
        dialog.ActionAsync("Показать сохранённое фото",RefreshPhoto)
            .Action("Найти фото",()=>FindModelPhoto(model(),serial(),machine()));
        if(!canEdit)return;
        dialog.ActionAsync("Добавить фото…",async ()=>{
            var key=model();
            _=InventoryModelPhoto.Key(key);
            var file=new Microsoft.Win32.OpenFileDialog{Title="Фото модели",Filter="Фотографии JPEG и PNG|*.jpg;*.jpeg;*.png",CheckFileExists=true};
            if(file.ShowDialog(dialog)!=true)return;
            var photo=await Task.Run(()=>ReadInventoryPhoto(file.FileName,key),lifetime.Token);
            if(!Confirm(dialog,"Сохранить это фото для всех карточек той же модели? Предыдущее фото модели будет заменено."))return;
            await remote.CommandAsync<bool>("inventory-photo-write",photo,lifetime.Token);
            await RefreshPhoto();
        });
    }

    private static InventoryPhotoWrite ReadInventoryPhoto(string path,InventoryModelKey key)
    {
        byte[] bytes;
        using(var source=File.OpenRead(path))
        {
            if(source.Length is <24 or >InventoryModelPhoto.MaxOriginalBytes)
                throw new InvalidDataException("Файл должен быть JPEG или PNG размером до 8 МБ.");
            bytes=new byte[(int)source.Length];source.ReadExactly(bytes);
            if(source.ReadByte()!=-1)throw new InvalidDataException("Размер файла изменился.");
        }
        var (width,height,png)=PhotoDimensions(bytes);
        if(width<=0||height<=0||(long)width*height>InventoryModelPhoto.MaxOriginalPixels)
            throw new InvalidDataException("Максимум 16 миллионов пикселей.");
        using var input=new MemoryStream(bytes,false);
        var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnDemand);
        if(png?decoder is not PngBitmapDecoder:decoder is not JpegBitmapDecoder)
            throw new InvalidDataException("Содержимое файла не соответствует JPEG или PNG.");
        var frame=decoder.Frames[0];
        if(frame.PixelWidth!=width||frame.PixelHeight!=height)
            throw new InvalidDataException("Размер изображения не совпадает с заголовком.");
        var scale=Math.Min(1d,InventoryModelPhoto.MaxSide/(double)Math.Max(width,height));
        BitmapSource resized=scale<1?new TransformedBitmap(frame,new ScaleTransform(scale,scale)):frame;
        var rgb=new FormatConvertedBitmap(resized,PixelFormats.Rgb24,null,0);
        var pixels=new byte[checked(rgb.PixelWidth*rgb.PixelHeight*3)];
        rgb.CopyPixels(pixels,rgb.PixelWidth*3,0);
        return new(key,rgb.PixelWidth,rgb.PixelHeight,pixels);
    }

    private static (int Width,int Height,bool Png) PhotoDimensions(ReadOnlySpan<byte> data)
    {
        if(data.StartsWith(new byte[]{137,80,78,71,13,10,26,10})&&data[8..16].SequenceEqual("\0\0\0\rIHDR"u8))
            return(BinaryPrimitives.ReadInt32BigEndian(data[16..]),BinaryPrimitives.ReadInt32BigEndian(data[20..]),true);
        if(data[0]==0xff&&data[1]==0xd8)
        {
            for(int at=2;at+4<=data.Length;)
            {
                if(data[at++]!=0xff)break;
                while(at<data.Length&&data[at]==0xff)at++;
                if(at+2>=data.Length)break;
                var marker=data[at++];
                if(marker is 0xda or 0xd9)break;
                var size=BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
                if(size<2||at+size>data.Length)break;
                if(marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
                {
                    if(size<8)break;
                    return(BinaryPrimitives.ReadUInt16BigEndian(data[(at+5)..]),BinaryPrimitives.ReadUInt16BigEndian(data[(at+3)..]),false);
                }
                at+=size;
            }
        }
        throw new InvalidDataException("Выберите корректный файл JPEG или PNG.");
    }
}
