import csv
import sqlite3
import base64
import hmac
import hashlib
import os
import re
from datetime import datetime, timezone
from Crypto.Cipher import AES

# 配置
DB_PATH = r'E:\个人诊所处方系统\clinic.db'
PATIENTS_CSV = r'E:\个人诊所处方系统\testdata\patients.csv'
PRESCRIPTIONS_CSV = r'E:\个人诊所处方系统\testdata\prescriptions.csv'
ITEMS_CSV = r'E:\个人诊所处方系统\testdata\prescription_items.csv'

# 加密密钥
ENCRYPTION_KEY_B64 = "JQqOAFkXsovwPnF2co89Xh3of3UXBPJ8rQxNtP3wOoo="
ENCRYPTION_KEY = base64.b64decode(ENCRYPTION_KEY_B64)

def encrypt_phone(phone):
    """AES-GCM 加密手机号"""
    if not phone:
        return ''
    nonce = os.urandom(12)
    cipher = AES.new(ENCRYPTION_KEY, AES.MODE_GCM, nonce=nonce)
    ciphertext, tag = cipher.encrypt_and_digest(phone.encode('utf-8'))
    result = nonce + tag + ciphertext
    return base64.b64encode(result).decode('ascii')

def hash_phone(phone):
    """HMAC-SHA256 哈希手机号"""
    if not phone:
        return ''
    return hmac.new(ENCRYPTION_KEY, phone.strip().encode('utf-8'), hashlib.sha256).hexdigest()

# 处方状态映射
STATUS_MAP = {'草稿': 0, '已保存': 1, '待审核': 1, '已收费': 2, '待发药': 2, '已作废': 3, '已审核': 4, '待收费': 4, '已发药': 5}
TYPE_MAP = {'普通处方': 0, '急诊处方': 1, '儿科处方': 2}

# 疾病-药品关联规则（用于一致性校验和修正）
DISEASE_DRUG_RULES = {
    '急性扁桃体炎': ['阿莫西林', '头孢', '布洛芬', '对乙酰氨基酚', '复方甘草'],
    '高脂血症': ['阿托伐他汀', '辛伐他汀', '瑞舒伐他汀', '非诺贝特'],
    '高血压': ['氨氯地平', '硝苯地平', '缬沙坦', '厄贝沙坦', '美托洛尔'],
    '糖尿病': ['二甲双胍', '格列美脲', '阿卡波糖', '胰岛素'],
    '感冒': ['复方氨酚', '板蓝根', '连花清瘟', '布洛芬'],
    '胃炎': ['奥美拉唑', '雷尼替丁', '铝碳酸镁', '多潘立酮'],
    '支气管炎': ['阿莫西林', '头孢', '氨溴索', '复方甘草'],
    '肺炎': ['头孢', '左氧氟沙星', '阿奇霉素', '氨溴索'],
    '痛风': ['秋水仙碱', '别嘌醇', '非布司他', '布洛芬'],
    '关节炎': ['布洛芬', '双氯芬酸', '塞来昔布', '氨基葡萄糖'],
    '皮炎': ['氯雷他定', '西替利嗪', '地塞米松', '炉甘石'],
    '腹泻': ['蒙脱石散', '双歧杆菌', '左氧氟沙星', '口服补液盐'],
    '头痛': ['布洛芬', '对乙酰氨基酚', '氟桂利嗪'],
    '失眠': ['艾司唑仑', '佐匹克隆', '谷维素'],
    '贫血': ['硫酸亚铁', '叶酸', '维生素B12'],
    '冠心病': ['阿司匹林', '阿托伐他汀', '美托洛尔', '硝酸甘油'],
    '咽喉炎': ['阿莫西林', '头孢', '咽炎片', '布洛芬'],
    '中耳炎': ['阿莫西林', '头孢', '氧氟沙星滴耳液'],
    '结膜炎': ['左氧氟沙星滴眼液', '氯霉素滴眼液', '红霉素眼膏'],
    '鼻炎': ['氯雷他定', '西替利嗪', '布地奈德鼻喷雾剂'],
}

def get_disease_keywords(diagnosis):
    """从诊断中提取疾病关键词"""
    keywords = []
    for key in DISEASE_DRUG_RULES:
        if key in diagnosis:
            keywords.append(key)
    return keywords

def is_drug_match_disease(drug_name, diagnosis):
    """检查药品是否与诊断匹配"""
    disease_keys = get_disease_keywords(diagnosis)
    if not disease_keys:
        return True  # 无法判断时视为匹配
    for key in disease_keys:
        for drug_pattern in DISEASE_DRUG_RULES[key]:
            if drug_pattern in drug_name:
                return True
    return False

