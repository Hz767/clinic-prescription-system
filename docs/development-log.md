# 个体诊所处方系统 — 开发过程文档

> 最后更新：2026-08-13
> 当前版本：v1.0.0
> 当前阶段：界面优化 + 字段完善 + Git 版本控制 + 开发文档完善

---

## 项目概况

- 技术栈：.NET 10 + WPF + EF Core 10 + SQLite (WAL) + CommunityToolkit.Mvvm
- 架构模式：Clean Architecture Lite（4 层 + 1 共享包）
- 解决方案：`Clinic.sln`，包含 5 个项目 + 3 个测试项目（待建）
- 数据库：SQLite，17 张表，字段级 AES-GCM 加密

---

## 阶段一：架构设计与项目搭建

### 完成内容

1. 分析 PRD v2 文档，评估项目难度（综合 3.3/5）
2. 对比 3 个参考项目（ref-hospital-wpf / ref-ddd-vet-sample / ref-medical-cos-his）
3. 选定 Clean Architecture Lite 架构模式，平衡 DDD 隔离与项目复杂度
4. 创建解决方案结构（5 个项目）：

```
Clinic.sln
├── src/
│   ├── Clinic.Domain          — 领域层（实体/接口/值对象，零外部依赖）
│   ├── Clinic.Application     — 应用层（用例编排/DTO/服务接口）
│   ├── Clinic.Infrastructure  — 基础设施层（EF Core/加密/仓储实现）
│   ├── Clinic.Presentation    — 表现层（WPF Views/ViewModels）
│   └── Clinic.Shared          — 共享包（枚举/常量/工具）
```

5. 配置项目引用链：
   - Domain → Shared
   - Application → Domain, Shared
   - Infrastructure → Domain, Application, Shared
   - Presentation → Application, Infrastructure, Shared

### 关键决策

- 选择 Clean Architecture Lite 而非完整 DDD：单人项目不需要多个限界上下文
- Domain 层为纯 C# 类库，零 NuGet 依赖，确保领域逻辑隔离
- ViewModel 只调用 Application 层服务，不直接访问 Infrastructure

### 后期注意事项

- 测试项目（Clinic.Domain.Tests / Clinic.Application.Tests / Clinic.Infrastructure.Tests）尚未创建
- 项目引用链严格遵循依赖方向，反向引用会破坏架构约束
- 若后续需要添加新项目，遵循 `src/` 目录约定

---

## 阶段二：DI 容器与 EF Core DbContext 配置

### 完成内容

1. 创建 `ClinicDbContext`，配置 17 张表的实体映射：
   - 全局 snake_case 命名约定（PascalCase → snake_case）
   - 唯一索引（SysUser.Username、Patient.PhoneHash、Prescription.NoYearSeq 等）
   - 字段长度约束和必填标注
   - 金额字段统一 `DECIMAL(10,2)`

2. 实现泛型仓储 `Repository<T>` 和工作单元 `UnitOfWork`：
   - 仓储自动过滤软删除记录（`DeletedAt == null`）
   - 工作单元管理 EF Core 事务边界（BeginTransaction / Commit / Rollback）

3. 实现安全服务：
   - `AesGcmEncryptionService`：AES-GCM 字段级加密（nonce[12] + tag[16] + ciphertext）
   - `Pbkdf2PasswordHasher`：PBKDF2-HMAC-SHA256 密码哈希（100,000 次迭代）
   - `SystemClock`：可测试的时间注入

4. 配置 DI 容器（`Infrastructure/DependencyInjection.cs`）：
   - SQLite 连接（Scoped）：WAL 模式 + synchronous=NORMAL + foreign_keys=ON
   - DbContext 共享 Scoped 连接
   - 仓储和工作单元（Scoped）
   - 安全服务（Singleton，无状态）

5. 配置 WPF 应用入口（`App.xaml.cs`）：
   - Generic Host 加载 `appsettings.json`
   - 调用 `AddInfrastructure()` + `AddApplication()`
   - 启动时 `EnsureCreatedAsync()` 创建数据库

### 关键决策

- SQLite 连接设为 Scoped 而非 Singleton：确保 PRAGMA 在每个操作范围内生效
- 加密密钥从密码派生（PBKDF2），P1 升级为 U 盘双因子
- 密码哈希存储格式：`{iterations}.{base64salt}.{base64hash}`
- AES-GCM 密文格式：`Base64(nonce[12] + tag[16] + ciphertext)`

### 后期注意事项

- **NuGet 漏洞警告**（21 个 NU1903）：`SQLitePCLRaw.lib.e_sqlite3` 2.1.11 和 `System.Security.Cryptography.Xml` 9.0.0 有已知漏洞，均为传递依赖，需在安全加固阶段更新
- **加密密钥管理**：当前使用固定 salt（`ClinicPrescription2026`），生产环境需改为随机 salt 并安全存储
- **P1 升级路径**：`EnsureCreatedAsync()` → `Migrate()`（EF Core 迁移）；密码哈希 PBKDF2 → Argon2id
- **数据库备份**：尚未实现，P1 需添加自动备份逻辑（`BackupManifest` 表已建）

---

## 阶段三：P0 核心服务层实现

### 完成内容

1. 创建会话状态管理：
   - `IUserSession` 接口 + `UserSession` 实现（Singleton）
   - 跨操作范围保持登录状态，AuthService 写入，其他服务/ViewModel 读取

2. 实现 5 个应用服务：

| 服务 | 核心逻辑 | 依赖 |
|------|---------|------|
| AuthService | 用户名查找 → PBKDF2 验证 → 更新 LastLoginAt → 写入会话 | IRepository<SysUser>, IPasswordHasher, IUserSession, IUnitOfWork, IClock |
| PatientService | 手机号双字段策略（AES-GCM 加密 + SHA-256 哈希索引） | IRepository<Patient>, IEncryptionService, IUnitOfWork |
| PrescriptionService | 处方编号生成（{年份}-{5位序号}）→ 添加明细 → 事务内汇总金额 | IRepository<Prescription/PrescriptionItem/DrugMaster>, IUnitOfWork, IClock |
| InventoryService | 入库流水 + 库存批次合并（同药品同批号）→ 效期预警（30天） | IRepository<DrugIn/DrugOut/DrugStock/DrugMaster>, IUnitOfWork, IClock |
| BillingService | 收费流水记录（校验处方状态）→ 日报表聚合（现金/POS 分类） | IRepository<PaymentLog/Prescription>, IUnitOfWork, IClock |

3. 创建 DTO（`DTOs/PrescriptionDtos.cs`）：
   - PrescriptionDto, PrescriptionItemDto, DrugDto, StockBatchDto, UserDto

4. 创建数据库种子数据（`DbSeeder.cs`）：
   - 默认管理员：admin / admin123（Doctor 角色）
   - 5 种基础药品（阿莫西林、布洛芬、复方甘草、头孢克洛、奥美拉唑）

5. 更新 DI 注册（`Application/DependencyInjection.cs`）：
   - IUserSession → Singleton
   - 5 个应用服务 → Scoped

### 关键决策

- 会话状态用 Singleton `UserSession`，而非在 Scoped AuthService 中持有状态（避免跨 scope 丢失）
- 手机号用 SHA-256 哈希做唯一索引，而非加密后直接索引（哈希长度固定 64 字符，查找效率高）
- 处方编号基于「当年已有处方数 + 1」生成，单用户桌面应用无并发风险
- 入库时同药品同批号合并到已有 DrugStock 记录，而非创建新记录

### 后期注意事项

- **处方编号生成性能**：当前用 `GetAllAsync()` 全量加载后计数，处方量大时需优化为数据库查询
- **库存扣减**：P0 未实现发药出库（DrugOut）逻辑，处方保存后不会自动扣减库存
- **药品交互检查**：DrugInteraction 表已建但未接入处方流程，P1 需在 AddPrescriptionItem 时校验
- **审计日志链**：AuditLog 表有 HashChain/PrevHash 字段但未实现哈希链逻辑
- **权限控制**：当前所有服务无权限校验，P1 需基于 UserRole 添加操作权限
- **输入验证**：当前仅基础参数校验，P1 需添加 FluentValidation 或 DataAnnotations

---

## 阶段四：WPF 界面开发

### 完成内容

1. 登录窗口（`LoginWindow.xaml` + `LoginViewModel.cs`）：
   - 用户名/密码输入，PasswordBox 手动绑定
   - `LoginSucceeded` 事件通知 App 切换窗口
   - `IServiceScopeFactory` 创建 scope 调用 Scoped AuthService

2. 主窗口导航框架（`MainWindow.xaml` + `MainViewModel.cs`）：
   - 左侧导航栏（患者管理 / 处方开具 / 退出登录）
   - `ContentControl` + `DataTemplate` 实现 ViewModel → View 自动映射
   - `LogoutRequested` 事件通知 App 返回登录窗口

3. 患者管理界面（`Views/PatientManagementView.xaml` + `PatientManagementViewModel.cs`）：
   - 搜索策略：纯数字 ≥ 4 位 → 手机号查找，否则 → 姓名模糊搜索
   - DataGrid 展示患者列表（姓名/性别/出生日期/电话）
   - 右侧表单：建档（姓名/性别/出生日期/电话/过敏史/病史/慢病标签）
   - 建档成功后自动搜索新创建的患者

4. 处方开具界面（`Views/PrescriptionView.xaml` + `PrescriptionViewModel.cs`）：
   - 四步流程：搜索患者 → 填写诊断并创建处方 → 选择药品并添加明细 → 保存
   - 药品目录加载（`LoadDrugsCommand` 在 View Loaded 时触发）
   - 明细本地同步（添加后直接更新 UI 列表，避免重新查库）
   - `TotalAmount` 随明细集合变化自动通知更新

5. 登录流程集成（`App.xaml.cs`）：
   - 启动 → 显示 LoginWindow → 登录成功 → 切换 MainWindow
   - 退出登录 → 隐藏 MainWindow → 显示 LoginWindow
   - LoginWindow 为 Transient（每次新建），MainWindow 为 Singleton（复用）

