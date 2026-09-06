import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')

# 检查所有表
print('=== 所有表 ===')
tables = conn.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").fetchall()
for t in tables:
    print(f'  {t[0]}')

# 检查drug_master_old表
print('\n=== drug_master_old 表 ===')
try:
    count = conn.execute('SELECT COUNT(*) FROM drug_master_old').fetchone()[0]
    print(f'  记录数: {count}')
    rows = conn.execute('SELECT id, generic_name_cn FROM drug_master_old LIMIT 5').fetchall()
    for r in rows:
        print(f'  id={r[0]}, name={r[1]}')
except Exception as e:
    print(f'  错误: {e}')

# 检查drug_master表
print('\n=== drug_master 表 ===')
count = conn.execute('SELECT COUNT(*) FROM drug_master WHERE deleted_at IS NULL').fetchone()[0]
print(f'  有效记录数: {count}')

# 检查是否启用外键
print('\n=== 外键状态 ===')
fk = conn.execute('PRAGMA foreign_keys').fetchone()
print(f'  foreign_keys = {fk[0]}')

conn.close()
