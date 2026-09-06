# AGENTS.md

> 本文件为AI助手（如Doubao、Cursor、Copilot等）提供项目工作指导，确保AI生成的代码符合项目规范。

## 项目概览

- **项目名称**：陈医生诊所处方系统
- **类型**：.NET WPF 桌面应用
- **目标框架**：.NET 9（`net9.0` / `net9.0-windows`）
- **数据库**：SQLite（WAL模式）
- **架构**：Clean Architecture（5层）

## 关键路径

| 项目 | 路径 |
|------|------|
| 解决方案 | `Clinic.sln` |
| 领域层 | `src/Clinic.Domain/` |
| 应用层 | `src/Clinic.Application/` |
| 基础设施层 | `src/Clinic.Infrastructure/` |
| 表现层 | `src/Clinic.Presentation/` |
| 共享层 | `src/Clinic.Shared/` |
| 单元测试 | `tests/Clinic.UnitTests/` |
| 集成测试 | `tests/Clinic.IntegrationTests/` |
| 数据库 | `clinic.db`（不纳入版本控制） |
| 可执行文件 | `src/Clinic.Presentation/bin/Debug/net9.0-windows/Clinic.Presentation.exe` |

## 构建与运行

### 构建

```bash
dotnet build Clinic.sln
```

### 测试

```bash
dotnet test tests/Clinic.UnitTests/Clinic.UnitTests.csproj
```

### 运行

```bash
dotnet run --project src/Clinic.Presentation/Clinic.Presentation.csproj
```

### 编译前注意

如果程序正在运行，需要先停止进程，否则会出现文件锁定错误：

```powershell
Get-Process -Name "Clinic.Presentation" | Stop-Process -Force
```

## 架构约束（必须遵守）

### 分层依赖规则

```
Presentation → Application → Domain ← Infrastructure
                    ↓
                  Shared
```

- **Domain层**：零外部依赖，不引用任何NuGet包
- **Application层**：只依赖Domain和Shared，不依赖Infrastructure
- **Infrastructure层**：实现Domain/Application定义的接口
- **Presentation层**：只调用Application层服务，不直接操作数据库
- **Shared层**：枚举、常量，不引用其他层

### 违反示例

```csharp
// ❌ 错误：Application层引用了Infrastructure
using Clinic.Infrastructure.Data;

// ❌ 错误：Presentation层直接操作DbContext
var db = new ClinicDbContext();
var patients = db.Patients.ToList();

// ✅ 正确：Presentation层调用Application服务
var patients = await _patientService.GetAllAsync();
```

## 编码规范

### 命名

- 类/方法/属性：PascalCase
- 私有字段：_camelCase
- 接口：I前缀
- 异步方法：Async后缀

### MVVM模式

- ViewModel继承 `ViewModelBase`（`src/Clinic.Presentation/ViewModels/ViewModelBase.cs`）
- 使用CommunityToolkit.Mvvm源生成器：`[ObservableProperty]`、`[RelayCommand]`
- 禁止在代码后台（xaml.cs）写业务逻辑

### WPF绑定陷阱

- `ComboBox.SelectedValue` 和 `DatePicker.SelectedDate` 默认是 `OneWay`，**必须显式 `Mode=TwoWay`**
- `TextBox.Text` 默认是 `TwoWay`
- `partial class` 拆分时，`[NotifyCanExecuteChangedFor]` 字段必须和对应的 `[RelayCommand]` 方法在同一个文件中

### WPF样式陷阱

- 同一ResourceDictionary中**不允许**两个相同TargetType的隐式样式（无x:Key），会抛 `ArgumentException`
- 颜色、字体、尺寸必须使用资源，不硬编码

### 异常处理

- 禁止空catch块
- 使用 `ILogger` 记录日志，禁止 `Console.WriteLine` 和 `Debug.WriteLine`
- 使用 `IDialogService` 显示消息，禁止直接调用 `MessageBox`

### 金额计算（重要）