6. 扩展 `IInventoryService`：
   - 新增 `GetAllDrugsAsync()` 方法，返回 `IReadOnlyList<DrugDto>`
   - 用于处方界面的药品目录加载

### 关键决策

- ViewModel 注册为 Transient 而非 Singleton：每次导航创建新实例，状态隔离
- `IServiceScopeFactory` 模式：ViewModel（Transient/Singleton）通过工厂创建 scope 访问 Scoped 服务，避免 captive dependency
- 处方草稿模式：先 CreatePrescription（生成编号），再逐步 AddItem，最后 Save（计算总额 + 事务提交）
- DataTemplate 隐式映射：MainWindow.xaml 中通过 `DataType` 自动将 ViewModel 渲染为对应 View，无需手动切换

### 后期注意事项

- **窗口生命周期**：退出登录时 MainWindow.Hide() 而非 Close()，因 MainWindow 为 Singleton 不可重复创建；再次登录时直接 Show()
- **PrescriptionView Loaded 事件**：每次导航到处方页都会触发 LoadDrugs，但有 `Drugs.Count > 0` 守卫避免重复加载
- **DataGrid GridLinesVisibility**：WPF 中属性名为 `GridLinesVisibility` 而非 `GridLines`（WinForms 命名）
- **处方草稿状态丢失**：如果用户在添加明细后退出登录，草稿处方（Status=Active）会残留在数据库中，P1 需添加清理逻辑
- **患者选择传递**：当前患者管理和处方开具是独立页面，处方页需重新搜索患者；P1 可考虑跨页面传参

---

## 阶段五：P0 验收与后续规划

### 当前状态

- 解决方案编译通过：0 错误，20 个 NuGet 漏洞警告（已知，低风险）
- P0 核心功能全部实现：认证、患者管理、处方开具、库存管理、收费结算
- WPF 界面覆盖核心流程：登录 → 导航 → 患者管理 → 处方开具

---

## 阶段六：库存扣减实现（P1-1）

### 完成内容

1. 扩展 `PrescriptionService` 构造函数，注入 `IRepository<DrugStock>` 和 `IRepository<DrugOut>`
2. `SavePrescriptionAsync` 新增库存扣减逻辑：
   - 保存前校验处方明细不为空（空处方不可保存）
   - 在事务内执行：计算总金额 → 库存扣减 → 事务提交
   - 库存不足时抛出 `InvalidOperationException`，事务自动回滚

3. 实现 `DeductInventoryAsync` 私有方法：
   - **FIFO 策略**：按 `ExpiryDate` 升序排列库存批次，优先扣减最早过期的批次
   - **多批次扣减**：单个处方明细可跨多个库存批次扣减，每个批次生成独立的 DrugOut 出库流水
   - **库存不足阻断**：当可用库存总量 < 需求数量时，抛出包含药品名称和数量信息的异常
   - **出库流水记录**：每次扣减生成 DrugOut 记录，包含药品 ID、批号、数量、处方 ID、操作人、时间戳

4. 处方保存流程更新：
   ```
   SavePrescriptionAsync
     → 校验处方状态 (Active)
     → 获取处方明细
     → 校验明细非空
     → BeginTransaction
       → 计算总金额 + Update 处方
       → DeductInventoryAsync (遍历明细 → FIFO 扣减 → 生成 DrugOut)
     → Commit (原子提交)
     → 异常时 Rollback
   ```

### 关键决策

- **FIFO 而非 LIFO**：诊所药品有有效期约束，优先消耗即将过期的批次，减少药品浪费
- **扣减时机在 SavePrescription 而非 AddItem**：处方明细在添加阶段可能被修改或删除，只有保存时才确定最终需求
- **库存不足时抛异常而非返回 false**：SavePrescriptionAsync 返回 bool 表示处方状态校验结果（Active/不存在），库存不足属于业务异常，用异常传播更清晰
- **多批次拆分出库**：DrugOut 每条记录对应一个批次的扣减，保留完整批号追溯链
- **OperatorId 使用处方 DoctorId**：当前阶段处方保存即发药，操作人为开方医生

### 后期注意事项

- **处方作废未回退库存**：`VoidPrescriptionAsync` 当前只修改处方状态，不生成 IsReversal=true 的 DrugOut 冲正记录。P1 需补充作废回退逻辑
- **并发扣减风险**：单用户桌面应用无并发问题，但 P1 如迁移到多用户需加乐观锁或 SELECT FOR UPDATE
- **PrescriptionItem.BatchIdOut 未设置**：当前未写入批次 ID 到处方明细，DrugOut 记录已提供完整追溯。如需反查可通过 PrescriptionId + DrugId 关联
- **库存预警未接入 UI**：InventoryService.GetExpiryAlertsAsync 已实现但未在界面展示，P1 需添加效期预警看板
- **入库数据为空**：DbSeeder 未创建 DrugStock 种子数据，首次使用需先通过 InventoryService.StockInAsync 入库

### P1 待开发项（更新）

| 优先级 | 功能 | 说明 | 状态 |
|--------|------|------|------|
| 高 | 库存扣减 | 处方保存后自动生成 DrugOut 记录，扣减 DrugStock | 已完成 |
| 高 | 药品交互检查 | AddPrescriptionItem 时查询 DrugInteraction 表，阻断 Major 级交互 | 已完成 |
| 高 | 输入验证 | 引入 FluentValidation，替代当前的 if-throw 模式 | 已完成 |
| 中 | 处方作废回退库存 | VoidPrescriptionAsync 生成 IsReversal DrugOut，恢复 DrugStock | 已完成 |
| 中 | 权限控制 | 基于 UserRole 限制操作（Readonly 不可开方、Nurse 不可收费） | 已完成 |
| 中 | 审计日志链 | 实现 AuditLog 的 HashChain/PrevHash 哈希链 | 待开发 |
| 中 | 处方打印 | Template 表 + 打印模板渲染 | 待开发 |
| 中 | 收费界面 | PaymentLog 记录 + 日报表查询界面 | 待开发 |
| 低 | 库存管理界面 | 入库操作界面 + 效期预警看板 | 待开发 |
| 低 | 数据库迁移 | EnsureCreated → EF Core Migrate | 待开发 |
| 低 | NuGet 漏洞修复 | 更新 SQLitePCLRaw 和 System.Security.Cryptography.Xml | 待开发 |

---

## 阶段七：FluentValidation 输入验证（P1-2）

### 完成内容

1. 引入 FluentValidation NuGet 包（`Clinic.Application.csproj`，版本 12.1.1）

2. 创建请求 DTO（`Validators/Requests.cs`）：
   - 将 6 个核心服务方法的参数封装为 record 类型
   - 每个请求对应一个验证器，职责单一

   | 请求 DTO | 对应服务方法 |
   |---------|------------|
   | LoginRequest | AuthService.LoginAsync |
   | CreatePatientRequest | PatientService.CreatePatientAsync |
   | CreatePrescriptionRequest | PrescriptionService.CreatePrescriptionAsync |
   | AddPrescriptionItemRequest | PrescriptionService.AddPrescriptionItemAsync |
   | StockInRequest | InventoryService.StockInAsync |
   | RecordPaymentRequest | BillingService.RecordPaymentAsync |

3. 创建 6 个 FluentValidation 验证器（`Validators/ServiceValidators.cs`）：

   | 验证器 | 验证规则 |
   |--------|---------|
   | LoginValidator | 用户名/密码非空 |
   | CreatePatientValidator | 姓名非空+长度≤50；性别必须为「男」或「女」；手机号正则 `^1\d{10}$`；出生日期<今天；过敏史≤500字；病史≤2000字；慢病标签≤200字 |
   | CreatePrescriptionValidator | 患者/医生 ID > 0；诊断非空+长度≤500；处方类型 0-1；急诊处方必须填写延长理由 |
   | AddPrescriptionItemValidator | 处方/药品 ID > 0；剂量 > 0；剂量单位/频次/给药途径非空；疗程 1-90 天；数量 > 0 |
   | StockInValidator | 药品 ID > 0；批号非空+长度≤50；效期>今天；数量 > 0；成本价≥0；操作人 ID > 0 |
   | RecordPaymentValidator | 处方 ID > 0；收费方式 0-1；金额 > 0；操作人 ID > 0；POS 收费必须有序列号 |

4. 修改 5 个应用服务，注入并调用验证器：
   - 每个服务构造函数新增 `IValidator<XxxRequest>` 参数
   - 方法入口处构造 Request 对象 → `ValidateAsync` → 验证失败抛 `ValidationException`
   - 移除原有的手动 `if-throw` 参数校验（ArgumentException）

5. 注册验证器到 DI 容器（`Application/DependencyInjection.cs`）：
   - 6 个验证器注册为 Scoped（跟随服务生命周期）

6. 创建异常格式化辅助类（`Presentation/Helpers/ExceptionFormatter.cs`）：
   - 将 FluentValidation 的 `ValidationException.Errors` 拼接为中文分号分隔的友好消息
   - 非 `ValidationException` 直接返回 `ex.Message`
   - 所有 ViewModel 的 `catch (Exception ex)` 块统一调用 `ExceptionFormatter.GetMessage(ex)`

7. 更新 3 个 ViewModel 的错误处理：
   - LoginViewModel：1 个 catch 块
   - PatientManagementViewModel：2 个 catch 块
   - PrescriptionViewModel：5 个 catch 块

### 关键决策

- **验证器注册为 Scoped 而非 Singleton**：验证器本身无状态，但跟随服务生命周期更清晰，且未来如需注入依赖（如查库校验唯一性）可无缝升级
- **Request DTO 用 record 而非 class**：不可变、结构化相等、简洁，适合一次性验证场景
- **验证失败抛 ValidationException 而非返回 Result 对象**：与现有异常处理模式一致，ViewModel 的 `catch (Exception)` 统一捕获，无需修改调用方式
- **ExceptionFormatter 统一格式化**：避免每个 ViewModel 重复编写 `if (ex is ValidationException)` 逻辑，错误消息用中文分号拼接更自然
- **条件验证用 `.When()`**：如急诊处方延长理由、POS 收费序列号，仅在特定条件下触发验证

