# 个体诊所处方系统 — 开发文件检索目录

> 最后更新：2026-09-16
> 用途：AI 开发时快速查找关键文件、接口、实现和配置位置
> 当前状态：v1.3.0（阶段三 个性化配置/销量报表/双屏布局 + P0 安全修复 + UI 渲染修复）

---

## 解决方案结构

```
Clinic.sln
└── src/
    ├── Clinic.Domain/          — 领域层（零外部依赖）
    ├── Clinic.Application/     — 应用层（用例编排）
    ├── Clinic.Infrastructure/  — 基础设施层（EF Core + 加密 + 仓储）
    ├── Clinic.Presentation/    — 表现层（WPF）
    └── Clinic.Shared/          — 共享枚举
```

依赖方向：Presentation → Application → Domain → Shared；Infrastructure → Domain + Application + Shared

---

## Clinic.Domain

### 实体（Entities/）

| 文件 | 实体 | 关键字段 | 备注 |
|------|------|---------|------|
| `Common/Entity.cs` | Entity（基类） | Id, CreatedAt, DeletedAt | 所有实体继承；long 主键 |
| `Entities/SysUser.cs` | SysUser | Username, DisplayName, Role, PasswordHash, IsActive, LastLoginAt, FailedLoginCount, LockedUntil | 用户权限 + 登录锁定 |
| `Entities/Patient.cs` | Patient | Name, Gender, Dob, PhoneEncrypted, PhoneHash, Allergies, History, ChronicTags | 手机号双字段 |
| `Entities/MedicalRecord.cs` | MedicalRecord | PatientId, DoctorId, VisitAt, ChiefComplaint, Diagnosis, Plan | 病历 |
| `Entities/Prescription.cs` | Prescription | NoYearSeq, PatientId, DoctorId, DiagnosisText, TotalAmount, Type, Status, VoidReason | 处方主表 |
| `Entities/PrescriptionItem.cs` | PrescriptionItem | PrescriptionId, DrugId, DrugName, Spec, Dose, DoseUnit, Frequency, Route, DurationDays, Qty, UnitPrice, Subtotal | 处方明细 |
| `Entities/DrugMaster.cs` | DrugMaster | GenericNameCn, GenericNameEn, Spec, Unit, IsAntibiotic, AntibioticLevel, RetailPriceRef | 药品目录 |
| `Entities/DrugStock.cs` | DrugStock | DrugId, BatchNo, ExpiryDate, QtyRemaining, CostPrice, Supplier | 库存批次 |
| `Entities/DrugIn.cs` | DrugIn | DrugId, BatchNo, ExpiryDate, Qty, CostPrice, Supplier, ReceivedAt, OperatorId | 入库流水 |
| `Entities/DrugOut.cs` | DrugOut | DrugId, BatchNo, Qty, PrescriptionId, OccurredAt, OperatorId, IsReversal | 出库流水 |
| `Entities/DrugInteraction.cs` | DrugInteraction | DdinterIdA, DrugNameA, DdinterIdB, DrugNameB, Level, SourceAtcCode | 药物交互 |
| `Entities/StockCheck.cs` | StockCheck | OccurredAt, OperatorId, Mode, DiffJson, Note | 盘点记录 |
| `Entities/PaymentLog.cs` | PaymentLog | PrescriptionId, Method, Amount, OccurredAt, OperatorId, PosSerialNo, IsReversal | 收费流水 |
| `Entities/Followup.cs` | Followup | PatientId, PlanAt, Channel, Status, Note, DoctorId | 随访 |
| `Entities/AuditLog.cs` | AuditLog | OccurredAt, UserId, Action, Target, PayloadHash, PrevHash, HashChain | 审计日志 |
| `Entities/SystemLog.cs` | SystemLog | OccurredAt, UserId, Action, Target, Note | 系统日志 |
| `Entities/Template.cs` | Template | Name, Version, Kind, DefinitionJson | 打印模板 |
| `Entities/BackupManifest.cs` | BackupManifest | TakenAt, SourceDir, Sha256, FileListJson | 备份记录 |

### 接口（Interfaces/）

| 文件 | 接口 | 方法 | 实现位置 |
|------|------|------|---------|
| `Interfaces/IRepository.cs` | IRepository\<T\> | GetByIdAsync, FindAsync, GetAllAsync, AddAsync, Update, SoftDelete | Infrastructure/Repositories/Repository.cs |
| `Interfaces/IUnitOfWork.cs` | IUnitOfWork | BeginTransactionAsync, CommitAsync, RollbackAsync, SaveChangesAsync | Infrastructure/Repositories/UnitOfWork.cs |
| `Interfaces/IEncryptionService.cs` | IEncryptionService | Encrypt, Decrypt | Infrastructure/Encryption/AesGcmEncryptionService.cs |
| `Interfaces/IPasswordHasher.cs` | IPasswordHasher | Hash, Verify | Infrastructure/Security/Pbkdf2PasswordHasher.cs |
| `Interfaces/IClock.cs` | IClock | UtcNow, Now | Infrastructure/Common/SystemClock.cs |

---

## Clinic.Application

### 服务接口（Interfaces/）

