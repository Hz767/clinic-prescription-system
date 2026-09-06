import sqlite3
conn = sqlite3.connect(r'E:\个人诊所处方系统\clinic.db')

print('=== sys_user 表结构 ===')
cols = conn.execute('PRAGMA table_info(sys_user)').fetchall()
for c in cols:
    print(f'  {c[1]}: {c[2]}, NOT NULL={c[3]}')

print('\n=== sys_user 数据 ===')
rows = conn.execute('SELECT id, username, real_name, role, is_active FROM sys_user').fetchall()
for r in rows:
    print(f'  id={r[0]}, username={r[1]}, real_name={r[2]}, role={r[3]}, active={r[4]}')

conn.close()
