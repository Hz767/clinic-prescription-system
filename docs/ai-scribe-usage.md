# AI智能问诊功能 - 使用说明

## 功能概述

AI智能问诊功能通过语音拾音，自动记录医患对话，并使用AI生成结构化病历草稿，减少医生手动输入时间。

## 系统架构

```
┌─────────────────┐     HTTP      ┌──────────────────────┐
│  诊所主程序(WPF) │ ◄──────────► │  AI问诊Agent服务      │
│  (端口: 主程序)  │               │  (端口: 5080)        │
└─────────────────┘               └──────────┬───────────┘
                                              │
                              ┌───────────────┼───────────────┐
                              ▼               ▼               ▼
                        ┌──────────┐   ┌──────────┐   ┌──────────┐
                        │ 录音模块  │   │ ASR识别  │   │ LLM病历  │
                        │ (NAudio) │   │(FunASR)  │   │(llama.cpp)│
                        └──────────┘   └──────────┘   └──────────┘
```

## 前置条件

### 1. 启动llama.cpp（必需，用于病历生成）

```bash
D:\llama.cpp\b10375\llama-server.exe -m D:\llama.cpp\models\qwen2.5-7b-instruct-q4_k_m.gguf --port 8080
```

### 2. 下载FunASR模型（必需，用于语音识别）

1. 访问 https://github.com/mightychaos/funasr-llama.cpp/releases
2. 下载 `paraformer-zh.gguf`（中文语音识别模型，约200MB）
3. 下载 `funasr-cli.exe`（Windows可执行文件）
4. 放置到以下目录：
   ```
   src/Clinic.AiScribe/tools/funasr-llama.cpp/funasr-cli.exe
   src/Clinic.AiScribe/models/paraformer-zh.gguf
   ```

### 3. 启动AI问诊Agent服务

双击运行 `start-ai-scribe.bat`，或手动执行：

```bash
dotnet run --project src/Clinic.AiScribe/Clinic.AiScribe.csproj
```

服务启动后监听 `http://127.0.0.1:5080`

## 使用流程

### 步骤1：选择患者
在处方开具页面搜索并选择患者。

### 步骤2：开始问诊
点击顶部患者信息条中的 **"🎙 开始问诊"** 按钮。
- 按钮变为红色 **"⏹ 停止并生成病历"**
- 显示录音状态指示器（红点闪烁 + 计时）

### 步骤3：正常问诊
医生与患者自然对话，系统自动录音。
- 建议：问诊时语速适中，避免多人同时说话
- 问诊中可随时说出体征数据（如"体温38.5度"、"血压120/80"）

### 步骤4：停止并生成病历
点击 **"⏹ 停止并生成病历"** 按钮。
系统自动执行：
1. 停止录音
2. ASR语音转文字
3. 说话人分离（医生/患者）
4. LLM提取结构化病历
5. 显示病历草稿预览

### 步骤5：核对并应用
AI生成的病历草稿显示在页面上，包含：
- 主诉
- 现病史
- 诊断
- 治疗建议
- 置信度
- 警告信息（需要医生确认的内容）

点击 **"应用到处方"** 按钮，将主诉和诊断自动填充到处方表单。

## API端点

| 方法 | 端点 | 说明 |
|------|------|------|
| GET | `/api/scribe/health` | 健康检查 |
| POST | `/api/scribe/start` | 开始问诊 |
| POST | `/api/scribe/stop` | 停止问诊并转写 |
| POST | `/api/scribe/generate` | 生成结构化病历 |
| GET | `/api/scribe/status/{id}` | 获取会话状态 |
| POST | `/api/scribe/cancel/{id}` | 取消问诊 |

## 配置说明

配置文件：`src/Clinic.AiScribe/appsettings.json`

```json
{
  "Urls": "http://127.0.0.1:5080",
  "Asr": {
    "ModelPath": "models/paraformer-zh.gguf",
    "FunAsrBinaryPath": "tools/funasr-llama.cpp/funasr-cli.exe"
  },
  "Llm": {
    "Endpoint": "http://127.0.0.1:8080/v1/chat/completions",
    "Model": "qwen2.5-7b-instruct"
  }
}
```

## 降级策略

- **无ASR模型**：录音功能可用，但无法转写
- **无LLM服务**：转写可用，但无法生成结构化病历
- **Agent服务未启动**：主程序正常运行，仅AI问诊按钮不显示

## 注意事项

1. **隐私保护**：录音文件保存在 `audio_temp/` 目录，建议定期清理
2. **AI仅供参考**：所有AI生成内容必须由医生核对确认后才能使用
3. **麦克风权限**：首次使用时Windows可能请求麦克风权限
4. **内存需求**：全本地运行建议16GB以上内存
