"""Installer control-flow tests with mocked commands; NOT a real systemd installation."""
from pathlib import Path
import os, subprocess, tempfile, shutil
source=(Path(__file__).resolve().parents[1]/'src/SysAdminAssistant.Server/LinuxInstall/install.sh').read_text()
for distro in ('debian','ubuntu'):
 for failure in (False,True):
  with tempfile.TemporaryDirectory(prefix='saa-installer-') as tmp:
   root=Path(tmp); mock=root/'commands';mock.mkdir();state=root/'var/lib/sysadminassistant-agent'
   mappings={'/opt/sysadminassistant-agent':str(root/'opt/sysadminassistant-agent'),'/var/lib/sysadminassistant-agent':str(state),'/etc/sysadminassistant-agent':str(root/'etc/sysadminassistant-agent'),'/etc/systemd/system':str(root/'etc/systemd/system'),'/run/systemd/system':str(root/'run/systemd/system'),'/etc/os-release':str(root/'os-release')}
   for p in ('etc/systemd/system','run/systemd/system'): (root/p).mkdir(parents=True)
   (root/'os-release').write_text('ID='+distro+'\nVERSION_ID='+('12' if distro=='debian' else '24.04')+'\n')
   script=source.replace('export PATH=/usr/sbin:/usr/bin:/sbin:/bin','export PATH="'+str(mock)+':/usr/bin:/bin"')
   for old,new in mappings.items():script=script.replace(old,new)
   installer=root/'install.sh';installer.write_text(script)
   bundle=root/'bundle';bundle.mkdir();(bundle/'agent').write_text('#!/bin/sh\nexit 0\n');(bundle/'bootstrap.json').write_text('fixture-registration')
   commands={
    'id':'if [ "$1" = -u ]; then echo 0; exit 0; fi\nexit 1',
    'uname':'echo x86_64', 'useradd':'exit 0','userdel':'exit 0','usermod':'exit 0','getent':'exit 0','sleep':'exit 0',
    'install':'args=(); while [ "$#" -gt 0 ]; do case "$1" in -o|-g) shift 2;; *) args+=("$1"); shift;; esac; done\nexec /usr/bin/install "${args[@]}"',
    'systemctl':'if [ "$1" = is-active ] && [ "${FAIL_START:-0}" = 1 ]; then exit 1; fi\nif [ "$1" = enable ] && [[ "$*" = *sysadminassistant-agent.service* ]]; then mkdir -p "$STATE"; echo \'{"Status":"Ожидается подтверждение","PublicKeySha256":"FIXTURE"}\' > "$STATE/status.json"; fi\nexit 0'
   }
   for name,text in commands.items():p=mock/name;p.write_text('#!/bin/bash\n'+text+'\n');p.chmod(0o700)
   env=dict(os.environ,STATE=str(state),FAIL_START='1' if failure else '0')
   result=subprocess.run(['/bin/bash',str(installer),str(bundle)],env=env,capture_output=True,text=True,timeout=20)
   if failure:
    assert result.returncode!=0, result.stdout
    assert not (root/'opt/sysadminassistant-agent').exists(), 'rollback left program'
    assert not state.exists(), 'rollback left identity directory'
    assert not (root/'etc/systemd/system/sysadminassistant-agent.service').exists(), 'rollback left service'
   else:
    assert result.returncode==0, result.stdout+result.stderr
    assert (state/'bootstrap.json').read_text()=='fixture-registration'
    unit=(root/'etc/systemd/system/sysadminassistant-agent-update.service').read_text()
    assert 'Type=oneshot' in unit and '--apply-update' in unit
    assert (root/'etc/systemd/system/sysadminassistant-agent-update.timer').is_file()
   print('PASS mocked',distro,'startup rollback' if failure else 'clean install flow; identity and timer preserved')
print('LIMITATION: no real Debian/Ubuntu systemd, packages or service accounts were installed')
