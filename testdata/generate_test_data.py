# -*- coding: utf-8 -*-
"""
陈医生诊所处方系统 - 测试数据生成脚本
生成：药物清单（~320 种）、300 名虚拟患者、约 450 次就诊（处方+医嘱+收费）
仅用于系统测试，所有患者信息均为虚构。

输出目录：脚本所在目录
"""
import csv
import random
import os
from datetime import date, timedelta

random.seed(20260905)

OUT_DIR = os.path.dirname(os.path.abspath(__file__))
os.makedirs(OUT_DIR, exist_ok=True)

# ────────────────────────────────────────────────────────────
# 1. 药物清单
# 字段: generic_name_cn,generic_name_en,spec,unit,default_usage,
#       is_antibiotic,antibiotic_level,is_toxic_drug,contraindication_tags,
#       cost_price_ref,retail_price_ref,reorder_level,drug_class,otc_type
# 合规说明：
#   - 不含麻醉药品/第一类精神药品/医疗用毒性药品（个人诊所不得经营）
#   - 抗菌药按《抗菌药物临床应用管理办法》标注分级
#   - otc_type: OTC甲/OTC乙/Rx
# ────────────────────────────────────────────────────────────
def drug(cn, en, spec, unit, usage, cls, otc, price_ref, ab=0, toxic=False, contra="", reorder=None):
    """ab: 0=非抗菌 1=非限制使用 2=限制使用；price_ref: 零售价基准"""
    cost = round(price_ref * 0.62, 2)
    retail = round(price_ref, 2)
    level = "None" if ab == 0 else ("NonRestricted" if ab == 1 else "Restricted")
    return [cn, en, spec, unit, usage, "1" if ab else "0", level,
            "1" if toxic else "0", contra, f"{cost:.2f}", f"{retail:.2f}",
            str(reorder if reorder is not None else ""), cls, otc]

DRUGS = []
A = DRUGS.append

# ── 抗感染药 ──
A(drug("阿莫西林胶囊", "Amoxicillin Capsules", "0.25g×24粒", "盒", "口服 0.5g tid", "抗感染-青霉素类", "Rx", 12.50, ab=1, contra="青霉素类", reorder=20))
A(drug("阿莫西林克拉维酸钾片", "Amoxicillin/Clavulanate Tablets", "0.457g×12片", "盒", "口服 0.457g bid", "抗感染-青霉素类", "Rx", 18.00, ab=1, contra="青霉素类", reorder=15))
A(drug("氨苄西林胶囊", "Ampicillin Capsules", "0.25g×24粒", "盒", "口服 0.5g qid", "抗感染-青霉素类", "Rx", 9.80, ab=1, contra="青霉素类"))
A(drug("头孢氨苄胶囊", "Cefalexin Capsules", "0.25g×24粒", "盒", "口服 0.5g qid", "抗感染-头孢类", "Rx", 8.50, ab=1, contra="头孢类", reorder=20))
A(drug("头孢拉定胶囊", "Cefradine Capsules", "0.25g×24粒", "盒", "口服 0.5g qid", "抗感染-头孢类", "Rx", 9.00, ab=1, contra="头孢类"))
A(drug("头孢克洛胶囊", "Cefaclor Capsules", "0.25g×12粒", "盒", "口服 0.25g tid", "抗感染-头孢类", "Rx", 25.00, ab=2, contra="头孢类", reorder=12))
A(drug("头孢呋辛酯片", "Cefuroxime Axetil Tablets", "0.25g×12片", "盒", "口服 0.25g bid", "抗感染-头孢类", "Rx", 28.00, ab=2, contra="头孢类"))
A(drug("头孢克肟分散片", "Cefixime Dispersible Tablets", "0.1g×12片", "盒", "口服 0.1g bid", "抗感染-头孢类", "Rx", 22.00, ab=2, contra="头孢类"))
A(drug("阿奇霉素分散片", "Azithromycin Dispersible Tablets", "0.25g×6片", "盒", "口服 0.5g qd", "抗感染-大环内酯类", "Rx", 15.00, ab=1, reorder=15))
A(drug("克拉霉素片", "Clarithromycin Tablets", "0.25g×12片", "盒", "口服 0.25g bid", "抗感染-大环内酯类", "Rx", 16.00, ab=1))
A(drug("罗红霉素胶囊", "Roxithromycin Capsules", "0.15g×12粒", "盒", "口服 0.15g bid", "抗感染-大环内酯类", "Rx", 13.00, ab=1))
A(drug("左氧氟沙星片", "Levofloxacin Tablets", "0.5g×7片", "盒", "口服 0.5g qd", "抗感染-喹诺酮类", "Rx", 16.50, ab=2, contra="喹诺酮类", reorder=15))
A(drug("盐酸环丙沙星片", "Ciprofloxacin Tablets", "0.25g×12片", "盒", "口服 0.5g bid", "抗感染-喹诺酮类", "Rx", 10.00, ab=2, contra="喹诺酮类"))
A(drug("诺氟沙星胶囊", "Norfloxacin Capsules", "0.1g×24粒", "盒", "口服 0.4g bid", "抗感染-喹诺酮类", "Rx", 6.50, ab=2, contra="喹诺酮类"))
A(drug("庆大霉素片", "Gentamicin Tablets", "40mg×100片", "瓶", "口服 80mg tid", "抗感染-氨基糖苷类", "Rx", 5.00, ab=1, contra="氨基糖苷类"))
A(drug("甲硝唑片", "Metronidazole Tablets", "0.2g×100片", "瓶", "口服 0.4g tid", "抗感染-硝基咪唑类", "Rx", 4.50, reorder=10))
A(drug("奥硝唑分散片", "Ornidazole Dispersible Tablets", "0.25g×12片", "盒", "口服 0.5g bid", "抗感染-硝基咪唑类", "Rx", 14.00))
A(drug("复方磺胺甲噁唑片", "Compound Sulfamethoxazole Tablets", "0.48g×12片", "盒", "口服 1片 bid", "抗感染-磺胺类", "Rx", 7.00, ab=1, contra="磺胺类"))
A(drug("盐酸小檗碱片", "Berberine Hydrochloride Tablets", "0.1g×100片", "瓶", "口服 0.3g tid", "抗感染-其他", "OTC甲", 8.00))
A(drug("阿昔洛韦片", "Aciclovir Tablets", "0.2g×24片", "盒", "口服 0.4g tid", "抗感染-抗病毒", "Rx", 9.00))
A(drug("利巴韦林颗粒", "Ribavirin Granules", "50mg×18袋", "盒", "口服 0.15g tid", "抗感染-抗病毒", "Rx", 12.00))
A(drug("磷酸奥司他韦胶囊", "Oseltamivir Phosphate Capsules", "75mg×10粒", "盒", "口服 75mg bid", "抗感染-抗病毒", "Rx", 45.00, reorder=10))
A(drug("伊曲康唑胶囊", "Itraconazole Capsules", "0.1g×14粒", "盒", "口服 0.2g qd", "抗感染-抗真菌", "Rx", 35.00))
A(drug("氟康唑胶囊", "Fluconazole Capsules", "0.15g×6粒", "盒", "口服 0.15g qd", "抗感染-抗真菌", "Rx", 18.00))
A(drug("制霉菌素片", "Nystatin Tablets", "50万U×100片", "瓶", "口服 50万U tid", "抗感染-抗真菌", "Rx", 20.00))

# ── 解热镇痛抗炎药 ──
A(drug("布洛芬缓释胶囊", "Ibuprofen Sustained-release Capsules", "0.3g×20粒", "盒", "口服 0.3g bid", "解热镇痛", "OTC乙", 15.00, contra="NSAIDs", reorder=25))
A(drug("布洛芬片", "Ibuprofen Tablets", "0.2g×100片", "瓶", "口服 0.2g tid", "解热镇痛", "OTC甲", 8.00, contra="NSAIDs", reorder=30))
A(drug("布洛芬混悬液", "Ibuprofen Suspension", "100ml:2g", "瓶", "口服 5ml qid", "解热镇痛-儿科", "OTC甲", 12.00, contra="NSAIDs"))
A(drug("对乙酰氨基酚片", "Paracetamol Tablets", "0.5g×20片", "盒", "口服 0.5g tid", "解热镇痛", "OTC甲", 5.00, reorder=30))
A(drug("对乙酰氨基酚缓释片", "Paracetamol Sustained-release Tablets", "0.65g×12片", "盒", "口服 0.65g bid", "解热镇痛", "OTC甲", 9.00))
A(drug("双氯芬酸钠缓释片", "Diclofenac Sodium SR Tablets", "75mg×10片", "盒", "口服 75mg qd", "解热镇痛", "Rx", 11.00, contra="NSAIDs"))
A(drug("塞来昔布胶囊", "Celecoxib Capsules", "0.2g×6粒", "盒", "口服 0.2g qd", "解热镇痛-COX2", "Rx", 22.00, contra="NSAIDs"))
A(drug("洛索洛芬钠片", "Loxoprofen Sodium Tablets", "60mg×24片", "盒", "口服 60mg tid", "解热镇痛", "Rx", 14.00, contra="NSAIDs"))
A(drug("吲哚美辛肠溶片", "Indometacin Enteric-coated Tablets", "25mg×100片", "瓶", "口服 25mg bid", "解热镇痛", "Rx", 7.00, contra="NSAIDs"))
A(drug("氨基比林咖啡因片", "Amidopyrine and Caffeine Tablets", "20片", "盒", "口服 1片 tid", "解热镇痛-复方", "OTC甲", 3.50))
A(drug("复方对乙酰氨基酚片(Ⅱ)", "Compound Paracetamol Tablets(Ⅱ)", "10片", "盒", "口服 1片 tid", "解热镇痛-复方", "OTC甲", 6.00))
A(drug("美洛昔康片", "Meloxicam Tablets", "7.5mg×10片", "盒", "口服 7.5mg qd", "解热镇痛", "Rx", 13.00, contra="NSAIDs"))
A(drug("尼美舒利颗粒", "Nimesulide Granules", "0.1g×10袋", "盒", "口服 0.1g bid", "解热镇痛", "Rx", 16.00, contra="NSAIDs"))
A(drug("去痛片", "Compound Aminopyrine Phenacetin Tablets", "100片", "瓶", "口服 1片 tid", "解热镇痛-复方", "OTC甲", 6.50))
A(drug("秋水仙碱片", "Colchicine Tablets", "0.5mg×20片", "盒", "口服 0.5mg bid", "解热镇痛-痛风", "Rx", 10.00))

