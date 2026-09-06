# -*- coding: utf-8 -*-
"""分步调试：定位迁移失败的语句。"""
import sqlite3, sys

p = r'E:\个人诊所处方系统\clinic.db'
conn = sqlite3.connect(p)
cur = conn.cursor()
cur.execute("PRAGMA foreign_keys=ON")

steps = [
    ("BEGIN", lambda: cur.execute("BEGIN IMMEDIATE")),
    ("RENAME", lambda: cur.execute("ALTER TABLE drug_master RENAME TO drug_master_legacy")),
    ("CREATE", lambda: cur.execute("""
        CREATE TABLE "drug_master" (
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
        )""")),
    ("INSERT", lambda: cur.execute("""
        INSERT INTO drug_master
            (id, generic_name_cn, generic_name_en, spec, unit, default_usage,
             is_antibiotic, antibiotic_level, is_toxic_drug, contraindication_tags,
             cost_price_ref, retail_price_ref, created_at, deleted_at, reorder_level)
        SELECT id, generic_name_cn, generic_name_en, spec, unit, default_usage,
               is_antibiotic, antibiotic_level, is_toxic_drug, contraindication_tags,
               cost_price_ref, retail_price_ref, created_at, deleted_at, reorder_level
        FROM drug_master_legacy""")),
    ("SEQ", lambda: cur.execute("UPDATE sqlite_sequence SET seq=(SELECT MAX(id) FROM drug_master) WHERE name='drug_master'")),
    ("DROP", lambda: cur.execute("DROP TABLE drug_master_legacy")),
]

for name, fn in steps:
    try:
        fn()
        print(f"  OK: {name}")
    except Exception as e:
        print(f"  FAIL: {name} -> {e}")
        conn.rollback()
        sys.exit(1)

cur.execute("COMMIT")
print("全部完成")
conn.close()
