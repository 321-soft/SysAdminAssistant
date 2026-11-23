using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace SysAdminAssistant.Core;

public sealed record ProductPackage(string Component,string Version,string MinimumVersion,string Url,long Bytes,string Sha256,string EulaSha256,int DatabaseSchema,string[]? Mirrors=null);
public sealed record ProductRelease(string Product,DateTime PublishedUtc,DateTime ExpiresUtc,ProductPackage Client,ProductPackage Server);
public sealed record ProductReleaseEnvelope(string Payload,string Signature);

public static class ProductReleases
{
    public const long MaximumBytes=1_000_000_000;
    public static string PublicKey {get{using var s=typeof(ProductReleases).Assembly.GetManifestResourceStream("SAA.ReleasePublicKey")!;using var r=new StreamReader(s);return r.ReadToEnd();}}
    public static Uri Https(string value){if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length!=0||uri.Fragment.Length!=0)throw new InvalidDataException("Use an HTTPS update URL without credentials or fragment.");return uri;}
    public static ProductRelease Verify(string envelope,string publicKey,DateTime utc)
    {
        if(envelope.Length>100_000)throw new InvalidDataException("Release manifest exceeds the size limit.");
        var signed=JsonSerializer.Deserialize<ProductReleaseEnvelope>(envelope)??throw new InvalidDataException("Empty release manifest.");
        byte[] payload,signature;
        try{payload=Convert.FromBase64String(signed.Payload);signature=Convert.FromBase64String(signed.Signature);}catch(Exception e)when(e is FormatException or ArgumentNullException){throw new InvalidDataException("Invalid release encoding.",e);}
        using var rsa=RSA.Create();
        try{rsa.FromXmlString(publicKey);}catch(Exception e)when(e is CryptographicException or System.Xml.XmlException or ArgumentException){throw new InvalidDataException("Update verification is not configured in this build. Contact the publisher.",e);}
        if(rsa.KeySize<3072||!rsa.VerifyData(payload,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new CryptographicException("Release signature is invalid; installation blocked.");
        var release=JsonSerializer.Deserialize<ProductRelease>(payload)??throw new InvalidDataException("Empty signed release.");
        if(release.Product!="SysAdminAssistant"||release.PublishedUtc.Kind!=DateTimeKind.Utc||release.ExpiresUtc.Kind!=DateTimeKind.Utc||release.PublishedUtc>utc.AddMinutes(5)||release.ExpiresUtc<=utc||release.ExpiresUtc<=release.PublishedUtc)throw new InvalidDataException("Release manifest expired, future-dated or belongs to another product. Check the clock.");
        Validate(release.Client,"client");Validate(release.Server,"server");return release;
    }
    private static void Validate(ProductPackage p,string component)
    {
        static bool Hex(string value)=>value is {Length:64}&&value.All(Uri.IsHexDigit);
        if(p==null||p.Component!=component||!Version.TryParse(p.Version,out var next)||!Version.TryParse(p.MinimumVersion,out var minimum)||minimum>next||p.Bytes<1||p.Bytes>MaximumBytes||!Hex(p.Sha256)||!Hex(p.EulaSha256)||p.DatabaseSchema!=7)throw new InvalidDataException("Invalid package metadata or unsupported database schema.");
        _=Https(p.Url);if(p.Mirrors is {Length:>3})throw new InvalidDataException("Too many package mirrors.");foreach(var mirror in p.Mirrors??[])_=Https(mirror);
    }
    public static void RequireUpgrade(ProductPackage p,Version installed,string component)
    {
        if(p.Component!=component||!Version.TryParse(p.Version,out var next)||next<=installed||installed<Version.Parse(p.MinimumVersion))throw new InvalidOperationException("No compatible newer version is available.");
        if(!p.EulaSha256.Equals(LicenseAgreement.Hash,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("The release contains a new license agreement. Use interactive setup to review the new terms.");
    }
    private static HttpClient Http()=>new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(15)};
    private static async Task<HttpResponseMessage> Get(HttpClient http,Uri uri,CancellationToken token)
    {
        for(int attempt=0;attempt<6;attempt++){
            var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
            if((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))return response;
            var location=response.Headers.Location;response.Dispose();if(location==null)throw new HttpRequestException("Redirect destination missing.");
            uri=Https((location.IsAbsoluteUri?location:new Uri(uri,location)).AbsoluteUri);
        }
        throw new HttpRequestException("Too many update redirects.");
    }
    public static async Task<string> ManifestAsync(string primary,string mirror,CancellationToken token)
    {
        List<Exception> failures=[];
        foreach(var source in new[]{primary,mirror}.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct()){
            try{
                using var http=Http();using var response=await Get(http,Https(source),token);response.EnsureSuccessStatusCode();
                await using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();byte[] buffer=new byte[8192];int read;
                while((read=await input.ReadAsync(buffer,token))!=0){if(output.Length+read>100_000)throw new InvalidDataException("Manifest exceeds size limit.");output.Write(buffer,0,read);}
                var json=System.Text.Encoding.UTF8.GetString(output.ToArray());_=Verify(json,PublicKey,DateTime.UtcNow);return json;
            }catch(Exception e)when(e is IOException or HttpRequestException or CryptographicException or TaskCanceledException or JsonException){if(token.IsCancellationRequested)throw;failures.Add(e);}
        }
        throw new AggregateException("Could not retrieve a verified release from configured sources.",failures);
    }
    public static async Task DownloadAsync(ProductPackage package,string destination,IProgress<int>? progress,CancellationToken token)
    {
        if(package.Component is not ("client" or "server"))throw new InvalidDataException("Invalid update component.");Validate(package,package.Component);
        List<Exception> failures=[];
        foreach(var source in new[]{package.Url}.Concat(package.Mirrors??[]).Distinct()){
            try{await DownloadOneAsync(package with{Url=source},destination,progress,token);return;}
            catch(Exception e)when(e is IOException or HttpRequestException or CryptographicException or TaskCanceledException){if(token.IsCancellationRequested)throw;failures.Add(e);}
        }
        throw new AggregateException("No package source returned the exact signed package.",failures);
    }
    private static async Task DownloadOneAsync(ProductPackage package,string destination,IProgress<int>? progress,CancellationToken token)
    {
        var temporary=destination+"."+Guid.NewGuid().ToString("N")+".part";
        try{
            using var http=Http();using var response=await Get(http,Https(package.Url),token);response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is long size&&size!=package.Bytes)throw new InvalidDataException("Package size differs from signed metadata.");
            await using var input=await response.Content.ReadAsStreamAsync(token);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long length=0;
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true)){
                byte[] buffer=new byte[81920];int read;
                while((read=await input.ReadAsync(buffer,token))!=0){length+=read;if(length>package.Bytes||length>MaximumBytes)throw new InvalidDataException("Package exceeds signed size.");hash.AppendData(buffer,0,read);await output.WriteAsync(buffer.AsMemory(0,read),token);progress?.Report((int)(length*100/package.Bytes));}
                await output.FlushAsync(token);
            }
            if(length!=package.Bytes||!Convert.ToHexString(hash.GetHashAndReset()).Equals(package.Sha256,StringComparison.OrdinalIgnoreCase))throw new CryptographicException("Package SHA-256 differs from signed metadata; installation blocked.");
            File.Move(temporary,destination,true);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