# ── 呼吸系统 ──
A(drug("氨溴索口服溶液", "Ambroxol Oral Solution", "100ml:0.6g", "瓶", "口服 10ml tid", "呼吸-祛痰", "OTC甲", 9.00))
A(drug("盐酸氨溴索片", "Ambroxol Hydrochloride Tablets", "30mg×20片", "盒", "口服 30mg tid", "呼吸-祛痰", "OTC甲", 8.00))
A(drug("乙酰半胱氨酸颗粒", "Acetylcysteine Granules", "0.2g×12袋", "盒", "口服 0.2g tid", "呼吸-祛痰", "OTC甲", 15.00))
A(drug("复方甘草片", "Compound Liquorice Tablets", "100片", "瓶", "含服 3片 tid", "呼吸-镇咳", "OTC甲", 5.50, reorder=20))
A(drug("右美沙芬愈创甘油醚糖浆", "Dextromethorphan and Guaifenesin Syrup", "100ml", "瓶", "口服 10ml tid", "呼吸-镇咳", "OTC甲", 13.00))
A(drug("氢溴酸右美沙芬片", "Dextromethorphan Hydrobromide Tablets", "15mg×24片", "盒", "口服 15mg tid", "呼吸-镇咳", "OTC甲", 8.00))
A(drug("喷托维林片", "Pentoxyverine Tablets", "25mg×100片", "瓶", "口服 25mg tid", "呼吸-镇咳", "OTC甲", 6.00))
A(drug("氨茶碱片", "Aminophylline Tablets", "0.1g×100片", "瓶", "口服 0.1g tid", "呼吸-平喘", "Rx", 6.00))
A(drug("沙丁胺醇吸入气雾剂", "Salbutamol Aerosol", "100μg×200揿", "支", "吸入 1-2揿 prn", "呼吸-平喘", "Rx", 18.00))
A(drug("茶碱缓释片", "Theophylline SR Tablets", "0.1g×24片", "盒", "口服 0.2g bid", "呼吸-平喘", "Rx", 10.00))
A(drug("孟鲁司特钠咀嚼片", "Montelukast Sodium Chewable Tablets", "5mg×5片", "盒", "口服 5mg qn", "呼吸-抗过敏", "Rx", 25.00))
A(drug("氯雷他定片", "Loratadine Tablets", "10mg×6片", "盒", "口服 10mg qd", "抗过敏-抗组胺", "OTC甲", 12.00, reorder=20))
A(drug("氯雷他定糖浆", "Loratadine Syrup", "60ml:60mg", "瓶", "口服 5ml qd", "抗过敏-抗组胺", "OTC甲", 15.00))
A(drug("西替利嗪片", "Cetirizine Tablets", "10mg×12片", "盒", "口服 10mg qd", "抗过敏-抗组胺", "OTC甲", 10.00))
A(drug("马来酸氯苯那敏片", "Chlorphenamine Maleate Tablets", "4mg×100片", "瓶", "口服 4mg tid", "抗过敏-抗组胺", "OTC甲", 3.00))
A(drug("盐酸非索非那定片", "Fexofenadine Hydrochloride Tablets", "60mg×12片", "盒", "口服 60mg bid", "抗过敏-抗组胺", "Rx", 18.00))
A(drug("复方甲氧那明胶囊", "Compound Methoxyphenamine Capsules", "60粒", "瓶", "口服 2粒 tid", "呼吸-平喘复方", "Rx", 22.00))
A(drug("孟鲁司特钠片", "Montelukast Sodium Tablets", "10mg×5片", "盒", "口服 10mg qn", "呼吸-抗过敏", "Rx", 28.00))
A(drug("丙卡特罗片", "Procaterol Tablets", "25μg×20片", "盒", "口服 25μg bid", "呼吸-平喘", "Rx", 16.00))
A(drug("百咳静糖浆", "Baikejing Syrup", "100ml", "瓶", "口服 10ml tid", "中成药-呼吸", "OTC甲", 12.00))

# ── 消化系统 ──
A(drug("奥美拉唑肠溶胶囊", "Omeprazole Enteric-coated Capsules", "20mg×14粒", "盒", "口服 20mg qd", "消化-抑酸", "Rx", 18.00, reorder=15))
A(drug("兰索拉唑肠溶片", "Lansoprazole Enteric-coated Tablets", "30mg×14片", "盒", "口服 30mg qd", "消化-抑酸", "Rx", 20.00))
A(drug("雷贝拉唑钠肠溶片", "Rabeprazole Sodium Enteric-coated Tablets", "10mg×14片", "盒", "口服 10mg qd", "消化-抑酸", "Rx", 24.00))
A(drug("埃索美拉唑镁肠溶片", "Esomeprazole Magnesium Enteric-coated Tablets", "20mg×7片", "盒", "口服 20mg qd", "消化-抑酸", "Rx", 26.00))
A(drug("法莫替丁片", "Famotidine Tablets", "20mg×24片", "盒", "口服 20mg bid", "消化-抑酸", "OTC甲", 8.00))
A(drug("西咪替丁片", "Cimetidine Tablets", "0.2g×100片", "瓶", "口服 0.2g tid", "消化-抑酸", "OTC甲", 7.00))
A(drug("铝碳酸镁咀嚼片", "Hydrotalcite Chewable Tablets", "0.5g×20片", "盒", "嚼服 1g tid", "消化-抗酸", "OTC甲", 10.00))
A(drug("硫糖铝咀嚼片", "Sucralfate Chewable Tablets", "0.25g×100片", "瓶", "嚼服 1g tid", "消化-胃黏膜保护", "OTC甲", 9.00))
A(drug("枸橼酸铋钾颗粒", "Bismuth Potassium Citrate Granules", "0.11g×28袋", "盒", "口服 1袋 qid", "消化-胃黏膜保护", "Rx", 14.00))
A(drug("复方氢氧化铝片", "Compound Aluminium Hydroxide Tablets", "100片", "瓶", "嚼服 2片 tid", "消化-抗酸", "OTC甲", 6.00))
A(drug("多潘立酮片", "Domperidone Tablets", "10mg×30片", "盒", "口服 10mg tid", "消化-促动力", "Rx", 12.00))
A(drug("枸橼酸莫沙必利片", "Mosapride Citrate Tablets", "5mg×24片", "盒", "口服 5mg tid", "消化-促动力", "Rx", 15.00))
A(drug("盐酸伊托必利片", "Itopride Hydrochloride Tablets", "50mg×20片", "盒", "口服 50mg tid", "消化-促动力", "Rx", 13.00))
A(drug("蒙脱石散", "Montmorillonite Powder", "3g×10袋", "盒", "口服 3g tid", "消化-止泻", "OTC甲", 12.00, reorder=20))
A(drug("盐酸洛哌丁胺胶囊", "Loperamide Hydrochloride Capsules", "2mg×6粒", "盒", "口服 2mg qd", "消化-止泻", "OTC甲", 8.00))
A(drug("双歧杆菌三联活菌胶囊", "Bifid Triple Viable Capsules", "0.21g×24粒", "盒", "口服 0.63g tid", "消化-微生态", "OTC甲", 20.00))
A(drug("枯草杆菌二联活菌颗粒", "Combined Bacillus Subtilis Granules", "1g×10袋", "盒", "口服 1g bid", "消化-微生态", "OTC甲", 18.00))
A(drug("乳果糖口服溶液", "Lactulose Oral Solution", "100ml:66.7g", "瓶", "口服 15ml qd", "消化-通便", "OTC乙", 16.00))
A(drug("聚乙二醇4000散", "Macrogol 4000 Powder", "10g×10袋", "盒", "口服 10g qd", "消化-通便", "OTC乙", 15.00))
A(drug("开塞露", "Glycerol Enema", "20ml×20支", "盒", "纳肛 20ml prn", "消化-通便", "OTC乙", 9.00))
A(drug("复方消化酶胶囊", "Compound Digestive Enzyme Capsules", "20粒", "盒", "口服 1粒 tid", "消化-助消化", "OTC甲", 18.00))
A(drug("多酶片", "Multienzyme Tablets", "100片", "瓶", "口服 2片 tid", "消化-助消化", "OTC乙", 5.00))
A(drug("熊去氧胆酸胶囊", "Ursodeoxycholic Acid Capsules", "0.25g×24粒", "盒", "口服 0.25g tid", "消化-肝胆", "Rx", 30.00))
A(drug("水飞蓟宾胶囊", "Silibinin Capsules", "35mg×30粒", "盒", "口服 70mg tid", "消化-肝胆", "Rx", 22.00))
A(drug("复方谷氨酰胺肠溶胶囊", "Compound Glutamine Enteric Capsules", "0.25g×24粒", "盒", "口服 0.5g tid", "消化-肠道修复", "Rx", 20.00))
A(drug("藿香正气口服液", "Huoxiangzhengqi Oral Liquid", "10ml×10支", "盒", "口服 10ml bid", "中成药-消化", "OTC甲", 14.00))
A(drug("健胃消食片", "Jianwei Xiaoshi Tablets", "0.8g×32片", "盒", "口服 3片 tid", "中成药-消化", "OTC甲", 8.00))
A(drug("香砂养胃丸", "Xiangsha Yangwei Pills", "9g×10袋", "盒", "口服 9g bid", "中成药-消化", "OTC甲", 12.00))
A(drug("三九胃泰颗粒", "Sanjiu Weitai Granules", "20g×10袋", "盒", "口服 20g bid", "中成药-消化", "OTC甲", 15.00))

