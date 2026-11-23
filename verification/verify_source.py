"""Offline source/package checks. This is NOT a C# compiler or runtime test."""
from pathlib import Path
import json, re, struct, xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[1]
checks = []
def check(name, ok):
    if not ok: raise AssertionError(name)
    checks.append(name)
    print('PASS', name)

def balanced(text, path):
    n = len(text)
    def string(i, verbatim=False):
        while i < n:
            if text[i] == '"':
                if verbatim and i+1 < n and text[i+1] == '"': i += 2; continue
                return i+1
            if not verbatim and text[i] == '\\': i += 2
            else: i += 1
        raise AssertionError(f'{path}: unterminated string')
    def interp(i, verbatim=False):
        while i < n:
            if text[i] == '"':
                if verbatim and i+1 < n and text[i+1] == '"': i += 2; continue
                return i+1
            if text[i:i+2] in ('{{', '}}'): i += 2; continue
            if text[i] == '{': i = code(i+1, stop=True); continue
            if not verbatim and text[i] == '\\': i += 2
            else: i += 1
        raise AssertionError(f'{path}: unterminated interpolation')
    def code(i, stop=False):
        stack=[]
        while i<n:
            if text[i:i+2] == '//':
                end=text.find('\n',i); i=n if end<0 else end+1; continue
            if text[i:i+2] == '/*':
                end=text.find('*/',i+2)
                if end<0: raise AssertionError(f'{path}: comment')
                i=end+2; continue
            if text[i:i+3] in ('$@"','@$"'): i=interp(i+3,True); continue
            if text[i:i+2] == '$"': i=interp(i+2); continue
            if text[i:i+2] == '@"': i=string(i+2,True); continue
            if text[i] == '"': i=string(i+1); continue
            if text[i] == "'":
                i+=1
                while i<n:
                    if text[i]=='\\': i+=2
                    elif text[i]=="'": i+=1; break
                    else: i+=1
                else: raise AssertionError(f'{path}: character')
                continue
            c=text[i]
            if c in '([{': stack.append(c)
            elif c in ')]}':
                if c=='}' and stop and not stack: return i+1
                if not stack or stack.pop()!=dict(zip(')]}','([{'))[c]: raise AssertionError(f'{path}:{text.count(chr(10),0,i)+1}: brackets')
            i+=1
        if stack or stop: raise AssertionError(f'{path}: unclosed block')
        return i
    code(0)

sources=list(root.rglob('*.cs'))
for p in sources: balanced(p.read_text(), p.relative_to(root))
check('C# lexical strings/comments/brackets', len(sources)>=20)
for p in [*root.rglob('*.csproj'), *root.rglob('*.xaml'), root/'Directory.Build.props', root/'NuGet.config']: ET.parse(p)
check('Project and XAML XML well-formed', True)
sln=(root/'SysAdminAssistant.sln').read_text()
refs=re.findall(r'Project\("[^"\n]+"\) = "([^"]+)", "([^"]+)", "([^"]+)"',sln)
refs=[ref for ref in refs if ref[1].endswith('.csproj')]
check('Solution retains application projects and protected product maintenance', len(refs)==12 and {'SysAdminAssistant.Desktop','SysAdminAssistant.Server','SysAdminAssistant.Agent','SysAdminAssistant.AgentLinux','SysAdminAssistant.ProductMaintenance'}.issubset({name for name,_,_ in refs}) and all((root/Path(p.replace('\\','/'))).exists() for _,p,_ in refs))
check('Desktop is first startup candidate', refs[0][0]=='SysAdminAssistant.Desktop')
for p in root.rglob('*.csproj'):
    tree=ET.parse(p)
    for node in tree.iter('ProjectReference'): check('Reference '+node.attrib['Include'], (p.parent/node.attrib['Include']).exists())
