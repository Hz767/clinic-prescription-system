# AI智能问诊功能 — 开发计划

> **目标**：实现医生与患者自然对话，AI自动录音转写并生成结构化病历，减少医生文书工作时间。
>
> **架构**：独立Agent服务（.NET 9）+ 全本地部署 + 复用现有llama.cpp
>
> **预计工期**：4周（MVP 2周 + 优化 2周）

---

## 一、技术选型

### 1.1 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│              诊所系统 (WPF, .NET 9)                          │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  PrescriptionView / MedicalRecordView               │    │
│  │  - 开始/停止问诊按钮                                 │    │
│  │  - 实时转写显示（可选）                              │    │
│  │  - AI病历草稿预览/确认/修改                          │    │
│  └───────────────────────┬─────────────────────────────┘    │
│                          │ HTTP (127.0.0.1:5080)            │
└──────────────────────────┼──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│              AI问诊Agent服务 (独立进程, .NET 9)               │
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │  录音模块     │→ │  ASR模块     │→ │  LLM结构化提取    │  │
│  │  (NAudio)    │  │  (FunASR-    │  │  (llama.cpp      │  │
│  │  WAV/PCM     │  │   llama.cpp) │  │   HTTP API)      │  │
│  └──────────────┘  └──────────────┘  └────────┬─────────┘  │
│                                                │            │
│  ┌──────────────┐  ┌──────────────┐           │            │
│  │ 说话人分离    │← │  VAD静音检测 │←───────────┘            │
│  │ (音量+时间戳) │  │ (内置)       │                        │
│  └──────────────┘  └──────────────┘                        │
│                                                              │
│  配置：config.json (端口、模型路径、热词表)                   │
└──────────────────────────────────────────────────────────────┘
                           │
                           │ HTTP (127.0.0.1:8080)
                           ▼
              ┌──────────────────────┐
              │ llama-server (已有)   │
              │ qwen2.5-7b-instruct   │
              └──────────────────────┘