# ── 心血管系统 ──
A(drug("硝苯地平缓释片(Ⅰ)", "Nifedipine SR Tablets(Ⅰ)", "10mg×30片", "盒", "口服 10mg bid", "心血管-降压CCB", "Rx", 9.00))
A(drug("硝苯地平控释片", "Nifedipine Controlled-release Tablets", "30mg×14片", "盒", "口服 30mg qd", "心血管-降压CCB", "Rx", 20.00, reorder=15))
A(drug("氨氯地平片", "Amlodipine Tablets", "5mg×14片", "盒", "口服 5mg qd", "心血管-降压CCB", "Rx", 15.00, reorder=15))
A(drug("苯磺酸左氨氯地平片", "Levamlodipine Besylate Tablets", "2.5mg×14片", "盒", "口服 2.5mg qd", "心血管-降压CCB", "Rx", 18.00))
A(drug("非洛地平缓释片", "Felodipine SR Tablets", "5mg×10片", "盒", "口服 5mg qd", "心血管-降压CCB", "Rx", 17.00))
A(drug("厄贝沙坦片", "Irbesartan Tablets", "0.15g×14片", "盒", "口服 0.15g qd", "心血管-降压ARB", "Rx", 16.00, reorder=15))
A(drug("缬沙坦胶囊", "Valsartan Capsules", "80mg×14粒", "盒", "口服 80mg qd", "心血管-降压ARB", "Rx", 19.00))
A(drug("氯沙坦钾片", "Losartan Potassium Tablets", "50mg×14片", "盒", "口服 50mg qd", "心血管-降压ARB", "Rx", 18.00))
A(drug("替米沙坦片", "Telmisartan Tablets", "40mg×14片", "盒", "口服 40mg qd", "心血管-降压ARB", "Rx", 21.00))
A(drug("培哚普利叔丁胺片", "Perindopril tert-Butylamine Tablets", "4mg×14片", "盒", "口服 4mg qd", "心血管-降压ACEI", "Rx", 24.00))
A(drug("贝那普利片", "Benazepril Tablets", "10mg×14片", "盒", "口服 10mg qd", "心血管-降压ACEI", "Rx", 17.00))
A(drug("氢氯噻嗪片", "Hydrochlorothiazide Tablets", "25mg×100片", "瓶", "口服 25mg qd", "心血管-利尿剂", "Rx", 3.50))
A(drug("呋塞米片", "Furosemide Tablets", "20mg×100片", "瓶", "口服 20mg qd", "心血管-利尿剂", "Rx", 5.00))
A(drug("酒石酸美托洛尔片", "Metoprolol Tartrate Tablets", "25mg×20片", "盒", "口服 25mg bid", "心血管-β受体阻滞剂", "Rx", 6.50, reorder=15))
A(drug("琥珀酸美托洛尔缓释片", "Metoprolol Succinate SR Tablets", "47.5mg×7片", "盒", "口服 47.5mg qd", "心血管-β受体阻滞剂", "Rx", 14.00))
A(drug("比索洛尔片", "Bisoprolol Tablets", "5mg×10片", "盒", "口服 5mg qd", "心血管-β受体阻滞剂", "Rx", 13.00))
A(drug("阿托伐他汀钙片", "Atorvastatin Calcium Tablets", "20mg×14片", "盒", "口服 20mg qn", "心血管-他汀", "Rx", 22.00, reorder=15))
A(drug("瑞舒伐他汀钙片", "Rosuvastatin Calcium Tablets", "10mg×14片", "盒", "口服 10mg qn", "心血管-他汀", "Rx", 26.00))
A(drug("辛伐他汀片", "Simvastatin Tablets", "20mg×14片", "盒", "口服 20mg qn", "心血管-他汀", "Rx", 12.00))
A(drug("匹伐他汀钙片", "Pitavastatin Calcium Tablets", "2mg×14片", "盒", "口服 2mg qn", "心血管-他汀", "Rx", 28.00))
A(drug("非诺贝特胶囊", "Fenofibrate Capsules", "0.2g×10粒", "盒", "口服 0.2g qd", "心血管-降脂贝特", "Rx", 18.00))
A(drug("阿司匹林肠溶片", "Aspirin Enteric-coated Tablets", "100mg×30片", "盒", "口服 100mg qd", "心血管-抗血小板", "Rx", 8.00, contra="NSAIDs", reorder=20))
A(drug("硫酸氢氯吡格雷片", "Clopidogrel Bisulfate Tablets", "75mg×14片", "盒", "口服 75mg qd", "心血管-抗血小板", "Rx", 30.00))
A(drug("华法林钠片", "Warfarin Sodium Tablets", "2.5mg×60片", "瓶", "口服 2.5mg qd", "心血管-抗凝", "Rx", 12.00))
A(drug("利伐沙班片", "Rivaroxaban Tablets", "10mg×14片", "盒", "口服 10mg qd", "心血管-抗凝", "Rx", 55.00))
A(drug("单硝酸异山梨酯缓释片", "Isosorbide Mononitrate SR Tablets", "40mg×14片", "盒", "口服 40mg qd", "心血管-抗心绞痛", "Rx", 20.00))
A(drug("硝酸甘油片", "Nitroglycerin Tablets", "0.5mg×24片", "盒", "舌下含服 0.5mg prn", "心血管-抗心绞痛", "Rx", 5.00))
A(drug("复方丹参滴丸", "Compound Danshen Dripping Pills", "27mg×180丸", "盒", "口服 10丸 tid", "中成药-心血管", "OTC甲", 22.00))
A(drug("速效救心丸", "Suxiao Jiuxin Pills", "40mg×60丸×2瓶", "盒", "含服 4-6丸 prn", "中成药-心血管", "OTC甲", 30.00))
A(drug("稳心颗粒", "Wenxin Granules", "9g×9袋", "盒", "口服 9g tid", "中成药-心血管", "Rx", 25.00))
A(drug("地奥心血康胶囊", "Diao Xinxuekang Capsules", "0.1g×30粒", "盒", "口服 0.2g tid", "中成药-心血管", "Rx", 18.00))
A(drug("通心络胶囊", "Tongxinluo Capsules", "0.26g×30粒", "盒", "口服 2粒 tid", "中成药-心血管", "Rx", 28.00))
A(drug("倍他司汀片", "Betahistine Tablets", "6mg×30片", "盒", "口服 6mg tid", "心血管-其他", "Rx", 10.00))
A(drug("曲克芦丁片", "Troxerutin Tablets", "60mg×100片", "瓶", "口服 120mg tid", "心血管-其他", "Rx", 8.00))

# ── 内分泌代谢 ──
A(drug("盐酸二甲双胍片", "Metformin Hydrochloride Tablets", "0.5g×20片", "盒", "口服 0.5g bid", "内分泌-降糖", "Rx", 8.00, reorder=15))
A(drug("盐酸二甲双胍缓释片", "Metformin Hydrochloride SR Tablets", "0.5g×30片", "盒", "口服 0.5g qd", "内分泌-降糖", "Rx", 12.00))
A(drug("格列美脲片", "Glimepiride Tablets", "2mg×12片", "盒", "口服 2mg qd", "内分泌-降糖", "Rx", 15.00))
A(drug("格列齐特缓释片", "Gliclazide SR Tablets", "30mg×20片", "盒", "口服 30mg qd", "内分泌-降糖", "Rx", 18.00))
A(drug("阿卡波糖片", "Acarbose Tablets", "50mg×30片", "盒", "口服 50mg tid", "内分泌-降糖", "Rx", 20.00))
A(drug("达格列净片", "Dapagliflozin Tablets", "10mg×14片", "盒", "口服 10mg qd", "内分泌-降糖SGLT2", "Rx", 45.00))
A(drug("西格列汀片", "Sitagliptin Tablets", "100mg×14片", "盒", "口服 100mg qd", "内分泌-降糖DPP4", "Rx", 48.00))
A(drug("左甲状腺素钠片", "Levothyroxine Sodium Tablets", "50μg×100片", "瓶", "口服 50μg qd", "内分泌-甲状腺", "Rx", 15.00))
A(drug("甲巯咪唑片", "Thiamazole Tablets", "5mg×100片", "瓶", "口服 5mg tid", "内分泌-甲状腺", "Rx", 9.00))
A(drug("碳酸钙D3片", "Calcium Carbonate and Vitamin D3 Tablets", "600mg×30片", "盒", "口服 1片 qd", "内分泌-钙剂", "OTC甲", 18.00))
A(drug("阿仑膦酸钠片", "Alendronate Sodium Tablets", "70mg×4片", "盒", "口服 70mg qw", "内分泌-抗骨质疏松", "Rx", 25.00))
A(drug("维生素D滴剂", "Vitamin D Drops", "400IU×30粒", "盒", "口服 1粒 qd", "内分泌-维生素D", "OTC甲", 22.00))
A(drug("骨化三醇胶丸", "Calcitriol Soft Capsules", "0.25μg×20粒", "盒", "口服 0.25μg qd", "内分泌-维生素D", "Rx", 35.00))
A(drug("非布司他片", "Febuxostat Tablets", "40mg×16片", "盒", "口服 40mg qd", "内分泌-痛风", "Rx", 30.00))
A(drug("别嘌醇片", "Allopurinol Tablets", "0.1g×100片", "瓶", "口服 0.1g bid", "内分泌-痛风", "Rx", 8.00))
A(drug("碳酸氢钠片", "Sodium Bicarbonate Tablets", "0.5g×100片", "瓶", "口服 1g tid", "内分泌-痛风碱化", "OTC甲", 4.00))

