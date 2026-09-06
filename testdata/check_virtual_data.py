import csv
import sqlite3

# 统计 CSV 数据量
files = {
    'patients': r'E:\个人诊所处方系统\testdata\patients.csv',
    'prescriptions': r'E:\个人诊所处方系统\testdata\prescriptions.csv',
    'prescription_items': r'E:\个人诊所处方系统\testdata\prescription_items.csv',
}

for name, path in files.items():
    with open(path, 'r', encoding='utf-8-sig') as f:
        reader = csv.reader(f)
        header = next(reader)
        count = sum(1 for _ in reader)
        print(f'{name}: {count} 条')
        print(f'  字段: {header}')

# 检查数据库表结构
print('\n=== 数据库表结构 ===')
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
for table in ['patient', 'prescription', 'prescription_item', 'medical_record']:
    try:
        cols = conn.execute(f'PRAGMA table_info({table})').fetchall()
        count = conn.execute(f'SELECT COUNT(*) FROM {table}').fetchone()[0]
        print(f'\n{table} ({count} 条):')
        for c in cols:
            print(f'  {c[1]}: {c[2]} nullable={c[3]==0}')
    except Exception as e:
        print(f'\n{table}: 错误 - {e}')

conn.close()
