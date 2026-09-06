import csv
from collections import Counter

csv_path = r'E:\个人诊所处方系统\testdata\药品清单\药品清单_完整.csv'

regions = Counter()
total = 0

with open(csv_path, 'r', encoding='utf-8-sig') as f:
    reader = csv.reader(f)
    for row in reader:
        if len(row) < 1:
            continue
        med_name = row[0].strip() if len(row) > 0 else ''
        if med_name == 'Med_Name' or not med_name:
            continue
        med_plc = row[2].strip() if len(row) > 2 else ''
        region = med_plc if med_plc else '国家目录(无地区标注)'
        regions[region] += 1
        total += 1

print(f'药品总数: {total}')
print(f'\n地域分布:')
for region, count in regions.most_common():
    print(f'  {region}: {count} 种 ({count/total*100:.1f}%)')