# ── 神经系统 ──
A(drug("谷维素片", "Oryzanol Tablets", "10mg×100片", "瓶", "口服 10mg tid", "神经-植物神经", "OTC乙", 5.00))
A(drug("甲钴胺片", "Mecobalamin Tablets", "0.5mg×24片", "盒", "口服 0.5mg tid", "神经-营养神经", "Rx", 12.00))
A(drug("维生素B1片", "Vitamin B1 Tablets", "10mg×100片", "瓶", "口服 10mg tid", "神经-维生素B族", "OTC乙", 3.00))
A(drug("维生素B12片", "Vitamin B12 Tablets", "25μg×100片", "瓶", "口服 25μg tid", "神经-维生素B族", "OTC乙", 4.00))
A(drug("艾司唑仑片", "Estazolam Tablets", "1mg×20片", "盒", "口服 1mg qn", "神经-镇静催眠", "Rx", 6.00))
A(drug("佐匹克隆片", "Zopiclone Tablets", "7.5mg×7片", "盒", "口服 7.5mg qn", "神经-镇静催眠", "Rx", 18.00))
A(drug("氟桂利嗪胶囊", "Flunarizine Capsules", "5mg×20粒", "盒", "口服 5mg qn", "神经-偏头痛", "Rx", 10.00))
A(drug("尼莫地平片", "Nimodipine Tablets", "20mg×50片", "盒", "口服 40mg tid", "神经-脑血管", "Rx", 15.00))
A(drug("吡拉西坦片", "Piracetam Tablets", "0.4g×100片", "瓶", "口服 0.8g tid", "神经-脑代谢", "Rx", 10.00))
A(drug("银杏叶片", "Ginkgo Leaf Tablets", "19.2mg×24片", "盒", "口服 1片 tid", "中成药-神经", "OTC甲", 16.00))
A(drug("养血清脑颗粒", "Yangxue Qingnao Granules", "4g×15袋", "盒", "口服 4g tid", "中成药-神经", "Rx", 20.00))
A(drug("天麻素胶囊", "Gastrodin Capsules", "50mg×24粒", "盒", "口服 100mg tid", "中成药-神经", "Rx", 14.00))
A(drug("甲磺酸倍他司汀片", "Betahistine Mesylate Tablets", "6mg×30片", "盒", "口服 6mg tid", "神经-前庭", "Rx", 11.00))

# ── 皮肤科 ──
A(drug("莫匹罗星软膏", "Mupirocin Ointment", "10g:0.2g", "支", "外用 bid", "皮肤-外用抗菌", "OTC甲", 12.00))
A(drug("红霉素软膏", "Erythromycin Ointment", "10g:0.1g", "支", "外用 bid", "皮肤-外用抗菌", "OTC甲", 4.00))
A(drug("夫西地酸乳膏", "Fusidic Acid Cream", "10g:0.2g", "支", "外用 bid", "皮肤-外用抗菌", "Rx", 18.00))
A(drug("盐酸特比萘芬乳膏", "Terbinafine Hydrochloride Cream", "10g:0.1g", "支", "外用 qd", "皮肤-抗真菌", "OTC甲", 10.00))
A(drug("酮康唑乳膏", "Ketoconazole Cream", "10g:0.2g", "支", "外用 bid", "皮肤-抗真菌", "OTC甲", 8.00))
A(drug("硝酸咪康唑乳膏", "Miconazole Nitrate Cream", "20g:0.4g", "支", "外用 bid", "皮肤-抗真菌", "OTC甲", 9.00))
A(drug("曲安奈德益康唑乳膏", "Triamcinolone Acetonide and Econazole Cream", "15g", "支", "外用 bid", "皮肤-复方抗真菌", "OTC甲", 11.00))
A(drug("糠酸莫米松乳膏", "Mometasone Furoate Cream", "10g:5mg", "支", "外用 qd", "皮肤-糖皮质激素", "OTC甲", 16.00))
A(drug("丁酸氢化可的松乳膏", "Hydrocortisone Butyrate Cream", "10g:10mg", "支", "外用 bid", "皮肤-糖皮质激素", "OTC甲", 13.00))
A(drug("卤米松乳膏", "Halometasone Cream", "10g:5mg", "支", "外用 bid", "皮肤-糖皮质激素", "Rx", 18.00))
A(drug("阿昔洛韦乳膏", "Aciclovir Cream", "10g:0.3g", "支", "外用 qid", "皮肤-抗病毒", "OTC甲", 6.00))
A(drug("炉甘石洗剂", "Calamine Lotion", "100ml", "瓶", "外用 bid", "皮肤-止痒收敛", "OTC甲", 7.00))
A(drug("氯雷他定凝胶", "Loratadine Gel", "20g", "支", "外用 qd", "皮肤-抗过敏", "OTC甲", 14.00))
A(drug("维A酸乳膏", "Tretinoin Cream", "15g:15mg", "支", "外用 qn", "皮肤-角质调节", "Rx", 10.00))
A(drug("阿达帕林凝胶", "Adapalene Gel", "30g:0.03g", "支", "外用 qn", "皮肤-痤疮", "Rx", 22.00))
A(drug("夫西地酸乳膏", "Fusidic Acid Cream", "15g", "支", "外用 bid", "皮肤-痤疮", "Rx", 20.00))
A(drug("尿素维E乳膏", "Urea and Vitamin E Cream", "20g", "支", "外用 bid", "皮肤-保湿", "OTC乙", 8.00))
A(drug("马应龙麝香痔疮膏", "Mayinglong Musk Hemorrhoids Ointment", "10g", "支", "外用 bid", "中成药-皮肤", "OTC甲", 18.00))
A(drug("湿润烧伤膏", "Moist Exposed Burn Ointment", "40g", "支", "外用 qd", "中成药-皮肤", "OTC甲", 25.00))
A(drug("云南白药气雾剂", "Yunnan Baiyao Aerosol", "85g", "瓶", "外用 bid", "中成药-皮肤", "OTC甲", 28.00))

# ── 五官科 ──
A(drug("氧氟沙星滴眼液", "Ofloxacin Eye Drops", "5ml:15mg", "支", "滴眼 qid", "五官-抗菌眼", "Rx", 9.00))
A(drug("左氧氟沙星滴眼液", "Levofloxacin Eye Drops", "5ml:24.4mg", "支", "滴眼 qid", "五官-抗菌眼", "Rx", 12.00))
A(drug("妥布霉素滴眼液", "Tobramycin Eye Drops", "5ml:15mg", "支", "滴眼 qid", "五官-抗菌眼", "Rx", 11.00))
A(drug("玻璃酸钠滴眼液", "Sodium Hyaluronate Eye Drops", "10ml:10mg", "支", "滴眼 tid", "五官-人工泪液", "OTC甲", 25.00))
A(drug("聚乙烯醇滴眼液", "Polyvinyl Alcohol Eye Drops", "0.8ml×20支", "盒", "滴眼 prn", "五官-人工泪液", "OTC甲", 18.00))
A(drug("盐酸羟甲唑啉滴鼻液", "Oxymetazoline Hydrochloride Nasal Drops", "10ml:5mg", "支", "滴鼻 bid", "五官-鼻减充血", "OTC甲", 8.00))
A(drug("丙酸氟替卡松鼻喷雾剂", "Fluticasone Propionate Nasal Spray", "120喷", "支", "喷鼻 qd", "五官-鼻激素", "Rx", 45.00))
A(drug("糠酸莫米松鼻喷雾剂", "Mometasone Furoate Nasal Spray", "60揿", "支", "喷鼻 qd", "五官-鼻激素", "Rx", 48.00))
A(drug("氧氟沙星滴耳液", "Ofloxacin Ear Drops", "5ml:15mg", "支", "滴耳 bid", "五官-抗菌耳", "Rx", 8.00))
A(drug("碳酸氢钠滴耳液", "Sodium Bicarbonate Ear Drops", "8ml", "支", "滴耳 tid", "五官-耵聍软化", "OTC甲", 4.00))
A(drug("复方薄荷脑滴鼻液", "Compound Menthol Nasal Drops", "10ml", "支", "滴鼻 tid", "五官-鼻腔润滑", "OTC甲", 5.00))
A(drug("西地碘含片", "Cydiodine Buccal Tablets", "1.5mg×24片", "盒", "含服 1片 tid", "五官-口腔抗菌", "OTC甲", 12.00))
A(drug("西瓜霜润喉片", "Xiguashuang Throat Tablets", "0.6g×48片", "盒", "含服 2片 tid", "中成药-五官", "OTC甲", 10.00))
A(drug("金嗓子喉片", "Jinsangzi Throat Tablets", "2g×20片", "盒", "含服 1片 tid", "中成药-五官", "OTC甲", 9.00))
A(drug("氯霉素滴眼液", "Chloramphenicol Eye Drops", "8ml:20mg", "支", "滴眼 qid", "五官-抗菌眼", "Rx", 6.00))

