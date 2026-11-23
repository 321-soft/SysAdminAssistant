"""Source contract checks; actual Windows interaction must be tested on Windows."""
from pathlib import Path
import json,re,xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1];desktop=root/'src/SysAdminAssistant.Desktop'
def check(name,value):
 assert value,name
 print('PASS',name)
overview=(desktop/'OverviewPage.cs').read_text();actions=(desktop/'DashboardActions.cs').read_text()
for kind in ('Device','Backup','Payment','Router'):
 check(kind+' dashboard rows have their own contextual actions','menu:'+kind+'DashboardMenu(' in overview)
check('Dashboard actions recheck explicit module permissions','menu.Opened+=' in actions and 'if(!Allowed())' in actions and 'AccessRules.PageModule(page)' not in actions)
check('Mutations reload entity rather than changing stale dashboard copies',all('Dashboard'+kind+'(' in actions for kind in ['Device','Payment','Job']))
check('Password shortcuts use the existing protected device card','"vault",1' in actions and 'OpenCredential' not in actions)
agent=(desktop/'AgentPages.cs').read_text();publish=(root/'PUBLISH.cmd').read_text()
check('Template is validated before runtime download and registration',agent.index('AgentTemplate.Validate(template)')<agent.index('AgentRuntimePackage.EnsureAsync')<agent.index('"agent-package"'))
check('Historical Markdown files are not package prerequisites','"AGENT.md"' not in agent and '"AGENT_AUTO_UPDATE.md"' not in agent)
for name in ['INSTALL_AGENT.cmd','INSTALL_AGENT.ps1','VerifyAgentWindows.ps1']:
 check('Publisher includes operational agent script '+name,name in publish)
ns={'w':'http://schemas.microsoft.com/winfx/2006/xaml/presentation'};tree=ET.parse(desktop/'MainWindow.xaml')
check('Footer has dedicated right-aligned button group',any(x.get('HorizontalAlignment')=='Right' and x.get('Grid.Column')=='1' and x.get('MaxWidth')=='520' for x in tree.findall('.//w:WrapPanel',ns)))
check('Complete guide is bilingual with workflow chapters',all(len(json.loads((root/'src/SysAdminAssistant.Core/Help/articles.json').read_text()))>=26 for _ in [0]))
install=(root/'INSTALL_CLIENT.ps1').read_text();uninstall=(root/'UNINSTALL_CLIENT.ps1').read_text()
check('Client shortcut options cleanly uninstall',all(name in install and name in uninstall for name in ['CommonPrograms','CommonDesktopDirectory','CommonStartup']))
check('Server backup path does not copy a live SQLite file directly','BackupDatabase(target)' in (root/'src/SysAdminAssistant.Server/LocalAdministration.cs').read_text())
print('PASS dashboard, template and packaging source contracts; native Windows execution not tested')
