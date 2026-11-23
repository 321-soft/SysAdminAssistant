"""Offline SQLite deletion/receipt scenario. This does not execute EF Core or C#."""
from pathlib import Path
import sqlite3,hashlib,json
root=Path(__file__).resolve().parents[1]
c=sqlite3.connect(':memory:');c.execute('PRAGMA foreign_keys=ON')
c.executescript('''CREATE TABLE reports(id INTEGER PRIMARY KEY, received TEXT);
CREATE TABLE pending(event_id INTEGER REFERENCES reports(id) ON DELETE CASCADE, body TEXT);
CREATE TABLE intake(digest TEXT UNIQUE, event_id INTEGER REFERENCES reports(id) ON DELETE CASCADE);
CREATE TABLE retired(digest TEXT UNIQUE);
CREATE TABLE incidents(report_id INTEGER, created TEXT);
CREATE TABLE inventory(id INTEGER PRIMARY KEY);
''')
key=hashlib.sha256(b'box\x00uid').hexdigest().upper()
c.execute('INSERT INTO reports VALUES(1,?)',('2026-06-01',));c.execute('INSERT INTO reports VALUES(2,?)',('2026-10-09',))
c.execute('INSERT INTO pending VALUES(1,?)',('old raw mail',));c.execute('INSERT INTO intake VALUES(?,1)',(key,))
c.execute('INSERT INTO incidents VALUES(1,?)',('2026-06-01',));c.execute('INSERT INTO inventory VALUES(1)');c.commit()
with c:
 c.execute("INSERT INTO retired SELECT digest FROM intake WHERE event_id IN (SELECT id FROM reports WHERE received<'2026-09-09')")
 c.execute("DELETE FROM incidents WHERE created<'2026-09-09' OR report_id IN (SELECT id FROM reports WHERE received<'2026-09-09')")
 c.execute("DELETE FROM reports WHERE received<'2026-09-09'")
checks={
 'old_raw_body_deleted':c.execute('SELECT COUNT(*) FROM pending').fetchone()[0]==0,
 'old_intake_reference_deleted':c.execute('SELECT COUNT(*) FROM intake').fetchone()[0]==0,
 'hash_only_receipt_preserved':c.execute('SELECT digest FROM retired').fetchone()[0]==key,
 'new_report_preserved':c.execute('SELECT id FROM reports').fetchall()==[(2,)],
 'inventory_preserved':c.execute('SELECT COUNT(*) FROM inventory').fetchone()[0]==1,
 'old_incident_deleted':c.execute('SELECT COUNT(*) FROM incidents').fetchone()[0]==0,
}
assert all(checks.values()),checks
result={'status':'passed','checks':checks,'engine':'Python sqlite3; minimal schema and algorithm scenario','ef_core':'not_run','csharp_compilation':'not_run_no_sdk'}
(root/'verification/report-feature-checks.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
print('PASS 6 SQLite deletion/receipt scenario checks; EF Core and C# NOT executed.')
