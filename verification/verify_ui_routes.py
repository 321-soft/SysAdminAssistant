"""Source route coverage; this does not run WPF or simulate clicking through Windows."""
from pathlib import Path
import re,xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1];desktop=root/'src/SysAdminAssistant.Desktop';count=0

def check(name,value):
 global count
 assert value,name
 count+=1;print('PASS',name)
files={p.name:p.read_text() for p in desktop.glob('*.cs')}
check('Client notices route through shared dialog at every source site',all(not re.search(r'(?<!App)(?<!Windows\.)MessageBox\.Show\(',t) for n,t in files.items() if n!='AppMessageBox.cs'))
check('Only shared shell creates raw window inheritance',all(not re.search(r'\bnew\s+Window\s*[{(]',t) and (not re.search(r':\s*Window\b',t) or n in ('DialogWindow.cs','MainWindow.xaml.cs')) for n,t in files.items()))
check('Login and reauthentication footers can wrap','new WrapPanel' in files['ConnectionWindow.cs'] and 'new WrapPanel' in files['SessionPasswordWindow.cs'])
check('Shared messages derive from shared dialog and preserve explicit default','new DialogWindow' in files['AppMessageBox.cs'] and 'defaultResult==MessageBoxResult.None?result:defaultResult' in files['AppMessageBox.cs'])
agent=(root/'src/SysAdminAssistant.AgentSetup/Program.cs').read_text();project=(root/'src/SysAdminAssistant.AgentSetup/SysAdminAssistant.AgentSetup.csproj').read_text()
check('Agent installer loads own complete theme and shared nested notices','AgentSetupTheme.xaml' in agent and 'AppMessageBox.Show' in agent and 'AppMessageBox.cs' in project and 'DialogWindow.cs' in project)
server=root/'src/SysAdminAssistant.ServerSetup';check('Server installer notices use themed form',all(not re.search(r'(?<!Setup)MessageBox\.Show\(',p.read_text()) for p in server.glob('*.cs')))
check('All three server installer forms apply shared palette',all('SetupTheme.Apply(this)' in (server/n).read_text() for n in ('SetupWindow.cs','UpdateWindow.cs','UninstallWindow.cs')))
ns={'w':'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
for path in [desktop/'App.xaml',root/'src/SysAdminAssistant.AgentSetup/AgentSetupTheme.xaml']:
 xml=ET.parse(path);styles={e.get('TargetType') for e in xml.findall('.//w:Style',ns)}
 check('Theme covers nested fields and buttons: '+path.name,{'Button','TextBox','PasswordBox','ComboBox','ComboBoxItem','CheckBox','ToggleButton','ListBox','GroupBox','Expander'}.issubset(styles))
print('PASS',count,'UI source route checks; Windows runtime not tested')
