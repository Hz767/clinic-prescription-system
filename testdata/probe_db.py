# -*- coding: utf-8 -*-
"""一次性探查：正式库 clinic.db 药品标签现状。只读，不修改。"""
import sqlite3

conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cur = conn.cursor()
rows = cur.execute(
    'SELECT id, generic_name_cn, generic_name_en, contraindication_tags '
    'FROM drug_master ORDER BY id').fetchall()
print('药品总数:', len(rows))
no_tag = [r for r in rows if not (r[3] or '').strip()]
print('缺标签药品数:', len(no_tag))
print('--- 缺标签药品 ---')
for r in no_tag:
    print(r[0], r[1], '|', r[2])
print('--- 有标签药品（前 15）---')
for r in [r for r in rows if (r[3] or '').strip()][:15]:
    print(r)
conn.close()
