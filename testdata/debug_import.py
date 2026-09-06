import csv
import sqlite3

db_path = r'E:\个人诊所处方系统\clinic.db'
csv_path = r'E:\个人诊所处方系统\testdata\drugs.csv'

conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# 读取现有药品
existing = {}
rows = cursor.execute('SELECT id, generic_name_cn, spec FROM drug_master WHERE deleted_at IS NULL').fetchall()
print('现有药品:')
for r in rows:
    key = f"{r[1].strip()}||{r[2].strip() if r[2] else ''}"
    existing[key.lower()] = r[0]
    print(f'  [{key.lower()}]')

print(f'\n现有字典大小: {len(existing)}')

# 读取 CSV 前10条，检查 key
print('\nCSV 前10条 key:')
with open(csv_path, 'r', encoding='utf-8') as f:
    reader = csv.DictReader(f)
    for i, row in enumerate(reader):
        if i >= 10:
            break
        name = (row.get('generic_name_cn') or '').strip()
        spec = (row.get('spec') or '').strip()
        key = f"{name}||{spec}".lower()
        exists = key in existing
        print(f'  [{key}] 存在={exists}')

conn.close()
