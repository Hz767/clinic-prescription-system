# -*- coding: utf-8 -*-
"""复核：正式库 clinic.db 药品标签修复结果。只读。"""
import sqlite3

conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cur = conn.cursor()
print('--- drug_master 标签状态 ---')
for r in cur.execute(
        'SELECT id, generic_name_cn, contraindication_tags FROM drug_master ORDER BY id'):
    print(r)
print('--- 表结构变化（P0-P1 迁移列）---')
for r in cur.execute('PRAGMA table_info(prescription)'):
    print(r[1], r[2])
conn.close()
