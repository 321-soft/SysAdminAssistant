using System.IO.Compression;
using System.Text;
using SysAdminAssistant.Infrastructure;
using SysAdminAssistant.Core;
internal static class WatchdogPackageSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var plan=new WatchdogDeployment("beget","/home/u/site.test","/home/u/site.test/public_html","/usr/local/bin/php8.4","https://site.test/heartbeat.php","assistant-server","Server's `test` $HOST");
        var secret=new string('a',64);
        var bytes=WatchdogPackage.Create(plan,secret,"123456:FAKE_TEST_TOKEN","-12345");
        using var zip=new ZipArchive(new MemoryStream(bytes));
        string Read(string path){using var reader=new StreamReader(zip.GetEntry(path)!.Open());return reader.ReadToEnd();}
        check("Watchdog archive has only five runtime files and two instructions",zip.Entries.Count==7&&zip.Entries.All(e=>!e.FullName.Contains("..")));
        check("Watchdog embeds complete PHP receiver and worker",Read("private/receiver.php").Contains("saa_config")&&Read("private/worker.php").Contains("saa_config")&&Read("private/lib.php").Contains("function saa_config"));
        var config=Read("private/config.php");
        check("Watchdog private config reuses passed key and current Telegram",config.Contains(secret)&&config.Contains("123456:FAKE_TEST_TOKEN")&&config.Contains("'telegram_chat' => '-12345'"));
        check("Watchdog state shares private directory without extra folder",config.Contains("'state_directory' => __DIR__")&&!Read("START-HERE.txt").Contains("saa-watchdog-state"));
        check("Watchdog PHP strings escape quotes without interpolating names",config.Contains("Server\\'s `test` $HOST")&&WatchdogPackage.PhpString("a\\b'c")=="'a\\\\b\\'c'");
        check("Watchdog public adapter has only private require, no credentials",Read("public/heartbeat.php").Contains("'/home/u/site.test/receiver.php'")&&!Read("public/heartbeat.php").Contains(secret)&&!Read("public/heartbeat.php").Contains("FAKE_TEST_TOKEN"));
        check("Watchdog instructions contain exact commands and no credentials",Read("START-HERE.txt").Contains("'/usr/local/bin/php8.4' '/home/u/site.test/worker.php' init")&&!Read("START-HERE.txt").Contains(secret)&&!Read("cron.txt").Contains("FAKE_TEST_TOKEN"));
        check("Watchdog cron runs check every five minutes with private log",Read("cron.txt")=="*/5 * * * * '/usr/local/bin/php8.4' '/home/u/site.test/worker.php' check >> '/home/u/site.test/cron.log' 2>&1\n");
        check("Watchdog shell quoting contains apostrophes safely",WatchdogPackage.Shell("/home/user's/site")=="'/home/user'\"'\"'s/site'");
        foreach(var invalid in new[]{plan with{PrivateDirectory=plan.PublicDirectory},plan with{PrivateDirectory=plan.PublicDirectory+"/private"},plan with{PrivateDirectory="/home/u/../public"},plan with{PhpExecutable="php"},plan with{PhpExecutable="/usr/bin/php\nwhoami"},plan with{PrivateDirectory="/home/%USER/site"},plan with{EndpointUrl="https://site.test/other.php"},plan with{Profile="unknown"}}){bool reject=false;try{WatchdogPackage.Create(invalid,secret,"123:FAKE","123");}catch(ArgumentException){reject=true;}check("Watchdog rejects invalid layout, command or URL",reject);}
        foreach(var profile in new[]{"beget","shared","self"}){using var z=new ZipArchive(new MemoryStream(WatchdogPackage.Create(plan with{Profile=profile},secret,"123:FAKE","1")));check("Watchdog profile builds: "+profile,z.Entries.Count==7);}
        bool bad=false;try{WatchdogPackage.Create(plan,secret,"REMOTE","1");}catch(ArgumentException){bad=true;}check("Watchdog never exports placeholder Telegram credentials",bad);
        var viewer=new UserView("viewer","viewer",false,true,1,new Dictionary<string,int>{{"settings",2}});
        bad=false;try{AccessRules.Command(viewer,"server-heartbeat-package");}catch(UnauthorizedAccessException){bad=true;}check("Watchdog secret archive blocked for delegated settings editor",bad);
        var sample=Environment.GetEnvironmentVariable("SAA_WATCHDOG_TEST_ZIP");if(!string.IsNullOrEmpty(sample))File.WriteAllBytes(sample,bytes);
    }
}
