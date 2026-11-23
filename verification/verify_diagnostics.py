"""Checks catalogue data and shared regex fixtures with Python, NOT the C# runtime."""
from pathlib import Path
import json,re
from urllib.parse import urlparse
root=Path(__file__).resolve().parents[1]
entries=json.loads((root/'src/SysAdminAssistant.Core/Diagnostics/catalog.json').read_text())
fixtures=json.loads((root/'verification/diagnostic-fixtures.json').read_text())
allowed={'www.veeam.com','helpcenter.veeam.com','sqlbackupandftp.com','kb.synology.com','www.qnap.com','docs.qnap.com','learn.microsoft.com'}
assert len({e['Id'] for e in entries})==len(entries)
for e in entries:
 u=urlparse(e['Source']);assert u.scheme=='https' and u.hostname in allowed and not u.username,e['Id']
 assert all(e[k] for k in ['Title','Providers','Meaning','Impact','Steps','Scope','Confidence','Reviewed']),e['Id']
 for p in e['Patterns']:re.compile(p,re.I)
metadata=re.compile(r'^(?:job(?: name)?|backup job|task(?: name)?|server|host|computer|nas|задание|имя задания|сервер|устройство|duration|size|items|errors|warnings|start time|end time)\s*[:=]',re.I)
negated=re.compile(r'(?:previous|historical|example|resolved|fixed|was resolved|was fixed|устранено|пример|ранее)\s*(?:message|error|warning|сообщение|ошибка)?\s*[:=-]?\s*$|(?:no|without|not|нет|без)\s*$',re.I)
def find(provider,body):
 lines=[re.sub(r'\s+',' ',x).strip() for x in body[:500000].replace('\r','').split('\n')[:4000] if len(x)<=4000]
 lines=[x for x in lines if x and not metadata.search(x) and not x.startswith('>')]
 return sorted(e['Id'] for e in entries if provider.casefold() in [x.casefold() for x in e['Providers']] and any((m:=re.search(p,line,re.I)) and not negated.search(line[:m.start()].rstrip()) for line in lines for p in e['Patterns']))
for f in fixtures:assert find(f['Provider'],f['Body'])==sorted(f['Expected']),f['Name']
assert {x for f in fixtures for x in f['Expected']}=={e['Id'] for e in entries},'Every article requires a positive fixture'
summary={'status':'passed','catalogue_entries':len(entries),'regex_fixtures':len(fixtures),'engine':'Python re (catalogue data check only)','csharp_runtime':'not_run_no_sdk'}
(root/'verification/diagnostic-checks.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n')
print(f"PASS {len(entries)} catalogue entries, {len(fixtures)} shared regex fixtures. C# runtime NOT checked.")
