import sqlite3
import os

db_path = r'E:\个人诊所处方系统\clinic.db'
conn = sqlite3.connect(db_path)

# 创建医生档案表
conn.execute("""
CREATE TABLE IF NOT EXISTS doctor_profile (
    id INTEGER NOT NULL CONSTRAINT PK_doctor_profile PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    full_name TEXT NOT NULL,
    gender TEXT NOT NULL DEFAULT '男',
    birth_date TEXT,
    id_card TEXT,
    phone TEXT,
    email TEXT,
    address TEXT,
    medical_license_no TEXT,
    medical_license_issue_date TEXT,
    medical_license_expiry_date TEXT,
    practice_license_no TEXT,
    practice_license_issue_date TEXT,
    practice_license_expiry_date TEXT,
    specialty TEXT,
    title TEXT,
    department TEXT,
    hospital TEXT,
    avatar_path TEXT,
    id_card_front_path TEXT,
    id_card_back_path TEXT,
    medical_license_photo_path TEXT,
    practice_license_photo_path TEXT,
    status INTEGER NOT NULL DEFAULT 0,
    remark TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT,
    CONSTRAINT FK_doctor_profile_sys_user_user_id FOREIGN KEY (user_id) REFERENCES sys_user (id) ON DELETE CASCADE
)
""")

# 创建索引
conn.execute('CREATE INDEX IF NOT EXISTS IX_doctor_profile_user_id ON doctor_profile (user_id)')

conn.commit()

# 验证
print('=== doctor_profile 表结构 ===')
cols = conn.execute('PRAGMA table_info(doctor_profile)').fetchall()
for c in cols:
    print(f'  {c[1]}: {c[2]}, NOT NULL={c[3]}')

print('\n表创建成功！')
conn.close()
