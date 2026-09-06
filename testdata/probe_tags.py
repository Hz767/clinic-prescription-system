# -*- coding: utf-8 -*-
"""统计 drugs.csv 的 contraindication_tags 值分布。只读。"""
import csv, collections

with open(r'E:\个人诊所处方系统\testdata\drugs.csv', encoding='utf-8-sig') as f:
    drugs = list(csv.DictReader(f))

cnt = collections.Counter()
for d in drugs:
    t = d['contraindication_tags'].strip()
    if t:
        for tag in t.replace('，', ',').replace('、', ',').split(','):
            cnt[tag.strip()] += 1
print('标签种类:', len(cnt))
for k, v in sorted(cnt.items()):
    print(f'  {k}: {v}')

# 特殊药品的用法（qty 计算参考）
for name in ['庆大霉素片', '布洛芬混悬液', '氨溴索口服溶液', '缬沙坦胶囊', '谷维素片', '口服补液盐Ⅲ', '西格列汀片']:
    for d in drugs:
        if d['generic_name_cn'] == name:
            print(name, '|', d['default_usage'], '|', d['unit'], '|', d['drug_class'])