| 文件 | 接口 | 核心方法 |
|------|------|---------|
| `Interfaces/IServices.cs` | IAuthService | LoginAsync, Logout, IsAuthenticated, CurrentUserId |
| | IPatientService | CreatePatientAsync, GetPatientByIdAsync, FindByPhoneAsync, SearchByNameAsync |
| | IPrescriptionService | CreatePrescriptionAsync, AddPrescriptionItemAsync, SavePrescriptionAsync, VoidPrescriptionAsync |
| | IInventoryService | StockInAsync, GetStockQuantityAsync, GetExpiryAlertsAsync, GetAllDrugsAsync, GetAllStockSummaryAsync, GetStockBatchesAsync, GetStockInHistoryAsync |
| | IBillingService | RecordPaymentAsync, GetDailyReportAsync, GetPaymentHistoryAsync |
| | IPrescriptionService | CreatePrescriptionAsync, AddPrescriptionItemAsync, SavePrescriptionAsync, VoidPrescriptionAsync, GeneratePrescriptionPdfAsync, GetPrescriptionHistoryAsync |
| `Interfaces/IUserSession.cs` | IUserSession | IsAuthenticated, UserId, UserName, DisplayName, Role, SetAuthenticated, Clear |
| `Interfaces/IPermissionChecker.cs` | IPermissionChecker | CurrentRole, RequireCanPrescribe, RequireCanBill, RequireCanModify, RequireRole |
| `Interfaces/IAuditService.cs` | IAuditService | LogAsync（带哈希链审计日志）, LogSystemAsync（无哈希链系统日志）, VerifyChainAsync（哈希链完整性验证） |
| `Interfaces/IPdfService.cs` | IPdfService | GeneratePrescriptionPdf（同步生成 PDF 处方） |
| `Interfaces/IBackupService.cs` | IBackupService | CreateBackupAsync, RestoreBackupAsync, GetBackupHistoryAsync |

### 服务实现（Services/）

| 文件 | 类 | 依赖 | 核心逻辑 |
|------|---|------|---------|
| `Services/AuthService.cs` | AuthService | IRepository\<SysUser\>, IPasswordHasher, IUserSession, IUnitOfWork, IClock, IValidator\<LoginRequest\>, IAuditService | 密码验证 → 登录锁定（5次/10分钟）→ 更新登录时间 → 写入会话 → 审计日志 |
| `Services/PatientService.cs` | PatientService | IRepository\<Patient\>, IEncryptionService, IUnitOfWork, IValidator\<CreatePatientRequest\>, IPermissionChecker, IAuditService | 手机号 AES-GCM 加密 + SHA-256 哈希索引 + 审计日志；建档需 Doctor/Nurse 权限 |
| `Services/PrescriptionService.cs` | PrescriptionService | 5 个仓储 + IRepository\<DrugInteraction\> + IUnitOfWork + IClock + 2 个验证器 + IPermissionChecker + IAuditService | 编号生成 + 处方校验（品种≤5/天数限制/过敏拦截）+ 药品交互检查 + 金额计算 + FIFO 库存扣减 + 作废回退库存 + 事务保存 + 处方历史查询 + 审计日志；全部操作需 Doctor 权限 |
| `Services/InventoryService.cs` | InventoryService | 4 个仓储 + IUnitOfWork + IClock + IValidator\<StockInRequest\> + IPermissionChecker + IAuditService | 入库流水 + 库存合并 + 效期预警 + 药品目录查询 + 库存汇总/批次明细/入库历史查询 + 审计日志；入库需 Doctor/Nurse 权限 |
| `Services/BillingService.cs` | BillingService | 2 个仓储 + IUnitOfWork + IClock + IValidator\<RecordPaymentRequest\> + IPermissionChecker + IAuditService | 收费记录 + 日报表聚合 + 收费流水历史查询 + 审计日志；收费需 Doctor 权限 |
| `Services/PermissionChecker.cs` | PermissionChecker | IUserSession | 基于角色的权限校验：Doctor 全部、Nurse 建档+入库、Readonly 仅查看 |
| `Services/AuditService.cs` | AuditService | IRepository\<AuditLog\>, IRepository\<SystemLog\>, IUserSession, IUnitOfWork, IClock | SHA-256 哈希链审计日志 + 系统日志 + 哈希链完整性验证；双日志体系 |

### DTO

| 文件 | DTO | 用途 |
|------|-----|------|
| `Interfaces/IServices.cs` | PatientDto, ExpiryAlertDto, DailyReportDto | 患者/效期/日报 |
| `DTOs/PrescriptionDtos.cs` | PrescriptionDto, PrescriptionItemDto, DrugDto, StockBatchDto, UserDto | 处方/药品/库存/用户 |
| `DTOs/PdfDtos.cs` | PrescriptionPdfData, PrescriptionItemPdfRow | PDF 处方渲染数据 |
| `DTOs/PrescriptionDtos.cs`（追加） | DrugStockSummaryDto, StockInRecordDto, PaymentRecordDto, PrescriptionHistoryDto, HashChainVerificationResult | 库存汇总/入库流水/收费流水/处方历史/哈希链验证 |

### 其他

