<?php
declare(strict_types=1);
// Private library. Keep this entire directory outside the site's document root.
function saa_config(): array {
    $path = getenv('SAA_WATCHDOG_CONFIG') ?: __DIR__ . '/config.php';
    if (!is_file($path)) throw new RuntimeException('Configuration missing');
    $documentRoot = $_SERVER['DOCUMENT_ROOT'] ?? '';
    $doc = $documentRoot !== '' ? (realpath($documentRoot) ?: '') : '';
    $configPath = realpath($path);
    if ($doc !== '' && ($configPath === $doc || str_starts_with((string)$configPath, $doc . '/'))) throw new RuntimeException('Configuration must be private');
    $c = require $path;
    if (!is_array($c) || !preg_match('/\A[A-Za-z0-9_-]{1,64}\z/', $c['server_id'] ?? '') ||
        !preg_match('/\A[0-9a-fA-F]{64}\z/', $c['secret'] ?? '') ||
        !is_string($c['state_directory'] ?? null) || !str_starts_with($c['state_directory'], '/')) throw new RuntimeException('Invalid configuration');
    $dir = realpath($c['state_directory']);
    if (!$dir || is_link($c['state_directory']) || ($doc !== '' && ($dir === $doc || str_starts_with($dir, $doc . '/')))) throw new RuntimeException('State must be private');
    return $c;
}
function saa_text(array $c,string $ru,string $en): string { return ($c['language']??'ru')==='en'?$en:$ru; }
function saa_initial(int $now): array {
    return ['version'=>1, 'watch_since'=>$now, 'last_seen'=>null, 'last_signed'=>0, 'nonces'=>[], 'notified_down'=>false, 'outage_since'=>null, 'next_attempt'=>0];
}
function saa_load(array $c, int $now): array {
    $p=$c['state_directory'].'/state.json';
    if (!file_exists($p)) return saa_initial($now);
    if (is_link($p) || filesize($p)>131072) throw new RuntimeException('Invalid state');
    $s=json_decode((string)file_get_contents($p),true,16,JSON_THROW_ON_ERROR);
    if (!is_array($s) || ($s['version']??0)!==1 || !is_array($s['nonces']??null) || !is_bool($s['notified_down']??null) || !is_int($s['watch_since']??null) || !is_int($s['last_signed']??null) || !is_int($s['next_attempt']??null) || (!is_null($s['last_seen']??null) && !is_int($s['last_seen']))) throw new RuntimeException('Invalid state');
    return $s;
}
function saa_save(array $c,array $s): void {
    $temp=$c['state_directory'].'/.state-'.bin2hex(random_bytes(12));
    try {$json=json_encode($s,JSON_THROW_ON_ERROR|JSON_UNESCAPED_UNICODE);if(file_put_contents($temp,$json,LOCK_EX)!==strlen($json))throw new RuntimeException('Write failed');chmod($temp,0600);if(!rename($temp,$c['state_directory'].'/state.json'))throw new RuntimeException('Rename failed');}finally{if(is_file($temp))unlink($temp);}
}
function saa_lock(array $c,bool $cron=false) {
    $p=$c['state_directory'].'/watchdog.lock';if(is_link($p))throw new RuntimeException('Invalid lock');
    $f=fopen($p,'c');if($f===false)throw new RuntimeException('Lock unavailable');chmod($p,0600);
    $until=microtime(true)+3;
    do {if(flock($f,LOCK_EX|LOCK_NB))return $f;if($cron)break;usleep(20000);}while(microtime(true)<$until);
    fclose($f);throw new RuntimeException('Busy');
}
function saa_mac(array $c,string $kind,array $p): string {
    return hash_hmac('sha256',"saa-{$kind}-v1\n".$p['serverId']."\n".$p['timestamp']."\n".$p['nonce'],hex2bin($c['secret']));
}
function saa_authenticate_heartbeat(array $c,string $raw,int $now): array {
    if(strlen($raw)>2048)throw new InvalidArgumentException('Invalid request');
    $p=json_decode($raw,true,8,JSON_THROW_ON_ERROR);
    if(!is_array($p)||($p['version']??null)!==1||($p['serverId']??null)!==$c['server_id']||!is_int($p['timestamp']??null)||abs($now-$p['timestamp'])>120||!is_string($p['nonce']??null)||!preg_match('/\A[0-9a-f]{32}\z/',$p['nonce'])||!is_string($p['signature']??null)||!preg_match('/\A[0-9a-f]{64}\z/',$p['signature'])||!hash_equals(saa_mac($c,'heartbeat',$p),$p['signature'])) throw new InvalidArgumentException('Authentication rejected');
    return $p;
}
function saa_accept_heartbeat(array $c,array $s,array $p,int $now): array {
    // Caller authenticated the packet before taking the lock. Replay checks must remain under the lock.
    foreach($s['nonces'] as $n=>$at)if($at<$now-180)unset($s['nonces'][$n]);
    if(isset($s['nonces'][$p['nonce']])||$p['timestamp']<$s['last_signed']||count($s['nonces'])>=256)throw new InvalidArgumentException('Replay rejected');
    $s['nonces'][$p['nonce']]=$now;$s['last_seen']=$now;$s['last_signed']=$p['timestamp'];
    return [$s,['ok'=>true,'nonce'=>$p['nonce'],'signature'=>saa_mac($c,'ack',$p)]];
}
function saa_receive(array $c,array $s,string $raw,int $now): array {
    return saa_accept_heartbeat($c,$s,saa_authenticate_heartbeat($c,$raw,$now),$now);
}
function saa_evaluate(array $c,array $s,int $now,callable $send): array {
    if(($c['maintenance']??false)===true){$s['maintenance_until']=$now+300;$s['next_attempt']=0;return $s;}
    $last=$s['last_seen']??$s['watch_since'];
    $down=$now-$last>=300;
    if($down&&($s['maintenance_until']??0)>$now)return $s;
    if($s['next_attempt']>$now)return $s;
    if($down&&!$s['notified_down']){
        $text=saa_text($c,"🔴 Потеряна связь с сервером мониторинга\n","🔴 Monitoring server connection lost\n").$c['name'].saa_text($c,"\nНет подтверждённых сигналов более 5 минут.\nПоследний сигнал: ","\nNo confirmed heartbeat for over five minutes.\nLast heartbeat: ").($s['last_seen']===null?saa_text($c,'не поступал','never received'):gmdate('Y-m-d H:i:s',$s['last_seen']).' UTC').saa_text($c,"\nВозможны остановка службы, проблема БД или сети. Это не доказательство падения всего ДЦ.","\nPossible causes include a stopped service, database or network problem. This does not prove that the entire data center is down.");
        $result=$send($text);if($result['ok']){$s['notified_down']=true;$s['outage_since']=$last;$s['next_attempt']=0;}else $s['next_attempt']=$now+max(300,min(3600,(int)($result['retry_after']??300)));
    }elseif(!$down&&$s['notified_down']){
        $text=saa_text($c,"🟢 Связь с сервером мониторинга восстановлена\n","🟢 Monitoring server connection restored\n").$c['name'].saa_text($c,"\nСнова поступают подтверждённые сигналы.\nПерерыв наблюдения: ","\nConfirmed heartbeats have resumed.\nObservation gap: ").max(0,(int)ceil(($s['last_seen']-($s['outage_since']??$last))/60)).saa_text($c,' мин.',' min.');
        $result=$send($text);if($result['ok']){$s['notified_down']=false;$s['outage_since']=null;$s['next_attempt']=0;}else $s['next_attempt']=$now+max(300,min(3600,(int)($result['retry_after']??300)));
    }elseif(!$down){$s['next_attempt']=0;}
    return $s;
}
function saa_telegram(array $c,string $text): array {
    if(!extension_loaded('curl'))throw new RuntimeException('PHP curl required');
    if(!preg_match('/\A[0-9]+:[A-Za-z0-9_-]{20,200}\z/',$c['telegram_token']??'')||!preg_match('/\A-?[0-9]{1,25}\z/',$c['telegram_chat']??''))throw new RuntimeException('Telegram configuration invalid');
    $ch=curl_init('https://api.telegram.org/bot'.$c['telegram_token'].'/sendMessage');$body='';
    curl_setopt_array($ch,[CURLOPT_POST=>true,CURLOPT_POSTFIELDS=>http_build_query(['chat_id'=>$c['telegram_chat'],'text'=>$text]),CURLOPT_RETURNTRANSFER=>false,CURLOPT_FOLLOWLOCATION=>false,CURLOPT_CONNECTTIMEOUT=>5,CURLOPT_TIMEOUT=>10,CURLOPT_SSL_VERIFYPEER=>true,CURLOPT_SSL_VERIFYHOST=>2,CURLOPT_PROTOCOLS=>CURLPROTO_HTTPS,CURLOPT_WRITEFUNCTION=>static function($ch,string $part)use(&$body):int{if(strlen($body)+strlen($part)>16384)return 0;$body.=$part;return strlen($part);}]);
    $ok=curl_exec($ch);$code=(int)curl_getinfo($ch,CURLINFO_HTTP_CODE);curl_close($ch);
    $data=json_decode($body,true);return ['ok'=>$ok!==false&&$code===200&&is_array($data)&&($data['ok']??false)===true,'retry_after'=>is_array($data)?(int)($data['parameters']['retry_after']??300):300];
}
