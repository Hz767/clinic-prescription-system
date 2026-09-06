import sqlite3

db_path = r'E:\个人诊所处方系统\clinic.db'
conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# 检查是否有唯一索引
print('=== 索引 ===')
indexes = cursor.execute("SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name='drug_master'").fetchall()
for idx in indexes:
    print(f'  {idx[0]}: {idx[1]}')

# SQLite 不支持直接 ALTER COLUMN，需要重建表
# 1. 重命名旧表
cursor.execute("ALTER TABLE drug_master RENAME TO drug_master_old")
print('\n已重命名旧表为 drug_master_old')

# 2. 创建新表（generic_name_en 可空）
cursor.execute("""
CREATE TABLE drug_master (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    generic_name_cn TEXT NOT NULL,
    generic_name_en TEXT,
    spec TEXT NOT NULL,
    unit TEXT NOT NULL,
    default_usage TEXT,
    is_antibiotic INTEGER NOT NULL,
    antibiotic_level INTEGER NOT NULL,
    is_toxic_drug INTEGER NOT NULL,
    contraindication_tags TEXT,
    cost_price_ref DECIMAL(10,2),
    retail_price_ref DECIMAL(10,2),
    created_at TEXT NOT NULL,
    deleted_at TEXT,
    reorder_level REAL
)
""")
print('已创建新表（generic_name_en 可空）')

# 3. 复制数据
cursor.execute("""
INSERT INTO drug_master (id, generic_name_cn, generic_name_en, spec, unit, default_usage,
    is_antibiotic, antibiotic_level, is_toxic_drug, contraindication_tags,
    cost_price_ref, retail_price_ref, created_at, deleted_at, reorder_level)
SELECT id, generic_name_cn, generic_name_en, spec, unit, default_usage,
    is_antibiotic, antibiotic_level, is_toxic_drug, contraindication_tags,
    cost_price_ref, retail_price_ref, created_at, deleted_at, reorder_level
FROM drug_master_old
""")
print(f'已复制 {cursor.rowcount} 条数据')

# 4. 删除旧表
cursor.execute("DROP TABLE drug_master_old")
print('已删除旧表')

# 5. 验证
cols = cursor.execute('PRAGMA table_info(drug_master)').fetchall()
print('\n=== 新表结构 ===')
for c in cols:
    print(f'  {c[1]}: {c[2]} nullable={c[3]==0}')

count = cursor.execute('SELECT COUNT(*) FROM drug_master').fetchone()[0]
print(f'\n数据条数: {count}')

conn.commit()
conn.close()
print('\n修复完成！')
