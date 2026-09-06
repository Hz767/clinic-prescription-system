# 开发流程文档

> 本文档定义了项目从需求到发布的完整开发流程，确保工程质量和可追溯性。

## 目录

- [需求管理](#需求管理)
- [任务分解](#任务分解)
- [开发阶段](#开发阶段)
- [测试阶段](#测试阶段)
- [代码审查](#代码审查)
- [发布流程](#发布流程)
- [缺陷管理](#缺陷管理)
- [文档维护](#文档维护)

---

## 需求管理

### 需求来源

1. **用户反馈**：实际使用中发现的问题和改进建议
2. **业务需求**：诊所运营流程优化
3. **技术债务**：代码重构、性能优化
4. **合规要求**：医疗行业规范更新

### 需求记录

所有需求必须记录为Issue，包含：

- **标题**：简洁描述需求
- **描述**：详细说明背景、目标、验收标准
- **优先级**：P0（紧急）/ P1（高）/ P2（中）/ P3（低）
- **类型**：feature / bug / refactor / docs / chore
- **关联**：相关Issue或PR

### 优先级定义

| 优先级 | 定义 | 响应时间 |
|--------|------|---------|
| P0 | 系统崩溃、数据丢失、核心功能不可用 | 立即修复 |
| P1 | 核心功能有缺陷但有 workaround、严重影响使用 | 24小时内 |
| P2 | 非核心功能缺陷、体验优化 | 本周内 |
| P3 | 锦上添花的改进、长期优化 | 排期处理 |

---

## 任务分解

### WBS分解原则

每个需求必须分解为可独立完成的子任务：

```
需求（Issue）
├── 子任务1：数据库设计/迁移
├── 子任务2：领域层修改（实体/接口）
├── 子任务3：应用层修改（服务/DTO/验证）
├── 子任务4：基础设施层修改（仓储/外部服务）
├── 子任务5：表现层修改（View/ViewModel）
├── 子任务6：单元测试
└── 子任务7：文档更新
```

### 任务粒度

- 每个子任务应在 **1-4小时** 内完成
- 超过8小时的任务必须进一步分解
- 每个子任务对应一个提交（或一组相关提交）

---

## 开发阶段

### 开发前检查

```bash
# 1. 确保本地main是最新的
git checkout main
git pull origin main

# 2. 创建功能分支
git checkout -b feat/your-feature

# 3. 确认能正常构建
dotnet build Clinic.sln
```

### 开发中

1. **小步提交**：每完成一个子任务就提交一次
2. **频繁同步**：每天至少从main合并一次，避免分支过久
3. **持续测试**：每完成一个模块就运行相关测试
4. **及时文档**：接口变更、配置变更同步更新文档

### 开发完成检查清单

- [ ] 功能实现完成
- [ ] 单元测试编写完成且全部通过
- [ ] 代码符合规范（无警告、无硬编码）
- [ ] 异常处理完善（无空catch）
- [ ] 日志记录合理（使用ILogger，无Console.WriteLine）
- [ ] 数据库变更有迁移说明
- [ ] 配置项有默认值和文档说明
- [ ] 手动测试通过（关键流程）

---

## 测试阶段

### 测试金字塔

```
        /\
       /  \        E2E测试（少量）
      /----\
     /      \      集成测试（适量）
    /--------\
   /          \    单元测试（大量）
  /------------\
```

### 单元测试

- **位置**：`tests/Clinic.UnitTests/`
- **框架**：xUnit + Moq + FluentAssertions
- **命名**：`{被测类名}Tests.cs`
- **覆盖率目标**：核心业务逻辑 > 80%

#### 测试命名规范

```
方法名_场景_预期结果

示例：
RecalculateSubtotalOnly_PackQuantityGreaterThanOne_RoundsUpToWholePack
AddPrescriptionItemAsync_InvalidDrugId_ThrowsException
```

#### 测试结构（AAA模式）

```csharp
[Fact]
public async Task SavePrescriptionAsync_WithItems_CalculatesTotalAmount()
{
    // Arrange
    var prescription = new Prescription { ConsultationFee = 10m };
    var items = new List<PrescriptionItem>
    {
        new() { Qty = 9, UnitPrice = 10m, PackQuantity = 12, Subtotal = 10m }
    };

    // Act
    await _service.SavePrescriptionAsync(prescription.Id, null, CancellationToken.None);

    // Assert
    prescription.TotalAmount.Should().Be(20m); // 10药品 + 10诊疗费
}
```

### 集成测试

- **位置**：`tests/Clinic.IntegrationTests/`
- **使用SQLite内存数据库**，不依赖外部服务
- **测试完整的服务层流程**

### 手动测试（提交前必做）

每次提交前必须手动验证以下核心流程：

| 流程 | 验证点 |
|------|--------|
| 登录 | 正确账号登录、错误账号提示、记住密码 |
| 患者管理 | 新建患者、编辑患者、搜索患者 |
| 处方开具 | 选择患者、添加药品、修改剂量、保存处方 |
| 收费管理 | 待收费列表、收费操作、收据生成 |
| 发药管理 | 待发药列表、发药确认、库存扣减 |
| 病历管理 | 新建病历、查看历史病历 |
| 库存管理 | 药品列表、入库、出库 |

---

## 代码审查

### 审查流程

```
开发者创建PR
    ↓
CI自动检查（构建+测试）
    ↓
审查者审查代码
    ↓
开发者修改（如有问题）
    ↓
审查者批准
    ↓
合并到main
```

### 审查者检查清单

#### 正确性
- [ ] 业务逻辑是否正确？
- [ ] 边界条件是否处理？（空值、零、负数、最大值）
- [ ] 并发场景是否安全？
- [ ] 事务是否正确？

#### 安全性
- [ ] 是否有SQL注入风险？
- [ ] 敏感数据是否加密？
- [ ] 权限检查是否到位？
- [ ] 是否有硬编码的密码/密钥？

#### 可维护性
- [ ] 命名是否清晰？
- [ ] 方法是否过长？（>50行需考虑拆分）
- [ ] 类是否承担过多职责？
- [ ] 注释是否准确？（不注释"做了什么"，注释"为什么"）
- [ ] 是否有重复代码？

#### 性能
- [ ] 是否有N+1查询？
- [ ] 是否有不必要的循环嵌套？
- [ ] 大对象是否及时释放？

#### 测试
- [ ] 是否有对应的单元测试？
- [ ] 测试是否覆盖了关键路径？
- [ ] 测试是否独立可重复？

### 审查反馈规范

- **明确指出问题位置**：文件+行号
- **给出改进建议**：不只是说"不好"，要说"怎么改更好"
- **区分严重程度**：
  - 🔴 必须修改（bug、安全问题）
  - 🟡 建议修改（可维护性、性能）
  - 🟢 可选修改（风格、偏好）

---

## 发布流程

### 发布前检查

- [ ] 所有P0/P1的Issue已关闭
- [ ] main分支构建通过
- [ ] 所有测试通过
- [ ] CHANGELOG.md已更新
- [ ] 版本号已更新（Directory.Build.props）
- [ ] 数据库迁移脚本已准备
- [ ] 回滚方案已确认

### 发布步骤

1. **准备发布分支**
```bash
git checkout main
git pull origin main
git checkout -b release/v1.2.0
```

2. **更新版本号**
- 修改 `Directory.Build.props` 中的 `<Version>`
- 修改 `README.md` 中的版本信息

3. **更新CHANGELOG**
```markdown
## [1.2.0] - 2026-09-10

### 新增
- AI智能问诊功能

### 修复
- 处方金额计算错误（#123）
```

4. **提交并创建PR**
```bash
git add .
git commit -m "chore(release): v1.2.0"
git push origin release/v1.2.0
```

5. **合并后打Tag**
```bash
git checkout main
git pull origin main
git tag -a v1.2.0 -m "Release v1.2.0"
git push origin v1.2.0
```

6. **创建GitHub Release**
- 标题：`v1.2.0`
- 内容：CHANGELOG中对应版本的内容
- 附件：编译后的可执行文件（可选）

### 紧急修复（Hotfix）

```bash
# 从main创建hotfix分支
git checkout main
git checkout -b hotfix/critical-bug

# 修复并提交
git commit -m "fix: 修复登录崩溃问题"

# 合并到main并打Tag
git checkout main
git merge hotfix/critical-bug
git tag v1.2.1
git push origin main --tags
```

---

## 缺陷管理

### 缺陷报告规范

每个Bug必须包含：

- **标题**：简洁描述问题
- **复现步骤**：1、2、3...
- **预期结果**：应该是什么
- **实际结果**：实际发生了什么
- **环境**：操作系统、.NET版本、程序版本
- **截图/日志**：如有
- **严重程度**：P0/P1/P2/P3

### 缺陷生命周期

```
新建 → 确认 → 分配 → 修复中 → 待验证 → 已关闭
                ↓
              拒绝（不是bug/重复/无法复现）
```

### 缺陷修复流程

1. 确认Bug可复现
2. 编写失败的测试用例（证明Bug存在）
3. 修复代码
4. 测试通过
5. 提交PR，关联Bug Issue
6. 验证后关闭Issue

---

## 文档维护

### 文档清单

| 文档 | 位置 | 更新时机 |
|------|------|---------|
| README.md | 根目录 | 版本发布、架构变更 |
| CONTRIBUTING.md | 根目录 | 开发流程变更 |
| CHANGELOG.md | 根目录 | 每次发布 |
| 开发流程文档 | docs/development-workflow.md | 流程变更 |
| 代码规范 | docs/coding-standards.md | 规范变更 |
| Git工作流 | docs/git-workflow.md | 分支策略变更 |
| API文档 | docs/api/ | 接口变更 |
| 数据库设计 | docs/database/ | 表结构变更 |

### 文档原则

1. **代码即文档**：优先写清晰的代码和注释
2. **及时更新**：代码变更时同步更新文档
3. **示例优先**：用示例说明，而不是抽象描述
4. **版本对应**：文档与代码版本一致

---

## 附录：常用命令

### 构建
```bash
dotnet build Clinic.sln --configuration Release
```

### 测试
```bash
# 所有测试
dotnet test

# 仅单元测试
dotnet test tests/Clinic.UnitTests/Clinic.UnitTests.csproj

# 带覆盖率
dotnet test --collect:"XPlat Code Coverage"
```

### 清理
```bash
dotnet clean Clinic.sln
```

### 运行
```bash
dotnet run --project src/Clinic.Presentation/Clinic.Presentation.csproj
```

### Git常用
```bash
# 查看状态
git status

# 查看变更
git diff

# 暂存所有
git add .

# 提交
git commit -m "feat: 描述"

# 推送
git push origin branch-name

# 拉取最新
git pull origin main

# 合并main到当前分支
git merge main
```
