# -*- coding: utf-8 -*-
"""统计：drugs.csv 子类分布 + patients.csv 过敏/慢病分布（simvisit 设计用）。只读。"""
import csv, collections

def load(path):
    with open(path, encoding='utf-8-sig') as f:
        return list(csv.DictReader(f))

drugs = load(r'E:\个人诊所处方系统\testdata\drugs.csv')
pats = load(r'E:\个人诊所处方系统\testdata\patients.csv')

cls = collections.Counter(d['drug_class'].strip() for d in drugs if d['drug_class'].strip())
print('子类数:', len(cls))
print('子类分布:')
for c, n in sorted(cls.items()):
    print(' ', c, n)

print('\n--- 患者 ---')
print('总数:', len(pats))
print('有过敏史:', sum(1 for p in pats if p['allergies'].strip()))
allergy_kw = collections.Counter()
for p in pats:
    for k in p['allergies'].replace('、', ',').replace('，', ',').split(','):
        k = k.strip()
        if k: allergy_kw[k] += 1
print('过敏关键词分布:', dict(allergy_kw))
print('有慢病标签:', sum(1 for p in pats if p['chronic_tags'].strip()))
print('有患者标签:', sum(1 for p in pats if p['tags'].strip()))
