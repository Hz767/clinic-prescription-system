# -*- coding: utf-8 -*-
"""
一次性迁移（修正版）：drug_master.generic_name_en 允许 NULL。
采用 SQLite 官方 12 步重建法：
  1. foreign_keys=OFF（事务外）
  2. 创建新表 drug_master_new（generic_name_en TEXT NULL + UNIQUE）
  3. INSERT 复制数据
  4. DROP 旧表 drug_master（子表 FK 文本仍指向 drug_master）
  5. ALTER TABLE drug_master_new RENAME TO drug_master（占回原名，子表 FK 有效）
  6. 更新 sqlite_sequence；foreign_key_check 验证；foreign_keys=ON
无论 RENAME 的 FK 引用更新行为如何，子表 FK 最终都指向 drug_master（新表）。
已备份：Backups/clinic_pre_druglist_*.db
"""
import sqlite3, sys

p = r'E:\个人诊所处方系统\clinic.db'
conn = sqlite3.connect(p)
cur = conn.cursor()

cur.execute("PRAGMA foreign_keys=OFF")  # 事务外生效

cur.execute("SELECT COUNT(*) FROM drug_master")
before = cur.fetchone()[0]
print(f"迁移前 drug_master 行数: {before}")

try:
    cur.execute("BEGIN")

    # 1. 建新表
    cur.execute("""
    CREATE TABLE "drug_master_new" (
        "id" INTEGER NOT NULL CONSTRAINT "PK_drug_master" PRIMARY KEY AUTOINCREMENT,
        "generic_name_cn" TEXT NOT NULL,
        "generic_name_en" TEXT NULL,
        "spec" TEXT NOT NULL,
        "unit" TEXT NOT NULL,
        "default_usage" TEXT NULL,
        "is_antibiotic" INTEGER NOT NULL,
        "antibiotic_level" INTEGER NOT NULL,
        "is_toxic_drug" INTEGER NOT NULL,
        "contraindication_tags" TEXT NULL,
        "cost_price_ref" DECIMAL(10,2) NULL,
        "retail_price_ref" DECIMAL(10,2) NULL,
        "created_at" TEXT NOT NULL,
        "deleted_at" TEXT NULL,
        "reorder_level" REAL,
        CONSTRAINT "AK_drug_master_generic_name_en" UNIQUE ("generic_name_en")
    )
    """)

    # 2. 复制数据
    cur.execute("""
    INSERT INTO drug_master_new
        (id, generic_name_cn, generic_name_en, spec, unit, default_usage,
         is_antibiotic, antibiotic_level, is_toxic_drug, contraindication_tags,
         cost_price_ref, retail_price_ref, created_at, deleted_at, reorder_level)
    SELECT id, generic_name_cn, generic_name_en, spec, unit, default_usage,
           is_antibiotic, antibiotic_level, is_toxic_drug, contraindication_tags,
           cost_price_ref, retail_price_ref, created_at, deleted_at, reorder_level
    FROM drug_master
    """)

    # 3. DROP 旧表（FK 已 OFF，无约束检查；子表 FK 文本仍指向 drug_master）
    cur.execute("DROP TABLE drug_master")

    # 4. 新表占回原名
    cur.execute("ALTER TABLE drug_master_new RENAME TO drug_master")

    # 5. 更新自增序列
    cur.execute("UPDATE sqlite_sequence SET seq=(SELECT MAX(id) FROM drug_master) WHERE name='drug_master'")

    cur.execute("COMMIT")
    print("迁移事务已提交")
except Exception as e:
    conn.rollback()
    print(f"迁移失败，已回滚: {e}")
    sys.exit(1)
finally:
    cur.execute("PRAGMA foreign_keys=ON")

# 迁移后验证
cur.execute("SELECT COUNT(*) FROM drug_master")
after = cur.fetchone()[0]
print(f"迁移后 drug_master 行数: {after}")
assert after == before, "行数不一致！"

cur.execute("PRAGMA foreign_key_check")
fk_issues = cur.fetchall()
print(f"外键完整性检查: {'通过' if not fk_issues else '发现问题: ' + str(fk_issues)}")
if fk_issues:
    sys.exit(1)

cur.execute("PRAGMA table_info(drug_master)")
cols = {r[1]: r for r in cur.fetchall()}
print(f"generic_name_en notnull={cols['generic_name_en'][3]}（期望 0）")

cur.execute("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='drug_master'")
print("索引:", [r[0] for r in cur.fetchall()])

# 子表 FK 引用目标确认
for t in ['drug_stock', 'drug_in', 'drug_out', 'prescription_item', 'drug_interaction']:
    try:
        cur.execute(f"SELECT sql FROM sqlite_master WHERE type='table' AND name='{t}'")
        sql = cur.fetchone()[0]
        if 'drug_master' in sql:
            print(f"子表 {t} 外键引用: 指向 drug_master（有效）")
    except Exception:
        pass

conn.close()
print("迁移完成")
