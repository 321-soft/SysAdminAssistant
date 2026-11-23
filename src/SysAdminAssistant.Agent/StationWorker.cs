using Microsoft.Win32;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Agent;
internal static class StationWorker
{
    public const string Service="SysAdminAssistantWorkstation";
    private static readonly string Data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),Service);
    private sealed record Identity(Guid Id,byte[] Key);
    [DllImport("kernel32.dll")]private static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("wtsapi32.dll",EntryPoint="WTSQuerySessionInformationW")]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool Query(IntPtr server,uint session,int info,out IntPtr buffer,out int bytes);
    [DllImport("wtsapi32.dll")]private static extern void WTSFreeMemory(IntPtr buffer);
    private static string? Session(uint id,int field)
    {
        if(!Query(IntPtr.Zero,id,field,out var ptr,out _))return null;
        try{return Marshal.PtrToStringUni(ptr);}finally{WTSFreeMemory(ptr);}
    }
    private static Identity Load()
    {
        using var root=Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SysAdminAssistant\WorkstationIdentity");
        if(root.GetValue("Identity") is byte[] old)return JsonSerializer.Deserialize<Identity>(ProtectedData.Unprotect(old,null,DataProtectionScope.CurrentUser))!;
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var id=new Identity(Guid.NewGuid(),key.ExportPkcs8PrivateKey());
        root.SetValue("Identity",ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(id),null,DataProtectionScope.CurrentUser));
        return id;
    }
    [DllImport("kernel32.dll")]private static extern uint GetSystemFirmwareTable(uint provider,uint table,byte[]? buffer,uint size);
    private static string? Serial()
    {
        const uint rawSmbios=0x52534D42;
        var size=GetSystemFirmwareTable(rawSmbios,0,null,0);
        if(size is <8 or >1048576)return null;
        var bytes=new byte[size];
        if(GetSystemFirmwareTable(rawSmbios,0,bytes,size)!=size)return null;
        for(int i=8;i+4<=bytes.Length;){
            int type=bytes[i],length=bytes[i+1],start=i+length;
            if(length<4||start+1>=bytes.Length||type==127)return null;
            int end=start;
            while(end+1<bytes.Length&&(bytes[end]!=0||bytes[end+1]!=0))end++;
            if(end+1>=bytes.Length)return null;
            if(type==1&&length>=8&&bytes[i+7]>0){
                var strings=System.Text.Encoding.UTF8.GetString(bytes,start,end-start).Split('\0');
                int index=bytes[i+7]-1;
                if(index>=strings.Length)return null;
                var value=WindowsUpdateResults.Clean(strings[index].Trim(),128);
                return value.ToLowerInvariant() is "" or "default string" or "to be filled by o.e.m." or "system serial number" or "none" or "unknown"?null:value;
            }
            i=end+2;
        }
        return null;
    }
    private static StationSample Collect(WindowsCollector collector,OsUpdateStatus? updates,string? serial)
    {
        var m=collector.Read(0,false);
        using var os=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string Get(string key)=>Convert.ToString(os?.GetValue(key))??"";
        var build=Get("CurrentBuildNumber");var product=Get("ProductName");
        if(int.TryParse(build,out var b)&&b>=22000)product=product.Replace("Windows 10","Windows 11");
        var disk=m.Disks.FirstOrDefault(d=>string.Equals(d.Name,Path.GetPathRoot(Environment.SystemDirectory),StringComparison.OrdinalIgnoreCase));
        var session=WTSGetActiveConsoleSessionId();
        var user=session==uint.MaxValue?null:Session(session,5);
        var domain=string.IsNullOrWhiteSpace(user)?null:Session(session,7);
        var addresses=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback)
            .SelectMany(n=>{var ip=n.GetIPProperties();return ip.UnicastAddresses.Where(x=>!System.Net.IPAddress.IsLoopback(x.Address))
                .Select(x=>new StationAddress(WindowsUpdateResults.Clean(n.Name,120),x.Address.ToString(),ip.GatewayAddresses.Count>0));})
            .OrderByDescending(a=>a.Gateway).ThenBy(a=>a.Address.Contains(':')).Take(96).ToArray();
        return new(WindowsUpdateResults.Clean($"{product} {Get("DisplayVersion")} · {build}.{Get("UBR")}",160),m.UptimeSeconds,
            string.IsNullOrWhiteSpace(user)?null:string.IsNullOrWhiteSpace(domain)?user:domain+"\\"+user,addresses,m.CpuPercent,
            m.MemoryTotalBytes>0?100d*(m.MemoryTotalBytes-m.MemoryAvailableBytes)/m.MemoryTotalBytes:null,
            disk?.TotalBytes>0?100d*disk.FreeBytes/disk.TotalBytes:null,updates,typeof(StationWorker).Assembly.GetName().Version!.ToString(),serial,Hardware:m.Hardware);
    }
    public static async Task Run(CancellationToken token)
    {
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(1,31)),token);
        var collector=new WindowsCollector();Identity? id=null;string? serial=null;bool serialRead=false;
        Task<OsUpdateStatus>? scan=null;OsUpdateStatus? updates=null;var next=DateTime.MinValue;
        var nextUpdate=DateTime.UtcNow.AddMinutes(Random.Shared.Next(1,6));
        var installed=typeof(StationWorker).Assembly.GetName().Version!;
        bool cleaned=false, peripheralSupported=false;
        Task<StationPeripheralScan>? peripheralTask=null;
        StationPeripheralScan? peripherals=null;
        var nextPeripheral=DateTime.MinValue;
        while(!token.IsCancellationRequested){
            var stage = "Настройки рабочей станции";
            string tls = "Сертификат не получен";
            try{
                using var p=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\SysAdminAssistant\Workstation");
                using var package=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SysAdminAssistant\WorkstationPackage");
                var configured=package?.GetValue("ServerUrl") as string;
                bool packaged=Uri.TryCreate(configured,UriKind.Absolute,out _);
                var source=packaged?package:p;
                var pin=source?.GetValue("ServerSha256") as string??"";
                var enrollment=source?.GetValue("Enrollment") as string??"";
                using var identityRoot=Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SysAdminAssistant\WorkstationIdentity");
                var trustKey="Authority-"+StationWire.Hash((source?.GetValue("ServerUrl") as string??"")+"|"+pin.ToUpperInvariant());
                var authority=packaged?source?.GetValue("Authority") as string??"":identityRoot.GetValue(trustKey) as string??"";
                if(Uri.TryCreate(source?.GetValue("ServerUrl") as string,UriKind.Absolute,out var url)&&url.Scheme=="https"
                   &&url.UserInfo==""&&url.AbsolutePath=="/"&&url.Query==""&&url.Fragment==""&&pin.Length==64&&pin.All(Uri.IsHexDigit)){
                    stage = "Ключ рабочей станции";
                    id??=Load();
                    using var key=ECDsa.Create();key.ImportPkcs8PrivateKey(id.Key,out _);
                    using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false,UseCookies=false,
                        ServerCertificateCustomValidationCallback=(_,cert,_,_)=>{
                            var accepted=ServerTrust.Accept(cert,url.IdnHost.Trim('[',']'),new[]{pin},authority);
                            tls=accepted?"Сертификат принят":cert==null?"Сертификат не получен":"Сертификат отклонён";
                            return accepted;
                        }}){Timeout=TimeSpan.FromSeconds(20)};
                    if(authority.Length==0){
                        stage = "Получение доверия HTTPS";
                        client.BaseAddress=url;
                        var info=await client.GetFromJsonAsync<ServerTrustInfo>("/api/server-trust",token)
                            ??throw new InvalidDataException("Сервер не передал доверие HTTPS");
                        var trust=AgentTrust.Verify(info.Trust,info.AuthorityCertificate);
                        if(!trust.Pins.Contains(pin,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Отпечаток HTTPS не подтверждён");
                        authority=info.AuthorityCertificate;identityRoot.SetValue(trustKey,authority);
                    }
                    StationRequest Sign(string? payload,string registration="")
                    {
                        var r=new StationRequest(id.Id,Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),registration,Environment.MachineName,
                            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),Guid.NewGuid().ToString("N"),payload,"");
                        return r with {Signature=Convert.ToBase64String(key.SignData(StationWire.Bytes(r),HashAlgorithmName.SHA256))};
                    }
                    async Task<StationReply> Send(string? payload)
                    {
                        var r=Sign(payload,payload==null?enrollment:"");
                        stage=payload==null?"Регистрация или проверка статуса":"Отправка отчёта";
                        using var response=await client.PostAsJsonAsync(new Uri(url,"/agent/workstation-report"),r,AgentWire.Json,token);
                        response.EnsureSuccessStatusCode();
                        if(payload==null) peripheralSupported=response.Headers.TryGetValues(StationPeripherals.CapabilityHeader,out var features)&&features.Contains("1");
                        return await response.Content.ReadFromJsonAsync<StationReply>(AgentWire.Json,token)??throw new InvalidDataException("Нет ответа регистрации");
                    }
                    if((await Send(null)).Status=="Approved"){
                        stage = "Сбор показателей";
                        if(scan?.IsCompleted==true){updates=await scan;scan=null;}
                        if(scan==null&&DateTime.UtcNow>=next){scan=Task.Run(WindowsUpdateQuery.Read);next=DateTime.UtcNow.AddMinutes(30);}
                        if(!serialRead){serial=Serial();serialRead=true;}
                        if(peripheralSupported && peripheralTask?.IsCompleted==true)
                        {
                            try {
                                var collectedPeripherals=await peripheralTask;
                                peripherals=collectedPeripherals.Partial&&peripherals is {Devices.Length:>0} prior
                                    ?prior with{Partial=true,Errors=collectedPeripherals.Errors,AttemptedUtc=collectedPeripherals.CapturedUtc}:collectedPeripherals;
                            }
                            catch(Exception e) { CrashLog.Record(Data,"Инвентаризация периферии",e); }
                            peripheralTask=null;
                        }
                        if(peripheralSupported && peripheralTask==null && DateTime.UtcNow>=nextPeripheral)
                        {
                            peripheralTask=Task.Run(StationPeripheralCollector.Read);
                            nextPeripheral=DateTime.UtcNow.AddMinutes(2);
                        }
                        var sample=Collect(collector,updates,serial);
                        if(peripheralSupported&&peripherals is {} observed)sample=sample with{Peripherals=observed};
                        var payload=StationPayload.Encode(sample);
                        await Send(payload);
                        if (packaged && !cleaned && DateTime.UtcNow>=nextUpdate && Version.TryParse(identityRoot.GetValue("AttemptedRelease") as string,out var attempted) && installed>=attempted)
                        {
                            StationUpdater.CleanPackage();
                            cleaned=true;
                        }
                        if (packaged && DateTime.UtcNow>=nextUpdate)
                        {
                            stage="Автообновление рабочей станции";
                            nextUpdate=DateTime.UtcNow.AddMinutes(60+Random.Shared.Next(0,16));
                            await StationUpdater.TryUpdate(client,url,Sign(installed.ToString()),pin,authority,enrollment,installed,identityRoot,token);
                        }
                    }
                }
                else throw new InvalidDataException("Нет корректного адреса HTTPS или отпечатка сервера");
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){return;}
            catch(Exception e){CrashLog.Record(Data,e is HttpRequestException {HttpRequestError:HttpRequestError.SecureConnectionError}?stage+" · "+tls:stage,e);}
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(120,151)),token);
        }
    }
}
