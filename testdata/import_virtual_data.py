import csv
import sqlite3
import base64
import hmac
import hashlib
import os
from datetime import datetime, timezone
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

# 配置
DB_PATH = r'E:\个人诊所处方系统\clinic.db'
PATIENTS_CSV = r'E:\个人诊所处方系统\testdata\patients.csv'
PRESCRIPTIONS_CSV = r'E:\个人诊所处方系统\testdata\prescriptions.csv'
ITEMS_CSV = r'E:\个人诊所处方系统\testdata\prescription_items.csv'

# 加密密钥（从 appsettings.json 读取）
ENCRYPTION_KEY_B64 = "JQqOAFkXsovwPnF2co89Xh3of3UXBPJ8rQxNtP3wOoo="
ENCRYPTION_KEY = base64.b64decode(ENCRYPTION_KEY_B64)

aesgcm = AESGCM(ENCRYPTION_KEY)

def encrypt_phone(phone):
    """AES-GCM 加密手机号，格式：Base64(nonce[12] + ciphertext + tag[16])"""
    if not phone:
        return ''
    nonce = os.urandom(12)
    plaintext = phone.encode('utf-8')
    ciphertext = aesgcm.encrypt(nonce, plaintext, None)
    # AESGCM.encrypt 返回 ciphertext + tag
    result = nonce + ciphertext
    return base64.b64encode(result).decode('ascii')

def hash_phone(phone):
    """HMAC-SHA256 哈希手机号"""
    if not phone:
        return ''
    return hmac.new(ENCRYPTION_KEY, phone.strip().encode('utf-8'), hashlib.sha256).hexdigest()

# 处方状态映射
STATUS_MAP = {
    '草稿': 0,
    '已保存': 1,
    '待审核': 1,
    '已收费': 2,
    '待发药': 2,
    '已作废': 3,
    '已审核': 4,
    '待收费': 4,
    '已发药': 5,
}

# 处方类型映射
TYPE_MAP = {
    '普通处方': 0,
    '急诊处方': 1,
    '儿科处方': 2,
}