### 后期注意事项

- **LoginViewModel 行为变化**：空用户名/密码原来返回 `false`（显示"用户名或密码错误"），现在抛 `ValidationException`（显示"用户名不能为空"）。但 `CanLogin()` 已阻止空值提交，实际影响极小
- **异步验证未使用**：当前验证器均为同步规则，未涉及查库校验（如手机号唯一性仍在服务层用 `InvalidOperationException` 处理）
- **FluentValidation 依赖注入扩展未引入**：未使用 `AddFluentValidationAutoValidation()` 自动管道验证，手动调用更显式可控
- **验证规则与领域规则分离**：FluentValidation 负责输入格式校验，领域层业务规则（如库存不足、处方状态）仍由服务层 `InvalidOperationException` 处理

---

## 阶段八：药品交互检查 + 作废回退库存（P1-3）

### 完成内容

1. 药品交互检查（`PrescriptionService.CheckDrugInteractionsAsync`）：
   - 在 `AddPrescriptionItemAsync` 中，药品验证通过后、创建明细前执行交互检查
   - 全量加载 DrugInteraction 表（诊所药品目录有限，内存匹配）
   - 仅阻断 `Major` 级交互，`Moderate`/`Minor`/`Unknown` 放行
   - 双向匹配：新药品 ↔ 处方已有药品，检查 A↔B 和 B↔A 两个方向
   - 名称模糊匹配：交互表名称可能不含剂型（"阿莫西林" vs "阿莫西林胶囊"），用 `Contains` 双向匹配
   - 匹配药品的 `GenericNameCn` 和 `GenericNameEn` 两个名称
   - 发现 Major 交互时抛 `InvalidOperationException`，包含双方药品名称

2. 处方作废回退库存（`PrescriptionService.ReverseInventoryAsync`）：
   - `VoidPrescriptionAsync` 在事务内增加库存回退步骤
   - 查找该处方所有 `IsReversal=false` 的 DrugOut 记录
   - 逐笔恢复 DrugStock 库存（`QtyRemaining += outRecord.Qty`）
   - 生成 `IsReversal=true` 的冲正 DrugOut 记录
   - 库存批次不存在时自动重建（默认效期 365 天）
   - 事务保证：状态变更 + 库存回退 + 冲正记录原子提交

3. 补充种子数据（`DbSeeder`）：
   - **DrugStock 种子数据**：为 5 种基础药品各创建 100 单位库存（批号 `SEED-{日期}`，效期 365 天），解决首次开具处方库存不足的问题
   - **DrugInteraction 种子数据**：4 条示例交互记录

   | 药品 A | 药品 B | 等级 | 说明 |
   |--------|--------|------|------|
   | 阿莫西林 | 头孢克洛 | Major | β-内酰胺类交叉过敏 |
   | 布洛芬 | 阿司匹林 | Major | NSAIDs 叠加出血风险 |
   | 奥美拉唑 | 头孢克洛 | Moderate | PPI 影响抗菌药物吸收 |
   | 布洛芬 | 复方甘草片 | Minor | 轻度胃部不适风险 |

4. PrescriptionService 依赖更新：
   - 新增 `IRepository<DrugInteraction>` 注入
   - 新增 `CheckDrugInteractionsAsync` + `ReverseInventoryAsync` 两个私有方法
   - 新增 `NamesMatch` 名称匹配辅助方法（单名称 + 多名称重载）

### 关键决策

- **交互检查时机在 AddItem 而非 Save**：处方保存时已扣减库存，此时阻止为时已晚；在添加明细阶段拦截，避免无效数据进入处方
- **全量加载交互数据而非逐对查询**：诊所药品目录有限（几十种），全量加载后内存匹配比多次数据库查询更高效；未来药品目录扩大时可改为按药品名称查询
- **仅阻断 Major 级**：Major 级交互在临床上属于禁忌或高风险，应硬阻断；Moderate 级可考虑后续添加警告提示（当前放行）
- **名称模糊匹配而非精确匹配**：DDInter 数据库的药品名称可能不含剂型后缀（"阿莫西林" vs "阿莫西林胶囊"），`Contains` 双向匹配覆盖这种情况
- **作废回退逐笔恢复**：DrugOut 记录了每个批次的扣减量，逐笔回退确保多批次扣减的精确恢复
- **批次不存在时重建**：极端情况下库存批次可能被清理，回退时自动重建批次并设置默认效期，确保回退不失败

### 后期注意事项

- **Moderate 级交互未警告**：当前仅阻断 Major，Moderate 级静默放行。P1 后续可考虑在 UI 层返回警告信息（需修改 `AddPrescriptionItemAsync` 返回类型或添加 out 参数）
- **交互数据来源**：当前为手动种子数据，实际应从 DDInter 2.0 数据库导入完整交互数据
- **名称匹配精度**：`Contains` 可能产生误匹配（如"阿莫"匹配"阿莫西林"和"阿莫莫"），但目前药品目录小，风险可控
- **作废回退的效期问题**：重建批次时使用默认 365 天效期，可能与原始批次效期不一致。实际场景中批次不太可能被删除，此为兜底逻辑
- **作废后再次作废**：`VoidPrescriptionAsync` 检查 `Status != Active` 返回 false，已作废处方不可重复作废
- **草稿处方作废**：未保存的处方（无 DrugOut 记录）作废时 `ReverseInventoryAsync` 直接返回，不产生冲正记录

---

## 阶段九：权限控制（P1-5）

### 完成内容

1. 创建权限检查器接口与实现（`IPermissionChecker` + `PermissionChecker`）：
   - 基于 `IUserSession` 中的用户角色进行权限校验
   - 4 个权限检查方法，覆盖所有业务操作

   | 方法 | 允许角色 | 对应操作 |
   |------|---------|---------|
   | `RequireCanPrescribe()` | Doctor | 创建处方、添加明细、保存处方、作废处方 |
   | `RequireCanBill()` | Doctor | 记录收费 |
   | `RequireCanModify()` | Doctor + Nurse | 患者建档、药品入库 |
   | `RequireRole(params)` | 通用 | 自定义角色检查 |

   - 未登录时抛出 `UnauthorizedAccessException("未登录，请先登录后再操作")`
   - 角色不匹配时抛出包含当前角色和所需角色的中文异常消息

2. 在所有应用服务的写操作入口添加权限检查：

   | 服务 | 方法 | 权限检查 |
   |------|------|---------|
   | PatientService | CreatePatientAsync | RequireCanModify() |
   | PrescriptionService | CreatePrescriptionAsync | RequireCanPrescribe() |
   | PrescriptionService | AddPrescriptionItemAsync | RequireCanPrescribe() |
   | PrescriptionService | SavePrescriptionAsync | RequireCanPrescribe() |
   | PrescriptionService | VoidPrescriptionAsync | RequireCanPrescribe() |
   | InventoryService | StockInAsync | RequireCanModify() |
   | BillingService | RecordPaymentAsync | RequireCanBill() |

3. 注册权限检查器到 DI 容器（`Application/DependencyInjection.cs`）：
   - `IPermissionChecker` → Scoped（依赖 Singleton `IUserSession`，跟随服务生命周期）

4. UI 层角色适配：
   - `MainViewModel` 新增 `CanPrescribe` 属性（仅 Doctor 返回 true）
   - `MainViewModel` 新增 `CurrentUserRoleText` 属性（角色中文显示）
   - `MainViewModel` 新增 `RefreshPermissions()` 方法：登录后手动通知属性变更和命令 CanExecute 重新评估
   - `MainWindow.xaml` 处方开具按钮绑定 `Visibility` 到 `CanPrescribe`（非 Doctor 角色隐藏按钮）
   - `MainWindow.xaml` 用户信息区显示角色文本
   - `App.xaml.cs` 在 `ShowMainWindow()` 中调用 `RefreshPermissions()` 刷新权限状态

5. 扩展种子用户数据（`DbSeeder`）：
   - 新增 3 个测试账户，覆盖全部 3 种角色

   | 用户名 | 密码 | 角色 | 显示名 |
   |--------|------|------|--------|
   | admin | admin123 | Doctor | 管理员 |
   | nurse | admin123 | Nurse | 护士 |
   | reader | admin123 | Readonly | 只读用户 |

### 关键决策

- **权限检查在 Application 层而非 ViewModel**：UI 层隐藏按钮只是用户体验优化，真正的安全边界在服务层。即使 UI 被绕过（如直接调用服务），权限检查仍然生效
- **权限检查器注册为 Scoped 而非 Singleton**：虽然 `IUserSession` 是 Singleton，但权限检查器跟随服务生命周期更合理，且与被检查的服务共享 scope
- **权限检查放在验证之前**：先检查权限（安全），再验证输入格式（数据有效性），最后执行业务逻辑。避免无权限用户触发验证规则暴露系统信息
- **RefreshPermissions 手动通知**：`CanPrescribe` 是计算属性，不会自动通知。由于 MainViewModel 为 Singleton 跨登录复用，需要在每次登录后手动刷新。同时处理了角色降级后处方页面仍显示的边界情况
- **种子用户统一密码**：三个测试账户使用相同密码 admin123，便于开发测试。生产环境应强制首次登录修改密码
- **非 Doctor 角色隐藏而非禁用按钮**：Visibility=Collapsed 比 IsEnabled=False 体验更好，用户不会看到无法操作的菜单项

### 后期注意事项

