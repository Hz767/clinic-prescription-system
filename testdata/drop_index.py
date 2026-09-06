import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
conn.execute('DROP INDEX IF EXISTS IX_drug_master_generic_name_en')
conn.commit()
print('已删除 generic_name_en 唯一索引')
idx = conn.execute("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='drug_master'").fetchall()
print('剩余索引:', idx)
conn.close()
