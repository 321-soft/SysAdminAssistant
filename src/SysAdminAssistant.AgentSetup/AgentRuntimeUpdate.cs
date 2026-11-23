using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Win32;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.AgentSetup;

internal static partial class Program
{
    private const string RuntimeRebootMarker = @"SOFTWARE\SysAdminAssistantAgentRuntimeReboot";

    private static bool DesktopRuntimeReady()
    {
        var shared=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"dotnet","shared");
        return AgentDesktopRuntime.HasRequiredFramework(Path.Combine(shared,"Microsoft.NETCore.App"),"System.Private.CoreLib.dll") &&
            AgentDesktopRuntime.HasRequiredFramework(Path.Combine(shared,"Microsoft.WindowsDesktop.App"),"PresentationFramework.dll");
    }

    // Called with the maintenance mutex held, before stopping the agent or replacing any of its files.
    private static async Task<bool> PrepareDesktopRuntime(HttpClient http,Func<AgentReleaseRequest> request,AgentReleaseDocument release,CancellationToken token)
    {
        using(var pending=Registry.LocalMachine.OpenSubKey(RuntimeRebootMarker)){
            if(pending!=null){Stage("Ожидание перезагрузки после установки .NET");AutoStatus("Для завершения установки .NET требуется плановая перезагрузка. Агент продолжает работать; автоматической перезагрузки не будет.");return false;}
        }
        if(DesktopRuntimeReady())return true;
        Stage("Подготовка .NET Desktop Runtime "+AgentDesktopRuntime.Version+" x64");
        if(release.Runtime is not {} descriptor)
            throw new InvalidOperationException("Сервер не опубликовал проверенный установщик .NET. Включите RuntimePackages при публикации сервера; файлы агента не изменены.");
        var package=await Response(http,"agent/update-file",new AgentReleaseDownload(request(),"Runtime",AgentDesktopRuntime.FileName),descriptor.Length,token);
        AgentRelease.VerifyFile(package,descriptor);
        if(!AgentDesktopRuntime.MatchesMicrosoftPackage(package))
            throw new CryptographicException("SHA-512 установщика .NET не совпадает с опубликованным Microsoft.");
        if(release.ExpiresUtc<=DateTime.UtcNow)
            throw new CryptographicException("Выпуск просрочен до установки .NET; будет выполнена повторная проверка.");
        var directory=Path.Combine(Maintenance,"runtime-"+Guid.NewGuid().ToString("N"));
        Safe(directory);Directory.CreateDirectory(directory);Safe(directory);
        var path=Path.Combine(directory,AgentDesktopRuntime.FileName);
        try{
            using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(package);file.Flush(true);}
            var start=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=directory};
            foreach(var arg in new[]{"/install","/quiet","/norestart"})start.ArgumentList.Add(arg);
            Stage("Установка .NET Desktop Runtime без перезагрузки");
            AutoStatus("Устанавливается .NET Desktop Runtime "+AgentDesktopRuntime.Version+". Служба агента ещё не остановлена.");
            using var installer=Process.Start(start)??throw new IOException("Не удалось запустить установщик .NET.");
            // Do not interrupt Windows Installer. The maintenance task already has a bounded execution time.
            await installer.WaitForExitAsync();
            if(installer.ExitCode==3010){
                using var marker=Registry.LocalMachine.CreateSubKey(RuntimeRebootMarker,true,RegistryOptions.Volatile);
                Stage("Ожидание перезагрузки после установки .NET");
                AutoStatus("Установщик .NET требует плановую перезагрузку (3010). Агент продолжает работать; автоматической перезагрузки не будет.");
                return false;
            }
            if(installer.ExitCode!=0||!DesktopRuntimeReady())
                throw new IOException("Установка .NET не подтверждена (код "+installer.ExitCode+"). Файлы агента не изменены.");
            return true;
        }finally{
            try{File.Delete(path);Directory.Delete(directory);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
