import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')

print('=== medical_record 表结构 ===')
cols = conn.execute('PRAGMA table_info(medical_record)').fetchall()
for c in cols:
    print(f'  {c[1]}: {c[2]}, NOT NULL={c[3]}')

print('\n=== 病历数据样本 ===')
rows = conn.execute('SELECT * FROM medical_record LIMIT 2').fetchall()
for r in rows:
    print(f'\n--- 病历 {r[0]} ---')
    cols = [c[1] for c in conn.execute('PRAGMA table_info(medical_record)').fetchall()]
    for i, col in enumerate(cols):
        val = r[i]
        if val and len(str(val)) > 100:
            val = str(val)[:100] + '...'
        print(f'  {col}: {val}')

print(f'\n总病历数: {conn.execute("SELECT COUNT(*) FROM medical_record").fetchone()[0]}')
conn.close()
