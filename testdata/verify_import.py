# -*- coding: utf-8 -*-
"""验证 clinic.db 药品清单导入结果（只读）。"""
import sqlite3

p = r'E:\个人诊所处方系统\clinic.db'
conn = sqlite3.connect(p)
cur = conn.cursor()

cur.execute("SELECT COUNT(*) FROM drug_master")
print("drug_master 总行数:", cur.fetchone()[0])

cur.execute("SELECT COUNT(*) FROM drug_master WHERE generic_name_en IS NULL")
print("英文名为 NULL:", cur.fetchone()[0])

cur.execute("SELECT COUNT(*) FROM drug_master WHERE is_antibiotic=1")
print("抗菌药:", cur.fetchone()[0])
cur.execute("SELECT COUNT(*) FROM drug_master WHERE antibiotic_level=2")
print("特殊/限制使用级抗菌药:", cur.fetchone()[0])

cur.execute("SELECT COUNT(*) FROM drug_master WHERE spec IS NULL OR spec=''")
print("无剂型:", cur.fetchone()[0])

# 抽查样例
cur.execute("""
SELECT generic_name_cn, generic_name_en, spec, is_antibiotic, antibiotic_level
FROM drug_master ORDER BY id LIMIT 12
""")
print("\n== 前 12 条（含 5 种子 + 新导入） ==")
for r in cur.fetchall():
    print(f"  {r[0]} | EN={r[1]} | 剂型={r[2]} | 抗菌={r[3]} | 级别={r[4]}")

# 阿莫西林 / 头孢 / 布洛芬 相关
print("\n== 阿莫西林相关 ==")
cur.execute("SELECT generic_name_cn, spec FROM drug_master WHERE generic_name_cn LIKE '%阿莫西林%' LIMIT 6")
for r in cur.fetchall(): print(f"  {r[0]} [{r[1]}]")

print("\n== 头孢相关（前6） ==")
cur.execute("SELECT generic_name_cn, spec FROM drug_master WHERE generic_name_cn LIKE '%头孢%' LIMIT 6")
for r in cur.fetchall(): print(f"  {r[0]} [{r[1]}]")

print("\n== 特殊/限制使用级抗菌药 ==")
cur.execute("SELECT generic_name_cn FROM drug_master WHERE antibiotic_level=2 LIMIT 20")
for r in cur.fetchall(): print(f"  {r[0]}")

# 审计日志
cur.execute("SELECT COUNT(*) FROM audit_log WHERE action='DRUG_CATALOG_IMPORT'")
print("\n审计 DRUG_CATALOG_IMPORT:", cur.fetchone()[0])

# 子表引用完整性
cur.execute("PRAGMA foreign_key_check")
print("外键完整性:", "通过" if not cur.fetchall() else "有问题")

conn.close()