# ── 妇科 ──
A(drug("甲硝唑阴道泡腾片", "Metronidazole Vaginal Effervescent Tablets", "0.2g×14片", "盒", "阴道给药 qn", "妇科-阴道炎", "Rx", 12.00))
A(drug("克霉唑阴道片", "Clotrimazole Vaginal Tablets", "0.5g×1片", "盒", "阴道给药 qn", "妇科-阴道炎", "OTC甲", 15.00))
A(drug("硝呋太尔制霉素阴道软胶囊", "Nifuratel and Nystatin Vaginal Soft Capsules", "6粒", "盒", "阴道给药 qn", "妇科-阴道炎", "Rx", 25.00))
A(drug("红核妇洁洗液", "Honghe Fujie Lotion", "150ml", "瓶", "外用冲洗 qd", "中成药-妇科", "OTC甲", 22.00))
A(drug("洁尔阴洗液", "Jieeryin Lotion", "350ml", "瓶", "外用冲洗 qd", "中成药-妇科", "OTC甲", 20.00))
A(drug("乌鸡白凤丸", "Wuji Baifeng Pills", "9g×10丸", "盒", "口服 1丸 bid", "中成药-妇科", "OTC甲", 25.00))
A(drug("益母草颗粒", "Yimucao Granules", "15g×10袋", "盒", "口服 15g bid", "中成药-妇科", "OTC甲", 12.00))
A(drug("妇科千金片", "Fuke Qianjin Tablets", "0.32g×144片", "盒", "口服 6片 tid", "中成药-妇科", "OTC甲", 28.00))
A(drug("逍遥丸", "Xiaoyao Pills", "6g×10袋", "盒", "口服 6g bid", "中成药-妇科", "OTC甲", 14.00))

# ── 儿科用药 ──
A(drug("小儿氨酚黄那敏颗粒", "Pediatric Paracetamol and Artificial Cow-bezoar Granules", "6g×10袋", "盒", "口服 1袋 tid", "儿科-感冒", "OTC甲", 8.00))
A(drug("小儿豉翘清热颗粒", "Xiaoer Chiqiao Qingre Granules", "2g×9袋", "盒", "口服 1袋 tid", "中成药-儿科", "OTC甲", 22.00))
A(drug("小儿肺热咳喘口服液", "Xiaoer Feire Kechuan Oral Liquid", "10ml×10支", "盒", "口服 10ml tid", "中成药-儿科", "OTC甲", 25.00))
A(drug("小儿化痰止咳颗粒", "Xiaoer Huatan Zhike Granules", "5g×10袋", "盒", "口服 5g tid", "中成药-儿科", "OTC甲", 10.00))
A(drug("小儿健脾散", "Xiaoer Jianpi Powder", "1.5g×10袋", "盒", "口服 1.5g bid", "中成药-儿科", "OTC甲", 12.00))
A(drug("妈咪爱散", "Medilac-Vita Powder", "1g×15袋", "盒", "口服 1g bid", "儿科-微生态", "OTC甲", 18.00))
A(drug("蒙脱石散(儿童)", "Montmorillonite Powder (Pediatric)", "3g×10袋", "盒", "口服 1袋 tid", "儿科-止泻", "OTC甲", 12.00))
A(drug("小儿退热贴", "Pediatric Cooling Patch", "4贴", "盒", "外用 qd", "儿科-物理降温", "OTC乙", 10.00))

# ── 中成药（补充分类） ──
A(drug("连花清瘟胶囊", "Lianhua Qingwen Capsules", "0.35g×24粒", "盒", "口服 4粒 tid", "中成药-感冒", "OTC甲", 14.00, reorder=20))
A(drug("板蓝根颗粒", "Banlangen Granules", "10g×20袋", "盒", "口服 10g tid", "中成药-感冒", "OTC甲", 12.00))
A(drug("感冒灵颗粒", "Ganmaoling Granules", "10g×9袋", "盒", "口服 10g tid", "中成药-感冒", "OTC甲", 11.00))
A(drug("维C银翘片", "Vitamin C Yinqiao Tablets", "24片", "盒", "口服 2片 tid", "中成药-感冒", "OTC甲", 6.00))
A(drug("双黄连口服液", "Shuanghuanglian Oral Liquid", "10ml×10支", "盒", "口服 20ml tid", "中成药-感冒", "OTC甲", 15.00))
A(drug("蒲地蓝消炎口服液", "Pudilan Xiaoyan Oral Liquid", "10ml×12支", "盒", "口服 10ml tid", "中成药-消炎", "Rx", 25.00))
A(drug("蓝芩口服液", "Lanqin Oral Liquid", "10ml×12支", "盒", "口服 10ml tid", "中成药-咽炎", "OTC甲", 22.00))
A(drug("金莲花颗粒", "Jinlianhua Granules", "8g×9袋", "盒", "口服 8g tid", "中成药-咽炎", "OTC甲", 16.00))
A(drug("肺力咳合剂", "Feilike Mixture", "150ml", "瓶", "口服 20ml tid", "中成药-咳嗽", "OTC甲", 20.00))
A(drug("急支糖浆", "Jizhi Syrup", "200ml", "瓶", "口服 20ml tid", "中成药-咳嗽", "OTC甲", 15.00))
A(drug("川贝枇杷膏", "Chuanbei Pipa Gao", "345g", "瓶", "口服 15ml tid", "中成药-咳嗽", "OTC甲", 25.00))
A(drug("京都念慈菴蜜炼川贝枇杷膏", "Nin Jiom Pei Pa Koa", "150ml", "瓶", "口服 15ml tid", "中成药-咳嗽", "OTC甲", 28.00))
A(drug("牛黄解毒片", "Niuhuang Jiedu Tablets", "0.25g×60片", "瓶", "口服 3片 tid", "中成药-清热解毒", "OTC甲", 8.00))
A(drug("黄连上清片", "Huanglian Shangqing Tablets", "0.3g×48片", "盒", "口服 4片 tid", "中成药-清热解毒", "OTC甲", 9.00))
A(drug("三黄片", "Sanhuang Tablets", "0.25g×100片", "瓶", "口服 4片 tid", "中成药-清热解毒", "OTC甲", 7.00))
A(drug("六味地黄丸", "Liuwei Dihuang Pills", "9g×10丸", "盒", "口服 1丸 bid", "中成药-滋阴", "OTC甲", 14.00))
A(drug("知柏地黄丸", "Zhibai Dihuang Pills", "9g×10丸", "盒", "口服 1丸 bid", "中成药-滋阴", "OTC甲", 15.00))
A(drug("杞菊地黄丸", "Qiju Dihuang Pills", "9g×10丸", "盒", "口服 1丸 bid", "中成药-滋阴", "OTC甲", 14.00))
A(drug("归脾丸", "Guipi Pills", "9g×10丸", "盒", "口服 1丸 tid", "中成药-补益", "OTC甲", 13.00))
A(drug("补中益气丸", "Buzhong Yiqi Pills", "6g×10袋", "盒", "口服 6g bid", "中成药-补益", "OTC甲", 15.00))
A(drug("金匮肾气丸", "Jinkui Shenqi Pills", "6g×10袋", "盒", "口服 6g bid", "中成药-温阳", "OTC甲", 16.00))
A(drug("血府逐瘀胶囊", "Xuefu Zhuyu Capsules", "0.4g×36粒", "盒", "口服 6粒 bid", "中成药-活血", "OTC甲", 22.00))
A(drug("舒筋活血片", "Shujin Huoxue Tablets", "0.3g×100片", "瓶", "口服 5片 tid", "中成药-骨伤", "OTC甲", 10.00))
A(drug("云南白药胶囊", "Yunnan Baiyao Capsules", "0.25g×16粒", "盒", "口服 2粒 tid", "中成药-骨伤", "OTC甲", 18.00))
A(drug("正骨水", "Zhenggushui", "45ml", "瓶", "外用 qid", "中成药-骨伤", "OTC甲", 15.00))
A(drug("伤湿止痛膏", "Shangshi Zhitong Plaster", "8贴", "盒", "外用 qd", "中成药-骨伤", "OTC甲", 10.00))
A(drug("安神补脑液", "Anshen Bunao Liquid", "10ml×10支", "盒", "口服 10ml bid", "中成药-安神", "OTC甲", 20.00))
A(drug("枣仁安神胶囊", "Zaoren Anshen Capsules", "0.45g×25粒", "盒", "口服 5粒 qn", "中成药-安神", "OTC甲", 24.00))
A(drug("保和丸", "Baohe Pills", "6g×10袋", "盒", "口服 6g bid", "中成药-消食", "OTC甲", 12.00))
A(drug("牛黄上清丸", "Niuhuang Shangqing Pills", "6g×10丸", "盒", "口服 1丸 bid", "中成药-清热", "OTC甲", 11.00))
A(drug("清开灵颗粒", "Qingkailing Granules", "3g×12袋", "盒", "口服 3g tid", "中成药-清热", "OTC甲", 15.00))
A(drug("康复新液", "Kangfuxin Liquid", "100ml", "瓶", "口服 10ml tid", "中成药-黏膜修复", "OTC甲", 28.00))
A(drug("六神丸", "Liushen Pills", "3.125g×10支", "盒", "含服 10粒 tid", "中成药-咽喉", "OTC甲", 18.00))
A(drug("藿香正气水", "Huoxiangzhengqi Tincture", "10ml×10支", "盒", "口服 10ml bid", "中成药-暑湿", "OTC甲", 10.00))
A(drug("正柴胡饮颗粒", "Zhengchaihuyin Granules", "10g×10袋", "盒", "口服 10g tid", "中成药-感冒", "OTC甲", 14.00))

