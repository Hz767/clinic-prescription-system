import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cur = conn.cursor()
cur.execute('PRAGMA table_info(drug_master)')
cols = cur.fetchall()
print('Columns:')
for i, c in enumerate(cols):
    print(f'  [{i}] {c[1]} ({c[2]}) notnull={c[3]}')
print()
cur.execute('SELECT COUNT(*) FROM drug_master')
print(f'Total rows: {cur.fetchone()[0]}')
print()
# 检查每列的NULL数量
for i, c in enumerate(cols):
    cur.execute(f'SELECT COUNT(*) FROM drug_master WHERE [{c[1]}] IS NULL')
    null_count = cur.fetchone()[0]
    if null_count > 0:
        print(f'  NULL in [{i}] {c[1]}: {null_count}')
print()
# 查看前3行数据
cur.execute('SELECT * FROM drug_master LIMIT 3')
rows = cur.fetchall()
for row in rows:
    print(row)
conn.close()