| 文件 | 内容 |
|------|------|
| `Session/UserSession.cs` | UserSession — Singleton 会话状态实现 |
| `Validators/Requests.cs` | 6 个请求 DTO record（LoginRequest, CreatePatientRequest, CreatePrescriptionRequest, AddPrescriptionItemRequest, StockInRequest, RecordPaymentRequest） |
| `Validators/ServiceValidators.cs` | 6 个 FluentValidation 验证器（LoginValidator, CreatePatientValidator, CreatePrescriptionValidator, AddPrescriptionItemValidator, StockInValidator, RecordPaymentValidator） |
| `DependencyInjection.cs` | AddApplication() — 注册 IUserSession(Singleton) + IPermissionChecker(Scoped) + IAuditService(Scoped) + 5 个业务服务(Scoped) + 6 个验证器(Scoped) |

---

## Clinic.Infrastructure

### 数据（Data/）

| 文件 | 类 | 职责 |
|------|---|------|
| `Data/ClinicDbContext.cs` | ClinicDbContext | 17 个 DbSet + OnModelCreating（snake_case + 索引 + 约束） |
| `Data/DbSeeder.cs` | DbSeeder | 种子数据：3 个用户（admin/nurse/reader）+ 5 种药品 + DrugStock 库存（100/种）+ 4 条 DrugInteraction 交互记录 |

### 仓储（Repositories/）

| 文件 | 类 | 职责 |
|------|---|------|
| `Repositories/Repository.cs` | Repository\<T\> | 泛型仓储，自动过滤软删除 |
| `Repositories/UnitOfWork.cs` | UnitOfWork | 事务管理（Begin/Commit/Rollback） |

### 安全（Encryption/ + Security/）

| 文件 | 类 | 算法 | 密文/哈希格式 |
|------|---|------|-------------|
| `Encryption/AesGcmEncryptionService.cs` | AesGcmEncryptionService | AES-GCM 256 | Base64(nonce[12]+tag[16]+ciphertext) |
| `Security/Pbkdf2PasswordHasher.cs` | Pbkdf2PasswordHasher | PBKDF2-SHA256 | {iterations}.{base64salt}.{base64hash} |

### 其他

| 文件 | 类 | 职责 |
|------|---|------|
| `Common/SystemClock.cs` | SystemClock | 真实时间实现 |
| `Pdf/QuestPdfService.cs` | QuestPdfService | QuestPDF 流式 API 生成 A5 处方 PDF；社区许可证；默认字体 Microsoft YaHei |
| `Backup/BackupService.cs` | BackupService | SQLite WAL checkpoint + Zip 压缩 + SHA-256 校验；恢复采用 pending-restore 标记 + 重启模式 |
| `DependencyInjection.cs` | AddInfrastructure(dbPath, encryptionPassword) | 注册连接/DbContext/仓储/安全服务/时钟/IPdfService/IBackupService |

---

## Clinic.Presentation

### 窗口与入口

| 文件 | 类 | 职责 |
|------|---|------|
| `App.xaml.cs` | App | Generic Host DI 配置 + 启动流程（TryExecutePendingRestore → 建库 → 种子 → 登录窗口 → 主窗口切换 + RefreshPermissions） |
| `LoginWindow.xaml` / `.cs` | LoginWindow | 登录窗口（用户名/密码，PasswordBox 手动绑定） |
| `MainWindow.xaml` / `.cs` | MainWindow | 主窗口（侧边导航 9 项菜单 + ContentControl + DataTemplate 映射 5 个 View + 备份/恢复/审计验证按钮） |
| `appsettings.json` | — | 配置：Database:Path, Security:EncryptionKey, Backup:Dir |

### ViewModels

| 文件 | 类 | 生命周期 | 职责 |
|------|---|---------|------|
| `ViewModels/LoginViewModel.cs` | LoginViewModel | Transient | 登录验证，LoginSucceeded 事件通知窗口切换 |
| `ViewModels/MainViewModel.cs` | MainViewModel | Singleton | 导航框架（患者/处方/收费/库存/处方查询）+ 角色控制（CanPrescribe/RefreshPermissions）+ 备份/恢复 + 审计验证 + LogoutRequested 事件 |
| `ViewModels/PatientManagementViewModel.cs` | PatientManagementViewModel | Transient | 患者搜索（手机号/姓名）+ 建档 + 列表 |
| `ViewModels/PrescriptionViewModel.cs` | PrescriptionViewModel | Transient | 处方开具（选患者 → 创建处方 → 添加明细 → 保存 → 生成PDF） |
| `ViewModels/BillingViewModel.cs` | BillingViewModel | Transient | 收费登记（处方ID+金额+方式）+ 日报表查询（4指标卡片）+ 收费流水历史查询 |
| `ViewModels/InventoryViewModel.cs` | InventoryViewModel | Transient | 库存管理三 Tab（库存概览/入库操作/效期预警）+ 入库历史查询；入库需 Doctor/Nurse 权限 |
| `ViewModels/PrescriptionHistoryViewModel.cs` | PrescriptionHistoryViewModel | Transient | 处方历史查询（关键词+日期范围筛选）+ PDF 重新打印 |

### Views（UserControl）

