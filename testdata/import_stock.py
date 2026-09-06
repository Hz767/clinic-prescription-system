import sqlite3
from datetime import datetime, timedelta

conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cur = conn.cursor()

# 先备份现有库存
cur.execute("CREATE TABLE IF NOT EXISTS drug_stock_backup AS SELECT * FROM drug_stock")
print("已备份现有库存到 drug_stock_backup")

# 清空现有库存
cur.execute("DELETE FROM drug_stock")
print(f"已清空现有库存: {cur.rowcount} 条")

# 获取所有药品
cur.execute("SELECT id, generic_name_cn, cost_price_ref FROM drug_master WHERE deleted_at IS NULL")
drugs = cur.fetchall()
print(f"待导入药品数量: {len(drugs)}")

# 批量插入库存记录
now = datetime.now().strftime('%Y-%m-%d %H:%M:%S.%f')
expiry = (datetime.now() + timedelta(days=365)).strftime('%Y-%m-%d')
batch_no = f"DEBUG-{datetime.now().strftime('%Y%m%d')}"

inserted = 0
for drug in drugs:
    drug_id = drug[0]
    cost_price = drug[2] if drug[2] is not None else 0
    cur.execute("""
        INSERT INTO drug_stock (drug_id, batch_no, expiry_date, qty_remaining, cost_price, supplier, received_at, created_at, deleted_at)
        VALUES (?, ?, ?, 999, ?, '调试库存', ?, ?, NULL)
    """, (drug_id, batch_no, expiry, cost_price, now, now))
    inserted += 1

conn.commit()
print(f"已导入库存记录: {inserted} 条")

# 验证
cur.execute("SELECT COUNT(*) FROM drug_stock")
print(f"库存总记录数: {cur.fetchone()[0]}")
cur.execute("SELECT SUM(qty_remaining) FROM drug_stock")
print(f"库存总量: {cur.fetchone()[0]}")
cur.execute("SELECT * FROM drug_stock LIMIT 3")
for row in cur.fetchall():
    print(row)

conn.close()
print("\n库存导入完成！")
