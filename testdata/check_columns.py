import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cur = conn.cursor()

# 获取表结构
cur.execute('PRAGMA table_info(drug_master)')
cols = cur.fetchall()
print("=== drug_master 列结构 ===")
for i, c in enumerate(cols):
    cur.execute(f'SELECT COUNT(*) FROM drug_master WHERE [{c[1]}] IS NULL')
    n = cur.fetchone()[0]
    print(f"  ordinal {i}: {c[1]} ({c[2]}) - NULL: {n}")

# 检查 is_antibiotic 列的所有值
cur.execute('SELECT DISTINCT is_antibiotic FROM drug_master')
print(f"\nis_antibiotic 所有值: {cur.fetchall()}")

# 检查 antibiotic_level
cur.execute('SELECT DISTINCT antibiotic_level FROM drug_master')
print(f"antibiotic_level 所有值: {cur.fetchall()}")

# 检查 is_toxic_drug
cur.execute('SELECT DISTINCT is_toxic_drug FROM drug_master')
print(f"is_toxic_drug 所有值: {cur.fetchall()}")

# 检查 generic_name_en
cur.execute('SELECT COUNT(*) FROM drug_master WHERE generic_name_en IS NULL')
print(f"generic_name_en NULL: {cur.fetchone()[0]}")

conn.close()