# ── 维生素矿物质营养 ──
A(drug("维生素C片", "Vitamin C Tablets", "0.1g×100片", "瓶", "口服 0.1g tid", "维生素", "OTC乙", 3.00, reorder=15))
A(drug("维生素B2片", "Vitamin B2 Tablets", "5mg×100片", "瓶", "口服 5mg tid", "维生素", "OTC乙", 3.50))
A(drug("维生素B6片", "Vitamin B6 Tablets", "10mg×100片", "瓶", "口服 10mg tid", "维生素", "OTC乙", 3.00))
A(drug("维生素E软胶囊", "Vitamin E Soft Capsules", "0.1g×60粒", "瓶", "口服 0.1g bid", "维生素", "OTC乙", 12.00))
A(drug("复合维生素B片", "Compound Vitamin B Tablets", "100片", "瓶", "口服 2片 tid", "维生素", "OTC乙", 5.00))
A(drug("21金维他", "21 Super-Vita", "60片", "瓶", "口服 1片 qd", "维生素-多维", "OTC甲", 28.00))
A(drug("葡萄糖酸锌口服液", "Zinc Gluconate Oral Solution", "10ml×20支", "盒", "口服 10ml bid", "矿物质", "OTC甲", 18.00))
A(drug("葡萄糖酸钙口服液", "Calcium Gluconate Oral Solution", "10ml×20支", "盒", "口服 10ml bid", "矿物质", "OTC甲", 20.00))
A(drug("琥珀酸亚铁片", "Ferrous Succinate Tablets", "0.1g×24片", "盒", "口服 1片 tid", "矿物质-补铁", "Rx", 15.00))
A(drug("叶酸片", "Folic Acid Tablets", "5mg×100片", "瓶", "口服 5mg tid", "维生素", "OTC甲", 6.00))
A(drug("多维元素片(21)", "Multivitamin Tablets(21)", "60片", "瓶", "口服 1片 qd", "维生素-多维", "OTC甲", 25.00))
A(drug("口服补液盐Ⅲ", "Oral Rehydration Salts Ⅲ", "5.125g×6袋", "盒", "冲服 250ml prn", "电解质", "OTC甲", 10.00))

# ── 其他 ──
A(drug("碘伏消毒液", "Povidone Iodine Solution", "100ml:0.5g", "瓶", "外用 prn", "消毒防腐", "OTC乙", 5.00))
A(drug("75%乙醇消毒液", "75% Alcohol Disinfectant", "500ml", "瓶", "外用 prn", "消毒防腐", "OTC乙", 8.00))
A(drug("过氧化氢溶液", "Hydrogen Peroxide Solution", "100ml:3g", "瓶", "外用 prn", "消毒防腐", "OTC甲", 3.00))
A(drug("医用纱布块", "Medical Gauze", "8cm×10cm×5片", "包", "外用 prn", "医用耗材", "其他", 3.00))
A(drug("医用胶带", "Medical Tape", "1.25cm×9.1m", "卷", "外用 prn", "医用耗材", "其他", 4.00))
A(drug("一次性使用无菌注射器", "Disposable Sterile Syringe", "5ml×10支", "包", "肌注 prn", "医用耗材", "其他", 6.00))
A(drug("人血白蛋白", "Human Albumin", "10g:50ml", "瓶", "静滴 prn", "血液制品", "Rx", 420.00))
A(drug("破伤风抗毒素", "Tetanus Antitoxin", "1500IU:1ml", "支", "肌注 1500IU", "生物制品", "Rx", 25.00))
A(drug("狂犬病疫苗", "Rabies Vaccine", "0.5ml×4支", "盒", "肌注 0.5ml d0/3/7/14", "生物制品", "Rx", 350.00))
A(drug("注射用青霉素钠", "Penicillin G Sodium for Injection", "80万U", "支", "肌注 80万U bid", "抗感染-注射", "Rx", 2.50, ab=1, contra="青霉素类"))
A(drug("硫酸庆大霉素注射液", "Gentamicin Sulfate Injection", "8万U:2ml", "支", "肌注 8万U bid", "抗感染-注射", "Rx", 1.80, ab=1, contra="氨基糖苷类"))
A(drug("地塞米松磷酸钠注射液", "Dexamethasone Sodium Phosphate Injection", "5mg:1ml", "支", "肌注 5mg qd", "激素-注射", "Rx", 1.50))
A(drug("维生素B12注射液", "Vitamin B12 Injection", "0.5mg:1ml", "支", "肌注 0.5mg qd", "维生素-注射", "Rx", 1.20))
A(drug("盐酸肾上腺素注射液", "Epinephrine Hydrochloride Injection", "1mg:1ml", "支", "皮下/肌注 0.5mg prn", "急救药品", "Rx", 5.00))
A(drug("硝酸甘油注射液", "Nitroglycerin Injection", "5mg:1ml", "支", "静滴 prn", "急救药品", "Rx", 8.00))
A(drug("阿托品注射液", "Atropine Injection", "0.5mg:1ml", "支", "肌注 0.5mg prn", "急救药品", "Rx", 2.00))
A(drug("氯解磷定注射液", "Pralidoxime Chloride Injection", "0.5g:2ml", "支", "静注 prn", "急救药品-解毒", "Rx", 10.00))
A(drug("纳洛酮注射液", "Naloxone Injection", "0.4mg:1ml", "支", "静注 0.4mg prn", "急救药品-解毒", "Rx", 15.00))

print(f"药物清单: {len(DRUGS)} 种")

# ────────────────────────────────────────────────────────────
# 2. 300 名虚拟患者
# ────────────────────────────────────────────────────────────
SURNAMES = list("赵钱孙李周吴郑王冯陈褚卫蒋沈韩杨朱秦尤许何吕施张孔曹严华金魏陶姜"
                "戚谢邹喻柏水窦章云苏潘葛奚范彭郎鲁韦昌马苗凤花方俞任袁柳酆鲍史唐"
                "费廉岑薛雷贺倪汤滕殷罗毕郝邬安常乐于时傅皮卞齐康伍余元卜顾孟平黄"
                "和穆萧尹姚邵湛汪祁毛禹狄米贝明臧计伏成戴谈宋茅庞熊纪舒屈项祝董梁"
                "杜阮蓝闵席季麻强贾路娄危江童颜郭梅盛林刁钟徐邱骆高夏蔡田樊胡凌霍"
                "虞万支柯昝管卢莫经房裘缪干解应宗丁宣贲邓郁单杭洪包诸左石崔吉钮龚程"
                "嵇邢滑裴陆荣翁荀羊於惠甄曲家封芮羿储靳汲邴糜松井段富巫乌焦巴弓牧隗"
                "山谷车侯宓蓬全郗班仰秋仲伊宫宁仇栾暴甘钭厉戎祖武符刘景詹束龙叶幸司")
GIVEN_M = ["伟","刚","勇","毅","俊","峰","强","军","平","保","东","文","辉","力","明","永","健","世","广","志",
           "义","兴","良","海","山","仁","波","宁","贵","福","生","龙","元","全","国","胜","学","祥","才","发",
           "武","新","利","清","飞","彬","富","顺","信","子","杰","涛","昌","成","康","星","光","天","达",
           "安","岩","中","茂","进","林","有","坚","和","彪","博","诚","先","敬","震","振","壮","会","思",
           "群","豪","心","邦","承","乐","绍","功","松","善","厚","庆","磊","民","友","裕","河","哲","江","超"]
GIVEN_F = ["秀","娟","英","华","慧","巧","美","娜","静","淑","惠","珠","翠","雅","芝","玉","萍","红","娥","玲",
           "芬","芳","燕","彩","春","菊","兰","凤","洁","梅","琳","素","云","莲","真","环","雪","荣","爱","妹",
           "霞","香","月","莺","媛","艳","瑞","凡","佳","嘉","琼","勤","珍","贞","莉","桂","娣","叶","璧","璐",
           "娅","琦","晶","妍","茜","秋","珊","莎","锦","黛","青","倩","婷","姣","婉","娴","瑾","颖","露","瑶",
           "怡","婵","雁","蓓","纨","仪","荷","丹","蓉","眉","君","琴","蕊","薇","菁","梦","岚","苑","筠","柔"]

