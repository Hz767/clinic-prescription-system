import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cur = conn.cursor()
# 分别检查每个字段的空值/空字符串数量
fields = ['generic_name_cn', 'generic_name_en', 'spec', 'unit', 'default_usage', 
          'is_antibiotic', 'antibiotic_level', 'is_toxic_drug', 'contraindication_tags',
          'cost_price_ref', 'retail_price_ref', 'reorder_level']
for f in fields:
    cur.execute(f'SELECT COUNT(*) FROM drug_master WHERE [{f}] IS NULL OR [{f}] = ""')
    n = cur.fetchone()[0]
    if n > 0:
        print(f'{f}: {n} 条空/NULL')
# 查看 generic_name_cn 为空的记录
cur.execute("SELECT id, generic_name_cn, spec, unit, generic_name_en FROM drug_master WHERE generic_name_cn IS NULL OR generic_name_cn = '' LIMIT 10")
print('\ngeneric_name_cn 为空的记录:')
for row in cur.fetchall():
    print(row)
# 查看 spec 为空的记录
cur.execute("SELECT id, generic_name_cn, spec, unit FROM drug_master WHERE spec IS NULL OR spec = '' LIMIT 10")
print('\nspec 为空的记录:')
for row in cur.fetchall():
    print(row)
conn.close()