- **读取操作无权限检查**：当前仅写操作有权限检查，读取操作（如 GetPatientById、SearchByName）未限制。如需严格权限（如 Readonly 不能查看患者详情），需在读取方法也添加检查
- **UI 层防御不完整**：患者管理页面对所有角色可见，但建档按钮未隐藏。Nurse 可以建档（符合权限），Readonly 虽然服务层会拒绝，但 UI 上按钮仍可点击。后续应在 ViewModel 添加 `CanModify` 属性控制按钮
- **角色降级页面切换**：`RefreshPermissions` 中处理了当前页面为处方页面但角色无处方权限的情况，自动切换回患者管理。但如果未来添加更多受权限控制的页面，需扩展此逻辑
- **权限检查与审计日志未联动**：当前权限检查失败只抛异常，未记录到审计日志。P1 后续可考虑在权限检查失败时记录审计日志
- **密码安全**：三个测试账户使用相同密码，生产环境应删除测试账户或强制改密

---

## 阶段十：P0 核心加固（处方校验 + 登录锁定 + 审计日志链 + PDF 处方 + 收费界面 + 备份恢复）

### P0-1：处方校验规则

#### 完成内容

1. 在 `PrescriptionService` 中定义处方校验常量：

   | 常量 | 值 | 说明 |
   |------|---|------|
   | `MaxDrugItemsPerPrescription` | 5 | 单张处方药品品种上限 |
   | `EmergencyMaxDays` | 3 | 急诊处方最大用药天数 |
   | `NormalMaxDays` | 7 | 普通处方最大用药天数 |
   | `ExtendedMaxDays` | 84 | 有延长理由时普通处方最大用药天数（12 周） |

2. `AddPrescriptionItemAsync` 中实现三重校验：
   - **药品品种上限**：查询已有明细数，`≥ 5` 则抛出 `InvalidOperationException`，符合《处方管理办法》规定
   - **用药天数限制**：根据处方类型计算 `maxDays`，急诊=3天 / 普通=7天 / 普通且有延长理由=84天，超限时拦截并提示
   - **过敏史匹配拦截**（`CheckAllergyAsync`）：读取患者 `Allergies` 自由文本，按 ``,，、;；`` 分割关键词，对药品的 `GenericNameCn` 和 `GenericNameEn` 做双向 `Contains` 匹配（忽略大小写），命中则抛异常

3. 校验执行顺序：权限检查 → FluentValidation 输入验证 → 处方状态校验 → 药品存在校验 → **品种上限** → **用药天数** → **过敏匹配** → 药品交互检查 → 创建明细

#### 关键决策

- **品种上限为 5 而非更多**：遵循《处方管理办法》西药/中成药处方不得超过 5 种药品的规定
- **急诊 3 天 / 普通 7 天**：遵循处方常规用量限制，延长处方需填写理由并上限 12 周（慢性病长期用药）
- **过敏匹配用关键词分割而非精确匹配**：过敏史为自由文本（如"青霉素、头孢类"），需按分隔符切分后逐个匹配药品名称
- **双向 Contains 匹配**：患者过敏词"阿莫西林"应匹配药品"阿莫西林胶囊"，反之亦然

#### 后期注意事项

- **过敏匹配精度**：`Contains` 可能产生误匹配（如"阿莫"匹配多种药品），目前药品目录小风险可控；后续可引入药品 ATC 编码精确匹配
- **用药天数限制为硬阻断**：未提供"确认继续"的软警告机制，急诊处方超过 3 天直接拦截

---

### P0-2：登录失败锁定机制

#### 完成内容

1. 在 `SysUser` 实体新增锁定字段：`FailedLoginCount`（失败计数）、`LockedUntil`（锁定截止时间）

2. `AuthService.LoginAsync` 实现锁定逻辑：

   ```
   LoginAsync
     → FluentValidation 输入校验
     → 查询用户（Username + IsActive）
     → 用户不存在 → return false
     → 锁定检查（LockedUntil > now）
         → 抛异常，提示剩余锁定时间
         → 记录 LOGIN_LOCKED 审计
     → 密码验证
         → 验证失败：
             → FailedLoginCount++
             → 若 ≥ 5：LockedUntil = now + 10min，FailedLoginCount = 0
             → 记录 LOGIN_FAILURE 审计
             → return false
         → 验证成功：
             → 重置 FailedLoginCount = 0, LockedUntil = null
             → 更新 LastLoginAt
             → 设置会话
             → 记录 LOGIN_SUCCESS 审计
   ```

3. 锁定参数：

   | 参数 | 值 | 说明 |
   |------|---|------|
   | `MaxFailedAttempts` | 5 | 连续失败 5 次触发锁定 |
   | `LockoutDuration` | 10 分钟 | 锁定持续时间 |

#### 关键决策

- **锁定参数使用 const/static readonly**：参数化便于后续调整，编译时确定
- **锁定后重置计数**：达到 5 次后重置 `FailedLoginCount = 0`，锁定期间不再累计，解锁后从 0 开始
- **审计采用 SafeAuditAsync**：登录场景中审计失败不阻断流程（best-effort）
- **锁定时抛异常而非返回 false**：锁定与密码错误是不同性质的失败，异常携带剩余时间信息，UI 可区分展示

#### 后期注意事项

- **无验证码机制**：当前仅靠锁定防止暴力破解，未实现验证码。P1 可考虑添加图形验证码
- **锁定信息不持久化到 UI**：当前锁定状态仅在登录时展示剩余时间，未在管理界面显示被锁定的用户列表
- **管理员解锁未实现**：用户被锁定后需等待 10 分钟自动解锁，无管理员手动解锁功能

---

### P0-3：审计日志系统（SHA-256 哈希链）

#### 完成内容

1. 创建 `IAuditService` 接口和 `AuditService` 实现，双日志体系：

   | 方法 | 日志类型 | 哈希链 | 用途 |
   |------|---------|--------|------|
   | `LogAsync` | AuditLog | ✓ | 业务关键操作（登录、处方、收费等） |
   | `LogSystemAsync` | SystemLog | ✗ | 系统事件（备份、恢复等） |

2. SHA-256 哈希链实现：

   ```
   每条 AuditLog 包含三个哈希字段：

   PayloadHash = SHA-256(OccurredAt | UserId | Action | Target | Payload)
       → 管道符 | 分隔，OccurredAt 使用 ISO 8601 格式（:O）
       → 确保字段拼接无歧义

   PrevHash = 上一条 AuditLog 的 HashChain
       → 首条记录 PrevHash = "" (空字符串)

   HashChain = SHA-256(PayloadHash | PrevHash)
       → 将当前记录的 PayloadHash 与前一条的 HashChain 串联

   哈希格式：Hex 小写（Convert.ToHexString + ToLowerInvariant）
   ```

3. 哈希链验证原理：任何一条记录被篡改，其 `PayloadHash` 会变化，导致 `HashChain` 变化，进而影响后续所有记录的 `PrevHash` → `HashChain`，形成连锁反应，可检测篡改。

4. 在所有业务服务中集成审计日志：
   - `AuthService`：LOGIN_SUCCESS / LOGIN_FAILURE / LOGIN_LOCKED / LOGOUT
   - `PrescriptionService`：PRESCRIPTION_CREATE / ADD_ITEM / PRESCRIPTION_SAVE / PRESCRIPTION_VOID
   - `BillingService`：PAYMENT_RECORD
   - `BackupService`：BACKUP_CREATE / BACKUP_RESTORE（SystemLog）

5. 审计策略：
   - **事务内审计**：关键业务操作（如处方保存）在事务内调用 `LogAsync`，审计失败则事务回滚
   - **Best-effort 审计**：非事务操作（如登录）使用 `SafeAuditAsync` 封装，审计失败不影响业务流程

#### 关键决策

- **管道符分隔防歧义**：`OccurredAt|UserId|Action|Target|Payload` 用 `|` 分隔，避免字段值包含分隔符导致拼接歧义
- **ISO 8601 时间格式**：`OccurredAt.ToString("O")` 保证时间序列化的一致性和可重现性
- **Hex 小写格式**：统一使用 `ToLowerInvariant()`，避免大小写差异导致哈希不匹配
- **双日志体系**：AuditLog 带哈希链用于业务合规审计，SystemLog 无哈希链用于系统运维记录，降低哈希链计算开销
- **PrevHash 从最后一条记录获取**：`GetAllAsync()` 取 `Id` 最大的记录，单用户桌面应用无并发写入风险

#### 后期注意事项

- **性能瓶颈**：`LogAsync` 中 `GetAllAsync()` 全量加载 AuditLog 取最后一条，数据量大时（>10000 条）可能成为性能瓶颈。P1 可优化为 `OrderByDescending(Id).Take(1)` 查询
- **哈希链验证工具未实现**：当前仅写入哈希链，未提供验证工具检测篡改。P1 可添加 `VerifyHashChainAsync()` 方法
- **审计日志清理**：未实现审计日志的归档/清理机制，AuditLog 表会持续增长

---

### P0-4：PDF 处方生成（QuestPDF）

#### 完成内容

1. 引入 QuestPDF NuGet 包（`Clinic.Infrastructure.csproj`），静态构造函数配置社区许可证：`QuestPDF.Settings.License = LicenseType.Community`

2. 创建 `IPdfService` 接口（Application 层）和 `QuestPdfService` 实现（Infrastructure 层）：
   - 接口方法：`string GeneratePrescriptionPdf(PrescriptionPdfData data, string outputPath)`
   - 同步方法（QuestPDF 的 `GeneratePdf` 本身为同步 API）
   - 自动追加 `.pdf` 扩展名

3. 创建 PDF 数据 DTO（`DTOs/PdfDtos.cs`）：
   - `PrescriptionPdfData` record：处方编号、日期、类型、患者/医生信息、诊断、明细列表、总金额
   - `PrescriptionItemPdfRow` record：序号、药品名称、规格、用法用量、数量、单价、小计