check('.NET 10 SDK', json.loads((root/'global.json').read_text())['sdk']['version'].startswith('10.'))
asset=root/'src/SysAdminAssistant.Desktop/Assets'
catalog=json.loads((asset/'catalog.json').read_text())
for family in catalog['families']:
    data=(asset/(family+'.png')).read_bytes()
    check('Icon '+family, data[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',data[16:24])==(128,96))
all_source='\n'.join(p.read_text() for p in sources)
check('No unfinished implementation stubs', not re.search(r'NotImplementedException|TODO|\.nonexistent',all_source))
check('SQLite and SQL Server providers', 'UseSqlite' in all_source and 'UseSqlServer' in all_source)
check('Mail stays read-only', 'FolderAccess.ReadOnly' in all_source and 'UidValidity' in all_source and 'SetFlagsAsync' not in all_source)
check('TLS validation requires trusted certificate or exact SHA256 pin', 'ServerCertificateValidationCallback' not in all_source and 'errors==SslPolicyErrors.None' in all_source and 'SHA256.HashData(certificate.GetRawCertData()))==pin' in all_source and 'Untrusted Router certificate is not silently accepted' in all_source)
check('Vault authenticated encryption', 'new AesGcm' in all_source and '600_000' in all_source and 'ZeroMemory' in all_source)
check('Reports manual rules / conflict', 'Требуется ручная привязка' in all_source and 'Конфликт правил' in all_source)
check('Transactions and replay preserve receive time', 'BeginTransactionAsync' in all_source and 'updated.ReceivedAt = previous.ReceivedAt' in all_source)
check('Dynamic interfaces', 'Snapshot?.Interfaces.Select' in all_source and not re.search(r'Interfaces\.Take\(8\)',all_source))
check('Optional payment amount uses integer kopecks', 'public long? AmountKopecks' in all_source and 'Transfer preserves payment amount' in all_source and 'decimal.Round(money,2)!=money' in all_source)
check('No old mail or expand navigation', '"Развернуть"' not in all_source and '"Почта", "' not in all_source)
check('Dates formatted dd.MM.yyyy', 'TryParseExact(Value(key), "dd.MM.yyyy"' in all_source and 'ToString("dd.MM.yyyy"' in all_source)
nav=(root/'src/SysAdminAssistant.Desktop/App.xaml').read_text()
check('Stable explicit navigation template', 'x:Key="NavigationButton"' in nav and 'TemplateBinding Background' in nav and 'TemplateBinding Foreground' in nav and 'IsEnabled' not in nav[nav.index('<Style x:Key="NavigationButton"'):].split('</Style>',1)[0])
check('Inventory navigation and scroll', 'case 9: await ShowInventory()' in all_source and 'x:Name="Navigation"' in (root/'src/SysAdminAssistant.Desktop/MainWindow.xaml').read_text() and '<ScrollViewer' in (root/'src/SysAdminAssistant.Desktop/MainWindow.xaml').read_text())
check('Provider presets and visible inheritance', 'KeywordDefaults.For(provider)' in all_source and 'Показаны действующие слова' in all_source)
check('Inventory immutable snapshot and printing', 'InventoryActLine' in all_source and 'Specifications = InventoryDisplay.Specs(a)' in all_source and 'dialog.PrintDocument' in all_source and 'PreviewInventoryAct' in all_source)
check('Inventory ownership, revisions and atomic operations', 'CK_InventoryAsset_Owner' in all_source and 'IsConcurrencyToken' in all_source and 'InventoryService' in all_source and 'IsolationLevel.Serializable' in all_source)
check('Additive versioned schema upgrade', 'schema?.Version == 1' in all_source and 'InventorySchema.UpgradeAsync' in all_source and 'schema!.Version = 3' in all_source and 'sp_getapplock' in all_source)
check('Inventory portable relations', 'EmployeeMap(a.EmployeeId)' in all_source and 'Map(assets, l.AssetId)' in all_source and 'data.Format is not (1 or 2 or 3 or 4 or 5)' in all_source)
check('Server updates preserve current page', 'remote.WatchAsync(RefreshFromServer' in all_source and 'await ShowPage(page);' in all_source)
check('Forms clone records before editing', all('Copy(' in (root/('src/SysAdminAssistant.Desktop/'+name)).read_text() for name in ['CatalogPages.cs','BackupPages.cs','InventoryPages.cs']))
check('Page selections and search restored', 'CapturePageState();' in all_source and 'RestorePageState(index' in all_source and 'grid.SelectedItems.Add(item)' in all_source)
check('Client settings apply after server confirmation', 'settings = await remote.SaveSettingsAsync(proposed, lifetime.Token);' in all_source and 'SettingsFile.Save(next, Path.Combine(DirectoryPath' in all_source)
check('Monitoring discards obsolete addresses', 'd.Host == device.Host && d.Enabled' in all_source and 's.Address == service.Address && s.Type == service.Type' in all_source)
check('Reports fallback to receipt time', 'var stamp=e.OccurredAt??e.ReceivedAt;' in all_source and 'stamp>=lower' in all_source and 'Off days do not create a new expectation' in all_source)
check('Act validates reviewed revision and date', 'ExpectedRevisions' in all_source and 'Return cannot predate issue' in all_source and 'Act cannot use stale reviewed hardware' in all_source)
check('Transfer rejects invalid enum and missing links', 'ValidateArchive(data);' in all_source and 'Enum.IsDefined(type, value)' in all_source and 'Link(devices, a.DeviceId)' in all_source)
check('Equipment image fallback and counters', 'Known.Contains(family) ? family : "generic"' in all_source and '=stats=' in all_source)
check('Windows scripts use real CRLF and full solution verification', all(b'\r\n' in (root/name).read_bytes() and b'\\r\\n' not in (root/name).read_bytes() for name in ['BUILD.cmd','VERIFY.cmd','PUBLISH.cmd']) and b'dotnet build SysAdminAssistant.sln' in (root/'VERIFY.cmd').read_bytes())
check('Act template snapshots and migration', 'UpgradeActTemplatesAsync(db, token)' in all_source and 'RepresentativePosition = request.RepresentativePosition.Trim()' in all_source and 'ValueKopecks = a.ValueKopecks' in all_source and 'DismissalDate = request.DismissalDate?.Date' in all_source)
check('Remembered act defaults and money', 'Запомнить реквизиты для следующих актов' in all_source and 'defaults.IssueReason' in all_source and 'InventoryMoney.Parse' in all_source and 'Стоимость, руб.' in all_source)
check('Runtime specifications included', len(re.findall(r'Check\("', (root/'tests/SysAdminAssistant.Specs/Program.cs').read_text()))>=45)
check('No settings, passwords, databases or build output packaged', not list(root.rglob('settings.json')) and not list(root.rglob('*.db')) and not list(root.rglob('bin')) and not list(root.rglob('obj')))
catalog = json.loads((root/'src/SysAdminAssistant.Core/Diagnostics/catalog.json').read_text())
check('Embedded offline diagnosis catalogue', len(catalog) == 33 and 'EmbeddedResource' in (root/'src/SysAdminAssistant.Core/SysAdminAssistant.Core.csproj').read_text() and 'GetManifestResourceStream("SysAdminAssistant.Core.Diagnostics.catalog.json")' in all_source)
check('Diagnosis stored before raw mail deletion', 'DiagnosticGuide.Analyze(analysis.Provider, mail.Body, custom)' in all_source and 'DiagnosticGuide.IssueEvidence(mail.Body)' in all_source)
check('Diagnosis product scope, bounds and evidence', 'x.Providers.Contains(provider, StringComparer.OrdinalIgnoreCase)' in all_source and 'TimeSpan.FromMilliseconds(100)' in all_source and 'MaxBody = 500_000' in all_source and 'Take(4000)' in all_source and 'Исходные строки:' in all_source)
check('Diagnosis catalogue and readable details available', 'ShowDiagnosticGuide' in all_source and 'ShowReportDiagnosis' in all_source and 'MaxHeight = 240' in all_source)
check('Shared runtime diagnostic fixtures packaged', 'DiagnosticFixtures.json' in (root/'tests/SysAdminAssistant.Specs/SysAdminAssistant.Specs.csproj').read_text() and 'Diagnostic fixture: ' in all_source)
check('Requested storage buttons removed and backup heading renamed', 'Place("Резервное копирование",jobs.Length' in all_source and 'ActionButton("Импорт базы HiAsm"' not in all_source and 'ActionButton("Экспорт для переноса"' not in all_source and 'ActionButton("Импорт C#-архива"' not in all_source)
check('Multiple IMAP accounts and legacy migration', 'settings.Mailboxes' in all_source and 'NormalizeMailboxes()' in all_source and 'PollMailboxAsync' in all_source and 'MailErrors = errors.ToArray()' in all_source)
check('Custom diagnosis separate from built-in articles', 'CustomDiagnostics' in all_source and 'EditCustomDiagnostic' in all_source and 'Рекомендация пользователя' in all_source)
check('Retention warnings, receipts and schema upgrade', 'EventRetentionDays' in all_source and 'Удаление необратимо' in all_source and 'RetiredMailKey' in all_source and 'schema.Version < 4' in all_source)
check('DMARC safe XML and compressed attachments', 'DtdProcessing.Prohibit' in all_source and 'XmlResolver = null' in all_source and 'LimitedMemoryStream' in all_source and 'GZipStream' in all_source and 'ZipArchive' in all_source)
check('DMARC alignment, duplicate identity and readable report', 'Spf == "pass" || Dkim == "pass"' in all_source and 'DmarcParser.Parse' in all_source and 'dmarcDigest' in all_source and 'case 10: await ShowDmarc()' in all_source)

check('Mail purpose and empty lists block intake', 'public enum MailboxPurpose { Reports, Dmarc }' in all_source and 'm.AllowedDomains.Count == 0' in all_source and 'm.TrustedDmarcSenders.Count == 0' in all_source)
check('Two IMAP sections and explicit sender approval', 'Header = "DMARC"' in all_source and 'Header = "Отчёты"' in all_source and '.Submit("Подтвердить отправителя")' in all_source)
check('Approval discovery downloads only headers', 'MessageSummaryItems.Envelope' in (root/'src/SysAdminAssistant.Infrastructure/MailAdmission.cs').read_text() and 'GetMessageAsync' not in (root/'src/SysAdminAssistant.Infrastructure/MailAdmission.cs').read_text() and 'GetBodyPartAsync' not in (root/'src/SysAdminAssistant.Infrastructure/MailAdmission.cs').read_text())
check('Subject and exact-domain filter precede body fetch', 'HasDmarcSubject(subject)' in all_source and 'AllowedDomains.Contains(sender[' in all_source and all_source.count('TrustedDmarcSenders.Contains') >= 2 and (root/'src/SysAdminAssistant.Infrastructure/MonitoringService.cs').read_text().index('if (!MailAdmission.Allows') < (root/'src/SysAdminAssistant.Infrastructure/MonitoringService.cs').read_text().index('MessageSummaryItems.BodyStructure'))
check('No full IMAP messages downloaded automatically', 'GetMessageAsync' not in (root/'src/SysAdminAssistant.Infrastructure/MonitoringService.cs').read_text() and 'GetBodyPartAsync' in all_source)
check('Compressed and XML complexity limits', 'entry.Length > 200 *' in all_source and 'scan.Depth > 64' in all_source and 'nodes > 150000' in all_source)

check('Client cannot select local database or start local checks', 'EditDatabase' not in (root/'src/SysAdminAssistant.Desktop/SettingsPages.cs').read_text() and 'new MonitoringService(activeFactory' not in (root/'src/SysAdminAssistant.Desktop/MainWindow.xaml.cs').read_text() and 'new(settings, remote)' in all_source)
server_state=(root/'src/SysAdminAssistant.Server/ServerState.cs').read_text()
check('Server schema 7 boot runs only additive source compatibility', all(x in server_state for x in ('EnsureCreatedAsync','schema.Version != 7','SourceSchema.UpgradeAsync')) and 'LegacyImporter' not in server_state)
check('HTTPS private bind and socket-peer allowlist', 'listener.UseHttps(https=>https.ServerCertificateSelector' in all_source and 'context.Connection.RemoteIpAddress' in all_source and 'ForwardedHeaders' not in all_source and 'networkAccess.Allows(ip)' in all_source and 'ServerNetworkRules.Allows' in all_source)
check('Certificate pin and normal trust validation', 'errors==SslPolicyErrors.None' in all_source and 'SHA256.HashData(certificate.RawData)' in all_source and 'FixedTimeEquals' in all_source)
check('Password hash, bounded sessions and login rate limit', 'ServerPassword.Create' in all_source and 'passwordGate' in all_source and 'sessions.Count >= 512' in all_source and 'attempts.AllowPeer' in all_source and 'attempts.AllowAccount' in all_source and 'accounts=new(1024,5)' in all_source)
check('Server integration secrets hidden from clients', 's.SqlConnectionString = ""; s.SqlitePath = "";' in all_source and 'ProtectedPassword = s.Smtp.ProtectedPassword.Length > 0 ? "REMOTE"' in all_source)
check('Version conflicts on server and metadata stamps', 'change.ExpectedHash != RemoteData.EntityStamp(current)' in all_source and 'request.ExpectedHash != RemoteData.Hash(Settings)' in all_source and 'SettingsStamp' in all_source)
check('Inventory and analysis commands executed on server', 'factory.Remote is RemoteRepository' in all_source and 'ServerCommands.ExecuteAsync' in all_source and 'new InventoryService(factory).ConductAsync' in all_source)
check('Client view in memory only', 'Data Source=:memory:;Foreign Keys=True' in all_source and 'SettingsFile.Load' not in (root/'src/SysAdminAssistant.Desktop/MainWindow.xaml.cs').read_text())
check('Installable server with restricted firewall and recovery', 'AddWindowsService' in all_source and 'rule.RemoteAddresses = string.Join' in all_source and 'restart/10000/restart/30000/restart/60000' in all_source)
check('Network integration scenarios included and verifier executes them', (root/'tests/SysAdminAssistant.NetworkSpecs/Program.cs').exists() and 'Stale writes rejected with HTTP 409' in all_source and 'SysAdminAssistant.NetworkSpecs' in (root/'VERIFY.cmd').read_text())
check('Per-section permissions and fail-closed defaults', 'ReadInfrastructure' not in (root/'src/SysAdminAssistant.Infrastructure/MultiuserContracts.cs').read_text() and 'AccessRules.Validate(request.Permissions)' in all_source and 'AccessRules.Require(actor' in all_source)
main=(root/'src/SysAdminAssistant.Desktop/MainWindow.xaml.cs').read_text()
background=main[main.index('private Task RefreshFromServer'):main.index('private Task ServerDisconnected')]
check('Every tab immune to background redraw', 'timer.Tick' not in main and 'ShowPage(' not in background and 'await Run(' not in background and 'Данные на сервере изменились' in background)
check('IMAP receipt requested and archived', 'MessageSummaryItems.InternalDate' in all_source and 'report.ReceivedAt=receipt' in all_source and 'Mail view contains sender subject body and both dates' in all_source)
check('Mail viewer uses text controls and server role checks', 'MailPreview(' in all_source and 'AccessRules.Require(actor,report.Provider=="DMARC"?"dmarc":"reports")' in all_source and 'public DbSet<MailSource>' in all_source)
check('Mail connection test and live diagnostics present', 'MailDiagnostics.TestAsync' in all_source and 'monitor.MailProgress +=' in all_source and 'Проверить подключение к ящику' in all_source)
check('Mailbox headers and structure match exact UID', 'summaries.SingleOrDefault(x=>x.UniqueId==uid)' in all_source and 'bodies.SingleOrDefault(x=>x.UniqueId==uid)' in all_source)
check('Mail independent of network switch and newest first', 'try {await PollReports();}' in all_source and 'class AvailabilityMonitoringWorker' in all_source and 'class RouterMonitoringWorker' in all_source and 'ids.OrderByDescending(x=>x.Id)' in all_source)
check('Single existing vault with device binding and ownership', 'Связать с устройством' in all_source and 'x.OwnerId == actor.Id' in all_source and 'Нет доступа к устройствам' in all_source)
readonly_route=re.search(r'command\.ToLowerInvariant\(\) is not \(([^)]+)\)\)state\.Touch\(\)',all_source)
check('Read commands do not trigger refresh loops', readonly_route is not None and all('"'+name+'"' in readonly_route.group(1) for name in ['device-problems','router-audit-view','device-updates','agent-events','agent-metric-history','agent-journal-history','agent-view','payment-history']))
check('Granular HTTP regression fixtures included', 'Permission changes revoke current session' in all_source and 'IMAP executes with network monitoring disabled' in all_source and 'Vault read role cannot delete own entry' in all_source)
check('HTML table pairs retained with executable content removed', 'cells[0].TrimEnd' in all_source and 'HTML existing colon is not doubled' in all_source and 'HTML executable content stays excluded' in all_source)
check('Legacy Vault and permission compatibility fixtures included', '0.4 Vault decrypts after update for owner' in all_source and '0.4 update preserves private encryption key' in all_source and 'AuthenticationTagMismatchException' in all_source)
check('Result-scope migration uses enum for both SQL providers', (root/'src/SysAdminAssistant.Infrastructure/SourceSchema.cs').read_text().count('{(int)SearchScope.Subject}') == 2 and 'Repeated initialization preserves explicit Both scope' in all_source and 'Repeated initialization preserves explicit Body scope' in all_source)
check('GUI update validates package version and rejects downgrade', 'ServerUpgradeVersions.Validate(oldVersion,nextVersion' in all_source and 'incoming<installed' in all_source and 'new UpdateWindow()' in all_source and 'config.InstallFormat != 6' in all_source)
check('Updater backs up before replacement and probes HTTPS', 'CopyTree(installed.Data,Path.Combine(backup,"data"),backupRoot)' in all_source and 'await ProbeAsync(service,ip,port,pin)' in all_source and 'CopyTree(savedProgram,installed.Target)' in all_source)
publish=(root/'PUBLISH.cmd').read_text()
check('Runtime bundling optional and guarded', 'choice /C YN' in publish and 'if "%include_runtimes%"=="0" goto runtime_done' in publish and 'goto runtime_download_failed' in publish)
check('Runtime fetch uses hashes and Microsoft mirrors', 'SHA512' in (root/'FETCH_RUNTIMES.ps1').read_text() and 'dotnetcli.blob.core.windows.net' in (root/'runtime-packages.json').read_text())
check('Dashboard priority and stale-state specifications', 'Dashboard offline device first' in all_source and 'Dashboard old failure becomes unknown' in all_source and 'Dashboard maintenance suppresses alarm' in all_source)
cleanup=(root/'CLEANUP_SERVER.ps1').read_text(encoding='utf-8-sig')
check('Cleanup defaults to retaining each database', '$grid.Rows.Add($false' in cleanup and "if(-not $row.Verified)" in cleanup and 'Удалить эту базу безвозвратно?' in cleanup)
check('Cleanup checks ownership and changed settings', 'sa2_SchemaInfo' in cleanup and 'File-Hash $row.Path' in cleanup and 'ReparsePoint' in cleanup and 'мастер из отдельной папки' in cleanup)
check('Binding uses server preview and role guards', 'MailBinding.RequireAccess(actor,binding)' in all_source and 'scan.Preview.Token!=request.PreviewToken' in all_source and 'Binding denies report read-only user' in all_source)
check('Multiline rules and repeated-run specifications included', 'Body AND requires every line' in all_source and 'Twice daily morning success cannot satisfy evening' in all_source and 'Grace boundary report counts exactly once' in all_source)
check('Binding UI directly available from letters', 'MailBindingPage.cs' in str(list(root.rglob('MailBindingPage.cs'))) and 'Header="Привязать"' in all_source and 'Добавить выделенное в условия' in all_source)
check('Network dictionary import is strict and separate', 'JsonUnmappedMemberHandling.Disallow' in all_source and '--import-network-dictionaries' in all_source and 'network-dictionaries.json' in all_source)
check('Network incidents require confirmed interface recovery', 'HealthyChecks>=rule.RecoveryChecks' in all_source and 'Network one good poll cannot clear alarm' in all_source and 'Журнал RouterOS' in all_source and 'Последние 1000 сообщений' in all_source)
agent_source='\n'.join(p.read_text() for p in (root/'src/SysAdminAssistant.Agent').glob('*.cs'))
check('Agent runtime has no command execution or network listener', not any(x in agent_source for x in ['Process.Start','TcpListener','HttpListener','UseKestrel','PowerShell.Create','Registry.SetValue']))
check('Agent transport prevents redirects proxy and certificate downloads', 'AllowAutoRedirect=false' in agent_source and 'UseProxy=false' in agent_source and 'DisableCertificateDownloads=true' in agent_source)
check('Agent setup uses LocalService with isolated service SID', 'NT AUTHORITY\\LocalService' in (root/'src/SysAdminAssistant.AgentSetup/Program.cs').read_text() and 'NT SERVICE' in (root/'src/SysAdminAssistant.AgentSetup/Program.cs').read_text())
probe=(root/'src/SysAdminAssistant.UpdateProbe/Program.cs').read_text()
probe_query=(root/'src/SysAdminAssistant.Agent/WindowsUpdateQuery.cs').read_text()
task=(root/'src/SysAdminAssistant.AgentSetup/UpdateProbeTask.cs').read_text()
check('Update probe has fixed location SYSTEM identity and no arguments', 'args.Length!=0' in probe and 'S-1-5-18' in probe and 'AppContext.BaseDirectory' in probe and 'UpdateCache' in probe)
check('Update probe performs only offline WUA query', 'Online=false' in probe_query and 'CanAutomaticallyUpgradeService=false' in probe_query and not any(x in probe+probe_query for x in ['Process.Start','HttpClient','TcpListener','CreateUpdateInstaller','CreateUpdateDownloader','CheckForUpdates','AcceptEula']))
check('Update task only privileged principals and fixed binary', 'D:P(A;;GA;;;SY)(A;;GA;;;BA)' in task and 'action.Path=Binary' in task and 'action.Arguments=""' in task and 'PT15M' in task and 'PT3M' in task and 'Own(task)' in task)
check('Runtime package reuse and declined downloads have executable coverage', 'Repeated runtime package creation uses cache offline' in all_source and 'Runtime download declined creates no file' in all_source and 'Concurrent package requests fetch runtime once' in all_source)
check('Update views have one close button', '.Note(UpdateDetails(u)).ViewOnly().ShowDialog()' in all_source)
check('Probe included before signature and server template copy', publish.index('SysAdminAssistant.UpdateProbe.csproj')<publish.index('SIGN_AGENT_RELEASE.ps1')<publish.index('xcopy /E /I /Y'))
maintenance=(root/'src/SysAdminAssistant.AgentSetup/AgentAutoUpdate.cs').read_text()
check('Automatic updater accepts only fixed signed server releases', 'AgentRelease.Verify(signed,bootstrap.IssuerCertificate' in maintenance and 'AgentRelease.VerifyFile(bytes,f.Value)' in maintenance and 'AgentWire.Endpoint(bootstrap)' in maintenance and 'ResponseHeadersRead' in maintenance)
check('Automatic updater has own protected dual slots and restricted task', 'SlotPath("A")' in maintenance and 'SlotPath("B")' in maintenance and 'D:P(A;;GA;;;SY)(A;;GA;;;BA)' in maintenance and 'a.Arguments="--auto-update"' in maintenance and 'PT15M' in maintenance)
check('Agent update retains registration and checks service before finalizing', 'package.EnrollmentId!=installed.EnrollmentId' in all_source and 'File.ReadAllBytes(p)' in all_source and 'service.Status!=ServiceControllerStatus.Running' in all_source and 'finish?.Invoke()' in all_source)
check('Missing task regression covers CLR HRESULT mapping', 'InstallerTaskErrors.Missing(e)' in maintenance and 'Installer missing task recognizes CLR and COM HRESULT mapping' in all_source)
check('Mail update category uses existing rules and metadata only', 'MailCategory="Update"' in all_source and 'UpdateDictionaryVersion' in all_source and 'Previously installed version cannot reopen from repeated mail' in all_source and 'MailUpdateStore.Capture' in all_source)
check('Consolidated release documentation included in publication', all((root/n).exists() and n in publish for n in ['CHANGELOG.md','USER_AGREEMENT_RU.txt','USER_AGREEMENT_EN.txt','THIRD_PARTY_NOTICES.md']))
audit=(root/'src/SysAdminAssistant.Core/RouterAudit.cs').read_text()
audit_store=(root/'src/SysAdminAssistant.Infrastructure/RouterAuditReader.cs').read_text()
check('Router audit keeps exact address sets and conservative unknown paths', 'class AuditAddresses' in audit and 'Except(AuditAddresses b)' in audit and 'AuditBudgetException' in audit and 'Match.Unknown' in audit)
check('Router audit keeps per-device scoped exclusions and review hash', 'i.Key==f.Key' in audit_store and 'request.ExpectedHash' in audit_store and 'finding.Ignored=request.Ignore' in audit_store)
check('Router audit covers RAW NAT IPv6 and hardware limitations', all(x in audit for x in ['raw4','nat4','filter6','connection-nat-state','l3-hw-offloading','bridge-ports']))
check('Router audit collector reads only explicit immutable print commands', 'FrozenDictionary' in audit_store and '/ip/firewall/filter/print' in audit_store and '/export' not in audit_store and '/system/script' not in audit_store)
check('Router audit has row list details and per-event ignore controls', all(x in (root/'src/SysAdminAssistant.Desktop/RouterAuditPages.cs').read_text() for x in ['Все проверки','Игнорировать','Не игнорировать','Скопировать команды','Внешние интерфейсы']))
check('Update icon uses content-only borderless template', 'FrameworkElementFactory(typeof(TextBlock))' in (root/'src/SysAdminAssistant.Desktop/UpdateIndicator.cs').read_text() and 'CellStyle=cellStyle' in all_source)
check('Device problems combine agent and journal with source-specific actions', 'UnifiedDeviceProblems.Merge' in all_source and 'row?.IsMail==true?Visibility.Visible:Visibility.Collapsed' in all_source and 'JournalId:entry?.Id' in all_source)
check('Reboot recovery requires fresh stable healthy reports and readable logs', 'RecoveryHealthySince' in all_source and 'systemReadable' in all_source and 'Repeated reboots stay critical despite live telemetry' in all_source)
check('Update availability is suppressed at common delivery boundary', 'UpdateNotifications.Suppress(s)' in (root/'src/SysAdminAssistant.Infrastructure/NetworkNotifications.cs').read_text() and 'Old queued availability notices are cancelled while real errors still send' in all_source)
check('NTFS informational correction preserves real errors and history', 'AgentEvents.HealthyNtfs' in all_source and 'Existing false NTFS alert reclassified without deleting event history' in all_source and 'Real NTFS 98 error still requires review' in all_source)
check('Large Router lists use exact filtered streaming and shared interval cache', 'ReadAddressListAsync' in all_source and '?list=' in all_source and 'AddressCache' in all_source and '50000 disjoint addresses stay exact rather than broadening into a CIDR' in all_source)
check('Server certificate renews and changes live TLS selection', 'now.AddDays(60)' in all_source and 'now.AddDays(7)' in all_source and 'ServerCertificateSelector' in all_source)
check('Agent and updater share anchored TLS policy', all_source.count('ServerTrust.Accept(cert,endpoint.IdnHost')>=2 and 'MatchesHostname(host,false,false)' in all_source)
check('Client persists server authority and refuses trust rollback', 'ClientServerTrust.Learn' in all_source and 'Конфликт версии доверия сервера' in all_source)
check('Timezone has UTC storage boundary and once-only legacy conversion', 'TimeStorage.Configure(b)' in all_source and 'sa2_TimeStorage' in all_source and 'AppTime.Scope(state.Settings.TimeZoneId)' in all_source)
check('Router audit separates IPsec policy NAT path and tracked state', 'case "ipsec-policy"' in all_source and 'UntrackedDefinite' in all_source and 'DST-NAT меняет назначение' in all_source)
check('Router policy collection uses no IPsec secrets', '=.proplist=.id,src-address,dst-address,protocol,src-port,dst-port,action,template,disabled,invalid,tunnel' in all_source)
check('Telegram topics apply at delivery and recovery icons are shown', 'Accepted(settings,n)' in all_source and 'TopicEnabled("recovery")' in all_source and '🟢' in all_source and 'ToggleChanged(key' in all_source)
check('TLS and full packet path regression tests included', 'Actual TLS handshake succeeds after server certificate rotation' in all_source and 'Earlier forward drop closes all later DNAT accepts' in all_source)
report={'status':'passed','checks':len(checks),'source_files':len(sources),'csharp_build':'see_build-verification.json','windows_runtime':'not_run','integration_tests':'not_run','checks_completed':checks}
(root/'verification/source-checks.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(f'{len(checks)} offline source/package checks passed. Source checks only; compiler/runtime results are in build-verification.json.')
