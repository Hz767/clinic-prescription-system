# 陈医生诊所处方系统

> 版本：v1.3.0 | 更新日期：2026-09-17

面向个体诊所的处方开具与管理系统，覆盖患者档案、电子处方、库存管理、收费结算、经营报表全流程，内置本地 AI 辅助问诊与病历生成。

## 技术栈

| 组件 | 技术 | 版本 |
|------|------|------|
| 运行时 | .NET | 9.0（SDK 固定 9.0.308，见 global.json） |
| UI 框架 | WPF + MVVM | .NET 9 |
| MVVM 工具 | CommunityToolkit.Mvvm | 8.x |
| ORM | EF Core | 9.0 |
| 数据库 | SQLite (WAL) | — |
| PDF 生成 | QuestPDF | 2026.7.2 |
| 验证 | FluentValidation | 12.1.1 |
| 加密 | AES-GCM + PBKDF2 | 内置 |
| 测试 | xUnit + Moq | — |
| 本地 LLM | llama.cpp（qwen2.5-7b） | 可选 |

## 功能特性

- **患者管理**：联想搜索（姓名/手机号）、快速建档（性别/出生日期/过敏史/基础疾病）、慢性病标签
- **电子处方**：门诊病历一体化、按整盒计价、处方类型（普通/急诊/儿科）、A5 PDF 自动生成
- **处方工作流**：草稿 → 已保存 → 已审核 → 已收费 → 已发药 / 作废，收费支持冲正与库存回退
- **库存管理**：批次入库、FIFO 扣减（有效期优先）、库存不足阻断、近效期预警
- **收费结算**：现金/POS 收费、收费作废冲正、日结报表、药品销量 Top 图表
- **经营报表**：销量排行条形图、按日期范围统计
- **AI 辅助**：本地 llama.cpp 预审 7 项规则（药物相互作用/过敏/剂量/重复/禁忌等）、AI 生成病历、问诊助手
- **医生个性化**：快捷词库、常用药品套餐、默认用药频次/用法/用量、处方模板（按医生本地存储）、一键复制最近处方
- **效率交互**：全局快捷键（Alt+1~7 切换页面）、数字键快速选词、双屏布局（药品选择可分离到副屏）
- **安全合规**：字段级 AES-GCM 加密、PBKDF2 哈希、登录失败锁定、SHA-256 哈希链审计日志、应用层强制权限检查

## 架构

**Clean Architecture Lite**，5 个项目 + 共享包：

```
Clinic.sln
├── src/
│   ├── Clinic.Domain          — 领域层（实体/值对象/领域规则，零外部依赖）
│   ├── Clinic.Application     — 应用层（用例编排/DTO/验证器/权限检查/事务边界）
│   ├── Clinic.Infrastructure  — 基础设施层（EF Core/仓储/加密/备份/LLM）
│   ├── Clinic.Presentation    — 表现层（WPF Views/ViewModels/Styles）
│   └── Clinic.Shared          — 共享包（枚举/常量）
├── tests/
│   ├── Clinic.UnitTests       — 单元测试
│   └── Clinic.IntegrationTests— 集成测试
├── docs/                      — 开发文档与知识库
├── Directory.Build.props      — 版本号统一管理
├── CHANGELOG.md               — 变更日志
└── global.json                — 固定 .NET SDK 版本
```

依赖方向：`Presentation → Application → Domain ← Infrastructure`，`Shared` 被各层共享。

## 快速开始

### 环境要求

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)（SDK 版本由 `global.json` 固定为 9.0.308）
- Windows 10/11（WPF 桌面应用）
- （可选）本地 llama.cpp 服务（`127.0.0.1:8080`，模型 qwen2.5-7b-instruct-q4_k_m.gguf），未启动时 AI 功能自动降级，不影响核心流程

### 构建与运行