| 文件 | 绑定 ViewModel | 功能 |
|------|---------------|------|
| `Views/PatientManagementView.xaml` | PatientManagementViewModel | 搜索栏 + 患者列表 DataGrid + 建档表单 |
| `Views/PrescriptionView.xaml` | PrescriptionViewModel | 四步流程：患者搜索 → 诊断创建 → 药品选择 → 明细保存 |
| `Views/BillingView.xaml` | BillingViewModel | 收费登记表单 + 日结报表（4个数据卡片）+ 收费流水查询（日期筛选+DataGrid） |
| `Views/InventoryView.xaml` | InventoryViewModel | 库存管理三 Tab：库存概览（DataGrid+批次明细）/ 入库操作（表单+历史）/ 效期预警（红色高亮） |
| `Views/PrescriptionHistoryView.xaml` | PrescriptionHistoryViewModel | 搜索栏 + 处方列表 DataGrid + 重新打印 PDF 按钮 |

### Converters

| 文件 | 类 | 用途 |
|------|---|------|
| `Converters/NullToCollapsedConverter.cs` | NullToCollapsedConverter | null/空字符串 → Collapsed，非空 → Visible |

### Helpers

| 文件 | 类 | 用途 |
|------|---|------|
| `Helpers/ExceptionFormatter.cs` | ExceptionFormatter | 将 FluentValidation ValidationException 转为中文分号分隔的友好消息；非验证异常返回 ex.Message |

### DataTemplate 映射

在 `MainWindow.xaml` 的 `Window.Resources` 中定义：
- `PatientManagementViewModel` → `PatientManagementView`
- `PrescriptionViewModel` → `PrescriptionView`
- `BillingViewModel` → `BillingView`
- `InventoryViewModel` → `InventoryView`
- `PrescriptionHistoryViewModel` → `PrescriptionHistoryView`

ContentControl 绑定 `CurrentViewModel`，WPF 自动选择对应 View 渲染。

---

## Clinic.Shared

| 文件 | 枚举 | 值 |
|------|------|---|
| `Enums.cs` | UserRole | Doctor(0), Nurse(1), Readonly(2) |
| | PaymentMethod | Cash(0), Pos(1) |
| | PrescriptionType | Normal(0), Emergency(1) |
| | PrescriptionStatus | Active(0), Voided(1) |
| | DrugInteractionLevel | Major(0), Moderate(1), Minor(2), Unknown(3) |
| | AntibioticLevel | None(0), NonRestricted(1), Restricted(2), Special(3) |
| | StockCheckMode | Spot(0), Full(1) |
| | FollowupStatus | Pending(0), Done(1), Cancelled(2) |
| | FollowupChannel | Onsite(0), Phone(1) |
| | TemplateKind | Prescription(0), Receipt(1), MedicalRecord(2) |

---

## 速查：常见开发任务

### 新增一个 WPF 页面（View + ViewModel）

1. 在 `Presentation/ViewModels/` 创建 ViewModel，继承 `ObservableObject`
2. 注入 `IServiceScopeFactory`（和 `IUserSession` 如需登录状态）
3. 用 `[ObservableProperty]` 声明绑定属性，`[RelayCommand]` 声明命令
4. 在 `Presentation/Views/` 创建 `XxxView.xaml`（UserControl）+ `.xaml.cs`
5. 在 `MainWindow.xaml` 的 `Window.Resources` 添加 DataTemplate 映射
6. 在 `MainViewModel.cs` 添加导航命令 `CurrentViewModel = _services.GetRequiredService<XxxViewModel>()`
7. 在 `App.xaml.cs` 注册 ViewModel 为 Transient
8. XAML 中使用 `d:DataContext="{d:DesignInstance Type=vm:XxxViewModel}"` 获得设计时智能提示

### 新增一个应用服务

1. 在 `Application/Interfaces/IServices.cs` 添加接口
2. 在 `Application/Services/` 创建实现类，注入需要的仓储/服务
3. 在 `Application/DependencyInjection.cs` 注册为 Scoped
4. 在 ViewModel 中通过 `IServiceScopeFactory` 创建 scope 调用

### 新增输入验证（FluentValidation）

1. 在 `Validators/Requests.cs` 添加请求 DTO record
2. 在 `Validators/ServiceValidators.cs` 添加 `AbstractValidator<XxxRequest>` 验证器
3. 在服务构造函数注入 `IValidator<XxxRequest>`
4. 方法入口处：构造 Request → `ValidateAsync` → 失败抛 `ValidationException`
5. 在 `Application/DependencyInjection.cs` 注册验证器为 Scoped
6. ViewModel 的 `catch (Exception ex)` 中使用 `ExceptionFormatter.GetMessage(ex)` 格式化错误

### 新增权限检查

1. 在服务构造函数注入 `IPermissionChecker`
2. 方法入口处（验证之前）调用对应的权限检查方法：
   - 处方相关 → `_permissionChecker.RequireCanPrescribe()`
   - 收费相关 → `_permissionChecker.RequireCanBill()`
   - 建档/入库 → `_permissionChecker.RequireCanModify()`
   - 自定义 → `_permissionChecker.RequireRole(UserRole.Xxx)`
3. 权限不足时自动抛出 `UnauthorizedAccessException`，ViewModel 的 `catch (Exception)` 统一捕获
4. 如需 UI 层联动：在 `MainViewModel` 添加计算属性 + 在 `RefreshPermissions()` 中通知

### 新增一个实体

