"""Local protocol check. Does not contact Telegram or a deployed website."""
import hashlib,hmac,json,os,socket,subprocess,sys,tempfile,time,urllib.request,urllib.error
from pathlib import Path
php=sys.argv[1];root=Path(__file__).resolve().parents[1];count=0
with tempfile.TemporaryDirectory(prefix='saa-php-http-') as temp:
 t=Path(temp);private=t/'private';web=t/'www';state=private/'state'
 state.mkdir(parents=True,mode=0o700);web.mkdir()
 secret='42'*32
 config=private/'config.php';config.write_text("<?php return ['server_id'=>'fixture','name'=>'Fixture','secret'=>'"+secret+"','state_directory'=>"+repr(str(state))+",'maintenance'=>false];")
 # Emulate trusted HTTPS termination only in this local test router, never in production receiver code.
 router=web/'router.php';router.write_text("<?php $_SERVER['HTTPS']='on'; require "+repr(str(root/'receiver.php'))+";")
 with socket.socket() as sock:sock.bind(('127.0.0.1',0));port=sock.getsockname()[1]
 process=subprocess.Popen([php,'-n','-S',f'127.0.0.1:{port}','-t',str(web),str(router)],env=dict(os.environ,SAA_WATCHDOG_CONFIG=str(config)),stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
 def request(body=None):
  req=urllib.request.Request(f'http://127.0.0.1:{port}/heartbeat.php',data=body,headers={'Content-Type':'application/json'})
  try:
   with opener.open(req,timeout=5) as response:return response.status,response.read()
  except urllib.error.HTTPError as e:return e.code,e.read()
 def check(name,value):
  global count
  assert value,name;count+=1;print('PASS',name)
 try:
  for _ in range(100):
   try:code,body=request();break
   except urllib.error.URLError:time.sleep(.05)
  else:raise RuntimeError('PHP test server did not start')
  check('Receiver rejects GET',code==405)
  stamp=int(time.time());nonce='01'*16
  canonical=f'saa-heartbeat-v1\nfixture\n{stamp}\n{nonce}'
  packet=dict(version=1,serverId='fixture',timestamp=stamp,nonce=nonce,signature=hmac.new(bytes.fromhex(secret),canonical.encode(),hashlib.sha256).hexdigest())
  raw=json.dumps(packet).encode();code,body=request(raw);ack=json.loads(body)
  expected=hmac.new(bytes.fromhex(secret),f'saa-ack-v1\nfixture\n{stamp}\n{nonce}'.encode(),hashlib.sha256).hexdigest()
  check('Real HTTP receiver persists signal and signs ACK',code==200 and ack['signature']==expected and json.loads((state/'state.json').read_text())['last_seen']>=stamp)
  code,_=request(raw);check('Real HTTP replay rejected',code==403)
  packet['nonce']='02'*16;code,_=request(json.dumps(packet).encode());check('Real HTTP forged packet rejected',code==403)
  code,_=request(b'x'*2049);check('HTTP oversized body rejected',code==403)
  code,_=request(b'{');check('HTTP malformed JSON rejected',code==403)
  env=dict(os.environ,SAA_WATCHDOG_CONFIG=str(config));status=subprocess.run([php,'-n',str(root/'worker.php'),'status'],env=env,cwd=t,capture_output=True,text=True)
  check('CLI status reads same persistent signal',status.returncode==0 and json.loads(status.stdout)['last_seen_utc'] is not None)
  worker=subprocess.run([php,'-n',str(root/'worker.php'),'check'],env=env,cwd=t,capture_output=True,text=True)
  check('Healthy cron does not need Telegram or send anything',worker.returncode==0)
 finally:process.terminate();process.wait(timeout=5)
print('PASS',count,'PHP HTTP/CLI checks')