- 药品金额按**整盒计价**：包装数量>1时 `ceil(数量/包装数量) × 单价`
- 总金额 = 药品明细金额 + 诊疗费（ConsultationFee）
- 保存处方时**必须重新计算**所有明细金额，不信任前端传入的Subtotal
- 使用 `decimal` 类型，不用 `double`
- 四舍五入使用 `MidpointRounding.AwayFromZero`

## 关键实体

### Prescription（处方）

```csharp
public class Prescription : Common.Entity
{
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    public long? MedicalRecordId { get; set; }  // 关联病历
    public string DiagnosisText { get; set; } = string.Empty;
    public decimal ConsultationFee { get; set; }  // 诊疗费
    public decimal TotalAmount { get; set; }
    public PrescriptionStatus Status { get; set; }
    // 体征字段（待迁移到病历表）
    public decimal? Weight { get; set; }
    public decimal? Temperature { get; set; }
    public int? SystolicBP { get; set; }
    public int? DiastolicBP { get; set; }
    public int? HeartRate { get; set; }
}
```

### PrescriptionItem（处方明细）

```csharp
public class PrescriptionItem : Common.Entity
{
    public long PrescriptionId { get; set; }
    public long DrugId { get; set; }
    public string DrugName { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal PackQuantity { get; set; } = 1;  // 每包装数量
    public decimal Subtotal { get; set; }
}
```

### 处方状态

```csharp
public enum PrescriptionStatus
{
    Draft = 0,      // 草稿
    Saved = 1,      // 已保存/待审核（不扣库存）
    Reviewed = 4,   // 已审核/待收费
    Paid = 2,       // 已收费/待发药
    Dispensed = 5,  // 已发药（扣库存）
    Voided = 3      // 已作废
}
```

## AI辅助功能

- LLM服务接口：`ILlmService`（Application层）
- 本地实现：`LlamaCppLlmService`（Infrastructure层）
- 无模型时：`NoOpLlmService`（不影响核心功能）
- llama.cpp配置：Host=127.0.0.1，Port=8080
- 模型路径：`D:\llama.cpp\models\qwen2.5-7b-instruct-q4_k_m.gguf`

## 测试账号

- 用户名：`admin`
- 密码：`admin123`

## 文档索引

| 文档 | 位置 |
|------|------|
| 贡献指南 | `CONTRIBUTING.md` |
| 开发流程 | `docs/development-workflow.md` |
| 代码规范 | `docs/coding-standards.md` |
| Git工作流 | `docs/git-workflow.md` |
| 前端审查报告 | `testdata/前端代码全面审查报告.md` |
| 批判式审查报告 | `testdata/批判式深度审查报告.md` |
| 变更日志 | `CHANGELOG.md` |

## 提交规范

使用Conventional Commits：

```
<type>(<scope>): <subject>

type: feat | fix | docs | style | refactor | perf | test | build | ci | chore
scope: prescription | billing | patient | inventory | ui | database | ai | auth
```

## AI助手工作原则

1. **先读后写**：修改文件前先读取，了解上下文
2. **小步修改**：每次修改一个功能点，不一次性大改
3. **验证优先**：修改后必须构建验证，不提交无法编译的代码
4. **测试同步**：修改业务逻辑必须同步更新或新增单元测试
5. **不越界**：严格遵守架构分层，不跨层调用
6. **可追溯**：提交信息清晰，关联Issue
7. **安全第一**：不硬编码密码/密钥，不提交敏感信息

## 常见陷阱

1. **WPF ComboBox/DatePicker绑定**：必须显式 `Mode=TwoWay`，否则值不回传
2. **WPF隐式样式重复**：同一ResourceDictionary中不能有两个相同TargetType的隐式样式
3. **partial class源生成器**：`[NotifyCanExecuteChangedFor]` 和 `[RelayCommand]` 必须在同一文件
4. **SQLite并发**：使用WAL模式，写操作需要锁
5. **金额计算**：前端和后端必须使用相同的计价逻辑（按整盒）
6. **诊疗费**：Prescription实体有ConsultationFee字段，保存时必须包含在TotalAmount中
