# 陈医生诊所处方系统

> 版本：v1.1.0 | 更新日期：2026-08-13

个体诊所处方开具与管理系统，覆盖患者管理、处方开具、库存管理、收费结算全流程。

## 技术栈

| 组件 | 技术 | 版本 |
|------|------|------|
| 运行时 | .NET | 10.0 |
| UI 框架 | WPF | .NET 10 |
| ORM | EF Core | 10.0 |
| 数据库 | SQLite (WAL) | — |
| MVVM | CommunityToolkit.Mvvm | 8.4.0 |
| PDF 生成 | QuestPDF | 2026.7.2 |
| 验证 | FluentValidation | 12.1.1 |
| LLM | Ollama (qwen2.5:7b) | 可选 |

## 项目结构

```
Clinic.sln
├── src/
│   ├── Clinic.Domain          — 领域层（实体/接口/值对象，零外部依赖）
│   ├── Clinic.Application     — 应用层（用例编排/DTO/服务接口/验证器）
│   ├── Clinic.Infrastructure  — 基础设施层（EF Core/加密/仓储/备份/LLM）
│   ├── Clinic.Presentation    — 表现层（WPF Views/ViewModels/Styles）
│   └── Clinic.Shared          — 共享包（枚举/常量）
├── tests/
│   └── Clinic.IntegrationTests — 集成测试
├── docs/                       — 文档（开发日志/知识库/UI设计）
├── Directory.Build.props       — 版本号统一管理
├── CHANGELOG.md                — 变更日志
└── .gitignore
```

## 架构说明

采用 **Clean Architecture Lite** 模式，4 层 + 1 共享包：

- **Domain**：纯 C# 类库，零 NuGet 依赖，包含实体、值对象、领域接口
- **Application**：用例编排、DTO 转换、FluentValidation 输入验证、权限检查
- **Infrastructure**：EF Core DbContext、泛型仓储、AES-GCM 加密、PBKDF2 哈希、备份服务
- **Presentation**：WPF MVVM，ViewModel 仅调用 Application 层服务
- **Shared**：跨层共享的枚举和常量

依赖方向：Presentation → Application → Domain → Shared，Infrastructure 实现 Domain/Application 接口

## 快速开始

### 环境要求

- .NET 10 SDK
- Windows 10/11（WPF 桌面应用）
- （可选）Ollama + qwen2.5:7b 用于 AI 过敏史整理

### 构建与运行

```bash
# 编译
dotnet build Clinic.sln

# 运行（开发模式）
dotnet run --project src/Clinic.Presentation

# 运行集成测试
dotnet test tests/Clinic.IntegrationTests
```

### 配置文件

`src/Clinic.Presentation/appsettings.json`：

- `Database:Path` — SQLite 数据库路径（默认 `E:\个人诊所处方系统\clinic.db`）
- `Security:EncryptionKey` — AES-GCM 加密密钥
- `Encryption:Pepper` — 密码哈希 Pepper
- `Backup:Dir` — 备份输出目录
- `Llm:Enabled` — 是否启用 LLM 辅助
- `Llm:Endpoint` — Ollama 服务地址

### 默认账户

系统启动时自动创建种子数据：
- 医生账户：`doctor` / `doctor123`（Doctor 角色）
- 护士账户：`nurse` / `nurse123`（Nurse 角色）
- 只读账户：`readonly` / `readonly123`（Readonly 角色）

## 核心业务流程

### 处方开具流程
1. 搜索/选择患者（支持联想搜索 + 快速建档）
2. 录入就诊体征（体重/体温/血压/心率，实时验证）
3. 填写主诉与诊断
4. 添加处方药品（≤5 种，AI 预审 7 项规则）
5. 保存处方 → 审核 → 收费 → 生成 PDF

### 收费流程
1. 收费页面显示待收费处方
2. 选择支付方式（现金/POS）
3. 确认收费 → 生成收费记录
4. 支持已收费处方作废（冲正退款 + 库存回退）

### 库存管理
- 药品入库（批次号/有效期/数量）
- 处方保存时 FIFO 扣减库存（按有效期优先）
- 库存不足时阻断处方保存
- 近效期药品预警

## 安全特性

- 字段级 AES-GCM 加密（患者敏感信息）
- PBKDF2 密码哈希 + Pepper
- 5 次登录失败锁定 10 分钟
- SHA-256 哈希链审计日志（防篡改）
- Application 层强制权限检查（UI 层隐藏仅为 UX）
- 处方保存时药物过敏检查（阻断 + 告警）

## 备份与恢复

- **手动备份**：主界面 → 备份按钮 → 生成 zip（WAL checkpoint + SHA-256）
- **恢复**：主界面 → 恢复按钮 → 选择 zip → 校验 SHA-256 → 重启应用
- **备份位置**：`<应用目录>/Backups/backup_<时间戳>.zip`

## 版本管理

版本号定义在 `Directory.Build.props` 中，遵循语义化版本：

- **主版本**：不兼容的 API 修改
- **次版本**：向下兼容的功能新增
- **修订号**：向下兼容的问题修复

变更记录详见 [CHANGELOG.md](CHANGELOG.md)

## 开发文档

- [开发日志](docs/development-log.md) — 14 个开发阶段的详细记录
- [文件索引](docs/file-index.md) — 项目文件结构说明
- [UI 设计系统](docs/ui-design-system.html) — 颜色/字体/组件规范
- [药物相互作用知识库](docs/knowledge-base/DDInter药物相互作用知识库.md)
- [处方规范与剂量合理性指南](docs/knowledge-base/处方规范与剂量合理性指南.md)

## 许可

Copyright © 2026 陈医生诊所. All rights reserved.