def gen_name(rng):
    """生成不重复的 2-3 字姓名"""
    while True:
        surname = rng.choice(SURNAMES)
        if rng.random() < 0.25:
            given = rng.choice(GIVEN_M if rng.random() < 0.5 else GIVEN_F)
            name = surname + given
        else:
            g1 = rng.choice(GIVEN_M + GIVEN_F)
            g2 = rng.choice(GIVEN_M + GIVEN_F)
            name = surname + g1 + g2
        if len(set(name)) >= 2:  # 避免"李李李"类
            return name

def gen_phone(rng, used):
    prefix = rng.choice(["139","138","137","136","135","150","151","152","158","159"])
    while True:
        p = prefix + "".join(str(rng.randint(0,9)) for _ in range(8))
        if p not in used:
            used.add(p)
            return p

ALLERGY_POOL = ["青霉素类","头孢类","磺胺类","喹诺酮类","氨基糖苷类","NSAIDs","海鲜","花粉","尘螨","阿司匹林"]
HISTORY_POOL = ["高血压病史","糖尿病史","冠心病史","慢性胃炎史","过敏性鼻炎史","哮喘史","高脂血症史","痛风史","甲状腺功能减退史","慢性支气管炎史"]
CHRONIC_POOL = ["高血压","糖尿病","冠心病","高脂血症","慢性胃炎","哮喘","痛风","甲状腺功能减退"]
TAG_POOL = ["高血压随访","糖尿病随访","慢病随访","孕妇","老年人随访","儿童保健","术后随访","过敏体质"]

PATIENTS = []
rng = random.Random(20260905)
used_names = set()
used_phones = set()
for i in range(300):
    name = gen_name(rng)
    while name in used_names:
        name = gen_name(rng)
    used_names.add(name)
    gender = "男" if rng.random() < 0.47 else "女"
    # 出生年份分布：以中老年为主（诊所常见人群）
    y = rng.choices(
        [rng.randint(1940,1959), rng.randint(1960,1979), rng.randint(1980,1999), rng.randint(2000,2023)],
        weights=[18,32,32,18])[0]
    m = rng.randint(1,12); d = rng.randint(1,28)
    dob = date(y, m, d)
    phone = gen_phone(rng, used_phones)
    # 过敏史：15% 有
    allergies = ""
    if rng.random() < 0.15:
        k = rng.randint(1,2)
        allergies = "、".join(rng.sample(ALLERGY_POOL, k))
    # 慢病：40% 有 1-2 个
    chronic = ""
    if rng.random() < 0.40:
        k = rng.randint(1,2)
        chronic = ",".join(rng.sample(CHRONIC_POOL, k))
    # 病史：与慢病对应
    history = ""
    if chronic:
        tags = [t for t in HISTORY_POOL if t.replace("病史","") in chronic]
        history = "；".join(tags) if tags else ""
    # 标签
    tags = ""
    if chronic:
        t = [c+"随访" for c in chronic.split(",")]
        tags = ",".join(t)
    elif gender == "女" and 20 <= (2026 - y) <= 40 and rng.random() < 0.3:
        tags = "孕妇" if rng.random() < 0.5 else "妇科随访"
    # 体征
    weight = round(rng.uniform(45, 95), 1)
    temperature = round(rng.uniform(36.0, 37.0), 1)
    sbp = rng.randint(105, 165); dbp = rng.randint(65, 100); hr = rng.randint(60, 100)
    PATIENTS.append({
        "id": i+1, "name": name, "gender": gender, "dob": dob, "phone": phone,
        "allergies": allergies, "history": history, "chronic_tags": chronic,
        "tags": tags, "weight": weight, "temperature": temperature,
        "sbp": sbp, "dbp": dbp, "hr": hr, "age": 2026 - y,
    })
print(f"患者: {len(PATIENTS)} 人")

# ────────────────────────────────────────────────────────────
# 3. 就诊数据（主诉/诊断/处方/医嘱/收费）
# 诊断模板: (ICD代码, 诊断名, 主诉, 体征, 医嘱模板, 常用药类别索引)
# ────────────────────────────────────────────────────────────
def pick(rng, seq, k=1):
    return rng.sample(seq, k) if len(seq) > 1 else [seq[0]]*k

DIAGNOSES = [
    # (icd, name, chief_complaint, note_template, drug_class_keywords)
    ("J06.9", "急性上呼吸道感染", "咳嗽、咽痛伴流涕{day}天", "多饮水、注意休息，清淡饮食，避免劳累；症状加重或发热持续超过3天请复诊。", ["呼吸","抗感染","中成药-感冒","中成药-咳嗽"]),
    ("J02.9", "急性咽炎", "咽部疼痛、异物感{day}天，吞咽时加重", "少说话，多饮温水，可用淡盐水漱口；3天后无好转请复诊。", ["五官","中成药-咽炎","抗感染"]),
    ("J03.9", "急性扁桃体炎", "发热伴咽痛{day}天，吞咽困难", "注意休息，流质饮食；若出现呼吸困难立即就诊。", ["抗感染","解热镇痛","中成药-消炎"]),
    ("J18.9", "急性支气管炎", "咳嗽{day}天，咳黄痰，无胸痛", "戒烟酒，避免辛辣刺激；观察痰色变化，1周后复诊。", ["呼吸-镇咳","呼吸-祛痰","抗感染"]),
    ("J30.4", "过敏性鼻炎", "阵发性喷嚏、流清涕{day}天，晨起加重", "避免接触过敏原，注意保暖；症状控制后维持用药1周。", ["抗过敏","五官-鼻激素","五官-鼻减充血"]),
    ("J45.9", "支气管哮喘（轻度）", "反复喘息{day}天，夜间明显", "规律吸入用药，避免接触冷空气及过敏原；随身携带急救药物。", ["呼吸-平喘","呼吸-抗过敏"]),
    ("I10", "高血压病", "头晕{day}天，自测血压偏高", "低盐低脂饮食，规律服药，每日晨起监测血压并记录；1月后复诊调整方案。", ["心血管-降压CCB","心血管-降压ARB","心血管-降压ACEI","心血管-β受体阻滞剂"]),
    ("E11.9", "2型糖尿病", "多饮多尿{day}天，空腹血糖偏高", "糖尿病饮食，规律运动，监测空腹及餐后血糖；每3个月复查糖化血红蛋白。", ["内分泌-降糖"]),
    ("E78.5", "高脂血症", "体检发现血脂偏高{day}月", "低脂饮食、控制体重、规律运动；服用调脂药期间监测肝功能。", ["心血管-他汀","心血管-降脂贝特"]),
    ("K29.7", "慢性胃炎", "上腹隐痛、嗳气{day}天，餐后明显", "规律三餐、少食多餐，忌辛辣油腻及浓茶咖啡；幽门螺杆菌阳性者规范治疗。", ["消化-抑酸","消化-胃黏膜保护","消化-促动力"]),
    ("K21.0", "胃食管反流病", "反酸烧心{day}天，夜间加重", "睡前3小时不进食，抬高床头；避免过饱及高脂饮食。", ["消化-抑酸","消化-促动力"]),
    ("K52.9", "急性胃肠炎", "腹痛腹泻{day}天，每日{times}次，无脓血便", "清淡饮食、补充水分，腹泻停止后注意肠道菌群恢复；出现血便或高热立即就诊。", ["消化-止泻","消化-微生态","抗感染-其他","电解质"]),
    ("K59.0", "功能性便秘", "排便困难{day}天，大便干结", "增加膳食纤维和饮水量，适当运动，养成定时排便习惯。", ["消化-通便","中成药-消食"]),
    ("M54.9", "腰肌劳损", "腰部酸痛{day}天，久坐后加重", "避免久坐及弯腰负重，局部热敷；加强腰背肌锻炼。", ["中成药-骨伤","解热镇痛","维生素"]),
    ("M25.5", "骨关节炎", "膝关节疼痛{day}天，上下楼梯加重", "减少负重活动，注意关节保暖；肥胖者建议减重。", ["解热镇痛","内分泌-钙剂","中成药-骨伤"]),
    ("M10.9", "痛风性关节炎", "足趾关节红肿热痛{day}天，夜间发作", "急性期减少活动，多饮水（每日2000ml以上），忌海鲜啤酒动物内脏。", ["解热镇痛-痛风","内分泌-痛风","内分泌-痛风碱化"]),
    ("L20.8", "湿疹", "皮肤红斑丘疹伴瘙痒{day}天", "避免搔抓、热水烫洗，保湿护肤；瘙痒严重时口服抗过敏药。", ["皮肤-糖皮质激素","皮肤-止痒收敛","抗过敏-抗组胺"]),
    ("L30.0", "接触性皮炎", "接触{day}天后皮肤起疹伴痒", "避免再次接触致敏物，急性期冷湿敷。", ["皮肤-糖皮质激素","抗过敏-抗组胺"]),
    ("L03.0", "皮肤软组织感染", "皮肤红肿疼痛{day}天，局部皮温升高", "保持局部清洁干燥，勿挤压；出现发热或红肿扩大立即就诊。", ["皮肤-外用抗菌","抗感染","解热镇痛"]),
    ("B35.9", "体癣（真菌感染）", "躯干环状红斑伴瘙痒{day}天", "坚持用药至少2周，内衣煮沸消毒，避免与家人共用毛巾。", ["皮肤-抗真菌"]),
    ("H10.9", "急性结膜炎", "眼红、分泌物增多{day}天", "注意手卫生，毛巾单独使用；两眼分开滴药，滴药前后洗手。", ["五官-抗菌眼"]),
    ("H66.9", "急性中耳炎", "耳痛{day}天，伴听力下降", "避免耳内进水，感冒期间勿用力擤鼻；耳痛加重或流脓立即就诊。", ["五官-抗菌耳","抗感染","解热镇痛"]),
    ("J32.9", "慢性鼻窦炎", "鼻塞流脓涕{day}天，伴头痛", "鼻部热敷、盐水洗鼻；坚持用药2周后复诊。", ["五官-鼻激素","抗感染"]),
    ("N39.0", "泌尿系感染", "尿频尿急尿痛{day}天", "多饮水勤排尿，注意会阴部卫生；症状无缓解需复查尿常规。", ["抗感染-喹诺酮类","抗感染-硝基咪唑类"]),
    ("N76.0", "阴道炎", "外阴瘙痒、白带增多{day}天", "治疗期间避免同房，内衣勤换烫洗；伴侣有症状需同治。", ["妇科-阴道炎","中成药-妇科"]),
    ("G47.0", "失眠", "入睡困难{day}天，夜醒多次", "规律作息、睡前避免兴奋刺激；药物短期使用，避免长期依赖。", ["中成药-安神","神经-镇静催眠","神经-植物神经"]),
    ("R51", "头痛（紧张性）", "双侧头部胀痛{day}天，劳累后加重", "避免长时间用眼和伏案，适度活动颈肩部。", ["解热镇痛","神经-偏头痛","中成药-神经"]),
    ("R50.9", "发热待查", "发热{day}天，最高{temp}℃", "多饮水、监测体温；发热超过3天或伴皮疹、抽搐立即就诊。", ["解热镇痛","抗感染","中成药-感冒"]),
    ("D64.9", "缺铁性贫血（轻度）", "乏力头晕{day}天，面色苍白", "加强营养，多食红肉及动物肝脏；服用铁剂后大便变黑属正常现象。", ["矿物质-补铁","维生素"]),
    ("E55.9", "维生素D缺乏", "体检发现25-羟维生素D偏低", "增加户外活动，补充维生素D制剂，3个月后复查。", ["内分泌-维生素D","内分泌-钙剂"]),
]

