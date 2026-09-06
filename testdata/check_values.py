import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cur = conn.cursor()
# 检查 antibiotic_level 的值分布
cur.execute('SELECT antibiotic_level, COUNT(*) FROM drug_master GROUP BY antibiotic_level')
print('antibiotic_level distribution:')
for row in cur.fetchall():
    print(f'  {row[0]}: {row[1]}')
# 检查 is_antibiotic 的值分布
cur.execute('SELECT is_antibiotic, COUNT(*) FROM drug_master GROUP BY is_antibiotic')
print('is_antibiotic distribution:')
for row in cur.fetchall():
    print(f'  {row[0]}: {row[1]}')
# 检查 is_toxic_drug 的值分布
cur.execute('SELECT is_toxic_drug, COUNT(*) FROM drug_master GROUP BY is_toxic_drug')
print('is_toxic_drug distribution:')
for row in cur.fetchall():
    print(f'  {row[0]}: {row[1]}')
# 检查是否有空字符串或异常值
cur.execute("SELECT COUNT(*) FROM drug_master WHERE generic_name_cn = '' OR spec = '' OR unit = ''")
print(f'Empty required fields: {cur.fetchone()[0]}')
# 查看 created_at 的值分布
cur.execute('SELECT created_at, COUNT(*) FROM drug_master GROUP BY created_at LIMIT 5')
print('created_at samples:')
for row in cur.fetchall():
    print(f'  {row[0]}: {row[1]}')
conn.close()