1. 在 `Domain/Entities/` 创建实体类，继承 `Entity`
2. 在 `Infrastructure/Data/ClinicDbContext.cs` 添加 DbSet + OnModelCreating 配置
3. 仓储通过 `IRepository<T>` 自动可用

### 修改数据库表结构

1. 修改 `ClinicDbContext.OnModelCreating` 中的实体配置
2. 删除 `clinic.db` 文件重新生成（P0 阶段），或添加 EF Core 迁移（P1）

### DI 生命周期规则

| 类型 | 生命周期 | 原因 |
|------|---------|------|
| IUserSession | Singleton | 跨操作保持登录状态 |
| IPermissionChecker | Scoped | 依赖 IUserSession，跟随服务生命周期 |
| IEncryptionService | Singleton | 无状态，密钥初始化一次 |
| IPasswordHasher | Singleton | 无状态 |
| IClock | Singleton | 无状态 |
| ClinicDbContext | Scoped | EF Core 标准 |
| IRepository\<T\> | Scoped | 依赖 DbContext |
| IUnitOfWork | Scoped | 依赖 DbContext |
| 应用服务 | Scoped | 依赖仓储 |
| IAuditService | Scoped | 依赖仓储 + IUserSession |
| IPdfService | Singleton | 无状态（QuestPDF 静态配置） |
| IBackupService | Scoped | 依赖 DbContext + 仓储 + 审计服务 |
| IValidator\<TRequest\> | Scoped | 跟随服务生命周期，未来可注入依赖 |
| MainViewModel | Singleton | 导航状态全局共享 |
| MainWindow | Singleton | 主窗口复用，Hide/Show 切换 |
| LoginViewModel | Transient | 每次登录新建实例 |
| LoginWindow | Transient | 每次显示新建窗口 |
| PatientManagementViewModel | Transient | 每次导航新建，状态隔离 |
| PrescriptionViewModel | Transient | 每次导航新建，状态隔离 |
| BillingViewModel | Transient | 每次导航新建，状态隔离 |
| InventoryViewModel | Transient | 每次导航新建，状态隔离 |
| PrescriptionHistoryViewModel | Transient | 每次导航新建，状态隔离 |

### ViewModel 调用 Scoped 服务的模式

ViewModel（Transient/Singleton）不能直接注入 Scoped 服务（captive dependency 问题）。
统一通过 `IServiceScopeFactory` 创建 scope：

```csharp
using var scope = _scopeFactory.CreateScope();
var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
var result = await patientService.SearchByNameAsync(keyword);
```

### 默认登录凭据

| 用户名 | 密码 | 角色 | 可用功能 |
|--------|------|------|---------|
| admin | admin123 | Doctor | 全部（处方开具、收费、建档、入库） |
| nurse | admin123 | Nurse | 建档、入库（不可开方、不可收费） |
| reader | admin123 | Readonly | 仅查看（不可任何写操作） |

### 应用启动流程

```
App.OnStartup
  → Host.StartAsync
  → BackupService.TryExecutePendingRestore (检查并执行延迟恢复)
  → EnsureCreatedAsync (建库)
  → DbSeeder.SeedAsync (种子数据)
  → ShowLoginWindow
      → LoginViewModel.LoginAsync (验证 + 锁定检查)
      → LoginSucceeded 事件
      → LoginWindow.Close
      → ShowMainWindow
          → MainViewModel.RefreshPermissions (刷新角色相关 UI)
          → MainViewModel (导航)
          → ContentControl → DataTemplate → View
          → LogoutRequested 事件
          → MainWindow.Hide
          → ShowLoginWindow (循环)
```

### 处方开具流程

```
PrescriptionView.Loaded → LoadDrugsCommand (加载药品目录)
  → 搜索患者 (手机号/姓名)
  → 选择患者
  → 填写诊断 → CreatePrescriptionCommand (创建草稿处方)
  → 选择药品 → 填写用法用量 → AddItemCommand
      → 权限检查 (RequireCanPrescribe)
      → FluentValidation 输入验证
      → 校验处方状态 (Active)
      → 校验药品存在
      → 校验品种上限 (≥5 → 拦截)
      → 校验用药天数 (急诊3/普通7/延长84)
      → 过敏史匹配拦截 (CheckAllergyAsync)
      → CheckDrugInteractionsAsync (Major 级交互阻断)
      → 创建 PrescriptionItem
  → 可重复添加多个明细
  → SavePrescriptionCommand
      → 权限检查 + 校验处方状态 (Active) + 明细非空
      → BeginTransaction
        → 计算总金额 + Update 处方
        → DeductInventoryAsync (FIFO 扣减 DrugStock + 生成 DrugOut)
        → 审计日志 (事务内)
      → Commit (原子提交)
      → 库存不足 → 抛异常 → Rollback → UI 显示错误
      → 保存成功 → 生成 PDF 处方 (Prescriptions/{编号}.pdf)
```

### 处方作废流程

```
VoidPrescriptionAsync
  → 权限检查 (RequireCanPrescribe)
  → 校验作废原因非空
  → 校验处方状态 (Draft/Saved/Paid 可作废)
  → BeginTransaction
    → 修改处方状态为 Voided
    → ReverseInventoryAsync
        → 查找该处方所有 IsReversal=false 的 DrugOut
        → 逐笔恢复 DrugStock 库存
        → 生成 IsReversal=true 冲正 DrugOut
    → ReversePaymentsAsync（若处方已收费）
        → 查找该处方所有 IsReversal=false 的 PaymentLog
        → 为每笔生成 IsReversal=true 退款冲正记录
    → 审计日志 (事务内)
    → Commit (原子提交)
  → 异常时 Rollback
```

