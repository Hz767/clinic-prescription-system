import csv
import sqlite3
import re
from datetime import datetime, timezone

db_path = r'E:\个人诊所处方系统\clinic.db'
csv_path = r'E:\个人诊所处方系统\testdata\药品清单\药品清单_完整.csv'

conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# 读取现有药品，用于去重
existing = {}
rows = cursor.execute('SELECT id, generic_name_cn, spec FROM drug_master WHERE deleted_at IS NULL').fetchall()
for r in rows:
    key = f"{(r[1] or '').strip()}||{(r[2] or '').strip()}"
    existing[key.lower()] = r[0]
print(f'现有药品: {len(existing)} 种')

# 抗生素关键词
antibiotic_keywords = [
    '西林', '头孢', '霉素', '沙星', '硝唑', '康唑', '康唑', '培南',
    '磺胺', '四环素', '米诺环素', '多西环素', '阿奇霉素', '克拉霉素',
    '罗红霉素', '红霉素', '林可霉素', '克林霉素', '万古霉素', '去甲万古霉素',
    '替考拉宁', '利奈唑胺', '夫西地酸', '莫匹罗星', '氨曲南', '拉氧头孢'
]

restricted_keywords = [
    '头孢克洛', '头孢呋辛', '头孢克肟', '头孢地尼', '头孢泊肟', '头孢他美',
    '阿奇霉素', '克拉霉素', '左氧氟沙星', '莫西沙星', '加替沙星'
]

def is_antibiotic(name):
    return any(k in name for k in antibiotic_keywords)

def get_antibiotic_level(name):
    if any(k in name for k in restricted_keywords):
        return 2  # Restricted
    return 1  # NonRestricted

def parse_dosage_form(med_kind):
    """从 Med_Kind 中提取剂型，如 [口服常释剂型] -> 口服常释剂型"""
    if not med_kind:
        return ''
    # 去掉方括号
    form = med_kind.strip('[]')
    return form.strip()

def clean_drug_name(name):
    """清理药品名称，去掉 [指:xxx] 等标注"""
    # 去掉 [指:xxx] 格式
    name = re.sub(r'\[指:[^\]]*\]', '', name)
    # 去掉多余的引号
    name = name.strip('"').strip()
    return name

# 读取 CSV
imported = 0
skipped_existing = 0
skipped_invalid = 0
antibiotic_count = 0
batch_seen = set()
now = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S')

with open(csv_path, 'r', encoding='utf-8-sig') as f:
    reader = csv.reader(f)
    for row in reader:
        if len(row) < 1:
            continue
        
        med_name = row[0].strip() if len(row) > 0 else ''
        med_kind = row[1].strip() if len(row) > 1 else ''
        med_plc = row[2].strip() if len(row) > 2 else ''
        
        # 跳过表头
        if med_name == 'Med_Name' or not med_name:
            continue
        
        # 清理药品名称
        name = clean_drug_name(med_name)
        if not name or len(name) > 100:
            skipped_invalid += 1
            continue
        
        # 提取剂型
        dosage_form = parse_dosage_form(med_kind)
        
        # 去重键：通用名+剂型
        dedup_key = f"{name}||{dosage_form}".lower()
        
        # 批内去重
        if dedup_key in batch_seen:
            skipped_existing += 1
            continue
        batch_seen.add(dedup_key)
        
        # 库内去重
        if dedup_key in existing:
            skipped_existing += 1
            continue
        
        # 判断抗生素
        is_abx = is_antibiotic(name)
        level = get_antibiotic_level(name) if is_abx else 1
        if is_abx:
            antibiotic_count += 1
        
        # 插入药品
        cursor.execute('''
            INSERT INTO drug_master (
                generic_name_cn, generic_name_en, spec, unit, default_usage,
                is_antibiotic, antibiotic_level, is_toxic_drug, contraindication_tags,
                cost_price_ref, retail_price_ref, reorder_level, created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ''', (
            name,
            None,  # 医保目录无英文名
            dosage_form if dosage_form else '常规剂型',  # 规格用剂型
            '',  # 单位
            None,  # 默认用法
            1 if is_abx else 0,
            level,
            0,  # 非毒性药品
            None,  # 禁忌标签
            None,  # 成本价
            None,  # 零售价
            None,  # 补货阈值
            now
        ))
        
        drug_id = cursor.lastrowid
        existing[dedup_key] = drug_id
        
        # 创建初始库存 999
        cursor.execute('''
            INSERT INTO drug_stock (drug_id, batch_no, expiry_date, qty_remaining, cost_price, received_at, created_at)
            VALUES (?, ?, ?, ?, ?, ?, ?)
        ''', (drug_id, 'INIT-999', '2027-12-31', 999, 0, now, now))
        
        imported += 1

conn.commit()

# 验证
total_drugs = cursor.execute('SELECT COUNT(*) FROM drug_master WHERE deleted_at IS NULL').fetchone()[0]
total_stock = cursor.execute('SELECT COUNT(*) FROM drug_stock').fetchone()[0]
total_qty = cursor.execute('SELECT SUM(qty_remaining) FROM drug_stock').fetchone()[0]
abx_total = cursor.execute('SELECT COUNT(*) FROM drug_master WHERE is_antibiotic=1 AND deleted_at IS NULL').fetchone()[0]

print(f'\n{"="*50}')
print(f'导入完成！')
print(f'{"="*50}')
print(f'  CSV 总行数: 13543')
print(f'  新增药品: {imported} 种')
print(f'  跳过(已存在/重复): {skipped_existing} 种')
print(f'  跳过(无效): {skipped_invalid} 种')
print(f'  其中抗生素: {antibiotic_count} 种')
print(f'  系统药品总数: {total_drugs} 种')
print(f'  库存记录总数: {total_stock} 条')
print(f'  库存总量: {total_qty}')
print(f'  抗生素总数: {abx_total} 种')

conn.close()
