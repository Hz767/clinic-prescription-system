import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
conn.execute('DELETE FROM prescription_item WHERE prescription_id = 481')
conn.execute('DELETE FROM prescription WHERE id = 481')
conn.commit()
print('测试数据已清理')
print(f'处方总数: {conn.execute("SELECT COUNT(*) FROM prescription").fetchone()[0]}')
print(f'明细总数: {conn.execute("SELECT COUNT(*) FROM prescription_item").fetchone()[0]}')
conn.close()
