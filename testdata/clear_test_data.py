import sqlite3

conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cursor = conn.cursor()

# 清空测试数据（保留前7个原始患者和前5张处方）
print('清空测试数据...')

# 删除外键关联的表
cursor.execute('DELETE FROM prescription_item WHERE prescription_id > 5')
cursor.execute('DELETE FROM prescription WHERE id > 5')
cursor.execute('DELETE FROM medical_record WHERE id > 0')
cursor.execute('DELETE FROM payment_log WHERE id > 2')
cursor.execute('DELETE FROM patient WHERE id > 7')

# 重置自增ID
cursor.execute("DELETE FROM sqlite_sequence WHERE name IN ('prescription_item', 'prescription', 'medical_record', 'payment_log', 'patient')")

conn.commit()

print('清空完成！')
print(f'  患者: {cursor.execute("SELECT COUNT(*) FROM patient").fetchone()[0]} 人')
print(f'  处方: {cursor.execute("SELECT COUNT(*) FROM prescription").fetchone()[0]} 张')
print(f'  明细: {cursor.execute("SELECT COUNT(*) FROM prescription_item").fetchone()[0]} 条')
print(f'  病历: {cursor.execute("SELECT COUNT(*) FROM medical_record").fetchone()[0]} 份')

conn.close()
