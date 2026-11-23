using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace SysAdminAssistant.Core;

public sealed record InventoryModelKey(string Category,string Manufacturer,string Model);
public sealed record InventoryPhotoWrite(InventoryModelKey Key,int Width,int Height,byte[] Rgb);
public sealed record InventoryPhotoResult(byte[]? Png);

public static class InventoryModelPhoto
{
    public const int MaxSide=480;
    public const int MaxOriginalBytes=8*1024*1024;
    public const int MaxOriginalPixels=16_000_000;
    public const int MaxPngBytes=MaxSide*MaxSide*3+8192;

    public static string Key(InventoryModelKey value)
    {
        ArgumentNullException.ThrowIfNull(value);
        static string Clean(string value)
        {
            if(string.IsNullOrWhiteSpace(value)||value.Length>160||value.Any(char.IsControl))
                throw new ArgumentException("Укажите категорию, производителя и модель.");
            return value.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n",Clean(value.Category),Clean(value.Manufacturer),Clean(value.Model)))));
    }

    // Encode pixels into a new PNG. No original container, metadata, chunks or links cross the trust boundary.
    public static byte[] Encode(int width,int height,byte[] rgb)
    {
        if(width is <1 or >MaxSide||height is <1 or >MaxSide||rgb==null||rgb.Length!=width*height*3)
            throw new ArgumentException("Недопустимый размер изображения.");
        using var result=new MemoryStream();
        result.Write(new byte[]{137,80,78,71,13,10,26,10});
        byte[] header=new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header,width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);
        header[8]=8;header[9]=2;
        Chunk(result,"IHDR"u8,header);
        using var compressed=new MemoryStream();
        using(var z=new ZLibStream(compressed,CompressionLevel.Fastest,true))
            for(int row=0;row<height;row++)
            {
                z.WriteByte(0);
                z.Write(rgb,row*width*3,width*3);
            }
        Chunk(result,"IDAT"u8,compressed.ToArray());
        Chunk(result,"IEND"u8,[]);
        return result.ToArray();
    }
    private static void Chunk(Stream output,ReadOnlySpan<byte> name,ReadOnlySpan<byte> bytes)
    {
        Span<byte> number=stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number,bytes.Length);
        output.Write(number);output.Write(name);output.Write(bytes);
        uint crc=uint.MaxValue;
        foreach(var b in name)crc=Crc(crc,b);
        foreach(var b in bytes)crc=Crc(crc,b);
        BinaryPrimitives.WriteUInt32BigEndian(number,~crc);
        output.Write(number);
    }
    private static uint Crc(uint crc,byte b)
    {
        crc^=b;
        for(int i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xEDB88320u:crc>>1;
        return crc;
    }
}
