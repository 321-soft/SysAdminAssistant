using System.Diagnostics;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Tesseract;
namespace SysAdminAssistant.Server;
public static class InvoiceExtraction
{
    public static async Task<int> ExtractAsync(string input,string output,bool convert)
    {
        try{
            if(new FileInfo(input).Length>8_000_000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Файл больше 8 МБ"));var bytes=await System.IO.File.ReadAllBytesAsync(input);var type=Type(bytes);
            if(convert){if(type=="application/pdf"){await System.IO.File.WriteAllBytesAsync(output,bytes);return 0;}using var pix=Pix.LoadFromMemory(bytes);if((long)pix.Width*pix.Height>25_000_000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большое изображение"));using var builder=new UglyToad.PdfPig.Writer.PdfDocumentBuilder();var width=595d;var height=width*pix.Height/pix.Width;var page=builder.AddPage(width,height);var rect=new UglyToad.PdfPig.Core.PdfRectangle(0,0,width,height);if(type=="image/png")page.AddPng(bytes,rect);else page.AddJpeg(bytes,rect);await System.IO.File.WriteAllBytesAsync(output,builder.Build());return 0;}
            string text="",method="PDF text";float confidence=1;
            if(type=="application/pdf"){
                using var pdf=PdfDocument.Open(bytes);if(pdf.NumberOfPages>10)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Не более 10 страниц"));var parts=new List<string>();
                foreach(var p in pdf.GetPages()){
                    var t=ContentOrderTextExtractor.GetText(p);if(t.Trim().Length>=40){parts.Add(t);continue;}method="PDF + OCR";var images=p.GetImages().OrderByDescending(i=>(long)i.WidthInSamples*i.HeightInSamples).Take(2).ToArray();if(images.Length==0)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось извлечь текст / скан PDF"));
                    foreach(var image in images){if((long)image.WidthInSamples*image.HeightInSamples>25_000_000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой скан"));byte[] imageBytes;var raw=image.RawBytes.ToArray();if(raw.Length>2&&raw[0]==255&&raw[1]==216)imageBytes=raw;else if(!image.TryGetPng(out imageBytes!))throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Не поддержан формат изображения внутри PDF"));var ocr=Ocr(imageBytes);parts.Add(ocr.Text);confidence=Math.Min(confidence,ocr.Confidence);}
                }text=string.Join("\n",parts);
            }else{method="OCR";var ocr=Ocr(bytes);text=ocr.Text;confidence=ocr.Confidence;}
            if(text.Length>100000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком много текста"));await System.IO.File.WriteAllTextAsync(output,JsonSerializer.Serialize(new InvoiceExtract(text,method,confidence),RemoteData.Json));return 0;
        }catch(Exception e){if(!convert)await System.IO.File.WriteAllTextAsync(output,JsonSerializer.Serialize(new InvoiceExtract("","Ошибка извлечения",0,e is InvalidDataException?e.Message:"OCR/PDF недоступен: "+e.GetType().Name),RemoteData.Json));return 1;}
    }
    static InvoiceExtract Ocr(byte[] bytes){using var pix=Pix.LoadFromMemory(bytes);if((long)pix.Width*pix.Height>25_000_000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большое изображение"));using var engine=new TesseractEngine(Path.Combine(AppContext.BaseDirectory,"tessdata"),"rus+eng",EngineMode.Default);using var page=engine.Process(pix);return new(page.GetText(),"OCR",page.GetMeanConfidence());}
    public static string Type(byte[] b)=>b.Length>=5&&System.Text.Encoding.ASCII.GetString(b,0,5)=="%PDF-"?"application/pdf":b.Length>=8&&b.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})?"image/png":b.Length>=3&&b[0]==255&&b[1]==216&&b[2]==255?"image/jpeg":throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Поддерживаются PDF, PNG, JPEG по содержимому файла"));
    private static readonly SemaphoreSlim NativeGate=new(1,1);
    public static async Task<byte[]> Run(string directory,byte[] bytes,bool convert,CancellationToken token){await NativeGate.WaitAsync(token);try{return await RunCore(directory,bytes,convert,token);}finally{NativeGate.Release();}}
    private static async Task<byte[]> RunCore(string directory,byte[] bytes,bool convert,CancellationToken token)
    {
        var root=Path.Combine(directory,"invoice-temp",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var input=Path.Combine(root,"input.bin");var output=Path.Combine(root,convert?"output.pdf":"output.json");
        try{
            await System.IO.File.WriteAllBytesAsync(input,bytes,token);var exe=Environment.ProcessPath??throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Не найден процесс сервера"));var pi=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppContext.BaseDirectory};if(Path.GetFileNameWithoutExtension(exe).Equals("dotnet",StringComparison.OrdinalIgnoreCase))pi.ArgumentList.Add(typeof(InvoiceExtraction).Assembly.Location);pi.ArgumentList.Add(convert?"--invoice-convert":"--invoice-extract");pi.ArgumentList.Add(input);pi.ArgumentList.Add(output);
            using var process=Process.Start(pi)??throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Не удалось запустить распознавание"));using var job=InvoiceProcessJob.Attach(process);using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(60));try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);throw;}if(!System.IO.File.Exists(output)||new FileInfo(output).Length>(convert?16_000_000:1_000_000))throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Не получен результат распознавания"));return await System.IO.File.ReadAllBytesAsync(output,token);
        }finally{try{Directory.Delete(root,true);}catch(IOException){}}
    }
}
