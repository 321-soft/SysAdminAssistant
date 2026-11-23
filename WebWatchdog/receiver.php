<?php
declare(strict_types=1);
require_once __DIR__.'/lib.php';
ini_set('display_errors','0');umask(0077);
header('Content-Type: application/json; charset=utf-8');header('Cache-Control: no-store');
if(($_SERVER['REQUEST_METHOD']??'')!=='POST'){http_response_code(405);exit('{"ok":false}');}
// Do not trust X-Forwarded-Proto from an arbitrary caller. Configure HTTPS at the hosting web server.
if(($_SERVER['HTTPS']??'')!=='on'&&($_SERVER['HTTPS']??'')!=='1'){http_response_code(400);exit('{"ok":false}');}
$lock=null;
try{
    if((int)($_SERVER['CONTENT_LENGTH']??0)>2048)throw new InvalidArgumentException();
    $raw=file_get_contents('php://input',false,null,0,2049);if($raw===false)throw new InvalidArgumentException();
    $c=saa_config();$now=time();$packet=saa_authenticate_heartbeat($c,$raw,$now);
    $lock=saa_lock($c);$now=time();$s=saa_load($c,$now);
    [$s,$reply]=saa_accept_heartbeat($c,$s,$packet,$now);saa_save($c,$s);echo json_encode($reply,JSON_THROW_ON_ERROR);
}catch(InvalidArgumentException|JsonException $e){http_response_code(403);echo '{"ok":false}';}
catch(Throwable $e){http_response_code(503);echo '{"ok":false}';}
finally{if(is_resource($lock)){flock($lock,LOCK_UN);fclose($lock);}}
