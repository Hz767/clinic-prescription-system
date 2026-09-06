import csv
import sqlite3
from datetime import datetime, timezone

db_path = r'E:\个人诊所处方系统\clinic.db'
csv_path = r'E:\个人诊所处方系统\testdata\drugs.csv'

conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# 读取现有药品，用于去重
existing = {}
rows = cursor.execute('SELECT id, generic_name_cn, spec FROM drug_master WHERE deleted_at IS NULL').fetchall()
for r in rows:
    key = f"{r[1].strip()}||{r[2].strip() if r[2] else ''}"
    existing[key.lower()] = r[0]
print(f'现有药品: {len(existing)} 种')

# 读取 CSV
imported = 0
skipped = 0
now = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S')

with open(csv_path, 'r', encoding='utf-8-sig') as f:
    reader = csv.DictReader(f)
    for row in reader:
        name = (row.get('generic_name_cn') or '').strip()
        spec = (row.get('spec') or '').strip()
        if not name:
            skipped += 1
            continue
        
        key = f"{name}||{spec}".lower()
        if key in existing:
            skipped += 1
            continue
        
        # 解析字段
        generic_name_en = row.get('generic_name_en') or None
        unit = row.get('unit') or ''
        default_usage = row.get('default_usage') or None
        is_antibiotic = 1 if (row.get('is_antibiotic') or '0') == '1' else 0
        
        level_str = (row.get('antibiotic_level') or 'NonRestricted').strip()
        level_map = {'NonRestricted': 1, 'Restricted': 2, 'Special': 3}
        antibiotic_level = level_map.get(level_str, 1)
        
        is_toxic = 1 if (row.get('is_toxic_drug') or '0') == '1' else 0
        contraindication = row.get('contraindication_tags') or None
        cost_price = float(row['cost_price_ref']) if row.get('cost_price_ref') else None
        retail_price = float(row['retail_price_ref']) if row.get('retail_price_ref') else None
        reorder = float(row['reorder_level']) if row.get('reorder_level') else None
        
        cursor.execute('''
            INSERT INTO drug_master (
                generic_name_cn, generic_name_en, spec, unit, default_usage,
                is_antibiotic, antibiotic_level, is_toxic_drug, contraindication_tags,
                cost_price_ref, retail_price_ref, reorder_level, created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ''', (
            name, generic_name_en, spec, unit, default_usage,
            is_antibiotic, antibiotic_level, is_toxic, contraindication,
            cost_price, retail_price, reorder, now
        ))
        imported += 1
        existing[key] = cursor.lastrowid

conn.commit()

# 验证
total = cursor.execute('SELECT COUNT(*) FROM drug_master WHERE deleted_at IS NULL').fetchone()[0]
print(f'\n导入完成:')
print(f'  新增: {imported} 种')
print(f'  跳过(已存在): {skipped} 种')
print(f'  总计: {total} 种')

# 统计
antibiotic = cursor.execute('SELECT COUNT(*) FROM drug_master WHERE is_antibiotic=1 AND deleted_at IS NULL').fetchone()[0]
print(f'  其中抗生素: {antibiotic} 种')

conn.close()