### 权限控制流程

```
服务写操作入口
  → IPermissionChecker.RequireCanXxx()
      → 检查 IUserSession.IsAuthenticated (未登录 → UnauthorizedAccessException)
      → 检查 UserRole 是否在允许列表 (不匹配 → UnauthorizedAccessException)
  → FluentValidation 输入验证
  → 业务逻辑执行

UI 层：
  → MainViewModel.CanPrescribe (仅 Doctor)
      → MainWindow.xaml 处方按钮 Visibility 绑定
      → NavigateToPrescriptionCommand CanExecute 绑定
  → App.xaml.cs ShowMainWindow → RefreshPermissions()
      → 通知 CanPrescribe/CurrentUserDisplayName/CurrentUserRoleText 属性变更
      → NavigateToPrescriptionCommand.NotifyCanExecuteChanged()
      → 角色降级时自动切换回患者管理页面
```

### 权限矩阵

| 操作 | Doctor | Nurse | Readonly |
|------|--------|-------|----------|
| 患者建档 | ✓ | ✓ | ✗ |
| 药品入库 | ✓ | ✓ | ✗ |
| 创建处方 | ✓ | ✗ | ✗ |
| 添加处方明细 | ✓ | ✗ | ✗ |
| 保存处方 | ✓ | ✗ | ✗ |
| 作废处方 | ✓ | ✗ | ✗ |
| 记录收费 | ✓ | ✗ | ✗ |
| 查看患者/药品 | ✓ | ✓ | ✓ |

### 加密密钥

- 配置位置：`appsettings.json` → `Security:EncryptionKey`
- 当前值：`Clinic-dev-key-change-in-production-2026`
- 生产环境必须修改

### 备份配置

- 配置位置：`appsettings.json` → `Backup:Dir`
- 默认值：`Backups`（相对路径，基于 AppContext.BaseDirectory）

### 处方校验规则

```
AddPrescriptionItemAsync 校验顺序：
  1. 权限检查（RequireCanPrescribe）
  2. FluentValidation 输入验证
  3. 处方状态校验（Active）
  4. 药品存在校验
  5. 药品品种上限（≥5 → 拦截）
  6. 用药天数限制（急诊3天 / 普通7天 / 延长84天）
  7. 过敏史匹配（关键词双向 Contains）
  8. 药品交互检查（Major 级拦截）
  9. 创建 PrescriptionItem
```

### 登录锁定流程

```
LoginAsync
  → 用户不存在 → return false
  → LockedUntil > now → 抛异常（提示剩余时间）+ 审计 LOGIN_LOCKED
  → 密码错误：
      → FailedLoginCount++
      → ≥5 → LockedUntil = now + 10min, FailedLoginCount = 0
      → 审计 LOGIN_FAILURE → return false
  → 密码正确：
      → 重置 FailedLoginCount/LockedUntil
      → 更新 LastLoginAt → 设置会话 → 审计 LOGIN_SUCCESS
```

### 审计日志哈希链

```
AuditLog 三字段哈希链：
  PayloadHash = SHA-256(OccurredAt|UserId|Action|Target|Payload)  → Hex 小写
  PrevHash    = 上一条记录的 HashChain（首条为空字符串）
  HashChain   = SHA-256(PayloadHash|PrevHash)                     → Hex 小写

双日志体系：
  AuditLog  → 带哈希链，业务关键操作（LogAsync）
  SystemLog → 无哈希链，系统事件（LogSystemAsync）

审计策略：
  事务内审计 → LogAsync（失败则回滚）
  Best-effort → SafeAuditAsync（失败不影响业务）
```

### PDF 处方生成流程

```
PrescriptionViewModel.SavePrescriptionCommand
  → 保存处方成功
  → 构造 PrescriptionPdfData（处方+患者+医生+明细+金额）
  → IPdfService.GeneratePrescriptionPdf(data, "Prescriptions/{编号}")
  → 生成 A5 PDF 文件（QuestPDF 流式 API）
  → 文件路径：{AppContext.BaseDirectory}/Prescriptions/{处方编号}.pdf
```

### 收费流程

```
BillingViewModel.RecordPaymentCommand
  → 前端校验（处方ID + 金额）
  → IBillingService.RecordPaymentAsync
      → 权限检查（RequireCanBill）
      → FluentValidation 验证
      → 校验处方状态（必须为 Saved）
      → 幂等检查（禁止同处方重复收费）
      → 记录 PaymentLog + 更新处方状态为 Paid + 审计
  → 清空表单 + 显示成功消息

BillingViewModel.LoadDailyReportCommand
  → IBillingService.GetDailyReportAsync(ReportDate)
  → 按 IsReversal 区分：有效收费 = 非冲正合计 − 冲正合计
  → 填充：PrescriptionCount / TotalAmount / CashAmount / PosAmount
```

### 备份恢复流程

