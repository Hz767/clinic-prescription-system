# -*- coding: utf-8 -*-
"""检查 simvisit.db 表结构与行数，确认二次模拟就诊测试是否完成。"""
import sqlite3, os, datetime

p = r'E:\个人诊所处方系统\testdata\simvisit\bin\Debug\net10.0\simvisit.db'
print('db:', p)
print('mtime:', datetime.datetime.fromtimestamp(os.path.getmtime(p)))
print('size:', os.path.getsize(p))

conn = sqlite3.connect(p)
cur = conn.cursor()
cur.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")
tables = [r[0] for r in cur.fetchall()]
print('tables:', tables)
for t in tables:
    try:
        cur.execute(f'SELECT COUNT(*) FROM "{t}"')
        print(f'  {t}: {cur.fetchone()[0]} rows')
    except Exception as e:
        print(f'  {t}: ERR {e}')
conn.close()
