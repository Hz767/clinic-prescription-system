# -*- coding: utf-8 -*-
"""合并 part1..part7 药品清单 CSV，处理分页截断行、重复表头、去重，输出完整清单。"""
import os, sys, csv, io, collections

base = r"E:\个人诊所处方系统\testdata\药品清单"
parts = [os.path.join(base, f"part{i}.csv") for i in range(1, 8)]

raw_lines = []
for p in parts:
    with open(p, "r", encoding="utf-8-sig") as f:
        for ln in f:
            raw_lines.append(ln.rstrip("\n").rstrip("\r"))

# 1) 拼接被截断的行：某行逗号数<2 时与下一行拼接
joined = []
i = 0
n = len(raw_lines)
while i < n:
    line = raw_lines[i]
    if line.strip() == "":
        i += 1
        continue
    # 重复表头行跳过
    if line.strip() == "Med_Name,Med_Kind,Med_Plc" and joined:
        i += 1
        continue
    while line.count(",") < 2 and i + 1 < n:
        nxt = raw_lines[i + 1]
        # 下一行若是表头或空行则不再拼接
        if nxt.strip() == "" or nxt.strip() == "Med_Name,Med_Kind,Med_Plc":
            break
        line = line + nxt  # 直接拼接（截断处无空格）
        i += 1
    joined.append(line)
    i += 1

# 2) 解析与去重（用 csv.reader 正确处理引号字段内含逗号的情况）
rows = []          # (med_name, med_kind, med_plc)
bad = []
seen = set()
reader = csv.reader(io.StringIO("\n".join(joined)))
for parts_cols in reader:
    if len(parts_cols) < 3:
        bad.append(",".join(parts_cols))
        continue
    name, kind, plc = parts_cols[0], parts_cols[1], parts_cols[2]
    name = name.strip()
    kind = kind.strip()
    plc = plc.strip()
    if not name:
        continue
    key = (name, kind, plc)
    if key in seen:
        continue
    seen.add(key)
    rows.append((name, kind, plc))

# 3) 统计
total = len(rows)
national = [r for r in rows if r[2] == "国家"]
print("拼接前原始数据行数:", len(joined))
print("有效记录数(去重后):", total)
print("国家目录记录数:", len(national))
print("省份增补记录数:", total - len(national))

# 按剂型空/非空统计
kind_empty = [r for r in rows if not r[1]]
print("无剂型标注(多为中成药):", len(kind_empty))

# 地区分布
plc_cnt = collections.Counter(r[2] for r in rows)
print("地区分布Top10:", plc_cnt.most_common(10))

# 4) 输出完整 CSV
out = os.path.join(base, "药品清单_完整.csv")
with open(out, "w", encoding="utf-8-sig", newline="") as f:
    w = csv.writer(f)
    w.writerow(["Med_Name", "Med_Kind", "Med_Plc"])
    for r in sorted(rows, key=lambda x: (x[2], x[0])):
        w.writerow(r)
print("输出文件:", out, "大小:", os.path.getsize(out))

# 4.5) 剂型/类别分布（供库存导入参考）
kind_top = collections.Counter()
for r in rows:
    k = r[1]
    # 提取方括号前的治疗类别
    if "[" in k:
        cat = k.split("[")[0].strip()
    else:
        cat = k.strip()
    kind_top[cat if cat else "(未分类-中成药)"] += 1
print("治疗类别分布Top25:")
for c, cnt in kind_top.most_common(25):
    print(f"  {c}: {cnt}")

# 5) 校验种子药品覆盖
for seed in ["阿莫西林", "布洛芬", "头孢克洛", "阿莫西林胶囊", "布洛芬片", "头孢克洛胶囊"]:
    hits = [r for r in rows if seed in r[0]]
    print(f"种子[{seed}] 命中 {len(hits)} 条:", hits[:3])

if bad:
    print("异常行数:", len(bad))
    for b in bad[:10]:
        print("  BAD:", b[:80])