```
备份（手动触发）：
  MainViewModel.CreateBackupCommand
    → BackupService.CreateBackupAsync(backupDir)
        → WAL checkpoint(TRUNCATE)
        → 收集 db + wal + shm
        → Zip 压缩 → SHA-256 → 记录 BackupManifest → 审计
    → 显示备份文件名

恢复（手动触发 + 重启）：
  MainViewModel.RestoreBackupCommand
    → OpenFileDialog 选择 zip
    → 确认对话框
    → BackupService.RestoreBackupAsync(zipPath)
        → SHA-256 校验 → 解压到临时目录 → 验证文件列表
        → 写入 .pending-restore 标记文件 → 返回 true
    → 提示重启 → 重启应用

延迟恢复（应用启动时）：
  App.OnStartup → BackupService.TryExecutePendingRestore()
    → 读取 .pending-restore 标记
    → 删除现有 wal/shm → File.Copy 覆盖数据库
    → 清理标记文件和临时目录
```

### 库存管理流程

```
库存概览：
  InventoryView.Loaded → LoadDataCommand
    → GetAllStockSummaryAsync (药品库存汇总)
    → GetExpiryAlertsAsync (效期预警)
    → GetStockInHistoryAsync (入库历史)
  → 选中药品 → ViewBatchesCommand
    → GetStockBatchesAsync(drugId) (批次明细)

入库操作：
  InventoryViewModel.StockInCommand
    → CanModify 权限校验 (Doctor/Nurse)
    → 前端校验（药品/批号/数量）
    → IInventoryService.StockInAsync
        → 权限检查（RequireCanModify）
        → FluentValidation 验证
        → 事务内：记录 DrugIn + 更新/创建 DrugStock + 审计
    → 清空表单 + 刷新库存汇总和入库历史

效期预警：
  LoadExpiryAlertsCommand → GetExpiryAlertsAsync
    → 查询 30 天内即将过期且剩余量>0 的批次
    → DaysRemaining < 7 红色高亮
```

### 处方历史查询 + PDF 重打印流程

```
PrescriptionHistoryViewModel.SearchCommand
  → IPrescriptionService.GetPrescriptionHistoryAsync(keyword, fromDate, toDate)
    → 全量加载处方 → 按关键词/日期筛选 → 批量关联患者/医生名称
  → 填充 Prescriptions 列表

PrescriptionHistoryViewModel.ReprintPdfCommand (CanExecute: SelectedPrescription != null)
  → IPrescriptionService.GeneratePrescriptionPdfAsync(prescriptionId, outputDir)
    → 加载处方完整数据 → 构造 PrescriptionPdfData → IPdfService.GeneratePrescriptionPdf
  → Process.Start(UseShellExecute=true) 打开 PDF
```

### 哈希链验证流程

```
MainViewModel.VerifyAuditChainCommand
  → IAuditService.VerifyChainAsync()
    → 全量加载 AuditLog，按 Id 排序
    → 逐条验证：
        1. PrevHash == 前一条的 HashChain？
        2. HashChain == SHA-256(PayloadHash | PrevHash) 重算？
    → 首次不匹配即返回（FirstBrokenId + ErrorMessage）
  → 验证通过：显示绿色消息（共 N 条记录）
  → 验证失败：显示红色消息（断裂点 ID + 错误描述）
```

### 收费流水查询流程

```
BillingViewModel.LoadPaymentHistoryCommand
  → IBillingService.GetPaymentHistoryAsync(fromDate, toDate)
    → 全量加载 PaymentLog → 日期范围筛选 → 关联 Prescription 获取编号
    → 返回 PaymentRecordDto 列表（含 MethodText 中文）
  → 填充 PaymentHistory 列表
```

---

## 阶段一 键盘优先交互优化（新增，2026-09-16）

### 功能文档

| 文件 | 说明 |
|------|------|
| `docs/阶段一-交互优化使用说明.md` | 面向医生的功能使用手册（快捷键/模板/复制上次/快捷词） |

### 实现文件

| 功能 | 位置 |
|------|------|
| 全局快捷键（Alt+1~7） | `Presentation/MainWindow.xaml` → Window.InputBindings |
| 导航当前页高亮 | `Presentation/Styles/Buttons.xaml` → BtnNav 样式 + `MainViewModel.CurrentNavKey` |
| 处方局部快捷键（F5/Ctrl+S/N/P） | `Presentation/Views/PrescriptionView.xaml` → UserControl.InputBindings |
| 处方模板 / 复制上次处方业务逻辑 | `Presentation/ViewModels/PrescriptionViewModel.cs`（Templates/SelectedTemplate/TemplateNameInput + CopyLastPrescription/ApplySelectedTemplate/SaveCurrentAsTemplate/DeleteSelectedTemplate） |
| 处方模板本地存储（按医生隔离 JSON） | `Presentation/Services/PrescriptionTemplateStore.cs`（`usertemplates/{登录名}_templates.json`） |
| 快捷词键盘交互（数字1~9/Esc） | `Presentation/Views/PrescriptionView.xaml.cs`（HandleQuickPhraseKey / QuickPhraseIndex / TryExecuteQuickPhrase） + 依赖 `PrescriptionViewModel.cs`（CommonChiefComplaints/CommonDiagnoses） |

## 阶段二 待办进度条 + 全局反馈（新增，2026-09-16）

