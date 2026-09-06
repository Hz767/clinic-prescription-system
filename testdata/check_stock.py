import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cur = conn.cursor()
cur.execute('PRAGMA table_info(drug_stock)')
cols = cur.fetchall()
print('drug_stock columns:')
for i, c in enumerate(cols):
    print(f'  [{i}] {c[1]} ({c[2]}) notnull={c[3]}')
cur.execute('SELECT COUNT(*) FROM drug_stock')
print(f'\nTotal rows: {cur.fetchone()[0]}')
# 检查每列NULL
for c in cols:
    cur.execute(f'SELECT COUNT(*) FROM drug_stock WHERE [{c[1]}] IS NULL')
    n = cur.fetchone()[0]
    if n > 0:
        print(f'  NULL in {c[1]}: {n}')
# 查看前3行
cur.execute('SELECT * FROM drug_stock LIMIT 3')
for row in cur.fetchall():
    print(row)
conn.close()