def find_matching_drug(drug_name, diagnosis, drug_map, drug_list):
    """如果药品与诊断不匹配，查找合适的替代药品"""
    disease_keys = get_disease_keywords(diagnosis)
    if not disease_keys:
        return drug_name, None  # 无法判断时保持原样

    # 收集该疾病的所有推荐药品
    recommended = set()
    for key in disease_keys:
        recommended.update(DISEASE_DRUG_RULES[key])

    # 在药品库中查找匹配的药品
    for pattern in recommended:
        for name, drug_id in drug_list:
            if pattern in name:
                return name, drug_id

    return drug_name, None  # 找不到替代药品

def main():
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    now = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S')

    # ========== 1. 导入患者 ==========
    print('=' * 60)
    print('1. 导入患者数据')
    print('=' * 60)

    existing_phones = set()
    rows = cursor.execute('SELECT phone_hash FROM patient WHERE phone_hash IS NOT NULL').fetchall()
    for r in rows:
        existing_phones.add(r[0])

    max_patient_id = cursor.execute('SELECT MAX(id) FROM patient').fetchone()[0] or 0
    patient_id_map = {}
    imported_patients = 0

    with open(PATIENTS_CSV, 'r', encoding='utf-8-sig') as f:
        reader = csv.DictReader(f)
        for row in reader:
            csv_id = int(row['id'])
            name = row['name'].strip()
            gender = row['gender'].strip()
            dob = row['dob'].strip() or None
            phone = row['phone'].strip()
            allergies = row.get('allergies', '').strip() or None
            history = row.get('history', '').strip() or None
            chronic_tags = row.get('chronic_tags', '').strip() or None
            tags = row.get('tags', '').strip() or None
            weight = row.get('weight_kg', '').strip() or None
            temperature = row.get('temperature_c', '').strip() or None
            sbp = int(row['sbp']) if row.get('sbp') else None
            dbp = int(row['dbp']) if row.get('dbp') else None
            heart_rate = int(row['heart_rate']) if row.get('heart_rate') else None

            phone_h = hash_phone(phone) if phone else ''
            if phone and phone_h in existing_phones:
                continue

            max_patient_id += 1
            phone_enc = encrypt_phone(phone) if phone else ''

            cursor.execute('''
                INSERT INTO patient (
                    id, name, gender, dob, phone_encrypted, phone_hash,
                    allergies, history, chronic_tags, tags,
                    weight, temperature, systolic_bp, diastolic_bp, heart_rate, created_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ''', (max_patient_id, name, gender, dob, phone_enc, phone_h,
                  allergies, history, chronic_tags, tags,
                  weight, temperature, sbp, dbp, heart_rate, now))

            patient_id_map[csv_id] = max_patient_id
            existing_phones.add(phone_h)
            imported_patients += 1

    conn.commit()
    print(f'  导入患者: {imported_patients} 人')
    print(f'  患者总数: {cursor.execute("SELECT COUNT(*) FROM patient").fetchone()[0]} 人')

    # ========== 2. 构建药品映射 ==========
    print('\n' + '=' * 60)
    print('2. 构建药品映射')
    print('=' * 60)

    drug_map = {}  # name.lower() -> (name, id)
    drug_list = []  # [(name, id)]
    rows = cursor.execute('SELECT id, generic_name_cn FROM drug_master WHERE deleted_at IS NULL').fetchall()
    for r in rows:
        drug_id, name = r
        drug_map[name.lower()] = (name, drug_id)
        drug_list.append((name, drug_id))

    print(f'  药品库: {len(drug_list)} 种')

    # ========== 3. 读取处方和明细，进行一致性校验 ==========
    print('\n' + '=' * 60)
    print('3. 读取并校验处方数据一致性')
    print('=' * 60)

    # 读取处方
    prescriptions = {}
    with open(PRESCRIPTIONS_CSV, 'r', encoding='utf-8-sig') as f:
        reader = csv.DictReader(f)
        for row in reader:
            csv_rx_id = int(row['rx_id'])
            prescriptions[csv_rx_id] = row

    # 读取明细
    items_by_rx = {}
    with open(ITEMS_CSV, 'r', encoding='utf-8-sig') as f:
        reader = csv.DictReader(f)
        for row in reader:
            csv_rx_id = int(row['rx_id'])
            if csv_rx_id not in items_by_rx:
                items_by_rx[csv_rx_id] = []
            items_by_rx[csv_rx_id].append(row)

    # 一致性校验和修正
    fixed_items = 0
    fixed_amount = 0
    for csv_rx_id, rx in prescriptions.items():
        diagnosis = rx.get('diagnosis', '')
        items = items_by_rx.get(csv_rx_id, [])

        # 校验药品与诊断匹配
        for item in items:
            drug_name = item['drug_name'].strip()
            if not is_drug_match_disease(drug_name, diagnosis):
                # 尝试查找替代药品
                new_name, new_id = find_matching_drug(drug_name, diagnosis, drug_map, drug_list)
                if new_name != drug_name and new_id:
                    item['drug_name'] = new_name
                    item['_drug_id'] = new_id
                    fixed_items += 1

        # 重新计算处方金额（确保收费与明细一致）
        calculated_total = sum(float(item.get('subtotal', 0) or 0) for item in items)
        original_total = float(rx.get('total_amount', 0) or 0)
        if abs(calculated_total - original_total) > 0.01:
            rx['total_amount'] = f"{calculated_total:.2f}"
            fixed_amount += 1

    print(f'  修正药品-诊断不匹配: {fixed_items} 条')
    print(f'  修正金额不一致: {fixed_amount} 张处方')

    # ========== 4. 导入处方 ==========
    print('\n' + '=' * 60)
    print('4. 导入处方数据')
    print('=' * 60)

    max_rx_id = cursor.execute('SELECT MAX(id) FROM prescription').fetchone()[0] or 0
    prescription_id_map = {}
    imported_rx = 0

    for csv_rx_id, rx in prescriptions.items():
        patient_csv_id = int(rx['patient_id'])
        if patient_csv_id not in patient_id_map:
            continue

        patient_id = patient_id_map[patient_csv_id]
        rx_no = rx['prescription_no'].strip()
        visit_date = rx['visit_date'].strip()
        chief_complaint = rx.get('chief_complaint', '').strip() or None
        diagnosis_code = rx.get('diagnosis_code', '').strip() or None
        diagnosis = rx.get('diagnosis', '').strip() or '未诊断'
        weight = rx.get('weight', '').strip() or None
        temperature = rx.get('temperature', '').strip() or None
        sbp = int(rx['sbp']) if rx.get('sbp') else None
        dbp = int(rx['dbp']) if rx.get('dbp') else None
        heart_rate = int(rx['heart_rate']) if rx.get('heart_rate') else None
        status_str = rx.get('status', '').strip()
        total_amount = float(rx.get('total_amount', 0) or 0)
        type_str = rx.get('prescription_type', '普通处方').strip()

        status = STATUS_MAP.get(status_str, 5)
        rx_type = TYPE_MAP.get(type_str, 0)

        max_rx_id += 1
        dispensed_at = visit_date if status == 5 else None

        cursor.execute('''
            INSERT INTO prescription (
                id, no_year_seq, patient_id, doctor_id, diagnosis_code,
                chief_complaint, weight, temperature, systolic_bp, diastolic_bp,
                heart_rate, diagnosis_text, total_amount, type, status,
                is_paper_signed, created_at, dispensed_at, dispensed_by
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ''', (max_rx_id, rx_no, patient_id, 1, diagnosis_code,
              chief_complaint, weight, temperature, sbp, dbp,
              heart_rate, diagnosis, total_amount, rx_type, status,
              1, visit_date, dispensed_at, 1 if status == 5 else None))

        prescription_id_map[csv_rx_id] = max_rx_id
        imported_rx += 1

    conn.commit()
    print(f'  导入处方: {imported_rx} 张')
    print(f'  处方总数: {cursor.execute("SELECT COUNT(*) FROM prescription").fetchone()[0]} 张')

    # ========== 5. 导入处方明细 ==========
    print('\n' + '=' * 60)
    print('5. 导入处方明细')
    print('=' * 60)

    max_item_id = cursor.execute('SELECT MAX(id) FROM prescription_item').fetchone()[0] or 0
    imported_items = 0
    drug_not_found = set()

    for csv_rx_id, items in items_by_rx.items():
        if csv_rx_id not in prescription_id_map:
            continue
        rx_id = prescription_id_map[csv_rx_id]
        rx = prescriptions[csv_rx_id]
        diagnosis = rx.get('diagnosis', '')

        for item in items:
            drug_name = item['drug_name'].strip()
            spec = item.get('spec', '').strip()
            unit = item.get('unit', '').strip()
            dose = item.get('dose', '').strip()
            dose_unit = item.get('dose_unit', '').strip()
            frequency = item.get('frequency', '').strip()
            route = item.get('route', '').strip()
            duration_days = int(item['duration_days']) if item.get('duration_days') else None
            qty = item.get('qty', '').strip()
            unit_price = float(item.get('unit_price', 0) or 0)
            subtotal = float(item.get('subtotal', 0) or 0)

            # 匹配药品ID
            drug_id = item.get('_drug_id')
            if not drug_id:
                key = drug_name.lower()
                if key in drug_map:
                    drug_id = drug_map[key][1]
                else:
                    # 模糊匹配
                    for name, did in drug_list:
                        if drug_name.lower() in name.lower() or name.lower() in drug_name.lower():
                            drug_id = did
                            break

            if not drug_id:
                drug_not_found.add(drug_name)
                drug_id = 1  # 默认用阿莫西林

            max_item_id += 1
            cursor.execute('''
                INSERT INTO prescription_item (
                    id, prescription_id, drug_id, drug_name, spec,
                    dose, dose_unit, frequency, route, duration_days,
                    qty, unit_price, subtotal, created_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ''', (max_item_id, rx_id, drug_id, drug_name, spec,
                  dose, dose_unit, frequency, route, duration_days,
                  qty, unit_price, subtotal, now))
            imported_items += 1

    conn.commit()
    print(f'  导入明细: {imported_items} 条')
    print(f'  明细总数: {cursor.execute("SELECT COUNT(*) FROM prescription_item").fetchone()[0]} 条')
    if drug_not_found:
        print(f'  未匹配药品({len(drug_not_found)}种): {list(drug_not_found)[:10]}')

    # ========== 6. 生成合理的病历 ==========
    print('\n' + '=' * 60)
    print('6. 生成病历数据（含主诉-现病史-查体-诊断-计划逻辑关联）')
    print('=' * 60)

    max_mr_id = cursor.execute('SELECT MAX(id) FROM medical_record').fetchone()[0] or 0
    imported_mr = 0

    rows = cursor.execute('''
        SELECT p.id, p.patient_id, p.chief_complaint, p.diagnosis_text,
               p.created_at, p.weight, p.temperature, p.systolic_bp,
               p.diastolic_bp, p.heart_rate
        FROM prescription p
        WHERE p.chief_complaint IS NOT NULL
    ''').fetchall()

    for r in rows:
        rx_id, patient_id, complaint, diagnosis, visit_date, weight, temp, sbp, dbp, hr = r
        max_mr_id += 1

        # 生成合理的现病史（基于主诉）
        present_illness = None
        if complaint:
            # 从主诉提取症状和持续时间
            duration_match = re.search(r'(\d+)\s*(天|日|周|月|小时)', complaint)
            duration = duration_match.group(0) if duration_match else '数天'
            present_illness = f"患者{duration}前无明显诱因出现{complaint}，症状逐渐加重/无明显缓解，为求进一步诊治来我院就诊。发病以来，精神、食欲、睡眠尚可，大小便正常，体重无明显变化。"

        # 生成查体（基于生命体征）
        exam_parts = []
        if temp:
            t = float(temp) if temp else 36.5
            if t >= 37.3:
                exam_parts.append(f"T {t}℃（发热）")
            else:
                exam_parts.append(f"T {t}℃")
        if sbp and dbp:
            if sbp >= 140 or dbp >= 90:
                exam_parts.append(f"BP {sbp}/{dbp}mmHg（血压偏高）")
            else:
                exam_parts.append(f"BP {sbp}/{dbp}mmHg")
        if hr:
            if hr > 100:
                exam_parts.append(f"P {hr}次/分（心率偏快）")
            elif hr < 60:
                exam_parts.append(f"P {hr}次/分（心率偏慢）")
            else:
                exam_parts.append(f"P {hr}次/分")
        if weight:
            exam_parts.append(f"W {weight}kg")

        exam_parts.append("神志清楚，精神可，自主体位，查体合作")
        exam_parts.append("全身皮肤黏膜无黄染、皮疹及出血点")
        exam_parts.append("浅表淋巴结未触及肿大")
        exam = "；".join(exam_parts) + "。"

        # 生成处理计划（基于诊断和处方）
        plan_parts = []
        if diagnosis:
            plan_parts.append(f"诊断：{diagnosis}")
        plan_parts.append("已开具处方，嘱患者按时服药")
        plan_parts.append("注意休息，清淡饮食，多饮水")
        plan_parts.append("不适随诊，必要时复诊")

        # 根据诊断添加特定医嘱
        disease_keys = get_disease_keywords(diagnosis or '')
        if '高血压' in disease_keys:
            plan_parts.append("监测血压，低盐低脂饮食")
        if '糖尿病' in disease_keys:
            plan_parts.append("监测血糖，糖尿病饮食")
        if '高脂血症' in disease_keys:
            plan_parts.append("低脂饮食，定期复查血脂")
        if '痛风' in disease_keys:
            plan_parts.append("低嘌呤饮食，多饮水，禁酒")

        plan = "。".join(plan_parts) + "。"

        cursor.execute('''
            INSERT INTO medical_record (
                id, patient_id, doctor_id, visit_at, chief_complaint,
                present_illness, exam, exam_na, auxiliary_exam, auxiliary_na,
                diagnosis, plan, created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ''', (max_mr_id, patient_id, 1, visit_date, complaint,
              present_illness, exam, 0, None, 1, diagnosis, plan, visit_date))
        imported_mr += 1

    conn.commit()
    print(f'  生成病历: {imported_mr} 份')
    print(f'  病历总数: {cursor.execute("SELECT COUNT(*) FROM medical_record").fetchone()[0]} 份')

    # ========== 7. 生成收费记录（与处方状态一致） ==========
    print('\n' + '=' * 60)
    print('7. 生成收费记录（与处方状态、金额一致）')
    print('=' * 60)

    try:
        max_pay_id = cursor.execute('SELECT MAX(id) FROM payment_log').fetchone()[0] or 0
        imported_pay = 0

        rows = cursor.execute('''
            SELECT id, patient_id, total_amount, created_at, status
            FROM prescription
            WHERE status IN (2, 5) AND total_amount > 0
        ''').fetchall()

        for r in rows:
            rx_id, patient_id, amount, pay_date, status = r
            max_pay_id += 1
            # 已收费=现金，已发药=已收费
            method = 1  # 现金
            cursor.execute('''
                INSERT INTO payment_log (
                    id, prescription_id, patient_id, amount, method,
                    operator_id, created_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?)
            ''', (max_pay_id, rx_id, patient_id, amount, method, 1, pay_date))
            imported_pay += 1

        conn.commit()
        print(f'  生成收费记录: {imported_pay} 条')
    except Exception as e:
        print(f'  跳过收费记录: {e}')

    # ========== 总结 ==========
    print('\n' + '=' * 60)
    print('导入完成！系统数据统计')
    print('=' * 60)
    print(f'  患者: {cursor.execute("SELECT COUNT(*) FROM patient").fetchone()[0]} 人')
    print(f'  处方: {cursor.execute("SELECT COUNT(*) FROM prescription").fetchone()[0]} 张')
    print(f'  处方明细: {cursor.execute("SELECT COUNT(*) FROM prescription_item").fetchone()[0]} 条')
    print(f'  病历: {cursor.execute("SELECT COUNT(*) FROM medical_record").fetchone()[0]} 份')
    print(f'  药品: {cursor.execute("SELECT COUNT(*) FROM drug_master WHERE deleted_at IS NULL").fetchone()[0]} 种')
    print(f'  库存: {cursor.execute("SELECT COUNT(*) FROM drug_stock").fetchone()[0]} 条')

    # 数据一致性验证
    print('\n' + '=' * 60)
    print('数据一致性验证')
    print('=' * 60)

    # 验证处方金额与明细一致
    inconsistent = cursor.execute('''
        SELECT p.id, p.total_amount, COALESCE(SUM(pi.subtotal), 0) as calc_total
        FROM prescription p
        LEFT JOIN prescription_item pi ON p.id = pi.prescription_id
        GROUP BY p.id
        HAVING ABS(p.total_amount - calc_total) > 0.01
    ''').fetchall()
    print(f'  金额不一致处方: {len(inconsistent)} 张')

    # 验证处方都有患者
    no_patient = cursor.execute('''
        SELECT COUNT(*) FROM prescription p
        LEFT JOIN patient pa ON p.patient_id = pa.id
        WHERE pa.id IS NULL
    ''').fetchone()[0]
    print(f'  无患者处方: {no_patient} 张')

    # 验证明细都有处方
    no_rx = cursor.execute('''
        SELECT COUNT(*) FROM prescription_item pi
        LEFT JOIN prescription p ON pi.prescription_id = p.id
        WHERE p.id IS NULL
    ''').fetchone()[0]
    print(f'  无处方明细: {no_rx} 条')

    # 验证病历都有患者
    no_patient_mr = cursor.execute('''
        SELECT COUNT(*) FROM medical_record mr
        LEFT JOIN patient pa ON mr.patient_id = pa.id
        WHERE pa.id IS NULL
    ''').fetchone()[0]
    print(f'  无患者病历: {no_patient_mr} 份')

    conn.close()

if __name__ == '__main__':
    main()
