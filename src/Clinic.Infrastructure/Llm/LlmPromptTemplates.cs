namespace Clinic.Infrastructure.Llm;

/// <summary>
/// LLM 提示词模板。
/// 所有模板均要求模型输出纯 JSON，不包含任何额外解释文本。
/// 通过 few-shot 示例引导模型理解输出格式。
/// 
/// 使用 $$""" 原始字符串语法：{ 为字面量，{{ 为插值表达式。
/// </summary>
internal static class LlmPromptTemplates
{
    /// <summary>
    /// 诊断信息结构化提示词。
    /// 将医生自由文本诊断描述转换为结构化 JSON。
    /// </summary>
    public static string DiagnosisPrompt(string freeText) => $$"""
你是一个医疗信息结构化助手。请将以下医生诊断描述整理为结构化 JSON，只输出 JSON，不要包含任何解释。

输入：
{{freeText}}

输出格式（严格 JSON，不要 markdown 代码块）：
{
  "chiefComplaint": "主诉（必填）",
  "signs": "体征描述（如体温、血压等，无则填null）",
  "suspectedDiagnosis": "疑似诊断（无则填null）",
  "suggestedExams": "建议检查项目（无则填null）"
}

示例：
输入："患者咳嗽三天，有黄痰，体温38.5度，咽部充血"
输出：
{"chiefComplaint":"咳嗽伴黄痰3天，发热","signs":"T38.5℃，咽部充血","suspectedDiagnosis":"急性支气管炎","suggestedExams":"血常规、胸部X线"}

现在请处理上述输入，只输出 JSON：
""";

    /// <summary>
    /// 过敏史结构化提示词。
    /// 将自由文本过敏史拆分为药物过敏和非药物过敏标签。
    /// 关键：必须将具体药品名映射到药物类别标签（如"阿莫西林"→"青霉素类"），
    /// 以便系统进行过敏匹配检查。
    /// </summary>
    public static string AllergyPrompt(string freeText) => $$"""
你是一个医疗信息结构化助手。请将以下患者过敏史整理为结构化标签，只输出 JSON，不要包含任何解释。

输入：
{{freeText}}

输出格式（严格 JSON，不要 markdown 代码块）：
{
  "drugAllergyTags": ["药物过敏类别标签列表"],
  "otherAllergyTags": ["非药物过敏标签列表"],
  "reactionDescription": "过敏表现描述（无则填null）"
}

重要规则：
1. drugAllergyTags 必须使用药物类别标签，而非具体药品名：
   - 阿莫西林、氨苄西林 → "青霉素类"
   - 头孢克洛、头孢呋辛 → "头孢类"
   - 磺胺甲噁唑 → "磺胺类"
   - 庆大霉素、阿米卡星 → "氨基糖苷类"
   - 左氧氟沙星、诺氟沙星 → "喹诺酮类"
   - 布洛芬、阿司匹林 → "NSAIDs"
   - 无法归类则保留原药名
2. otherAllergyTags 包含非药物过敏（如"海鲜"、"花粉"、"尘螨"）
3. reactionDescription 描述过敏反应表现（如"皮疹"、"呼吸困难"、"休克"）

示例：
输入："青霉素过敏身上起疹子，吃海鲜也会过敏，磺胺类吃了呼吸困难"
输出：
{"drugAllergyTags":["青霉素类","磺胺类"],"otherAllergyTags":["海鲜"],"reactionDescription":"青霉素类致皮疹，磺胺类致呼吸困难"}

现在请处理上述输入，只输出 JSON：
""";

    /// <summary>
    /// 处方用药建议结构化提示词。
    /// 将口语化用药描述转换为标准化的处方明细项。
    /// </summary>
    public static string PrescriptionPrompt(string freeText) => $$"""
你是一个医疗信息结构化助手。请将以下用药描述整理为标准化处方明细，只输出 JSON，不要包含任何解释。

输入：
{{freeText}}

输出格式（严格 JSON，不要 markdown 代码块）：
{
  "drugName": "药品通用名（必填）",
  "dose": 单次剂量数值（如0.5，无则填null）,
  "doseUnit": "剂量单位（如g、mg、粒、片，无则填null）",
  "frequency": "用药频次（如tid、bid、qd，无则填null）",
  "route": "给药途径（如口服、静脉注射、外用，无则填null）",
  "durationDays": 疗程天数（整数，无则填null）,
  "totalQty": 总量（数值，无则填null）
}

频次映射：
- "一天三次" → "tid"
- "一天两次" → "bid"
- "一天一次" → "qd"
- "睡前" → "hs"

示例：
输入："阿莫西林一天三次一次两粒吃七天"
输出：
{"drugName":"阿莫西林","dose":2,"doseUnit":"粒","frequency":"tid","route":"口服","durationDays":7,"totalQty":42}

示例：
输入："布洛芬片发热时吃一片，一天不超过四次"
输出：
{"drugName":"布洛芬","dose":1,"doseUnit":"片","frequency":"qid prn","route":"口服","durationDays":null,"totalQty":null}

现在请处理上述输入，只输出 JSON：
""";
}