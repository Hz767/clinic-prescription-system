import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')

# 检查布洛芬相关药品
print('=== 布洛芬相关药品 ===')
rows = conn.execute("SELECT id, generic_name_cn, spec, unit, retail_price_ref FROM drug_master WHERE generic_name_cn LIKE '%布洛芬%'").fetchall()
for r in rows:
    print(f'  id={r[0]}, name={r[1]}, spec={r[2]!r}, unit={r[3]!r}, price={r[4]}')

# 检查处方481的明细
print('\n=== 处方481明细 ===')
rows = conn.execute('SELECT * FROM prescription_item WHERE prescription_id = 481').fetchall()
print(f'  明细数量: {len(rows)}')

# 检查drug_master中spec为空的药品
print('\n=== spec为空的药品数量 ===')
count = conn.execute("SELECT COUNT(*) FROM drug_master WHERE spec IS NULL OR spec = ''").fetchone()[0]
print(f'  {count} 种')

# 检查unit为空的药品
print('\n=== unit为空的药品数量 ===')
count = conn.execute("SELECT COUNT(*) FROM drug_master WHERE unit IS NULL OR unit = ''").fetchone()[0]
print(f'  {count} 种')

conn.close()
