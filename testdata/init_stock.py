import sqlite3
from datetime import datetime, timezone

db_path = r'E:\个人诊所处方系统\clinic.db'
conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# 检查现有库存
existing_stock = cursor.execute('SELECT drug_id FROM drug_stock').fetchall()
existing_ids = set(r[0] for r in existing_stock)
print(f'已有库存的药品: {len(existing_ids)} 种')

# 获取所有药品
drugs = cursor.execute('SELECT id, generic_name_cn, spec FROM drug_master WHERE deleted_at IS NULL').fetchall()
print(f'药品总数: {len(drugs)} 种')

# 为没有库存的药品创建初始库存 999
now = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S')
added = 0
for drug in drugs:
    drug_id = drug[0]
    if drug_id not in existing_ids:
        cursor.execute('''
            INSERT INTO drug_stock (drug_id, batch_no, expiry_date, qty_remaining, cost_price, received_at, created_at)
            VALUES (?, ?, ?, ?, ?, ?, ?)
        ''', (drug_id, 'INIT-999', '2027-12-31', 999, 0, now, now))
        added += 1

conn.commit()

# 验证
total_stock = cursor.execute('SELECT COUNT(*) FROM drug_stock').fetchone()[0]
total_qty = cursor.execute('SELECT SUM(qty_remaining) FROM drug_stock').fetchone()[0]
print(f'\n库存初始化完成:')
print(f'  新增库存记录: {added} 条')
print(f'  库存记录总数: {total_stock} 条')
print(f'  库存总量: {total_qty}')

conn.close()
