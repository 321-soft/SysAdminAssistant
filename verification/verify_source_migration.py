"""Exercise SQLite DDL extracted from SourceSchema.cs; EF/C# and SQL Server are not executed."""
from pathlib import Path
import re,sqlite3,json
root=Path(__file__).resolve().parents[1]
model=(root/'src/SysAdminAssistant.Core/Models.cs').read_text()
enum=re.search(r'enum SearchScope\s*\{([^}]+)\}',model)[1]
values={};ordinal=0
for member in enum.split(','):
 name,separator,value=member.strip().partition('=')
 if separator:ordinal=int(value.strip())
 values[name.strip()]=ordinal;ordinal+=1
source=(root/'src/SysAdminAssistant.Infrastructure/SourceSchema.cs').read_text()
columns=re.findall(r'\("(\w+)", \$?"([^"]+)", \$?"([^"]+)"\)',source)
assert len(columns)==7
columns=[(name,sqlite.replace('{(int)SearchScope.Subject}',str(values['Subject'])),sqlserver.replace('{(int)SearchScope.Subject}',str(values['Subject']))) for name,sqlite,sqlserver in columns]
c=sqlite3.connect(':memory:')
c.execute('CREATE TABLE sa2_KeywordProfile (Id INTEGER PRIMARY KEY, Provider TEXT, Critical TEXT)')
c.executemany('INSERT INTO sa2_KeywordProfile VALUES(?,?,?)',[(1,'Любой','keep custom words'),(2,'ExampleBackup','keep other words')])
def migrate():
 existing={row[1] for row in c.execute("PRAGMA table_info('sa2_KeywordProfile')")}
 for name,sqlite,_ in columns:
  if name not in existing:c.execute(f'ALTER TABLE [sa2_KeywordProfile] ADD [{name}] {sqlite}')
migrate()
assert c.execute("SELECT EventRulesJson, AdditionalEventRulesJson FROM sa2_KeywordProfile WHERE Id=1").fetchone()==("","")
assert c.execute('SELECT ResultScope,MatchAllSourceFields,SenderTerms,SubjectTerms,BodyTerms,Critical FROM sa2_KeywordProfile WHERE Id=1').fetchone()==(values['Subject'],1,'','','','keep custom words')
c.execute('UPDATE sa2_KeywordProfile SET ResultScope=? WHERE Id=1',(values['Both'],))
c.execute('UPDATE sa2_KeywordProfile SET ResultScope=? WHERE Id=2',(values['Body'],))
migrate();migrate()
assert c.execute('SELECT ResultScope,Critical FROM sa2_KeywordProfile ORDER BY Id').fetchall()==[(values['Both'],'keep custom words'),(values['Body'],'keep other words')]
assert next(sql for name,_,sql in columns if name=='ResultScope').endswith('DEFAULT '+str(values['Subject']))
result={'status':'passed','engine':'Python sqlite3; DDL extracted from C# source','enum':values,'checks':['new column defaults to Subject','source filters empty and all-fields match enabled','custom dictionaries retained','repeated initialization preserves Both and Body','SQL Server DDL declares matching enum default (not executed)'],'csharp_ef_core':'not_run_no_sdk','sql_server':'not_run'}
(root/'verification/source-migration-checks.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
print('PASS extracted SQLite migration DDL and repeat-initialization scenarios; EF/C# and SQL Server NOT executed.')