```bash
# 编译
dotnet build Clinic.sln

# 运行单元测试
dotnet test tests/Clinic.UnitTests/Clinic.UnitTests.csproj

# 运行集成测试
dotnet test tests/Clinic.IntegrationTests/Clinic.IntegrationTests.csproj

# 运行（开发模式）
dotnet run --project src/Clinic.Presentation/Clinic.Presentation.csproj
```

> 重新编译前若程序正在运行，先停止进程，避免文件锁定：
> `Get-Process -Name "Clinic.Presentation" | Stop-Process -Force`

### 配置文件

`src/Clinic.Presentation/appsettings.json`：

- `Database:Path` — SQLite 数据库路径（默认 `E:\个人诊所处方系统\clinic.db`，绝对路径）
- `Security` — 加密密钥与 Pepper 存于 gitignored `secrets/` 目录，不硬编码于配置
- `Backup:Dir` — 备份输出目录
- `Llm:Enabled` — 是否启用 LLM 辅助
- `Llm:Endpoint` — llama.cpp 服务地址（默认 `http://127.0.0.1:8080`）

### 默认账户

系统启动时自动创建种子数据（**首次登录后请立即修改密码**）：

| 账户 | 密码 | 角色 |
|------|------|------|
| `admin` | `admin123` | 医生（Doctor） |
| `nurse` | `admin123` | 护士（Nurse） |
| `reader` | `admin123` | 只读（Readonly） |

## 核心业务流程

### 处方开具流程
1. 选择/新建患者（联想搜索，支持快速建档与自动建档）
2. 录入就诊体征（体重/体温/血压/心率，两级实时校验）
3. 填写主诉与诊断（支持快捷词库与数字键选词）
4. 添加处方药品（≤5 种，AI 预审 7 项规则，按整盒计价）
5. 保存处方 → 审核 → 收费 → 发药 → 自动生成 A5 PDF

### 收费流程
1. 收费页面显示待收费处方（支持从处方历史直接跳转）
2. 选择支付方式（现金/POS）
3. 确认收费 → 生成收费记录（含冲正标记）
4. 支持已收费处方作废（冲正退款 + 库存回退，事务保证）

### 库存管理
- 药品入库（批次号/有效期/数量），多服务共享互斥锁
- 处方保存时 FIFO 扣减库存（按有效期优先，`Serializable` 事务）
- 库存不足阻断处方保存并回滚
- 近效期药品预警

## 安全特性

- 字段级 AES-GCM 加密（患者敏感信息），密钥存放于 `secrets/`（gitignored）
- PBKDF2 密码哈希 + Pepper
- 连续 5 次登录失败锁定 10 分钟
- SHA-256 哈希链审计日志（防篡改，`PayloadHash + PrevHash + HashChain`）
- Application 层强制权限检查（UI 层隐藏按钮仅为 UX）
- 处方保存时药物过敏史匹配检查（阻断 + 告警）
- 处方时限控制（急诊 3 天 / 常规 7 天 / 慢病 84 天）

## 备份与恢复

- **手动备份**：主界面 → 备份 → 生成 zip（WAL checkpoint + SHA-256 校验）
- **恢复**：主界面 → 恢复 → 校验 SHA-256 → 重启应用生效
- **位置**：`<应用目录>/Backups/backup_<时间戳>.zip`

## 版本管理

版本号定义于 `Directory.Build.props`，遵循语义化版本。变更记录详见 [CHANGELOG.md](CHANGELOG.md)。

## 开发文档

- [贡献指南](CONTRIBUTING.md)
- [开发流程](docs/development-workflow.md)
- [代码规范](docs/coding-standards.md)
- [文件索引](docs/file-index.md)
- [阶段一：交互优化使用说明](docs/阶段一-交互优化使用说明.md)
- [阶段三：个性化配置与双屏布局使用说明](docs/阶段三-个性化配置与双屏布局使用说明.md)
- [药物相互作用知识库](docs/knowledge-base/DDInter药物相互作用知识库.md)
- [处方规范与剂量合理性指南](docs/knowledge-base/处方规范与剂量合理性指南.md)

## 许可

Copyright © 2026 陈医生诊所. All rights reserved.
