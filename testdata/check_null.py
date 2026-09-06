import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cur = conn.cursor()
# 检查 is_antibiotic 是否有NULL
cur.execute('SELECT COUNT(*) FROM drug_master WHERE is_antibiotic IS NULL')
print(f'is_antibiotic NULL: {cur.fetchone()[0]}')
cur.execute('SELECT COUNT(*) FROM drug_master WHERE antibiotic_level IS NULL')
print(f'antibiotic_level NULL: {cur.fetchone()[0]}')
cur.execute('SELECT COUNT(*) FROM drug_master WHERE is_toxic_drug IS NULL')
print(f'is_toxic_drug NULL: {cur.fetchone()[0]}')
# 查看是否有异常行
cur.execute('SELECT id, generic_name_cn, is_antibiotic, antibiotic_level, is_toxic_drug FROM drug_master WHERE is_antibiotic IS NULL OR antibiotic_level IS NULL OR is_toxic_drug IS NULL LIMIT 10')
rows = cur.fetchall()
for r in rows:
    print(r)
# 查看所有列的类型
cur.execute('SELECT typeof(is_antibiotic), COUNT(*) FROM drug_master GROUP BY typeof(is_antibiotic)')
print('is_antibiotic types:', cur.fetchall())
conn.close()
