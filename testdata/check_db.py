import sqlite3
import os

db = r'E:\个人诊所处方系统\Backups\clinic_pre_druglist_20260905_051920.db'
conn = sqlite3.connect(db)
print('完整性:', conn.execute('PRAGMA integrity_check').fetchone()[0])
print('表列表:')
tables = conn.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").fetchall()
for t in tables:
    try:
        count = conn.execute(f'SELECT COUNT(*) FROM [{t[0]}]').fetchone()[0]
        print(f'  {t[0]}: {count} 条')
    except:
        print(f'  {t[0]}: (无法查询)')
conn.close()