| 功能 | 位置 |
|------|------|
| 待办项"下一步"命令 + 跳转事件 | `Presentation/ViewModels/DashboardViewModel.cs`（HandlePendingPrescriptionCommand / NavigateToPendingHandleRequested） |
| 待办进度条 + 处方编号 + 下一步按钮 | `Presentation/Views/DashboardView.xaml`（待审核/待收费/待发药三个队列 ItemTemplate） |
| 首页跳转收费页办理区分发 | `Presentation/ViewModels/MainViewModel.cs`（OnDashboardNavigateToPendingHandle） |
| 收费页按状态预填办理区 | `Presentation/ViewModels/BillingViewModel.cs`（ProcessPendingPrescription：状态1→审核区、4→收费区、2→发药区） |
| 全局 Toast 服务 | `Presentation/Services/ToastService.cs`（静态单例 + ObservableCollection\<ToastMessage>） |
| Toast 宿主控件 | `Presentation/Views/ToastHostControl.xaml`（右下角 3s 自动消失，按类型变色），挂载于 `MainWindow.xaml` 内容区顶层 |

## 阶段三 个性化配置 · 销量报表 · 双屏布局（新增，2026-09-16）

### 功能文档

| 文件 | 说明 |
|------|------|
| `docs/阶段三-个性化配置与双屏布局使用说明.md` | 面向医生的功能使用手册（快捷词库/药品套餐/默认用药/销量Top/双屏选药） |

### 实现文件

| 功能 | 位置 |
|------|------|
| 个性化配置本地存储（按医生登录名隔离 JSON） | `Presentation/Services/DoctorPreferencesStore.cs`（`preferences/{登录名}_preferences.json`） |
| 个性化配置业务逻辑（快捷词/套餐/默认用药） | `Presentation/ViewModels/PrescriptionViewModel.Personalization.cs`（按登录名加载/覆盖内置词库） |
| 个性化配置 UI 面板 | `Presentation/Views/PrescriptionView.xaml`（⚙ 个性化设置：套餐库 + 快捷词库 + 默认用药） |
| 药品销量 Top 统计接口 | `Application/Interfaces/IServices.cs` → `IBillingService.GetTopDrugSalesAsync` + `DrugSalesDto` |
| 药品销量 Top 统计实现（多表聚合） | `Application/Services/BillingService.cs` → `GetTopDrugSalesAsync`（Paid/Dispensed 成交口径） |
| 销量 Top 数据加载 / 范围切换 | `Presentation/ViewModels/DashboardViewModel.cs`（TopDrugSales / SelectedSalesRange / SalesMaxAmount / RefreshTopDrugSalesAsync） |
| 销量 Top 条形图 UI | `Presentation/Views/DashboardView.xaml`（药品销量 Top 区块，ProgressBar 模拟条形图，近7天/30天/本月切换） |
| 双屏选药命令 / 状态 / 事件 | `Presentation/ViewModels/PrescriptionViewModel.cs`（ToggleDrugPickerSecondaryCommand / IsDrugPickerSecondaryOpen / OpenDrugPickerSecondaryRequested） |
| 双屏窗口 UI | `Presentation/Views/DrugPickerSecondaryWindow.xaml`（药品列表 + 分类筛选 + 当前处方明细概览） |
| 双屏窗口逻辑（共享 ViewModel、双击添加） | `Presentation/Views/DrugPickerSecondaryWindow.xaml.cs` |
| 双屏窗口生命周期（打开/关闭/事件绑定） | `Presentation/Views/PrescriptionView.xaml.cs`（PrescriptionView_DataContextChanged / OnOpenDrugPickerSecondary / Unloaded） |

## 阶段三修补：P0 安全 + UI 渲染修复（2026-09-16）

### 安全与健壮性

| 修复 | 位置 |
|------|------|
| 库存并发共享互斥锁 | `Application/Services/InventoryLock.cs`（`InventoryLock.Instance`），应用于 入库/出库/扣减/退库 全链路 |
| 事务隔离升级 Serializable（BEGIN IMMEDIATE） | `Infrastructure/Repositories/UnitOfWork.cs` → `BeginTransactionAsync` |
| 软删除唯一索引过滤 | `Infrastructure/Data/ClinicDbContext.cs`（5 个唯一索引加 `[deleted_at] IS NULL`） |
| 密钥/Pepper 外置 | 从 `appsettings.json` 迁至 gitignored `secrets/`（`encryption.key` / `pepper.key`） |

### UI 渲染

| 修复 | 位置 |
|------|------|
| `SurfaceBgBrush` 缺失崩溃（级联异常弹窗堆叠） | `Styles/Colors.xaml` → 新增 `SurfaceBgBrush` |
| 新患者字段文字被布局裁剪不可见 | `Styles/Inputs.xaml` → DatePicker 模板 DatePickerTextBox Padding 改紧凑；`Views/PrescriptionView.xaml` → 性别 ComboBox 加宽、过敏史/基础疾病 TextBox 加紧凑 Padding |
| 处方笺姓名/性别不同步（新患者回退为「—」） | `ViewModels/PrescriptionViewModel.Patient.cs` → `PrescriptionPatientName`/`PrescriptionPatientGender` 显示属性；`Views/PrescriptionView.xaml` 处方笺绑定改用该属性 |
