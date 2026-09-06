# -*- coding: utf-8 -*-
"""查询 clinic.db 的 drug_master 表结构与数据概况（只读）。"""
import sqlite3

p = r'E:\个人诊所处方系统\clinic.db'
conn = sqlite3.connect(p)
cur = conn.cursor()
cur.execute("PRAGMA table_info(drug_master)")
print("== drug_master 列定义 ==")
for r in cur.fetchall():
    print(f"  {r[1]} type={r[2]} notnull={r[3]} default={r[4]} pk={r[5]}")

cur.execute("SELECT sql FROM sqlite_master WHERE type='table' AND name='drug_master'")
print("\n== CREATE TABLE SQL ==")
print(cur.fetchone()[0])

cur.execute("SELECT COUNT(*) FROM drug_master")
print("\ndrug_master 总行数:", cur.fetchone()[0])
cur.execute("SELECT COUNT(*) FROM drug_master WHERE generic_name_en IS NULL OR generic_name_en=''")
print("英文名为空的行数:", cur.fetchone()[0])
cur.execute("SELECT generic_name_cn, generic_name_en FROM drug_master LIMIT 5")
print("样例:", cur.fetchall())
conn.close()