4. PDF 模板结构（A5 尺寸，适合诊所处方打印）：

   ```
   ┌─────────────────────────────────┐
   │        诊所名称（14pt Bold）      │
   │  处方类型  │
   │       处 方 笺（18pt Bold 居中）  │
   │  编号：2026-00001  日期：...     │
   │  ─────────────────────────────  │
   │  姓名：XXX  性别：男  年龄：30   │
   │  电话：138XXXX  医生：XXX        │
   │  临床诊断：XXX                   │
   │  ┌──┬──────┬──┬────┬──┬───┐    │
   │  │序│药品  │规│用法│数│金额│    │
   │  │号│名称  │格│用量│量│    │    │
   │  ├──┼──────┼──┼────┼──┼───┤    │
   │  │1 │阿莫西│..│..  │..│.. │    │
   │  └──┴──────┴──┴────┴──┴───┘    │
   │  合计金额：￥XX.XX（11pt Bold）  │
   │  延长用药理由：XXX（8pt 斜体）   │
   │                                 │
   │  医生签名：___  审核/调配：___   │
   │  ─────────────────────────────  │
   │  有效期：急诊1天/普通3天          │
   │  打印时间：XXX（7pt 灰色）       │
   └─────────────────────────────────┘
   ```

5. 在 `PrescriptionViewModel` 中集成 PDF 导出：
   - 保存处方成功后自动生成 PDF
   - 文件保存路径：`Prescriptions/{处方编号}.pdf`
   - 使用 `IServiceScopeFactory` 获取 `IPdfService`（Scoped）

#### 关键决策

- **QuestPDF 流式 API 而非模板文件**：纯 C# 代码定义模板，无 XAML/HTML 模板文件依赖，编译时检查类型安全
- **A5 尺寸**：标准处方笺尺寸，适合诊所打印机；A4 过大浪费纸张
- **接口定义在 Application 层**：遵循依赖倒置，ViewModel 依赖 `IPdfService` 接口而非具体实现
- **默认字体 Microsoft YaHei**：中文显示兼容性好，QuestPDF 内置 CJK 字体支持
- **保存后自动生成 PDF**：减少用户操作步骤，确保每张处方都有可打印的电子版

#### 后期注意事项

- **字体依赖系统安装**：`Microsoft YaHei` 需系统已安装，Linux 部署需更换字体
- **PDF 文件管理**：生成的 PDF 保存在应用目录下，未提供文件管理界面（查看/删除/重新打印）
- **打印参数未配置**：直接生成 PDF 文件，未集成打印机调用（用户需手动打开 PDF 打印）

---

### P0-5：收费界面（BillingViewModel + BillingView + 日报表）

#### 完成内容

1. 创建 `BillingViewModel`（Presentation 层），实现收费登记和日报表查询：

   | 命令 | 功能 | CanExecute 条件 |
   |------|------|----------------|
   | `RecordPaymentCommand` | 记录收费 | `CanBill && 处方ID非空 && 金额非空` |
   | `LoadDailyReportCommand` | 查询日报表 | 始终可执行 |

2. 收费登记流程：
   - 前端校验：处方 ID（`long.TryParse`）和金额（`decimal.TryParse` > 0）
   - 通过 `IServiceScopeFactory` 获取 `IBillingService`（Scoped）
   - 调用 `RecordPaymentAsync(prescriptionId, PaymentMethod, amount, userId, posSerialNo?, note?)`
   - 成功后清空表单，显示成功消息（含流水号）

3. 日报表查询：
   - 调用 `BillingService.GetDailyReportAsync(ReportDate)`
   - 返回 `DailyReportDto`：处方数、总金额、现金金额、POS 金额
   - 四个数据卡片展示，`HasReportData` 控制整体可见性

4. 创建 `BillingView.xaml`（UserControl，嵌入主窗口内容区）：

   | 区域 | 控件 | 绑定 |
   |------|------|------|
   | 收费登记表单 | 处方ID输入框 | `PrescriptionIdInput` |
   | | 收费方式下拉 | `PaymentMethod`（现金/POS） |
   | | 金额输入框 | `AmountInput` |
   | | POS序列号 | `PosSerialNo`（仅 POS 时显示） |
   | | 备注 | `Note` |
   | | 确认收费按钮 | `RecordPaymentCommand` |
   | 日结报表 | 日期选择器 | `ReportDate` |
   | | 查询按钮 | `LoadDailyReportCommand` |
   | | 四个数据卡片 | `PrescriptionCount`/`TotalAmount`/`CashAmount`/`PosAmount` |

5. 在 `MainWindow.xaml` 添加导航按钮和 DataTemplate 映射

6. 权限控制：`CanBill` 属性（`_session.Role == UserRole.Doctor`），仅 Doctor 可收费

#### 关键决策

- **BillingView 使用 UserControl 而非 Window**：嵌入主窗口 ContentControl，与患者管理/处方开具保持一致的导航体验
- **日报表用数据卡片而非表格**：日报数据量小（4 个指标），卡片布局比表格更直观
- **卡片配色区分**：处方数（绿）/ 总金额（蓝）/ 现金（黄）/ POS（紫），视觉快速区分
- **CanBill 基于 Doctor 角色**：与权限矩阵一致，收费需 Doctor 权限（单人诊所医生兼收费员）

#### 后期注意事项

- **无收费记录列表**：当前仅支持单笔收费登记和日报表汇总，未提供历史收费流水查询
- **无退费功能**：`RecordPaymentAsync` 只记录正向收费，未实现退费/冲正
- **POS 凭证图片未实现**：`PaymentLog.PosImagePath` 字段存在但 UI 未提供上传功能
- **日报表无导出**：仅界面展示，未提供 PDF/Excel 导出

---

### P0-6：备份与恢复（SQLite Backup + zip + SHA-256 校验）

#### 完成内容

1. 创建 `IBackupService` 接口（Application 层）和 `BackupService` 实现（Infrastructure 层）：

   | 方法 | 功能 | 返回值 |
   |------|------|--------|
   | `CreateBackupAsync` | 创建备份 | zip 文件路径 |
   | `RestoreBackupAsync` | 从备份恢复 | bool（标记成功） |
   | `GetBackupHistoryAsync` | 获取备份历史 | `IReadOnlyList<BackupManifest>` |

2. 备份流程（`CreateBackupAsync`）：

   ```
   1. WAL checkpoint：PRAGMA wal_checkpoint(TRUNCATE)
      → 将 WAL 日志刷入主数据库文件，确保数据完整

   2. 收集文件：数据库主文件 + -wal + -shm（如存在）

   3. Zip 压缩：ZipArchive + CompressionLevel.Optimal
      → 文件名：backup_{yyyyMMdd_HHmmss}.zip

   4. SHA-256 校验：SHA256.HashDataAsync(stream)
      → 计算整个 zip 文件的哈希（Hex 小写 64 字符）

   5. 记录 BackupManifest：TakenAt / SourceDir / Sha256 / FileListJson / SizeBytes

   6. 审计日志：LogSystemAsync("BACKUP_CREATE", ...)（best-effort）
   ```

3. 恢复流程（`RestoreBackupAsync`）：

   ```
   1. SHA-256 校验：计算备份文件哈希，与 BackupManifest 记录匹配
      → 不匹配则抛异常（文件被篡改或损坏）

   2. 解压到临时目录：Path.GetTempPath()/clinic_restore_{Guid}

   3. 验证文件列表：对照 FileListJson 检查解压文件完整性

   4. 写入 pending-restore 标记：在数据库同目录创建 .pending-restore 文件
      → 内容：{tempDir}|{dbFileName}

   5. 审计日志：LogSystemAsync("BACKUP_RESTORE", ...)
   → 返回 true，提示用户重启应用
   ```

4. 延迟恢复机制（`TryExecutePendingRestore`，静态方法，应用启动时调用）：
   - 读取 `.pending-restore` 标记文件
   - 删除现有 `-wal`/`-shm` 文件
   - `File.Copy` 覆盖数据库文件
   - 清理标记文件和临时目录

5. 在 `MainViewModel` 集成备份/恢复命令：
   - `CreateBackupCommand`：调用 `CreateBackupAsync`，显示备份文件名
   - `RestoreBackupCommand`：OpenFileDialog 选择 zip → 确认对话框 → 调用 `RestoreBackupAsync` → 重启应用

6. 在 `App.xaml.cs` 启动流程中调用 `BackupService.TryExecutePendingRestore()` 执行延迟恢复

7. 配置：`appsettings.json` → `Backup:Dir`（默认 "Backups"）

#### 关键决策

- **WAL checkpoint 在备份前执行**：SQLite WAL 模式下，最新数据可能在 WAL 文件中，checkpoint 确保主数据库文件包含所有最新数据
- **恢复采用"标记文件 + 重启"模式**：运行时 SQLite 连接锁定数据库文件，无法直接覆盖；写入标记文件后重启，在连接建立前完成文件替换
- **SHA-256 双向校验**：备份时计算哈希并记录到 BackupManifest，恢复时重新计算并比对，防止备份文件被篡改或损坏
- **Zip 压缩而非直接复制**：减少备份文件体积，SQLite 数据库文件压缩率通常较高
- **备份文件列表用 JSON 序列化**：`FileListJson` 记录备份包含的所有文件名，恢复时验证完整性
- **审计用 SystemLog 而非 AuditLog**：备份/恢复属于系统运维操作，不需要哈希链防篡改

#### 后期注意事项

- **无自动备份计划**：当前为手动触发，未实现定时自动备份。P1 可用 cron 或 Windows 任务计划
- **备份文件无清理策略**：备份文件持续累积，未实现自动清理旧备份（如保留最近 30 天）
- **恢复后数据一致性**：恢复操作覆盖整个数据库文件，恢复后的数据状态为备份时刻的完整快照
- **无备份加密**：zip 文件未加密，包含敏感患者数据。P1 可考虑 AES 加密 zip
- **WAL checkpoint 使用独立连接**：`WalCheckpointAsync` 创建独立的 `SqliteConnection`，不影响当前 DbContext 的事务

---

### P0-7：编译验证

- 解决方案编译通过：**0 错误，0 警告**
- 5 个项目全部成功构建：Clinic.Shared / Clinic.Domain / Clinic.Application / Clinic.Infrastructure / Clinic.Presentation
- NuGet 漏洞警告（NU1903）已在之前阶段解决

### P0 待开发项更新

