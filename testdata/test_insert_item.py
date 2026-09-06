import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')

# 检查prescription_item表结构
print('=== prescription_item 表结构 ===')
cols = conn.execute('PRAGMA table_info(prescription_item)').fetchall()
for c in cols:
    print(f'  {c[1]}: {c[2]}, NOT NULL={c[3]}, default={c[4]}')

# 检查外键约束
print('\n=== prescription_item 外键 ===')
fks = conn.execute('PRAGMA foreign_key_list(prescription_item)').fetchall()
for fk in fks:
    print(f'  {fk}')

# 检查索引
print('\n=== prescription_item 索引 ===')
indexes = conn.execute('PRAGMA index_list(prescription_item)').fetchall()
for idx in indexes:
    print(f'  {idx}')

# 尝试插入一条明细
print('\n=== 尝试插入明细 ===')
try:
    conn.execute("""
        INSERT INTO prescription_item 
        (prescription_id, drug_id, drug_name, spec, dose, dose_unit, frequency, route, duration_days, qty, unit_price, subtotal, created_at)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    """, (481, 29, '布洛芬缓释胶囊', '0.3g×20粒', '1.0', '粒', '每日三次', '口服', 3, '9.0', 15.0, 135.0, '2026-09-06 01:00:00'))
    conn.commit()
    print('  插入成功！')
except Exception as e:
    print(f'  插入失败: {e}')

# 检查插入结果
rows = conn.execute('SELECT * FROM prescription_item WHERE prescription_id = 481').fetchall()
print(f'\n  处方481明细数量: {len(rows)}')

conn.close()
