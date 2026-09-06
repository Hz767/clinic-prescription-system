import sqlite3
import shutil
import os

db_path = r'E:\个人诊所处方系统\clinic.db'
backup_path = r'E:\个人诊所处方系统\clinic_pre_fk_fix.db'

# 备份
shutil.copy2(db_path, backup_path)
print(f'已备份到: {backup_path}')

conn = sqlite3.connect(db_path)
conn.execute('PRAGMA foreign_keys = OFF')

# 获取prescription_item表的创建语句
print('\n=== 原表创建语句 ===')
create_sql = conn.execute("SELECT sql FROM sqlite_master WHERE type='table' AND name='prescription_item'").fetchone()[0]
print(create_sql)

# 创建新表（外键指向drug_master）
print('\n=== 创建新表 ===')
conn.execute("""
CREATE TABLE prescription_item_new (
    id INTEGER NOT NULL CONSTRAINT PK_prescription_item PRIMARY KEY AUTOINCREMENT,
    prescription_id INTEGER NOT NULL,
    drug_id INTEGER NOT NULL,
    drug_name TEXT NOT NULL,
    spec TEXT NOT NULL,
    dose TEXT NOT NULL,
    dose_unit TEXT NOT NULL,
    frequency TEXT NOT NULL,
    route TEXT NOT NULL,
    duration_days INTEGER NOT NULL,
    qty TEXT NOT NULL,
    batch_id_out INTEGER,
    unit_price DECIMAL(10,2) NOT NULL,
    subtotal DECIMAL(10,2) NOT NULL,
    created_at TEXT NOT NULL,
    deleted_at TEXT,
    CONSTRAINT FK_prescription_item_prescription_prescription_id FOREIGN KEY (prescription_id) REFERENCES prescription (id) ON DELETE CASCADE,
    CONSTRAINT FK_prescription_item_drug_master_drug_id FOREIGN KEY (drug_id) REFERENCES drug_master (id) ON DELETE RESTRICT
)
""")
print('新表创建成功')

# 复制数据
conn.execute("""
INSERT INTO prescription_item_new 
SELECT id, prescription_id, drug_id, drug_name, spec, dose, dose_unit, frequency, route, 
       duration_days, qty, batch_id_out, unit_price, subtotal, created_at, deleted_at 
FROM prescription_item
""")
count = conn.execute('SELECT COUNT(*) FROM prescription_item_new').fetchone()[0]
print(f'已复制 {count} 条数据')

# 删除旧表，重命名新表
conn.execute('DROP TABLE prescription_item')
conn.execute('ALTER TABLE prescription_item_new RENAME TO prescription_item')
print('表替换完成')

# 重建索引
conn.execute('CREATE INDEX IX_prescription_item_prescription_id ON prescription_item (prescription_id)')
conn.execute('CREATE INDEX IX_prescription_item_drug_id ON prescription_item (drug_id)')
print('索引重建完成')

# 验证
print('\n=== 验证 ===')
fks = conn.execute('PRAGMA foreign_key_list(prescription_item)').fetchall()
for fk in fks:
    print(f'  外键: {fk}')

count = conn.execute('SELECT COUNT(*) FROM prescription_item').fetchone()[0]
print(f'  总记录数: {count}')

conn.commit()
conn.close()
print('\n修复完成！')