| 优先级 | 功能 | 状态 |
|--------|------|------|
| 高 | 处方校验规则（品种上限/天数/过敏） | ✓ 已完成 |
| 高 | 登录失败锁定（5次/10分钟） | ✓ 已完成 |
| 高 | 审计日志哈希链 | ✓ 已完成 |
| 高 | PDF 处方生成 | ✓ 已完成 |
| 高 | 收费界面 + 日报表 | ✓ 已完成 |
| 高 | 备份与恢复 | ✓ 已完成 |
| 中 | 哈希链验证工具 | ✓ 已完成 |
| 中 | 备份文件自动清理 | 待开发 |
| 中 | 收费流水查询界面 | ✓ 已完成 |
| 中 | PDF 重新打印/管理 | ✓ 已完成 |
| 低 | 图形验证码 | 待开发 |
| 低 | 管理员手动解锁用户 | 待开发 |

---

## 阶段十一：P1 功能扩展（库存管理界面 + 收费流水查询 + 哈希链验证 + 处方历史查询）

### P1-A：库存管理界面

#### 完成内容

1. 扩展 `IInventoryService` 接口，新增 3 个查询方法：

   | 方法 | 返回类型 | 功能 |
   |------|---------|------|
   | `GetAllStockSummaryAsync` | `IReadOnlyList<DrugStockSummaryDto>` | 所有药品库存汇总（总库存量/批次数/最早效期/零售价） |
   | `GetStockBatchesAsync(drugId)` | `IReadOnlyList<StockBatchDto>` | 指定药品的库存批次明细（按效期排序） |
   | `GetStockInHistoryAsync(from, to)` | `IReadOnlyList<StockInRecordDto>` | 入库流水记录（可按日期范围筛选） |

2. 新增 DTO（`DTOs/PrescriptionDtos.cs`）：
   - `DrugStockSummaryDto`：药品库存汇总
   - `StockInRecordDto`：入库流水记录
   - `PaymentRecordDto`：收费流水记录
   - `PrescriptionHistoryDto`：处方历史记录
   - `HashChainVerificationResult`：哈希链验证结果

3. 创建 `InventoryViewModel`（Presentation 层）：
   - 三个功能页通过 `CurrentTab` 切换：库存概览、入库操作、效期预警
   - **库存概览**：DataGrid 展示所有药品库存汇总，选中后查看批次明细
   - **入库操作**：表单（药品选择/批号/效期/数量/成本价/供应商）+ 入库历史 DataGrid
   - **效期预警**：30 天内即将过期的库存批次列表，7 天内到期红色高亮
   - 权限控制：`CanModify` 属性（Doctor + Nurse），入库表单仅对有权限角色可见

4. 创建 `InventoryView.xaml`（UserControl）：
   - TabControl 切换三个功能页
   - DataGrid 手动定义列，`AutoGenerateColumns=False`
   - 效期预警 DataGrid 使用 DataTrigger 对 `DaysRemaining < 7` 的行设置红色前景色
   - View Loaded 事件触发 `LoadDataCommand` 加载初始数据

5. 修改 `NullToCollapsedConverter`：增强对非字符串类型的支持，修复选中药品对象时批次明细区域不可见的问题

#### 关键决策

- **三 Tab 布局而非分页**：库存概览/入库/预警三个功能逻辑独立，TabControl 切换比页面导航更直观
- **入库历史在入库 Tab 内**：入库操作和历史查询在同一 Tab，方便操作后立即查看结果
- **效期预警独立 Tab**：效期预警是日常关注项，独立 Tab 便于快速查看
- **批量加载优化**：`GetAllStockSummaryAsync` 先全量加载药品和库存，内存中分组聚合，避免 N+1 查询
- **CanModify 控制表单可见性**：入库表单整体通过 `Visibility` 绑定 `CanModify`，Readonly 角色看不到入库操作

#### 后期注意事项

- **入库流水查询无分页**：当前返回全部记录，数据量大时需加分页
- **OperatorName 留空**：`StockInRecordDto.OperatorName` 当前为空字符串，未关联 SysUser 查询操作人名称
- **无低库存预警**：仅有效期预警，无库存量阈值预警（如库存低于 10 单位时提醒补货）
- **药品 CRUD 未实现**：当前只能通过种子数据或数据库直接操作添加药品，无 UI 管理药品目录

---

### P1-B：收费流水查询界面

#### 完成内容

1. 扩展 `IBillingService` 接口，新增 `GetPaymentHistoryAsync(fromDate, toDate)` 方法
2. 在 `BillingService` 中实现：全量加载 PaymentLog，按日期范围过滤，关联 Prescription 获取处方编号
3. 扩展 `BillingViewModel`，新增属性和命令：
   - `PaymentHistory`（`ObservableCollection<PaymentRecordDto>`）
   - `HistoryFromDate` / `HistoryToDate`（日期筛选）
   - `LoadPaymentHistoryCommand`（查询收费流水）
4. 扩展 `BillingView.xaml`，在日报表区域下方添加"收费流水查询"区域：
   - 日期范围筛选（两个 DatePicker + 查询按钮）
   - DataGrid 展示收费流水（流水号/处方编号/收费方式/金额/收费时间/POS序列号/备注）

#### 关键决策

- **流水查询嵌入收费页面而非独立页面**：收费登记和流水查询逻辑相关，同一页面操作更流畅
- **日期范围筛选而非全量列表**：收费流水可能很多，日期范围筛选避免一次性加载过多数据
- **PaymentRecordDto 包含 MethodText**：服务层将 `PaymentMethod` 枚举转换为中文文本，UI 直接显示

#### 后期注意事项

- **PatientName 和 OperatorName 留空**：DTO 中这两个字段当前为空字符串，未关联 Patient 和 SysUser 查询
- **无退费功能**：仍只支持正向收费记录，无退费/冲正
- **无处方已收费状态联动**：收费后未标记处方为"已收费"，可能重复收费

---

### P1-C：哈希链验证工具

#### 完成内容

1. 扩展 `IAuditService` 接口，新增 `VerifyChainAsync()` 方法
2. 在 `AuditService` 中实现验证逻辑：
   - 全量加载 AuditLog，按 Id 排序
   - 逐条验证 `PrevHash` 是否等于前一条的 `HashChain`
   - 逐条验证 `HashChain` 是否等于 `SHA-256(PayloadHash | PrevHash)` 的重算结果
   - 发现不匹配时返回 `HashChainVerificationResult`（IsValid=false, FirstBrokenId, ErrorMessage）
3. 新增 `HashChainVerificationResult` DTO（IsValid, TotalRecords, VerifiedRecords, FirstBrokenId, ErrorMessage）
4. 在 `MainViewModel` 添加 `VerifyAuditChainCommand`：
   - 调用 `IAuditService.VerifyChainAsync()`
   - 验证通过显示绿色状态消息（含总记录数）
   - 验证失败显示红色错误消息（含断裂点 ID 和错误描述）
5. 在 `MainWindow.xaml` 侧边栏添加"审计验证"按钮

#### 关键决策

- **验证策略为链式一致性检查**：原始 Payload 未存储在 AuditLog 中（仅存 PayloadHash），无法重算 PayloadHash。但可以验证 HashChain 的链式一致性：PrevHash 链接是否正确 + HashChain 计算是否匹配。这足以检测任何记录的篡改（修改任意字段会导致 PayloadHash 变化，进而 HashChain 不匹配）
- **首次断裂即返回**：发现第一个不匹配点立即返回，不继续验证后续记录（因为链已断裂，后续验证无意义）
- **验证按钮在侧边栏**：审计验证是系统级功能，放在侧边栏与备份/恢复同组

#### 后期注意事项

- **全表加载性能**：`VerifyChainAsync` 全量加载 AuditLog，数据量大时（>10000 条）可能较慢。可优化为分批加载验证
- **无自动验证**：当前为手动触发，未实现定期自动验证或启动时验证
- **无修复工具**：发现断链后只能定位问题，无自动修复工具

---

### P1-D：处方历史查询 + PDF 重新打印

#### 完成内容

1. 扩展 `IPrescriptionService` 接口，新增 `GetPrescriptionHistoryAsync(searchKeyword, fromDate, toDate)` 方法
2. 在 `PrescriptionService` 中实现：全量加载处方，按关键词（处方编号/患者姓名）和日期范围筛选，批量关联患者和医生名称
3. 创建 `PrescriptionHistoryViewModel`：
   - 搜索栏：关键词 + 日期范围 + 搜索按钮
   - 处方列表 DataGrid（编号/患者/医生/诊断/药品数/总金额/类型/状态/开具时间）
   - "重新打印 PDF"按钮（CanExecute: 选中处方不为空）
   - 生成 PDF 后自动用默认阅读器打开
4. 创建 `PrescriptionHistoryView.xaml`（UserControl）
5. 在 `MainViewModel` 添加 `NavigateToPrescriptionHistory` 导航命令
6. 在 `MainWindow.xaml` 添加"处方查询"导航按钮和 DataTemplate 映射
7. 在 `App.xaml.cs` 注册 `PrescriptionHistoryViewModel` 为 Transient

#### 关键决策

- **独立页面而非嵌入处方开具页面**：处方历史查询是只读操作，与处方开具（写操作）分离，职责清晰
- **PDF 输出目录可配置**：通过 `IConfiguration["Pdf:OutputDir"]` 读取配置，与处方开具时的 PDF 生成使用相同目录
- **自动打开 PDF**：生成后用 `Process.Start(UseShellExecute=true)` 调用系统默认 PDF 阅读器，减少用户操作步骤
- `[NotifyCanExecuteChangedFor]` 特性：`SelectedPrescription` 属性变化时自动通知 `ReprintPdfCommand` 重新评估 CanExecute

#### 后期注意事项

