import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
conn.execute('CREATE UNIQUE INDEX IF NOT EXISTS IX_drug_master_generic_name_en ON drug_master(generic_name_en)')
conn.commit()
print('唯一索引已重建')
idx = conn.execute("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='drug_master'").fetchall()
print('索引列表:', idx)
conn.close()
