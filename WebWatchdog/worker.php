<?php
declare(strict_types=1);
if(PHP_SAPI!=='cli'){http_response_code(404);exit;}
require_once __DIR__.'/lib.php';
umask(0077);$lock=null;
try{
    $c=saa_config();$lock=saa_lock($c,true);$now=time();$s=saa_load($c,$now);
    $mode=$argv[1]??'check';
    if($mode==='test'){
        $sent=saa_telegram($c,saa_text($c,"ℹ️ Тест PHP-наблюдателя\n","ℹ️ PHP watchdog test\n").$c['name'].saa_text($c,"\nСвязь сайта с Telegram работает. Cron и приём сигналов проверяются отдельно.","\nWebsite connectivity to Telegram works. Test cron and heartbeat reception separately."));
        if(!$sent['ok'])throw new RuntimeException('Telegram test not confirmed');echo "Telegram test confirmed\n";
    }elseif($mode==='status'){
        echo json_encode(['last_seen_utc'=>$s['last_seen']===null?null:gmdate('c',$s['last_seen']),'down_notified'=>$s['notified_down'],'maintenance'=>$c['maintenance']??false],JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR)."\n";
    }elseif($mode==='check'||$mode==='init'){
        if($mode==='check')$s=saa_evaluate($c,$s,$now,fn(string $text):array=>saa_telegram($c,$text));saa_save($c,$s);echo "Check completed\n";
    }else throw new RuntimeException('Mode must be check, init, status or test');
}catch(Throwable $e){fwrite(STDERR,"Watchdog failed: check private configuration, PHP curl, state permissions and Telegram access. No secrets logged.\n");exit(1);}
finally{if(is_resource($lock)){flock($lock,LOCK_UN);fclose($lock);}}