- **类型和状态显示为数字**：DataGrid 中 Type（0=普通/1=急诊）和 Status（0=Active/1=Voided）显示为数字，未转换为中文文本。后续可用 Converter 或 DataTrigger 优化
- **搜索为内存过滤**：全量加载处方后内存筛选，处方量大时需改为数据库查询
- **无处方详情查看**：列表仅显示概要信息，无法展开查看处方明细。后续可添加双击查看详情功能
- **PDF 文件可能覆盖**：重新打印时如果文件名相同（基于处方编号），会覆盖之前的 PDF 文件

---

### P1 编译验证

- 解决方案编译通过：**0 错误，0 警告**
- 5 个项目全部成功构建
- 新增文件：InventoryViewModel.cs, InventoryView.xaml(.cs), PrescriptionHistoryViewModel.cs, PrescriptionHistoryView.xaml(.cs)
- 修改文件：IServices.cs, IAuditService.cs, InventoryService.cs, BillingService.cs, AuditService.cs, PrescriptionService.cs, PrescriptionDtos.cs, BillingViewModel.cs, BillingView.xaml, MainViewModel.cs, MainWindow.xaml, App.xaml.cs, NullToCollapsedConverter.cs

### 导航菜单更新

主窗口侧边栏菜单（自上而下）：
1. 患者管理 → `NavigateToPatientCommand`
2. 处方开具 → `NavigateToPrescriptionCommand`（仅 Doctor）
3. 收费管理 → `NavigateToBillingCommand`
4. **库存管理** → `NavigateToInventoryCommand`（新增）
5. **处方查询** → `NavigateToPrescriptionHistoryCommand`（新增）
6. 数据备份 → `CreateBackupCommand`
7. 数据恢复 → `RestoreBackupCommand`
8. **审计验证** → `VerifyAuditChainCommand`（新增）
9. 退出登录 → `LogoutCommand`

### P1 待开发项更新

| 优先级 | 功能 | 状态 |
|--------|------|------|
| 高 | 库存管理界面 | ✓ 已完成 |
| 高 | 收费流水查询 | ✓ 已完成 |
| 中 | 哈希链验证工具 | ✓ 已完成 |
| 中 | 处方历史查询 + PDF 重打印 | ✓ 已完成 |
| 中 | 备份文件自动清理 | 待开发 |
| 低 | 图形验证码 | 待开发 |
| 低 | 管理员手动解锁用户 | 待开发 |
| 低 | 数据库迁移（EnsureCreated → Migrate） | 待开发 |
| 低 | NuGet 漏洞修复 | 待开发 |

## 阶段十二：处方状态机完善 + 已收费处方作废（退款冲正）

### 背景

在全流程业务测试（患者建档 → 开处方 → 保存扣库存 → 收费 → 日报表）中发现：作废已收费（Paid 状态）处方时被拦截（"当前处方状态不可作废"）。这是诊所真实业务场景——处方开错、患者退药等都需要作废已收费处方并退款。

### 完成内容

- **状态机扩展**：作废允许范围从 `Draft/Saved` 扩展为 `Draft/Saved/Paid`，作废后统一进入 `Voided`。
- **退款冲正**：作废已收费处方时，为每笔非冲正收费记录生成 `IsReversal=true` 的冲正记录（金额、收款方式与原收款一致，备注标注原流水号）。
- **库存恢复**：沿用既有 `ReverseInventoryAsync`，作废时恢复已扣减库存批次，并生成 `IsReversal=true` 的冲正出库记录。
- **日报表核算**：`GetDailyReportAsync` 将冲正记录抵减收入，`总金额/现金/POS` 均按「非冲正合计 − 冲正合计」计算，避免退款虚增当日收入。
- **事务原子性**：作废（改状态 + 回退库存 + 退款冲正 + 审计日志）在同一事务内提交，任一步失败整体回滚。

### 改动文件

| 文件 | 变更 |
|------|------|
| `src/Clinic.Domain/Entities/PaymentLog.cs` | 新增 `IsReversal` 冲正标记属性 |
| `src/Clinic.Application/Services/PrescriptionService.cs` | 注入 `IRepository<PaymentLog>`；作废逻辑扩展至 Paid；新增 `ReversePaymentsAsync` |
| `src/Clinic.Application/Services/BillingService.cs` | 日报表按冲正记录抵减收入 |
| 数据库 `clinic.db` | 手工迁移 `payment_log` 表新增 `is_reversal` 列 |

### 关键决策

- **冲正模型沿用 DrugOut 约定**：`PaymentLog` 增加 `IsReversal` 布尔标记（而非负数金额），与库存出库冲正模式一致，便于识别与核算。
- **数据库迁移**：应用使用 `EnsureCreatedAsync`（不自动迁移已有库），新增列通过一次性 Python 脚本手工执行 `ALTER TABLE payment_log ADD COLUMN is_reversal`。
- **测试可重复性**：全流程测试会消耗库存（阿莫西林每次 42），增加 `reset_stock.py` 将各药品批次恢复至 100 后重跑。

### 全流程测试结果

31 项全部通过，覆盖：登录 → 建档 → 开处方 → 加药 → 保存扣库存 → PDF → 收费 → 重复收费拦截 → 日报表 → 已收费处方作废 → 状态核验 → 库存恢复核验 → 退款冲正核验。

---

## 阶段十三：处方开具界面重构（主诉 + 体征 + 常见诊断 + AI辅助病历 + 模拟病历处方单）

### 背景

根据诊所实际业务需求，原处方开具界面存在以下问题：
1. 缺少主诉字段，不符合门诊病历规范
2. 患者体征（体重、体温、血压、心率）无法在问诊阶段记录
3. 常见诊断种类不足，且不支持鼠标滚轮滚动浏览
4. 处方明细的用法用量需要单独区域设置，操作繁琐
5. 界面缺乏直观的病历单和处方单模拟视图，医生无法获得职业习惯的视觉反馈
6. AI药剂师审核过于强制，应改为参考性建议

### 完成内容

#### 1. 主诉功能
- **实体扩展**：`Prescription` 实体新增 `ChiefComplaint` 属性，数据库 `prescription` 表新增 `chief_complaint` 列
- **常见主诉列表**：`PrescriptionViewModel` 新增 `CommonChiefComplaints` 集合（29 项），覆盖发热、咳嗽、头痛、腹痛、皮疹等常见症状
- **交互模式**：点击标签添加到主诉栏（分号分隔），支持多选累积，自动去重
- **双侧同步**：左侧输入面板和右侧模拟病历单的主诉栏双向绑定，均可直接编辑

#### 2. 患者体征集成
- **实体扩展**：`Patient` 实体新增 `Weight`、`Temperature`、`SystolicBP`、`DiastolicBP`、`HeartRate` 属性
- **服务层**：`PatientService.CreatePatientAsync` 新增体征参数；新增 `UpdateVitalsAsync` 方法支持独立更新体征
- **UI 集成**：处方开具界面左侧患者信息区新增体重(kg)、体温(°C)、血压(mmHg)、心率(次/分)输入框
- **病历单展示**：右侧模拟病历单体征栏以 `T/P/BP/Wt` 格式展示，符合临床书写习惯
- **保存联动**：保存处方时自动调用 `UpdateVitalsAsync` 同步更新患者体征

#### 3. 常见诊断扩展与滚动
- **诊断列表扩展**：`CommonDiagnoses` 从原 10 余项扩展至 55+ 项，按系统分类：
  - 呼吸系统（9 项）、心血管系统（5 项）、内分泌代谢（4 项）
  - 消化系统（7 项）、泌尿系统（3 项）、皮肤科（6 项）
  - 神经系统（4 项）、骨科（8 项）、五官科（5 项）、妇科（3 项）、其他（5 项）
- **滚动支持**：常见诊断区域使用 `ScrollViewer`（`MaxHeight="80"`），鼠标悬停时滚轮可上下滚动浏览全部诊断
- **去重逻辑**：`AddDiagnosis` 方法检查 `DiagnosisText.Contains(diagnosis)` 防止重复添加

#### 4. AI 辅助病历生成
- **LLM 集成**：`GenerateMedicalRecordCommand` 调用 `ILlmService.GenerateMedicalRecordAsync`，传入主诉+诊断+患者信息
- **可编辑结果**：AI 生成的规范病历显示在 `AiDiagnosisResult` 区域（蓝色背景框），医生可直接编辑修改
- **确认机制**：`ConfirmAiDiagnosisCommand` 将 AI 结果追加到病历文本，医生可在模拟病历单上进一步修改
- **降级处理**：LLM 不可用时隐藏 AI 按钮，不影响正常开方流程

#### 5. 处方明细内联编辑
- **DTO 改造**：`PrescriptionItemDto` 从 `record` 改为 `class`，实现 `INotifyPropertyChanged`，支持双向绑定
- **DataGrid 内联编辑**：处方明细表格直接编辑剂量、单位（下拉）、频次（下拉）、用法（下拉）、天数、数量
- **自动重算**：`Recalculate()` 方法根据剂量×频次×天数自动计算数量和小计
- **持久化**：`ItemsDataGrid_CellEditEnding` 事件触发 `UpdateItemInlineAsync`，调用 `UpdatePrescriptionItemAsync` 持久化到数据库
- **删除按钮**：每行末尾 ✕ 按钮调用 `RemovePrescriptionItemAsync`，Draft 状态下可见

#### 6. 模拟病历单和处方单（右侧面板）
- **病历单**：1:1 还原门诊病历格式，包含：
  - 诊所名称 + "门 诊 病 历" 标题
  - 患者基本信息（姓名、性别、电话、日期）
  - 主诉（可编辑）、病史/AI病历（可编辑）、体征（T/P/BP/Wt）、诊断（可编辑）
  - 医师签名行
- **处方单**：1:1 还原处方笺格式，包含：
  - 诊所名称 + "处 方 笺" 标题 + 日期
  - 处方类型标签（普通/急诊黄色/儿科绿色），背景色对应（白/淡黄#FFFDE7/淡绿#F1F8E9）
  - 患者信息 + 主诉 + 临床诊断
  - "Rp" 标示 + 处方明细表格 + "/" 结尾标示
  - 医师签名 + 金额合计
