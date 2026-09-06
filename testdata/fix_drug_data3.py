import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cur = conn.cursor()

# 1. 修复 antibiotic_level 为 0 的记录（枚举 NonRestricted=1）
cur.execute("UPDATE drug_master SET antibiotic_level = 1 WHERE antibiotic_level = 0")
print(f"修复 antibiotic_level=0 -> 1: {cur.rowcount} 条")

# 2. 修复 unit 为空的记录
cur.execute("UPDATE drug_master SET unit = '盒' WHERE unit IS NULL OR unit = ''")
print(f"修复 unit 为空 -> '盒': {cur.rowcount} 条")

# 3. 修复 spec 为空的记录
cur.execute("UPDATE drug_master SET spec = '详见说明书' WHERE spec IS NULL OR spec = ''")
print(f"修复 spec 为空 -> '详见说明书': {cur.rowcount} 条")

# 4. 修复 retail_price_ref 为 NULL 的记录（默认0）
cur.execute("UPDATE drug_master SET retail_price_ref = 0 WHERE retail_price_ref IS NULL")
print(f"修复 retail_price_ref NULL -> 0: {cur.rowcount} 条")

# 5. 修复 cost_price_ref 为 NULL 的记录（默认0）
cur.execute("UPDATE drug_master SET cost_price_ref = 0 WHERE cost_price_ref IS NULL")
print(f"修复 cost_price_ref NULL -> 0: {cur.rowcount} 条")

# 跳过 generic_name_en（可空+唯一索引，NULL合法）

# 6. 修复 default_usage 为 NULL 的记录
cur.execute("UPDATE drug_master SET default_usage = '' WHERE default_usage IS NULL")
print(f"修复 default_usage NULL -> '': {cur.rowcount} 条")

# 7. 修复 contraindication_tags 为 NULL 的记录
cur.execute("UPDATE drug_master SET contraindication_tags = '' WHERE contraindication_tags IS NULL")
print(f"修复 contraindication_tags NULL -> '': {cur.rowcount} 条")

# 8. 修复 reorder_level 为 NULL 的记录（默认10）
cur.execute("UPDATE drug_master SET reorder_level = 10 WHERE reorder_level IS NULL")
print(f"修复 reorder_level NULL -> 10: {cur.rowcount} 条")

conn.commit()

# 验证修复结果
print("\n=== 验证 ===")
cur.execute("SELECT COUNT(*) FROM drug_master WHERE unit IS NULL OR unit = ''")
print(f"unit 空值: {cur.fetchone()[0]}")
cur.execute("SELECT COUNT(*) FROM drug_master WHERE spec IS NULL OR spec = ''")
print(f"spec 空值: {cur.fetchone()[0]}")
cur.execute("SELECT antibiotic_level, COUNT(*) FROM drug_master GROUP BY antibiotic_level")
print(f"antibiotic_level 分布: {cur.fetchall()}")
cur.execute("SELECT COUNT(*) FROM drug_master WHERE retail_price_ref IS NULL")
print(f"retail_price_ref NULL: {cur.fetchone()[0]}")
cur.execute("SELECT COUNT(*) FROM drug_master WHERE cost_price_ref IS NULL")
print(f"cost_price_ref NULL: {cur.fetchone()[0]}")

conn.close()
print("\n数据修复完成！")
