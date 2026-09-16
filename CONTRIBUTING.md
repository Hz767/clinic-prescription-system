# 贡献指南

感谢你为陈医生诊所处方系统做出贡献！本文档定义了项目的开发流程、代码规范和协作方式。

- **仓库地址**：https://github.com/Hz767/clinic-prescription-system
- **问题反馈**：在仓库提交 Issue 时请附上：复现步骤、期望行为、实际行为、错误日志（`crash_*.log` / `run-*.log`）

## 目录

- [开发环境准备](#开发环境准备)
- [开发流程](#开发流程)
- [Git 工作流](#git-工作流)
- [代码规范](#代码规范)
- [安全与合规](#安全与合规)
- [关键业务约束](#关键业务约束)
- [提交规范](#提交规范)
- [代码审查](#代码审查)
- [发布流程](#发布流程)

---

## 开发环境准备

### 必备工具

| 工具 | 版本要求 | 说明 |
|------|---------|------|
| .NET SDK | 9.0.x | 目标框架 net9.0 / net9.0-windows |
| Git | 2.40+ | 版本控制 |
| Visual Studio 2022 | 17.12+ | 推荐IDE（需安装.NET桌面开发 workload） |
| 或 Rider | 2024.3+ | 可选IDE |

### 可选工具

| 工具 | 用途 |
|------|------|
| llama.cpp | 本地AI辅助功能（可选） |
| DB Browser for SQLite | 数据库查看 |

### 首次搭建

```bash
# 1. 克隆仓库
git clone <repository-url>
cd 个人诊所处方系统

# 2. 还原依赖
dotnet restore Clinic.sln

# 3. 构建
dotnet build Clinic.sln

# 4. 运行测试
dotnet test tests/Clinic.UnitTests/Clinic.UnitTests.csproj

# 5. 运行程序
dotnet run --project src/Clinic.Presentation/Clinic.Presentation.csproj
```

### 登录账号

- 用户名：`admin`
- 密码：`admin123`

> 种子账户默认标记 `MustChangePassword`，首次登录后必须立即修改密码，严禁将默认密码用于生产环境。

---

## 开发流程

### 标准开发步骤

```
1. 从 main 创建功能分支
      ↓
2. 开发（编码 + 单元测试）
      ↓
3. 本地验证（构建 + 测试 + 手动测试）
      ↓
4. 提交（符合提交规范）
      ↓
5. 推送分支 + 创建 Pull Request
      ↓
6. 代码审查（至少1人审批）
      ↓
7. 合并到 main
      ↓
8. 删除功能分支
```

### 详细说明

#### 1. 创建分支

```bash
git checkout main
git pull origin main
git checkout -b feat/your-feature-name
```

#### 2. 开发

- 遵循[代码规范](docs/coding-standards.md)
- 新功能必须编写单元测试
- 修改业务逻辑必须更新或新增测试

#### 3. 本地验证（提交前必须执行）

```bash
# 构建（必须0错误0警告）
dotnet build Clinic.sln --configuration Release

# 单元测试（必须全部通过）
dotnet test tests/Clinic.UnitTests/Clinic.UnitTests.csproj

# 手动测试关键流程
# - 登录 → 患者管理 → 处方开具 → 收费 → 发药
```

#### 4. 提交

遵循[提交规范](#提交规范)。

#### 5. 创建PR

- PR标题使用与提交相同的格式
- PR描述必须包含：变更内容、测试方式、关联Issue
- 至少1人审查通过后才能合并

---

## Git 工作流

### 分支策略

| 分支 | 用途 | 命名规范 | 生命周期 |
|------|------|---------|---------|
| `main` | 主分支，始终保持可发布状态 | - | 永久 |
| `feat/*` | 新功能开发 | `feat/简短描述` | 合并后删除 |
| `fix/*` | Bug修复 | `fix/简短描述` | 合并后删除 |
| `hotfix/*` | 线上紧急修复 | `hotfix/简短描述` | 合并后删除 |
| `refactor/*` | 重构 | `refactor/简短描述` | 合并后删除 |
| `docs/*` | 文档更新 | `docs/简短描述` | 合并后删除 |

### 分支命名示例

```
feat/ai-medical-record-assistant
fix/prescription-amount-calculation
refactor/prescription-viewmodel-split
docs/development-workflow
hotfix/login-crash
```

### 提交规范（Conventional Commits）

```
<type>(<scope>): <subject>

<body>

<footer>
```

#### Type 类型

| 类型 | 说明 |
|------|------|
| `feat` | 新功能 |
| `fix` | Bug修复 |
| `docs` | 文档变更 |
| `style` | 代码格式（不影响功能） |
| `refactor` | 重构（既不是新功能也不是修bug） |
| `perf` | 性能优化 |
| `test` | 测试相关 |
| `build` | 构建系统或依赖变更 |
| `ci` | CI配置变更 |
| `chore` | 杂项（不修改src或test） |

#### Scope（可选）

影响范围，如：`prescription`、`billing`、`ui`、`database`、`ai`

#### 示例

```
feat(prescription): 添加诊疗费字段并修复金额计算

- Prescription实体添加ConsultationFee字段
- PrescriptionItem实体添加PackQuantity字段
- 后端金额计算改为按整盒计价
- 保存处方时重新计算明细金额

修复 #123
```

```
fix(ui): 修复性别下拉框无法选择问题

ComboBox模板缺少ToggleButton导致下拉按钮不可点击
```

### 合并策略

- **Squash Merge**：功能分支合并到main时使用压缩合并，保持main历史整洁
- 合并前必须通过CI检查（构建+测试）
- 合并后删除功能分支

---

## 代码规范

详见 [docs/coding-standards.md](docs/coding-standards.md)

### 核心原则

1. **架构分层**：严格遵循Clean Architecture，依赖方向不可反向
2. **命名清晰**：使用有意义的名称，不使用缩写（除非是通用缩写）
3. **单一职责**：每个类/方法只做一件事
4. **异常处理**：不吞异常，使用ILogger记录
5. **可测试性**：依赖注入，面向接口编程

### 架构依赖规则

```
Presentation → Application → Domain ← Infrastructure
                    ↓
                  Shared
```

- Domain层：零外部依赖
- Application层：依赖Domain，不依赖Infrastructure
- Infrastructure层：实现Domain/Application定义的接口
- Presentation层：只调用Application层服务

---

## 安全与合规

> 本项目处理患者医疗数据，安全合规为最高优先级，违反以下约定将阻止合并。

1. **密钥外置**：加密密钥与 Pepper 存放于 gitignored 的 `secrets/` 目录（`encryption.key` / `pepper.key`），**严禁**硬编码进 `appsettings.json` 或任何源代码
2. **禁止提交敏感信息**：数据库（`*.db`）、密钥（`*.key`）、日志（`*.log`）、备份（`Backups/`）均已被 `.gitignore` 排除，提交前用 `git status` 复查
3. **权限在应用层强制**：所有写操作必须在 Application 层服务入口执行权限检查（`IPermissionChecker`）；UI 层隐藏按钮仅为 UX，不构成安全边界
4. **审计日志**：所有系统操作写入 AuditLog，`PayloadHash + PrevHash + HashChain` 构成防篡改哈希链，业务修改不得绕过审计
5. **加密与哈希**：患者敏感字段使用 AES-GCM 加密，密码使用 PBKDF2 + Pepper 哈希，不得自行实现加密算法或降低强度

---

## 关键业务约束

> 修改以下业务逻辑前必读，违规修改即使编译通过也会被审查拒绝。

1. **金额按整盒计价**：药品金额 = `ceil(数量 / 包装数量) × 单价`；总金额 = 明细合计 + 诊疗费（ConsultationFee）；金额一律使用 `decimal`，四舍五入 `MidpointRounding.AwayFromZero`
2. **保存处方时重算金额**：服务端必须重新计算所有明细 Subtotal，不信任前端传入值
3. **库存并发**：所有库存操作（入库/出库/扣减/退库）必须使用共享互斥锁 `InventoryLock.Instance`，事务隔离级别 `Serializable`（SQLite: BEGIN IMMEDIATE）
4. **FIFO 扣减**：处方扣库存按有效期优先（FIFO），库存不足必须阻断保存并回滚全部变更
5. **处方规则**：处方药品 ≤ 5 种；时限 急诊3天/常规7天/慢病84天；保存时执行过敏史匹配检查（命中则阻断并告警）
6. **收费冲正**：已收费处方作废必须生成冲正支付记录（`IsReversal` 标记）并在同一事务内回退库存
7. **事务边界在应用层**：Application 层负责事务管理，禁止在 Presentation 层直接操作 DbContext 或仓储

---

## 代码审查

### 审查清单

- [ ] 构建通过，无警告
- [ ] 单元测试全部通过
- [ ] 新功能有对应测试
- [ ] 命名清晰，符合规范
- [ ] 没有硬编码的魔法数字/字符串
- [ ] 异常处理合理，无空catch块
- [ ] 没有敏感信息（密码、密钥）提交
- [ ] 数据库变更有迁移脚本
- [ ] 文档已更新（如需要）

### 审查重点

1. **业务逻辑正确性**：金额计算、状态流转、权限检查
2. **安全性**：SQL注入、XSS、敏感数据加密
3. **性能**：N+1查询、内存泄漏
4. **可维护性**：代码可读性、复杂度

---

## 发布流程

### 版本号规范

遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)：

```
主版本号.次版本号.修订号
  │       │       │
  │       │       └─ 向后兼容的问题修正
  │       └───────── 向后兼容的功能性新增
  └───────────────── 不兼容的API修改
```

### 发布步骤

1. 更新 `Directory.Build.props` 中的版本号
2. 更新 `CHANGELOG.md`
3. 更新 `README.md` 中的版本信息
4. 提交：`chore(release): v1.2.0`
5. 创建Tag：`git tag v1.2.0`
6. 推送：`git push origin main --tags`
7. 在GitHub创建Release

---

## 常见问题

### Q: 可以直接提交到main吗？
A: 不可以。所有变更必须通过PR合并到main。

### Q: 数据库结构变更怎么处理？
A: 在 `ClinicDbContext` 中修改实体，然后编写迁移脚本。修改数据库结构必须在PR描述中说明。

### Q: 可以提交数据库文件吗？
A: 不可以。`*.db` 已在 `.gitignore` 中排除。测试数据通过 `DbSeeder` 初始化。

### Q: AI辅助功能需要本地大模型吗？
A: 不需要。AI功能是可选的，没有llama.cpp时系统使用NoOpLlmService，不影响核心功能。
