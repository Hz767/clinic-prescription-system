# 代码规范

> 本文档定义了项目的代码编写规范，所有提交的代码必须符合本规范。

## 目录

- [通用原则](#通用原则)
- [命名规范](#命名规范)
- [C# 代码规范](#c-代码规范)
- [XAML 代码规范](#xaml-代码规范)
- [架构规范](#架构规范)
- [异常处理规范](#异常处理规范)
- [日志规范](#日志规范)
- [测试规范](#测试规范)
- [数据库规范](#数据库规范)

---

## 通用原则

### 1. 可读性优先

代码是写给人看的，顺便让机器执行。

```csharp
// ❌ 不好
var r = items.Sum(i => i.Qty * i.P);

// ✅ 好
var totalAmount = prescriptionItems.Sum(item => item.Quantity * item.UnitPrice);
```

### 2. 单一职责

每个类、方法、模块只做一件事。

```csharp
// ❌ 不好：一个方法做了验证、计算、保存、通知
public void ProcessOrder(Order order)
{
    if (order.Items.Count == 0) throw ...;
    var total = CalculateTotal(order);
    _db.Save(order);
    _email.SendNotification(order);
}

// ✅ 好：每个方法一个职责
public void ProcessOrder(Order order)
{
    ValidateOrder(order);
    CalculateTotal(order);
    SaveOrder(order);
    SendNotification(order);
}
```

### 3. 避免重复（DRY）

相同的逻辑不要写两遍，提取为方法或服务。

### 4. 延迟决策

不要过早优化，先让代码正确、清晰，再考虑性能。

---

## 命名规范

### 类型命名

| 类型 | 规范 | 示例 |
|------|------|------|
| 类/接口 | PascalCase | `PrescriptionService`、`IPatientRepository` |
| 接口 | I前缀 + PascalCase | `IRepository<T>`、`ILogger` |
| 方法 | PascalCase | `CalculateTotalAmount()` |
| 属性 | PascalCase | `TotalAmount`、`PatientName` |
| 公共字段 | PascalCase | `MaxRetries` |
| 私有字段 | _camelCase | `_prescriptionRepository`、`_logger` |
| 局部变量 | camelCase | `totalAmount`、`patientList` |
| 常量 | PascalCase | `MaxRetryCount` |
| 枚举 | PascalCase | `PrescriptionStatus` |
| 枚举值 | PascalCase | `Saved`、`Paid`、`Dispensed` |

### 命名原则

1. **使用完整单词**，不使用缩写（除非是通用缩写如Id、Url）
2. **布尔值用Is/Has/Can前缀**：`IsActive`、`HasPermission`、`CanEdit`
3. **集合用复数**：`Patients`、`PrescriptionItems`
4. **异步方法加Async后缀**：`GetPatientAsync()`

### 命名示例

```csharp
// ❌ 不好
public class PtlSvc
{
    private readonly IPtlRepo _repo;
    public async Task<PtlDto> GetPtl(long id) { }
}

// ✅ 好
public class PatientService
{
    private readonly IPatientRepository _patientRepository;
    public async Task<PatientDto> GetPatientAsync(long patientId) { }
}
```

---

## C# 代码规范

### 文件结构

每个文件一个类，文件名与类名一致。

```
PrescriptionService.cs → public class PrescriptionService
```

### using 排序

```csharp
// 1. System命名空间
using System;
using System.Collections.Generic;
using System.Linq;

// 2. 第三方命名空间
using Microsoft.EntityFrameworkCore;

// 3. 项目命名空间
using Clinic.Domain.Entities;
using Clinic.Application.Interfaces;
```

### 类成员顺序

```csharp
public class PrescriptionService
{
    // 1. 常量
    private const int MaxRetries = 3;

    // 2. 私有字段
    private readonly IPrescriptionRepository _repository;
    private readonly ILogger _logger;

    // 3. 构造函数
    public PrescriptionService(IPrescriptionRepository repository, ILogger logger) { }

    // 4. 公共属性
    public int ActiveCount { get; private set; }

    // 5. 公共方法
    public async Task<Prescription> GetByIdAsync(long id) { }

    // 6. 私有方法
    private void ValidatePrescription(Prescription prescription) { }
}
```

### 方法规范

- 方法长度 **不超过50行**，超过则拆分
- 参数数量 **不超过5个**，超过则使用参数对象
- 使用 `async/await`，不使用 `.Result` 或 `.Wait()`

```csharp
// ❌ 不好
public async Task<Result> Process(long patientId, long doctorId, string diagnosis, 
    string notes, decimal fee, bool urgent, string category, string priority)

// ✅ 好
public async Task<Result> Process(ProcessPrescriptionRequest request)
```

### 空值处理

```csharp
// ✅ 使用可空引用类型
public string? Diagnosis { get; set; }
public Patient Patient { get; set; } = null!;

// ✅ 参数验证
public async Task<Patient> GetPatientAsync(long patientId)
{
    if (patientId <= 0)
        throw new ArgumentException("患者ID必须大于0", nameof(patientId));

    var patient = await _repository.GetByIdAsync(patientId);
    return patient ?? throw new NotFoundException($"患者 {patientId} 不存在");
}
```

### 集合初始化

```csharp
// ✅ 使用集合初始化器
var items = new List<PrescriptionItem>
{
    new() { DrugName = "阿莫西林", Quantity = 9 },
    new() { DrugName = "布洛芬", Quantity = 6 }
};

// ✅ 使用表达式体成员
public bool IsValid => !string.IsNullOrWhiteSpace(DiagnosisText);
```

### LINQ规范

```csharp
// ✅ 优先使用方法语法
var activePatients = patients
    .Where(p => p.IsActive)
    .OrderBy(p => p.Name)
    .ToList();

// ❌ 避免复杂的查询语法嵌套
```

---

## XAML 代码规范

### 格式规范

- 属性按字母顺序排列
- 使用4空格缩进
- 每个属性一行（复杂控件）

```xml
<!-- ✅ 好 -->
<TextBox
    AutomationProperties.Name="患者姓名"
    Grid.Column="1"
    Margin="0,0,0,8"
    Text="{Binding PatientName, UpdateSourceTrigger=PropertyChanged}"
    Style="{StaticResource TextBoxPrimary}" />
```

### 资源引用

- 颜色、字体、尺寸必须使用资源，不硬编码
- 样式定义在 `Styles/` 目录的对应文件中

```xml
<!-- ❌ 不好 -->
<TextBlock FontSize="14" Foreground="#333333" Text="姓名" />

<!-- ✅ 好 -->
<TextBlock Style="{StaticResource TextBody}" Text="姓名" />
```

### 绑定规范

```xml
<!-- ✅ 显式指定Mode和UpdateSourceTrigger -->
<TextBox Text="{Binding PatientName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />

<!-- ✅ ComboBox和DatePicker必须显式TwoWay（默认是OneWay） -->
<ComboBox SelectedValue="{Binding Gender, Mode=TwoWay}" />
<DatePicker SelectedDate="{Binding Dob, Mode=TwoWay}" />
```

### 命名规范

- x:Name 使用 camelCase
- 资源键使用 PascalCase + 类型后缀

```xml
<TextBox x:Name="patientNameTextBox" />
<SolidColorBrush x:Key="PrimaryBrush" Color="#2563EB" />
<Style x:Key="TextBoxPrimary" TargetType="TextBox">
```

---

## 架构规范

### 分层依赖

```
Presentation → Application → Domain ← Infrastructure
                    ↓
                  Shared
```

### 各层职责

| 层 | 职责 | 不允许 |
|----|------|--------|
| Domain | 实体、值对象、领域接口、领域逻辑 | 引用任何外部库 |
| Application | 用例编排、DTO、验证器、服务接口 | 引用Infrastructure |
| Infrastructure | EF Core、仓储实现、外部服务、加密 | 包含业务逻辑 |
| Presentation | WPF Views、ViewModels、样式 | 直接操作数据库 |
| Shared | 枚举、常量、通用工具 | 引用其他层 |

### 依赖注入

- 所有服务通过构造函数注入
- 接口定义在Application/Domain层，实现在Infrastructure层
- 注册在各层的 `DependencyInjection.cs` 中

```csharp
// Application层定义接口
public interface IPrescriptionService
{
    Task<long> CreatePrescriptionAsync(...);
}

// Infrastructure层实现
public class PrescriptionService : IPrescriptionService { }

// 注册
public static IServiceCollection AddInfrastructure(this IServiceCollection services)
{
    services.AddScoped<IPrescriptionService, PrescriptionService>();
    return services;
}
```

### ViewModel规范

- 继承 `ViewModelBase`
- 使用 `CommunityToolkit.Mvvm` 源生成器
- 属性使用 `[ObservableProperty]`
- 命令使用 `[RelayCommand]`

```csharp
public partial class PrescriptionViewModel : ViewModelBase
{
    [ObservableProperty]
    private PatientDto? _selectedPatient;

    [ObservableProperty]
    private decimal _consultationFee;

    [RelayCommand]
    private async Task SavePrescriptionAsync()
    {
        await ExecuteAsync(async () =>
        {
            // 业务逻辑
        });
    }
}
```

---

## 异常处理规范

### 原则

1. **不吞异常**：空catch块是严重错误
2. **具体异常**：捕获具体异常类型，不捕获 `Exception` 除非是顶层
3. **日志记录**：捕获异常必须记录日志
4. **用户友好**：向用户显示友好的错误信息，不显示堆栈跟踪

```csharp
// ❌ 不好：吞异常
try
{
    SavePrescription();
}
catch { }

// ❌ 不好：捕获Exception且不记录
try
{
    SavePrescription();
}
catch (Exception ex)
{
    MessageBox.Show("出错了");
}

// ✅ 好
try
{
    await SavePrescriptionAsync();
}
catch (ValidationException ex)
{
    _logger.LogWarning(ex, "处方验证失败");
    await _dialogService.ShowWarningAsync("处方验证失败", ex.Message);
}
catch (Exception ex)
{
    _logger.LogError(ex, "保存处方时发生未处理异常");
    await _dialogService.ShowErrorAsync("保存失败", "系统错误，请联系管理员");
}
```

### 自定义异常

```csharp
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class PrescriptionValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }
    public PrescriptionValidationException(IReadOnlyList<string> errors)
        : base("处方验证失败")
    {
        Errors = errors;
    }
}
```

---

## 日志规范

### 日志级别

| 级别 | 使用场景 |
|------|---------|
| Trace | 最详细的调试信息（默认不启用） |
| Debug | 调试信息，开发阶段使用 |
| Information | 正常业务流程记录 |
| Warning | 可恢复的异常、不预期但可处理的情况 |
| Error | 错误，当前操作失败 |
| Critical | 严重错误，系统可能无法继续运行 |

### 日志规范

```csharp
// ✅ 使用结构化日志
_logger.LogInformation("保存处方成功，处方ID：{PrescriptionId}，金额：{Amount}", 
    prescription.Id, prescription.TotalAmount);

// ❌ 不好：字符串拼接
_logger.LogInformation("保存处方成功，处方ID：" + prescription.Id);

// ✅ 异常日志包含异常对象
_logger.LogError(ex, "保存处方失败，处方ID：{PrescriptionId}", prescription.Id);
```

### 禁止

- ❌ 使用 `Console.WriteLine`
- ❌ 使用 `Debug.WriteLine`（生产环境）
- ❌ 记录敏感信息（密码、身份证号）

---

## 测试规范

### 测试项目结构

```
tests/
├── Clinic.UnitTests/          # 单元测试
│   ├── DTOs/
│   ├── Services/
│   └── ViewModels/
└── Clinic.IntegrationTests/   # 集成测试
    └── Services/
```

### 测试命名

```
方法名_场景_预期结果

示例：
SavePrescriptionAsync_WithConsultationFee_IncludesInTotal
AddPrescriptionItemAsync_PackQuantityGreaterThanOne_CalculatesWholePack
```

### 测试结构（AAA）

```csharp
[Fact]
public async Task MethodName_Scenario_ExpectedResult()
{
    // Arrange
    var service = CreateService();
    var input = CreateTestInput();

    // Act
    var result = await service.ProcessAsync(input);

    // Assert
    result.Should().NotBeNull();
    result.Status.Should().Be(Status.Success);
}
```

### Mock规范

```csharp
// ✅ 使用Moq
var mockRepository = new Mock<IPatientRepository>();
mockRepository
    .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
    .ReturnsAsync(new Patient { Id = 1, Name = "张三" });

// ✅ 验证调用
mockRepository.Verify(r => r.SaveAsync(It.IsAny<Patient>()), Times.Once);
```

---

## 数据库规范

### 表命名

- 使用小写+下划线：`prescription`、`prescription_item`、`medical_record`
- 表名用单数：`patient`（不是patients）

### 列命名

- 使用小写+下划线：`patient_id`、`total_amount`、`created_at`
- 主键：`id`
- 外键：`{表名}_id`
- 时间戳：`created_at`、`updated_at`

### 实体规范

```csharp
public class Prescription : Common.Entity
{
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    public string DiagnosisText { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public PrescriptionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### 迁移规范

- 数据库结构变更必须有迁移脚本
- 迁移脚本放在 `Infrastructure/Data/Migrations/`
- 迁移必须可回滚
- 破坏性变更（删除列/表）必须先废弃再删除

---

## 附录：代码检查清单

提交前自查：

- [ ] 命名清晰，无缩写
- [ ] 方法不超过50行
- [ ] 无硬编码的魔法数字/字符串
- [ ] 无空catch块
- [ ] 使用ILogger，无Console.WriteLine
- [ ] async方法使用await，无.Result/.Wait()
- [ ] 参数验证完善
- [ ] 资源释放正确（using语句）
- [ ] XAML无硬编码颜色/字体
- [ ] 绑定Mode正确（ComboBox/DatePicker显式TwoWay）
- [ ] 单元测试通过
- [ ] 无编译器警告
