import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cur = conn.cursor()

# 跳过 generic_name_en（可空+唯一索引，NULL合法）
# 5. 修复 default_usage 为 NULL 的记录
cur.execute("UPDATE drug_master SET default_usage = '' WHERE default_usage IS NULL")
print(f"修复 default_usage NULL -> '': {cur.rowcount} 条")

# 6. 修复 contraindication_tags 为 NULL 的记录
cur.execute("UPDATE drug_master SET contraindication_tags = '' WHERE contraindication_tags IS NULL")
print(f"修复 contraindication_tags NULL -> '': {cur.rowcount} 条")

# 7. 修复 reorder_level 为 NULL 的记录（默认10）
cur.execute("UPDATE drug_master SET reorder_level = 10 WHERE reorder_level IS NULL")
print(f"修复 reorder_level NULL -> 10: {cur.rowcount} 条")

conn.commit()

# 验证修复结果
cur.execute("SELECT COUNT(*) FROM drug_master WHERE unit IS NULL OR unit = ''")
print(f"\n修复后 unit 空值: {cur.fetchone()[0]}")
cur.execute("SELECT COUNT(*) FROM drug_master WHERE spec IS NULL OR spec = ''")
print(f"修复后 spec 空值: {cur.fetchone()[0]}")
cur.execute("SELECT antibiotic_level, COUNT(*) FROM drug_master GROUP BY antibiotic_level")
print(f"修复后 antibiotic_level 分布: {cur.fetchall()}")
cur.execute("SELECT COUNT(*) FROM drug_master WHERE retail_price_ref IS NULL")
print(f"修复后 retail_price_ref NULL: {cur.fetchone()[0]}")
cur.execute("SELECT COUNT(*) FROM drug_master WHERE cost_price_ref IS NULL")
print(f"修复后 cost_price_ref NULL: {cur.fetchone()[0]}")

conn.close()
print("\n数据修复完成！")
