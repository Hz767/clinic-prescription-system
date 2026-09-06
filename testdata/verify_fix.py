import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cur = conn.cursor()

# 检查 drug_master 各列的 NULL 情况
cur.execute('PRAGMA table_info(drug_master)')
cols = cur.fetchall()
print("=== drug_master NULL 检查 ===")
for c in cols:
    cur.execute(f'SELECT COUNT(*) FROM drug_master WHERE [{c[1]}] IS NULL')
    n = cur.fetchone()[0]
    if n > 0:
        print(f"  {c[1]}: {n} NULL")

# 检查 drug_stock
cur.execute('SELECT COUNT(*) FROM drug_stock')
print(f"\ndrug_stock 总数: {cur.fetchone()[0]}")
cur.execute('PRAGMA table_info(drug_stock)')
cols = cur.fetchall()
print("=== drug_stock NULL 检查 ===")
for c in cols:
    cur.execute(f'SELECT COUNT(*) FROM drug_stock WHERE [{c[1]}] IS NULL')
    n = cur.fetchone()[0]
    if n > 0:
        print(f"  {c[1]}: {n} NULL")

# 查看前3条 drug_master
cur.execute('SELECT * FROM drug_master LIMIT 3')
print("\n=== drug_master 前3条 ===")
for row in cur.fetchall():
    print(row)

conn.close()
