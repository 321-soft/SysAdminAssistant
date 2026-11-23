<?php
declare(strict_types=1);
require __DIR__.'/../lib.php';
$count=0;function check(string $name,bool $value):void{global $count;if(!$value)throw new RuntimeException('FAIL '.$name);$count++;echo 'PASS '.$name."\n";}
$v=json_decode(file_get_contents(__DIR__.'/protocol-vector.json'),true,8,JSON_THROW_ON_ERROR);$c=['server_id'=>'assistant-server','secret'=>$v['secret'],'name'=>'Fixture','maintenance'=>false];$now=$v['packet']['timestamp'];
[$s,$reply]=saa_receive($c,saa_initial($now),json_encode($v['packet']),$now);check('PHP verifies independent C# wire vector',$reply['signature']===$v['ack']&&$s['last_seen']===$now);
function reject(array $c,array $s,string $raw,int $now):bool{try{saa_receive($c,$s,$raw,$now);return false;}catch(InvalidArgumentException|JsonException $e){return true;}}
check('Same authenticated request cannot be replayed',reject($c,$s,json_encode($v['packet']),$now));
check('Expired signed signal rejected',reject($c,saa_initial($now),json_encode($v['packet']),$now+121));
check('Future signed signal rejected',reject($c,saa_initial($now),json_encode($v['packet']),$now-121));
$p=$v['packet'];$p['serverId']='other';check('Different server ID rejected',reject($c,saa_initial($now),json_encode($p),$now));
$p=$v['packet'];$p['signature']=str_repeat('0',64);check('Forged signature rejected',reject($c,saa_initial($now),json_encode($p),$now));
check('Oversized request rejected',reject($c,saa_initial($now),str_repeat('x',2049),$now));
check('Malformed JSON rejected',reject($c,saa_initial($now),'[',$now));
$messages=[];$send=static function(string $text)use(&$messages):array{$messages[]=$text;return ['ok'=>true];};
$s=saa_initial($now);$s['last_seen']=$now;$s=saa_evaluate($c,$s,$now+299,$send);check('No early alert',count($messages)===0);
$s=saa_evaluate($c,$s,$now+300,$send);check('Five-minute missing signal sends one red alert',count($messages)===1&&$s['notified_down']&&str_starts_with($messages[0],'🔴'));
$s=saa_evaluate($c,$s,$now+900,$send);check('Repeated cron never repeats delivered down alert',count($messages)===1);
$s['last_seen']=$now+901;$s=saa_evaluate($c,$s,$now+902,$send);check('Recovery sends one green alert',count($messages)===2&&!$s['notified_down']&&str_starts_with($messages[1],'🟢'));
$s=saa_evaluate($c,$s,$now+903,$send);check('Repeated healthy cron is silent',count($messages)===2);
$failed=0;$fail=static function(string $text)use(&$failed):array{$failed++;return ['ok'=>false,'retry_after'=>600];};$s=saa_evaluate($c,saa_initial($now),$now+300,$fail);check('Failed delivery does not mark incident notified',!$s['notified_down']&&$failed===1);
$s=saa_evaluate($c,$s,$now+600,$fail);check('Delivery retry waits for rate backoff',$failed===1);
$s=saa_evaluate($c,$s,$now+900,$send);check('Delivery can retry after failure',$s['notified_down']);
$before=count($messages);$s=saa_initial($now);$s=saa_evaluate($c,$s,$now+300,$fail);$s['last_seen']=$now+301;$s=saa_evaluate($c,$s,$now+302,$send);check('Unsent outage has no phantom recovery',count($messages)===$before);
$pause=$c;$pause['maintenance']=true;$s=saa_evaluate($pause,saa_initial($now),$now+1000,$send);check('Maintenance suppresses outage',count($messages)===$before);
$s=saa_evaluate($c,$s,$now+1001,$send);check('Maintenance exit gives grace without invented recovery',count($messages)===$before);
$dir=sys_get_temp_dir().'/saa-watchdog-'.bin2hex(random_bytes(8));mkdir($dir,0700);$c['state_directory']=$dir;
try{$s=saa_initial($now);$s['notified_down']=true;$lock=saa_lock($c);saa_save($c,$s);flock($lock,LOCK_UN);fclose($lock);$reopened=saa_load($c,$now+100);check('Restart preserves notified incident',$reopened['notified_down']);
 file_put_contents($dir.'/state.json','broken');$rejected=false;try{saa_load($c,$now);}catch(Throwable $e){$rejected=true;}check('Corrupt state fails closed',$rejected);
}finally{foreach(glob($dir.'/*')as $file)unlink($file);rmdir($dir);}
echo "PASS {$count} PHP watchdog checks\n";