- **GridSplitter**：左右面板间可拖拽调整宽度

#### 7. AI 药剂师辅助审核（非强制）
- **分级分色**：`GetAiReviewSuggestionsAsync` 检查 7 项规则，生成分级建议：
  - ✅ 通过（绿色）：无问题
  - ℹ️ 提示（蓝色）：轻度关注
  - ⚠️ 警告（橙色）：需注意
  - 🚫 严重（红色）：高风险
- **非阻塞性**：AI 审核仅提供参考意见，不拦截处方保存流程，最终由执业医生判断
- **7 项检查规则**：药物相互作用、过敏史、剂量合理性、重复用药、禁忌症、抗生素分级、品种上限

#### 8. 工作流简化
- **去除步骤标签**：移除"第一步、第二步、第三步"等引导性步骤，医生按实际情况自由填写
- **必填项检查**：保存时仅检查关键信息（患者、诊断、至少 1 种药品）是否已填
- **自动创建处方**：选择患者并添加药品时自动创建处方草稿，无需手动"创建处方"按钮

### 改动文件

| 文件 | 变更 |
|------|------|
| `src/Clinic.Domain/Entities/Prescription.cs` | 新增 `ChiefComplaint` 属性 |
| `src/Clinic.Domain/Entities/Patient.cs` | 新增 `Weight/Temperature/SystolicBP/DiastolicBP/HeartRate` 属性 |
| `src/Clinic.Application/DTOs/PrescriptionDtos.cs` | `PrescriptionItemDto` 从 record 改为 class + INotifyPropertyChanged；`PrescriptionDto` 新增 `ChiefComplaint` |
| `src/Clinic.Application/Services/PrescriptionService.cs` | 新增 `UpdatePrescriptionItemAsync/RemovePrescriptionItemAsync`；`CreatePrescriptionAsync` 新增 `chiefComplaint` 参数；`SavePrescriptionAsync` 同步保存体征 |
| `src/Clinic.Application/Services/PatientService.cs` | `CreatePatientAsync` 新增体征参数；新增 `UpdateVitalsAsync` |
| `src/Clinic.Presentation/ViewModels/PrescriptionViewModel.cs` | 新增主诉/体征/常见诊断/AI病历/内联编辑等完整逻辑 |
| `src/Clinic.Presentation/Views/PrescriptionView.xaml` | 左右分栏布局重构：左侧输入面板 + 右侧模拟病历单和处方单 |
| `src/Clinic.Presentation/Views/PrescriptionView.xaml.cs` | 新增 `DrugDataGrid_MouseDoubleClick/ItemsDataGrid_CellEditEnding` 事件处理 |
| `src/Clinic.Infrastructure/Pdf/QuestPdfService.cs` | PDF 新增主诉显示 + Rp 标示 + / 结尾标示 |

### 关键决策

- **PrescriptionItemDto 从 record 改为 class**：record 的不可变性导致 DataGrid 双向绑定无法直接编辑，改为 class + INotifyPropertyChanged 后支持内联编辑和自动重算
- **AI 审核非阻塞性设计**：合规要求 AI 辅助审核，但最终处方权属于执业医生，因此 AI 仅提供参考意见，不拦截保存流程
- **模拟病历单和处方单同屏显示**：让医生在左侧输入时右侧实时看到病历和处方的最终效果，符合手写病历的职业习惯
- **常见诊断用 ScrollViewer + WrapPanel**：WrapPanel 自动换行排列标签，ScrollViewer 限制最大高度并支持滚轮滚动，兼顾美观和可浏览性

---

## 阶段十四：集成测试体系建设

### 背景

为全面验证系统功能性和业务逻辑正确性，需要建立可重复执行的集成测试体系，覆盖不少于 5 个不同业务场景，以发现不常见的 BUG。

### 完成内容

#### 测试项目搭建
- **项目结构**：`tests/Clinic.IntegrationTests/`，控制台应用（`net10.0`），引用 Application + Infrastructure + Domain + Shared
- **TestHost 测试宿主**：
  - 每个测试创建独立临时 SQLite 文件数据库（`Guid` 命名），测试间数据完全隔离
  - 注册完整 DI 容器（仓储、工作单元、安全服务、PDF、LLM）
  - 使用固定测试密钥（32 字节零数组）和测试 pepper
  - `NoOpLlmService` 替代真实 LLM，避免外部依赖
  - 播种数据：1 个测试医生 + 7 种药品（含抗生素标记、禁忌标签）+ 每药 100 单位库存 + 药物交互数据
  - `IDisposable` 实现自动清理临时数据库文件

#### 9 个测试案例

| 编号 | 场景 | 验证点 |
|------|------|--------|
| TC01 | 完整普通处方流程 | 主诉+体征+创建→加药→保存→审核→AI预审→收费→PDF→库存扣减→体征持久化 |
| TC02 | 过敏史拦截 | 阿莫西林过敏患者→添加阿莫西林被拦截→添加布洛芬成功 |
| TC03 | 库存不足+事务回滚 | Qty=200>库存100→保存被拦截→状态保持Draft→库存未扣减 |
| TC04 | 急诊处方天数超限 | 急诊5天>3天上限→被拦截→3天添加成功 |
| TC05 | 处方作废+退款+库存恢复 | 收费→作废→冲正记录→库存恢复至100→日报表收入=0 |
| TC06 | 儿科处方完整流程 | 儿科类型+儿童剂量0.25片→保存→审核→收费→PDF |
| TC07 | 重复药品拦截 | 同一药品第二次添加→被拦截 |
| TC08 | 药品品种上限 | 5种成功→第6种被拦截（上限5种） |
| TC09 | 内联编辑+删除+空处方拦截 | 编辑剂量/频次/疗程→验证持久化→删除明细→空处方保存被拦截 |

### 测试结果

```
═══════════════════════════════════════════════════════════
  测试结果：9 项，通过 9 项，失败 0 项
═══════════════════════════════════════════════════════════
```

### 关键决策

- **控制台测试运行器而非 xUnit/NUnit**：单人项目无需测试框架开销，自定义 `Assert` + `AssertThrowsAsync<T>` 足够，输出更直观
- **每个测试独立 TestHost**：避免测试间状态污染，每次创建新数据库，`Dispose` 时自动清理
- **TC05 日报表验证**：验证作废后日报表收入为 0（正常收费 - 冲正 = 0），确保退款不虚增收入
- **TC09 内联编辑验证**：验证剂量1粒/频次每日两次/疗程3天→Qty=6→小计=4.8（6×0.8），确保 Recalculate 逻辑正确

---

## 阶段十五：界面优化 + 字段完善 + Git 版本控制

### 背景

集成测试全部通过后，进入界面打磨和功能完善阶段。针对实际使用场景优化 UI 交互、补全患者信息字段、建立 Git 版本控制和开发文档体系。

### 完成内容

#### 1. 诊所名称合规
- 将"个人诊所"更名为"陈医生诊所"，符合《医疗机构管理条例实施细则》命名规范
- 更新 PDF 处方生成、处方开具界面、登录界面等所有显示位置

#### 2. UI 样式修复
- 修复 DataGrid 选中行白色文字不可见问题（合并重复样式定义，IsSelected 触发器设置 Foreground）
- 修复 ListBox 选中项白色文字问题
- 统一选中样式使用 TextPrimaryBrush

#### 3. 处方流程优化
- 处方历史页面增加"前往收费"按钮和双击跳转功能
- 收费成功后自动清除待收费处方卡片
- 处方开具页面各模块（就诊体征/主诉诊断/处方开具）支持折叠展开

#### 4. 快速建档完善
- 添加字段标签（姓名/性别/手机号/出生日期/过敏史/基础疾病）
- 修复性别选择问题（SelectedItem → SelectedValue + SelectedValuePath）
- 新增出生日期（DatePicker，用于年龄计算和儿科用药）
- 新增过敏史输入框（处方保存时药物过敏检查）
- 新增基础疾病输入框（药物禁忌检查）
- 手机号实时验证（11位，1开头，第二位3-9，带错误提示）

#### 5. 就诊体征实时验证
- 两级验证：黄色警告（异常范围）/ 红色错误（超出范围）
- 红色错误时禁用保存按钮
- 验证范围：体重 0.1-500kg，体温 30-45°C，收缩压 40-250mmHg，舒张压 20-150mmHg，心率 20-250次/分
- 收缩压 ≤ 舒张压触发红色错误
- 输入框下方显示灰色参考范围提示

#### 6. 患者信息卡优化
- 选中患者绿色信息卡显示：姓名、性别、年龄（自动计算）、手机号、过敏史（红色字体）、基础疾病

#### 7. 患者管理页面同步
- DataGrid 新增过敏史列（红色字体，悬浮显示完整内容）和基础疾病列
- 表单标签"慢病标签"统一为"基础疾病"
- 添加输入提示 ToolTip

#### 8. Logo 与品牌
- 登录界面和主窗口添加诊所 Logo
- 手机号验证同步到患者管理页面

#### 9. Git 版本控制
- 初始化 Git 仓库，创建 `.gitignore`（排除 bin/obj/db/log/backup 等）
- 创建 `Directory.Build.props` 统一版本号管理（v1.0.0）
- 创建 `README.md` 项目说明文档
- 创建 `CHANGELOG.md` 变更日志
- 首次提交并打标签 `v1.0.0`

### 关键决策

- **出生日期用 DateTime? 而非 DateOnly?**：WPF DatePicker 的 SelectedDate 是 DateTime? 类型，ViewModel 使用 DateTime? 避免创建额外转换器，调用服务层时转换为 DateOnly?
- **快速建档标签统一**：处方页面的"基础疾病"与患者管理页面的"慢病标签"统一为"基础疾病"，避免概念混淆
- **DataGrid 过敏史列红色字体**：医生浏览患者列表时一眼识别过敏风险，悬浮显示完整内容避免截断
- **Git 版本控制选在 v1.0.0**：14 个开发阶段全部完成，9 项集成测试全部通过，功能完备可正式使用