```

### 1.2 技术栈选择

| 模块 | 技术选型 | 选择理由 |
|------|---------|---------|
| Agent服务 | .NET 9 Console/Worker | 与主项目统一，无需Python环境，部署简单 |
| 录音 | NAudio | .NET生态成熟，支持WASAPI回环和麦克风采集 |
| ASR | FunASR-llama.cpp (Paraformer) | 纯C++，与llama.cpp同生态，中文准确率高，22倍实时 |
| VAD | 内置能量检测 / Silero VAD ONNX | 轻量，无需Python |
| 说话人分离 | 简化方案（音量+时间戳） | MVP阶段够用，避免pyannote的Python依赖和1.5GB内存 |
| LLM | 复用现有llama.cpp (qwen2.5-7b) | 零额外内存，同一实例服务AI辅助和问诊 |
| 通信 | HTTP REST (ASP.NET Core Minimal API) | 简单可靠，易于调试 |
| 音频格式 | WAV (16kHz, 16bit, mono) | ASR标准输入格式 |

### 1.3 为什么不用Python？

- 用户Python版本是3.14，太新，FunASR/pyannote等库可能不兼容
- 避免额外的Python环境依赖和包管理复杂性
- .NET与主项目统一，维护成本低
- FunASR已提供llama.cpp版本（纯C++），无需Python

---

## 二、项目结构

### 2.1 新增项目

```
Clinic.sln
├── src/
│   ├── Clinic.AiScribe/          ← 新增：AI问诊Agent服务
│   │   ├── AiScribe.csproj
│   │   ├── Program.cs            ← 入口（ASP.NET Core Minimal API）
│   │   ├── appsettings.json      ← 配置
│   │   ├── Services/
│   │   │   ├── IAudioRecorder.cs     ← 录音接口
│   │   │   ├── AudioRecorder.cs      ← NAudio录音实现
│   │   │   ├── IAsrService.cs        ← ASR接口
│   │   │   ├── FunAsrService.cs      ← FunASR-llama.cpp实现
│   │   │   ├── ILlmService.cs        ← LLM接口
│   │   │   ├── LlamaCppService.cs    ← llama.cpp实现（复用）
│   │   │   ├── ISpeakerDiarizer.cs   ← 说话人分离接口
│   │   │   ├── SimpleDiarizer.cs     ← 简化实现（音量+时间戳）
│   │   │   └── MedicalRecordExtractor.cs  ← 病历结构化提取
│   │   ├── Models/
│   │   │   ├── TranscriptSegment.cs  ← 转写片段（含说话人）
│   │   │   ├── MedicalRecordDraft.cs ← 病历草稿
│   │   │   └── ScribeSession.cs      ← 问诊会话状态
│   │   └── Endpoints/
│   │       └── ScribeEndpoints.cs    ← HTTP API端点
│   │
│   ├── Clinic.Presentation/      ← 修改：集成AI问诊
│   │   ├── Services/
│   │   │   └── IAiScribeClient.cs    ← 新增：Agent客户端
│   │   │   └── AiScribeClient.cs     ← 新增：HTTP客户端实现
│   │   ├── ViewModels/
│   │   │   └── PrescriptionViewModel.cs  ← 修改：添加问诊命令
│   │   └── Views/
│   │       └── PrescriptionView.xaml    ← 修改：添加问诊UI
│   │
│   └── ... (其他项目不变)
```

### 2.2 数据库变更

无需新增表。AI生成的病历草稿通过现有 `MedicalRecord` 表存储，增加一个字段标记来源：

```sql
ALTER TABLE medical_record ADD COLUMN source TEXT DEFAULT 'manual';
-- source: 'manual'（手动）/ 'ai_scribe'（AI问诊生成）
```

---

## 三、API设计

### 3.1 Agent服务HTTP API

| 方法 | 端点 | 说明 |
|------|------|------|
| POST | `/api/scribe/start` | 开始录音问诊 |
| POST | `/api/scribe/stop` | 停止录音，返回转写文本 |
| POST | `/api/scribe/generate` | 根据转写文本生成结构化病历 |
| GET | `/api/scribe/status` | 获取当前会话状态 |
| GET | `/api/scribe/transcript` | 获取实时转写（轮询） |
| POST | `/api/scribe/cancel` | 取消当前问诊 |

### 3.2 请求/响应示例

**开始问诊**
```json
POST /api/scribe/start
{
  "patientId": 123,
  "doctorId": 1,
  "expectedDuration": 600
}
Response: { "sessionId": "uuid", "status": "recording" }
```

**停止问诊**
```json
POST /api/scribe/stop
{ "sessionId": "uuid" }
Response: {
  "sessionId": "uuid",
  "duration": 320,
  "transcript": [
    { "speaker": "doctor", "start": 0, "end": 5, "text": "你哪里不舒服？" },
    { "speaker": "patient", "start": 6, "end": 15, "text": "我头疼三天了..." }
  ],
  "fullText": "医生：你哪里不舒服？患者：我头疼三天了..."
}
```

**生成病历**
```json
POST /api/scribe/generate
{ "sessionId": "uuid" }
Response: {
  "chiefComplaint": "头痛3天",
  "presentIllness": "患者3天前无明显诱因出现头痛...",
  "pastHistory": "高血压病史5年",
  "allergies": "青霉素过敏",
  "vitals": { "temperature": 36.8, "systolicBP": 140, "diastolicBP": 90, "heartRate": 78 },
  "diagnosis": "高血压病",
  "treatmentPlan": "建议监测血压，调整用药...",
  "confidence": 0.85,
  "warnings": ["血压值未在对话中明确提及，为推断值"]
}
```

---

## 四、分阶段开发计划

### 阶段一：MVP核心功能（第1-2周）

#### 任务1：创建Agent服务项目（1天）
- [ ] 创建 `Clinic.AiScribe` 项目（.NET 9 Worker Service）
- [ ] 添加到解决方案
- [ ] 配置依赖（NAudio、ASP.NET Core）
- [ ] 实现基础HTTP服务框架
- [ ] 编写 `appsettings.json` 配置

#### 任务2：录音模块（2天）
- [ ] 实现 `IAudioRecorder` 接口
- [ ] 使用NAudio实现麦克风采集
- [ ] 输出WAV格式（16kHz, 16bit, mono）
- [ ] 实现VAD静音检测（能量阈值法）
- [ ] 录音文件临时存储和清理
- [ ] 单元测试：录音启动/停止/取消

#### 任务3：ASR模块（3天）
- [ ] 下载FunASR-llama.cpp模型（Paraformer中文模型，约200MB）
- [ ] 实现 `IAsrService` 接口
- [ ] 集成FunASR-llama.cpp命令行调用
- [ ] 实现WAV文件转文字
- [ ] 处理长音频分段（>30秒自动切分）
- [ ] 添加医学热词表（药品名、疾病名，提升识别准确率）
- [ ] 单元测试：ASR转写准确率测试

#### 任务4：说话人分离（简化版）（1天）
- [ ] 实现 `ISpeakerDiarizer` 接口
- [ ] 简化方案：基于音量阈值区分（医生麦克风音量通常更高）
- [ ] 基于时间戳分段
- [ ] 允许用户在UI上手动调整说话人标签
- [ ] 单元测试：分段逻辑测试

#### 任务5：LLM病历结构化提取（2天）
- [ ] 实现 `ILlmService` 接口（复用llama.cpp HTTP API）
- [ ] 设计病历提取Prompt模板
- [ ] 实现JSON输出解析
- [ ] 实现 `MedicalRecordExtractor` 服务
- [ ] 处理LLM输出异常和重试
- [ ] 置信度评估和警告标记
- [ ] 单元测试：Prompt模板测试、JSON解析测试

#### 任务6：HTTP API端点（1天）
- [ ] 实现 `/api/scribe/start` 端点
- [ ] 实现 `/api/scribe/stop` 端点
- [ ] 实现 `/api/scribe/generate` 端点
- [ ] 实现 `/api/scribe/status` 端点
- [ ] 会话状态管理（内存存储，支持并发）
- [ ] 全局异常处理和日志

#### 任务7：WPF前端集成（2天）
- [ ] 实现 `IAiScribeClient` HTTP客户端
- [ ] PrescriptionView添加"AI问诊"按钮
- [ ] 录音状态指示（录音中/转写中/生成中）
- [ ] 转写文本显示窗口
- [ ] AI病历草稿预览对话框
- [ ] 医生确认/修改后保存到病历
- [ ] 患者知情同意弹窗

#### 阶段一验收标准
- [ ] 点击"开始问诊"后正常录音
- [ ] 点击"停止问诊"后5秒内返回转写文本
- [ ] 转写文本中文准确率 > 85%（安静环境）
- [ ] 点击"生成病历"后10秒内返回结构化病历
- [ ] 病历包含：主诉、现病史、诊断、治疗建议
- [ ] 医生可修改病历并保存
- [ ] Agent服务崩溃不影响主程序运行
- [ ] 无llama.cpp时给出友好提示

---

### 阶段二：优化与增强（第3周）

#### 任务8：实时转写（2天）
- [ ] 实现流式ASR（边录边转）
- [ ] WebSocket或长轮询实时推送
- [ ] 转写文本实时显示在UI上
- [ ] 延迟 < 3秒

#### 任务9：医学热词增强（1天）
- [ ] 从药品目录提取9270种药品名作为热词
- [ ] 从ICD诊断提取常见疾病名
- [ ] 热词动态加载和更新
- [ ] 热词识别准确率对比测试

#### 任务10：体征自动提取（1天）
- [ ] 从对话中识别体温、血压、心率等数值
- [ ] 正则表达式+LLM双重提取
- [ ] 提取结果高亮显示，医生可修正
- [ ] 体征自动填入处方页面

#### 任务11：说话人分离优化（2天）
- [ ] 引入声纹注册功能（医生录制10秒声纹）
- [ ] 基于声纹的说话人识别
- [ ] 双麦克风阵列支持（可选）
- [ ] 准确率对比测试

#### 任务12：病历质量检查（1天）
- [ ] AI生成病历的完整性检查
- [ ] 医学术语规范性检查
- [ ] 缺失字段提醒
- [ ] 病历模板匹配

#### 阶段二验收标准
- [ ] 实时转写延迟 < 3秒
- [ ] 药品名识别准确率 > 95%
- [ ] 体征数值提取准确率 > 90%
- [ ] 说话人分离准确率 > 80%（声纹模式）

---

### 阶段三：完善与高级功能（第4周）

#### 任务13：处方建议（2天）
- [ ] 从病历诊断自动推荐常用药品
- [ ] 基于历史处方的个性化推荐
- [ ] 药品相互作用提醒
- [ ] 医生一键添加到处方

#### 任务14：问诊统计与反馈（1天）
- [ ] 问诊时长统计
- [ ] AI修改率统计（医生修改了多少内容）
- [ ] 识别准确率反馈收集
- [ ] Dashboard展示

#### 任务15：录音管理（1天）
- [ ] 录音文件加密存储
- [ ] 自动清理策略（默认7天）
- [ ] 录音回放功能
- [ ] 录音与病历关联

#### 任务16：性能优化与内存管理（1天）
- [ ] ASR模型按需加载/卸载
- [ ] 内存使用监控
- [ ] 低内存模式自动降级
- [ ] 长时间问诊稳定性测试

#### 阶段三验收标准
- [ ] 处方推荐命中率 > 60%
- [ ] 内存占用峰值 < 12GB（含llama.cpp）
- [ ] 连续问诊10次无崩溃
- [ ] 录音文件自动清理正常

---

## 五、LLM Prompt设计

### 5.1 病历提取Prompt模板

```
你是一位专业的门诊病历整理助手。请根据以下医患对话，提取结构化的门诊病历信息。

