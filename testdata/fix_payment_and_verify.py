import sqlite3
from datetime import datetime, timezone

conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')
cursor = conn.cursor()
now = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S')

# 1. 修复原始处方金额不一致（用明细合计更新）
print('1. 修复金额不一致处方...')
rows = cursor.execute('''
    SELECT p.id, COALESCE(SUM(pi.subtotal), 0) as calc_total
    FROM prescription p
    LEFT JOIN prescription_item pi ON p.id = pi.prescription_id
    GROUP BY p.id
    HAVING ABS(p.total_amount - calc_total) > 0.01
''').fetchall()
for r in rows:
    rx_id, calc_total = r
    cursor.execute('UPDATE prescription SET total_amount = ? WHERE id = ?', (calc_total, rx_id))
    print(f'  处方{rx_id}: 金额更新为 {calc_total}')

conn.commit()

# 2. 生成收费记录
print('\n2. 生成收费记录...')
max_pay_id = cursor.execute('SELECT MAX(id) FROM payment_log').fetchone()[0] or 0
imported_pay = 0

rows = cursor.execute('''
    SELECT id, total_amount, created_at, status
    FROM prescription
    WHERE status IN (2, 5) AND total_amount > 0
''').fetchall()

for r in rows:
    rx_id, amount, pay_date, status = r
    max_pay_id += 1
    cursor.execute('''
        INSERT INTO payment_log (
            id, prescription_id, method, amount, occurred_at,
            operator_id, is_reversal, created_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
    ''', (max_pay_id, rx_id, 1, amount, pay_date, 1, 0, pay_date))
    imported_pay += 1

conn.commit()
print(f'  生成收费记录: {imported_pay} 条')
print(f'  收费记录总数: {cursor.execute("SELECT COUNT(*) FROM payment_log").fetchone()[0]} 条')

# 3. 最终数据一致性验证
print('\n3. 最终数据一致性验证')
inconsistent = cursor.execute('''
    SELECT COUNT(*) FROM (
        SELECT p.id
        FROM prescription p
        LEFT JOIN prescription_item pi ON p.id = pi.prescription_id
        GROUP BY p.id
        HAVING ABS(p.total_amount - COALESCE(SUM(pi.subtotal), 0)) > 0.01
    )
''').fetchone()[0]
print(f'  金额不一致处方: {inconsistent} 张')

# 验证收费与处方匹配
paid_rx = cursor.execute('SELECT COUNT(DISTINCT prescription_id) FROM payment_log').fetchone()[0]
should_pay = cursor.execute('SELECT COUNT(*) FROM prescription WHERE status IN (2,5) AND total_amount > 0').fetchone()[0]
print(f'  有收费记录的处方: {paid_rx} 张')
print(f'  应有收费的处方: {should_pay} 张')

# 数据统计
print('\n4. 系统数据总览')
print(f'  患者: {cursor.execute("SELECT COUNT(*) FROM patient").fetchone()[0]} 人')
print(f'  处方: {cursor.execute("SELECT COUNT(*) FROM prescription").fetchone()[0]} 张')
print(f'  处方明细: {cursor.execute("SELECT COUNT(*) FROM prescription_item").fetchone()[0]} 条')
print(f'  病历: {cursor.execute("SELECT COUNT(*) FROM medical_record").fetchone()[0]} 份')
print(f'  收费记录: {cursor.execute("SELECT COUNT(*) FROM payment_log").fetchone()[0]} 条')
print(f'  药品: {cursor.execute("SELECT COUNT(*) FROM drug_master WHERE deleted_at IS NULL").fetchone()[0]} 种')
print(f'  库存: {cursor.execute("SELECT COUNT(*) FROM drug_stock").fetchone()[0]} 条')

# 处方状态分布
print('\n5. 处方状态分布')
rows = cursor.execute('SELECT status, COUNT(*) FROM prescription GROUP BY status').fetchall()
status_names = {0: '草稿', 1: '待审核', 2: '待发药', 3: '已作废', 4: '待收费', 5: '已发药'}
for r in rows:
    print(f'  {status_names.get(r[0], r[0])}: {r[1]} 张')

conn.close()
print('\n完成！')
