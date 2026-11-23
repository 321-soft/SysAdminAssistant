"""Offline consistency checks, not execution of csc.exe/PowerShell on Windows."""
from pathlib import Path
import re,xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
version=ET.parse(root/'Directory.Build.props').findtext('.//Version')
publish=(root/'PUBLISH.cmd').read_text();launcher=(root/'BUILD_AGENT_LAUNCHER.ps1').read_text(encoding='utf-8-sig');sign=(root/'SIGN_AGENT_RELEASE.ps1').read_text(encoding='utf-8-sig')
count=0
def check(label,ok):
 global count
 assert ok,label
 count+=1;print('PASS',label)
check('All publication paths match project version',set(re.findall(r'Release_(\d+\.\d+\.\d+)',publish))=={version})
check('Launcher output explicitly passed quoted by publisher',f'-OutputPath "%~dp0publish\\Release_{version}\\Client\\AgentTemplate\\AgentSetupLauncher.exe"' in publish)
check('Launcher default reads project version without baked release path','Directory.Build.props' in launcher and '$props.Project.PropertyGroup.Version' in launcher and not re.search(r'Release_\d',launcher))
check('Compiler output directory created before invocation',launcher.index('[IO.Directory]::CreateDirectory')<launcher.index('& $csc'))
check('Launcher uses quoted output and manifest compiler arguments','"/out:$out"' in launcher and '"/win32manifest:$manifest"' in launcher)
check('Signing default also follows project version','Directory.Build.props' in sign and not re.search(r'Release_\d',sign))
labels={l[1:].strip().lower() for l in publish.splitlines() if l.startswith(':')}
check('All publication failure labels exist',all(label.lower() in labels for label in re.findall(r'\bgoto\s+(\w+)',publish,re.I)))
copies=re.findall(r'^copy /y ([^\s]+)',publish,re.M)
check('All published source guides and scripts exist',all((root/n.replace('\\','/')).exists() for n in copies if not n.startswith(('publish','RuntimePackages'))))
print('PASS',count,'publication path checks; Windows PowerShell/csc not executed')