【医患对话】
{transcript}

【要求】
1. 只提取对话中明确提到的信息，不要编造
2. 对话中未提及的字段留空
3. 体征数值（体温、血压、心率）必须是对话中明确说出的数字
4. 诊断应使用规范的医学术语
5. 输出严格的JSON格式，不要有其他文字

【输出格式】
{
  "chiefComplaint": "主诉（主要症状+持续时间）",
  "presentIllness": "现病史（起病情况、症状特点、诊疗经过）",
  "pastHistory": "既往史（既往疾病、手术史、外伤史）",
  "allergies": "过敏史（药物、食物过敏）",
  "personalHistory": "个人史（吸烟、饮酒、职业等）",
  "familyHistory": "家族史",
  "vitals": {
    "temperature": null,
    "systolicBP": null,
    "diastolicBP": null,
    "heartRate": null,
    "weight": null
  },
  "physicalExam": "体格检查发现",
  "diagnosis": "初步诊断",
  "treatmentPlan": "治疗建议和医嘱",
  "medicationSuggestions": ["建议药品1", "建议药品2"],
  "confidence": 0.0,
  "warnings": ["需要医生确认的内容"]
}
```

### 5.2 Prompt优化策略

1. **Few-shot示例**：在Prompt中加入1-2个完整的对话→病历示例
2. **医学术语约束**：明确要求使用ICD-10标准诊断名
3. **数值提取规则**：明确体温单位（摄氏度）、血压单位（mmHg）
4. **空值处理**：未提及的字段必须为null，不能猜测
5. **置信度评估**：要求AI对提取结果的可信度打分

---

## 六、配置文件设计

### 6.1 Agent服务配置 (appsettings.json)

```json
{
  "Urls": "http://127.0.0.1:5080",
  "Scribe": {
    "SampleRate": 16000,
    "BitsPerSample": 16,
    "Channels": 1,
    "MaxSessionDuration": 1800,
    "AudioTempPath": "audio_temp/",
    "AutoCleanupDays": 7
  },
  "Asr": {
    "Enabled": true,
    "Engine": "funasr-llama.cpp",
    "ModelPath": "models/paraformer-zh.gguf",
    "FunAsrBinaryPath": "tools/funasr-llama.cpp/funasr-cli.exe",
    "HotWordsPath": "config/hotwords.txt",
    "Language": "zh"
  },
  "Llm": {
    "Enabled": true,
    "Endpoint": "http://127.0.0.1:8080/v1/chat/completions",
    "Model": "qwen2.5-7b-instruct",
    "MaxTokens": 2048,
    "Temperature": 0.3,
    "TimeoutSeconds": 60,
    "MaxRetries": 2
  },
  "Diarization": {
    "Enabled": true,
    "Mode": "simple",
    "DoctorVolumeThreshold": -20,
    "MinSegmentDuration": 1.0
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

---

## 七、风险与应对

| 风险 | 概率 | 影响 | 应对措施 |
|------|------|------|---------|
| FunASR-llama.cpp模型下载困难 | 中 | 高 | 准备备用方案：调用云端ASR API（阿里云/腾讯云） |
| ASR识别准确率不达标 | 中 | 高 | 医学热词增强 + 医生手动修正 + 持续优化 |
| 说话人分离准确率低 | 高 | 中 | MVP用简化方案，允许手动调整；阶段二引入声纹 |
| LLM生成病历质量差 | 中 | 高 | Few-shot Prompt优化 + 医生确认环节 + 质量检查 |
| 内存不足导致卡顿 | 中 | 高 | ASR按需加载 + 低内存降级模式 + 内存监控 |
| 录音环境噪声大 | 高 | 中 | 降噪处理 + 推荐使用定向麦克风 |
| 患者不同意录音 | 低 | 中 | 知情同意弹窗 + 可关闭AI问诊功能 |

---

## 八、验收标准汇总

### MVP必须达到
- [ ] 完整流程：开始录音 → 对话 → 停止 → 转写 → 生成病历 → 医生确认 → 保存
- [ ] 安静环境下中文转写准确率 > 85%
- [ ] 病历生成时间 < 10秒
- [ ] 医生可修改AI生成的所有字段
- [ ] Agent服务独立运行，崩溃不影响主程序
- [ ] 无LLM时可降级为纯转写模式

### 性能指标
- [ ] 内存占用：Agent服务 < 2GB（不含llama.cpp）
- [ ] 转写速度：RTF < 0.5（10分钟音频5分钟内转完）
- [ ] 启动时间：Agent服务 < 5秒
- [ ] 并发支持：同时1个问诊会话（单医生场景）

### 安全合规
- [ ] 录音文件本地存储，不上传
- [ ] 患者知情同意机制
- [ ] 录音自动清理
- [ ] AI生成病历标记来源
- [ ] 医生确认后才保存

---

## 九、开发任务分配（单人开发）

| 周次 | 任务 | 工作量 | 交付物 |
|------|------|--------|--------|
| 第1周 | 任务1-3：项目框架+录音+ASR | 5天 | 可运行的Agent服务，录音转写可用 |
| 第2周 | 任务4-7：说话人分离+LLM+API+前端 | 5天 | MVP完整流程可用 |
| 第3周 | 任务8-12：实时转写+热词+体征+优化 | 5天 | 优化版本，准确率提升 |
| 第4周 | 任务13-16：处方建议+统计+性能 | 5天 | 完善版本，可正式使用 |

---

## 十、后续扩展方向

1. **中药处方支持**：识别中医对话，生成中药处方
2. **多语言支持**：粤语、方言识别
3. **云端同步**：多设备间病历同步（可选）
4. **语音播报**：AI朗读医嘱给患者
5. **质控分析**：基于问诊数据的医疗质量分析
6. **医保对接**：自动生成医保结算所需数据
