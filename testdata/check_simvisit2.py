# -*- coding: utf-8 -*-
import sqlite3, os, datetime
p = r'E:\个人诊所处方系统\testdata\simvisit\bin\Debug\net10.0\simvisit.db'
print('mtime:', datetime.datetime.fromtimestamp(os.path.getmtime(p)))
conn = sqlite3.connect(p)
cur = conn.cursor()
for t in ['patient','prescription','prescription_item','drug_master','drug_stock','drug_out','payment_log','audit_log']:
    cur.execute(f'SELECT COUNT(*) FROM {t}')
    print(f'{t}:', cur.fetchone()[0])
cur.execute("SELECT status, COUNT(*) FROM prescription GROUP BY status")
print('prescription 状态分布:', cur.fetchall())
conn.close()