def main():
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    now = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S')

    # ========== 1. 导入患者 ==========
    print('=' * 60)
    print('1. 导入患者数据')
    print('=' * 60)

    # 获取现有患者的电话哈希，用于去重
    existing_phones = set()
    rows = cursor.execute('SELECT phone_hash FROM patient WHERE phone_hash IS NOT NULL').fetchall()
    for r in rows:
        existing_phones.add(r[0])

    # 获取现有最大ID
    max_patient_id = cursor.execute('SELECT MAX(id) FROM patient').fetchone()[0] or 0

    patient_id_map = {}  # CSV id -> 数据库 id
    imported_patients = 0
    skipped_patients = 0

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

            # 电话去重
            phone_h = hash_phone(phone) if phone else ''
            if phone and phone_h in existing_phones:
                skipped_patients += 1
                continue

            max_patient_id += 1
            phone_enc = encrypt_phone(phone) if phone else ''

            cursor.execute('''
                INSERT INTO patient (
                    id, name, gender, dob, phone_encrypted, phone_hash,
                    allergies, history, chronic_tags, tags,
                    weight, temperature, systolic_bp, diastolic_bp, heart_rate, created_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ''', (
                max_patient_id, name, gender, dob, phone_enc, phone_h,
                allergies, history, chronic_tags, tags,
                weight, temperature, sbp, dbp, heart_rate, now
            ))

            patient_id_map[csv_id] = max_patient_id
            existing_phones.add(phone_h)
            imported_patients += 1

    conn.commit()
    print(f'  导入患者: {imported_patients} 人')
    print(f'  跳过(重复): {skipped_patients} 人')
    print(f'  患者总数: {cursor.execute("SELECT COUNT(*) FROM patient").fetchone()[0]} 人')

    # ========== 2. 构建药品名称映射 ==========
    print('\n' + '=' * 60)
    print('2. 构建药品映射')
    print('=' * 60)

    # 从 drug_master 构建药品名称->id 映射
    drug_map = {}
    rows = cursor.execute('SELECT id, generic_name_cn, spec FROM drug_master WHERE deleted_at IS NULL').fetchall()
    for r in rows:
        drug_id, name, spec = r
        # 按名称+规格匹配
        key1 = f"{name}||{spec}".lower()
        # 按名称匹配（宽松）
        key2 = name.lower()
        if key1 not in drug_map:
            drug_map[key1] = drug_id
        if key2 not in drug_map:
            drug_map[key2] = drug_id

    print(f'  药品映射条目: {len(drug_map)}')

    # ========== 3. 导入处方 ==========
    print('\n' + '=' * 60)
    print('3. 导入处方数据')
    print('=' * 60)

    max_rx_id = cursor.execute('SELECT MAX(id) FROM prescription').fetchone()[0] or 0
    prescription_id_map = {}  # CSV rx_id -> 数据库 id

    imported_rx = 0
    skipped_rx = 0
    status_counts = {}

    with open(PRESCRIPTIONS_CSV, 'r', encoding='utf-8-sig') as f:
        reader = csv.DictReader(f)
        for row in reader:
            csv_rx_id = int(row['rx_id'])
            patient_csv_id = int(row['patient_id'])

            # 患者必须存在
            if patient_csv_id not in patient_id_map:
                skipped_rx += 1
                continue

            patient_id = patient_id_map[patient_csv_id]
            rx_no = row['prescription_no'].strip()
            visit_date = row['visit_date'].strip()
            chief_complaint = row.get('chief_complaint', '').strip() or None
            diagnosis_code = row.get('diagnosis_code', '').strip() or None
            diagnosis = row.get('diagnosis', '').strip() or '未诊断'
            weight = row.get('weight', '').strip() or None
            temperature = row.get('temperature', '').strip() or None
            sbp = int(row['sbp']) if row.get('sbp') else None
            dbp = int(row['dbp']) if row.get('dbp') else None
            heart_rate = int(row['heart_rate']) if row.get('heart_rate') else None
            status_str = row.get('status', '').strip()
            total_amount = float(row.get('total_amount', 0) or 0)
            type_str = row.get('prescription_type', '普通处方').strip()
            advice = row.get('advice', '').strip() or None

            status = STATUS_MAP.get(status_str, 5)  # 默认已发药
            rx_type = TYPE_MAP.get(type_str, 0)

            status_counts[status_str] = status_counts.get(status_str, 0) + 1

            max_rx_id += 1
            dispensed_at = visit_date if status == 5 else None

            cursor.execute('''
                INSERT INTO prescription (
                    id, no_year_seq, patient_id, doctor_id, diagnosis_code,
                    chief_complaint, weight, temperature, systolic_bp, diastolic_bp,
                    heart_rate, diagnosis_text, total_amount, type, status,
                    created_at, dispensed_at, dispensed_by
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ''', (
                max_rx_id, rx_no, patient_id, 1, diagnosis_code,
                chief_complaint, weight, temperature, sbp, dbp,
                heart_rate, diagnosis, total_amount, rx_type, status,
                visit_date, dispensed_at, 1 if status == 5 else None
            ))

            prescription_id_map[csv_rx_id] = max_rx_id
            imported_rx += 1

    conn.commit()
    print(f'  导入处方: {imported_rx} 张')
    print(f'  跳过(患者不存在): {skipped_rx} 张')
    print(f'  处方总数: {cursor.execute("SELECT COUNT(*) FROM prescription").fetchone()[0]} 张')
    print(f'  状态分布: {status_counts}')

    # ========== 4. 导入处方明细 ==========
    print('\n' + '=' * 60)
    print('4. 导入处方明细')
    print('=' * 60)

    max_item_id = cursor.execute('SELECT MAX(id) FROM prescription_item').fetchone()[0] or 0
    imported_items = 0
    skipped_items = 0
    drug_not_found = set()

    with open(ITEMS_CSV, 'r', encoding='utf-8-sig') as f:
        reader = csv.DictReader(f)
        for row in reader:
            csv_rx_id = int(row['rx_id'])
            if csv_rx_id not in prescription_id_map:
                skipped_items += 1
                continue

            rx_id = prescription_id_map[csv_rx_id]
            drug_name = row['drug_name'].strip()
            spec = row.get('spec', '').strip()
            unit = row.get('unit', '').strip()
            dose = row.get('dose', '').strip()
            dose_unit = row.get('dose_unit', '').strip()
            frequency = row.get('frequency', '').strip()
            route = row.get('route', '').strip()
            duration_days = int(row['duration_days']) if row.get('duration_days') else None
            qty = row.get('qty', '').strip()
            unit_price = float(row.get('unit_price', 0) or 0)
            subtotal = float(row.get('subtotal', 0) or 0)

            # 匹配药品ID
            drug_id = None
            key1 = f"{drug_name}||{spec}".lower()
            key2 = drug_name.lower()
            if key1 in drug_map:
                drug_id = drug_map[key1]
            elif key2 in drug_map:
                drug_id = drug_map[key2]
            else:
                # 模糊匹配：包含关系
                for k, v in drug_map.items():
                    if drug_name.lower() in k or k in drug_name.lower():
                        drug_id = v
                        break

            if drug_id is None:
                drug_not_found.add(drug_name)
                # 用默认药品ID（阿莫西林）
                drug_id = 1

            max_item_id += 1
            cursor.execute('''
                INSERT INTO prescription_item (
                    id, prescription_id, drug_id, drug_name, spec,
                    dose, dose_unit, frequency, route, duration_days,
                    qty, unit_price, subtotal, created_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ''', (
                max_item_id, rx_id, drug_id, drug_name, spec,
                dose, dose_unit, frequency, route, duration_days,
                qty, unit_price, subtotal, now
            ))
            imported_items += 1

    conn.commit()
    print(f'  导入明细: {imported_items} 条')
    print(f'  跳过(处方不存在): {skipped_items} 条')
    print(f'  明细总数: {cursor.execute("SELECT COUNT(*) FROM prescription_item").fetchone()[0]} 条')
    if drug_not_found:
        print(f'  未匹配药品({len(drug_not_found)}种): {list(drug_not_found)[:10]}...')

    # ========== 5. 生成病历 ==========
    print('\n' + '=' * 60)
    print('5. 生成病历数据')
    print('=' * 60)

    max_mr_id = cursor.execute('SELECT MAX(id) FROM medical_record').fetchone()[0] or 0
    imported_mr = 0

    # 从处方生成病历
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

        # 构建现病史
        present_illness = f"患者因{complaint}就诊。" if complaint else None

        # 构建查体
        exam_parts = []
        if temp:
            exam_parts.append(f"T {temp}℃")
        if sbp and dbp:
            exam_parts.append(f"BP {sbp}/{dbp}mmHg")
        if hr:
            exam_parts.append(f"P {hr}次/分")
        exam = "，".join(exam_parts) if exam_parts else None

        # 构建处理计划
        plan = f"诊断：{diagnosis}。已开具处方。" if diagnosis else None

        cursor.execute('''
            INSERT INTO medical_record (
                id, patient_id, doctor_id, visit_at, chief_complaint,
                present_illness, exam, diagnosis, plan, created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ''', (
            max_mr_id, patient_id, 1, visit_date, complaint,
            present_illness, exam, diagnosis, plan, visit_date
        ))
        imported_mr += 1

    conn.commit()
    print(f'  生成病历: {imported_mr} 份')
    print(f'  病历总数: {cursor.execute("SELECT COUNT(*) FROM medical_record").fetchone()[0]} 份')

    # ========== 6. 生成收费记录 ==========
    print('\n' + '=' * 60)
    print('6. 生成收费记录')
    print('=' * 60)

    # 检查 payment_log 表结构
    try:
        cols = cursor.execute('PRAGMA table_info(payment_log)').fetchall()
        col_names = [c[1] for c in cols]
        print(f'  payment_log 字段: {col_names}')

        max_pay_id = cursor.execute('SELECT MAX(id) FROM payment_log').fetchone()[0] or 0
        imported_pay = 0

        # 为已收费和已发药的处方生成收费记录
        rows = cursor.execute('''
            SELECT id, patient_id, total_amount, created_at
            FROM prescription
            WHERE status IN (2, 5) AND total_amount > 0
        ''').fetchall()

        for r in rows:
            rx_id, patient_id, amount, pay_date = r
            max_pay_id += 1
            cursor.execute('''
                INSERT INTO payment_log (
                    id, prescription_id, patient_id, amount, method,
                    operator_id, created_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?)
            ''', (
                max_pay_id, rx_id, patient_id, amount, 1, 1, pay_date
            ))
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

    conn.close()

if __name__ == '__main__':
    main()