def compute_qty(dose_val, freq, days):
    """按系统口径：数量 = 单次剂量 × 频次 × 天数（粗略，测试用）
    数量上限 999：对齐系统明细数量校验（AddPrescriptionItem 数量不能超过 999），
    避免出现如口服补液盐III 单张 7000 盒等不现实数量。"""
    freq_map = {"tid":3, "bid":2, "qd":1, "qn":1, "qid":4, "q4h":6, "hs":1}
    f = freq_map.get(freq, 1)
    q = max(1, round(dose_val * f * days))
    return min(q, 999)

VISITS = []
VISIT_ITEMS = []
rx_id = 1
for p in PATIENTS:
    n_visits = rng.choices([1,2,3], weights=[55,35,10])[0]
    visit_dates = sorted(rng.sample(range(1, 360), n_visits))
    for v_idx, days_ago in enumerate(visit_dates):
        # 就诊日期：过去一年内
        vdate = date(2026, 9, 5) - timedelta(days=days_ago)
        diag = rng.choice(DIAGNOSES)
        icd, dname, chief_tpl, note_tpl, kw = diag
        day = rng.randint(1, 14)
        chief = chief_tpl.replace("{day}", str(day)).replace("{times}", str(rng.randint(3,8)))
        if "{temp}" in chief:
            chief = chief.replace("{temp}", f"{rng.uniform(37.5, 39.5):.1f}")
        # 体征：随就诊实时
        weight = round(p["weight"] + rng.uniform(-2, 2), 1)
        temp = round(rng.uniform(36.2, 38.8), 1)
        sbp = rng.randint(100, 170); dbp = rng.randint(60, 105); hr = rng.randint(60, 105)
        # 处方明细：1-4 种药，从诊断对应类别选
        drug_candidates = [d for d in DRUGS if any(k in d[12] for k in kw)]
        if not drug_candidates:
            drug_candidates = DRUGS
        n_items = rng.choices([1,2,3,4], weights=[30,40,22,8])[0]
        chosen = pick(rng, drug_candidates, min(n_items, len(drug_candidates)))
        items = []
        for d in chosen:
            # 根据类别推断用法
            usage = d[4]
            if "tid" in usage: freq = "tid"
            elif "bid" in usage: freq = "bid"
            elif "qd" in usage: freq = "qd"
            elif "qn" in usage: freq = "qn"
            else: freq = "bid"
            days = rng.choices([3,5,7,10,14,30], weights=[15,20,30,15,10,10])[0]
            dose_val = 1
            # 简单解析剂量
            import re
            m = re.search(r"(\d+(?:\.\d+)?)\s*(g|mg|μg|ml|粒|片|丸|袋|支|揿)", d[4])
            if m:
                dose_val = float(m.group(1))
                unit = m.group(2)
            else:
                unit = "片"
            qty = compute_qty(dose_val, freq, days)
            price = float(d[10])
            subtotal = round(price * qty, 2)
            items.append({
                "drug_cn": d[0], "spec": d[2], "unit": d[3],
                "dose": dose_val, "dose_unit": unit, "freq": freq,
                "route": "口服" if "口" in d[4] else ("外用" if "外" in d[4] else "口服"),
                "days": days, "qty": qty, "unit_price": price, "subtotal": subtotal
            })
        total = round(sum(i["subtotal"] for i in items), 2)
        # 状态分布：最近的就诊更可能完成全流程
        p_complete = 0.75 if days_ago < 90 else (0.5 if days_ago < 200 else 0.3)
        r = rng.random()
        if r < p_complete:
            status = "已发药"
        elif r < p_complete + 0.12:
            status = "已收费"
        elif r < p_complete + 0.22:
            status = "已审核"
        elif r < p_complete + 0.28:
            status = "已保存"
        else:
            status = "草稿"
        # 医嘱
        note = note_tpl
        if "高血压" in dname:
            note += "血压控制目标：<140/90mmHg。"
        elif "糖尿病" in dname:
            note += "空腹血糖目标：4.4-7.0mmol/L。"
        # 处方号
        no_year = f"{vdate.year}-{rx_id:04d}"
        VISITS.append({
            "rx_id": rx_id, "patient_id": p["id"], "patient_name": p["name"],
            "date": vdate, "no": no_year,
            "chief": chief, "diag_code": icd, "diag": dname,
            "weight": weight, "temp": temp, "sbp": sbp, "dbp": dbp, "hr": hr,
            "note": note, "status": status, "total": total,
            "type": "普通处方",
            "payment": ("现金" if rng.random() < 0.65 else "POS") if status in ("已收费","已发药") else "",
            "pos_serial": f"POS{rx_id:06d}" if status in ("已收费","已发药") and rng.random() < 0.35 else "",
        })
        for it in items:
            VISIT_ITEMS.append({**it, "rx_id": rx_id})
        rx_id += 1

print(f"就诊处方: {len(VISITS)} 条，明细 {len(VISIT_ITEMS)} 条")

# ────────────────────────────────────────────────────────────
# 输出 CSV
# ────────────────────────────────────────────────────────────
def write_csv(name, header, rows):
    path = os.path.join(OUT_DIR, name)
    with open(path, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows)
    print(f"已生成: {path} ({len(rows)} 行)")

write_csv("drugs.csv",
    ["generic_name_cn","generic_name_en","spec","unit","default_usage",
     "is_antibiotic","antibiotic_level","is_toxic_drug","contraindication_tags",
     "cost_price_ref","retail_price_ref","reorder_level","drug_class","otc_type"],
    DRUGS)

write_csv("patients.csv",
    ["id","name","gender","dob","phone","allergies","history","chronic_tags",
     "tags","weight_kg","temperature_c","sbp","dbp","heart_rate","age"],
    [[p["id"], p["name"], p["gender"], p["dob"].isoformat(), p["phone"],
      p["allergies"], p["history"], p["chronic_tags"], p["tags"],
      p["weight"], p["temperature"], p["sbp"], p["dbp"], p["hr"], p["age"]] for p in PATIENTS])

write_csv("prescriptions.csv",
    ["rx_id","prescription_no","patient_id","patient_name","visit_date",
     "chief_complaint","diagnosis_code","diagnosis","weight","temperature","sbp","dbp","heart_rate",
     "status","total_amount","prescription_type","advice","payment_method","pos_serial_no"],
    [[v["rx_id"], v["no"], v["patient_id"], v["patient_name"], v["date"].isoformat(),
      v["chief"], v["diag_code"], v["diag"], v["weight"], v["temp"], v["sbp"], v["dbp"], v["hr"],
      v["status"], f"{v['total']:.2f}", v["type"], v["note"], v["payment"], v["pos_serial"]] for v in VISITS])

write_csv("prescription_items.csv",
    ["rx_id","drug_name","spec","unit","dose","dose_unit","frequency","route","duration_days","qty","unit_price","subtotal"],
    [[i["rx_id"], i["drug_cn"], i["spec"], i["unit"], i["dose"], i["dose_unit"],
      i["freq"], i["route"], i["days"], i["qty"], f"{i['unit_price']:.2f}", f"{i['subtotal']:.2f}"] for i in VISIT_ITEMS])

# 状态统计
from collections import Counter
print("处方状态分布:", dict(Counter(v["status"] for v in VISITS)))
print("全部数据生成完成。")
