import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')

print('=== drug_master 表结构 ===')
cols = conn.execute('PRAGMA table_info(drug_master)').fetchall()
for c in cols:
    print(f'  {c[1]}: {c[2]}')

print('\n=== 盐酸环丙沙星片数据 ===')
rows = conn.execute("SELECT * FROM drug_master WHERE generic_name_cn LIKE '%环丙沙星%' LIMIT 3").fetchall()
cols = [c[1] for c in conn.execute('PRAGMA table_info(drug_master)').fetchall()]
for r in rows:
    for i, col in enumerate(cols):
        print(f'  {col}: {r[i]}')
    print()

conn.close()
