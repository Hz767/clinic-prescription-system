using FluentValidation;

namespace Clinic.Application.Validators;

// ── FluentValidation 验证器：每个业务操作一个验证器 ──

/// <summary>登录验证：用户名和密码不能为空</summary>
public class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("用户名不能为空");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("密码不能为空");
    }
}

/// <summary>创建患者验证：姓名、性别、手机号必填，格式校验</summary>
public class CreatePatientValidator : AbstractValidator<CreatePatientRequest>
{
    public CreatePatientValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("患者姓名不能为空")
            .MaximumLength(50).WithMessage("患者姓名不能超过 50 个字符");

        RuleFor(x => x.Gender)
            .NotEmpty().WithMessage("性别不能为空")
            .Must(g => g is "男" or "女").WithMessage("性别必须为「男」或「女」");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("联系电话不能为空")
            .Matches(@"^1\d{10}$").WithMessage("手机号格式不正确（应为 11 位数字，以 1 开头）");

        RuleFor(x => x.Dob)
            .LessThan(DateOnly.FromDateTime(DateTime.Today))
            .WithMessage("出生日期不能晚于今天")
            .When(x => x.Dob.HasValue);

        RuleFor(x => x.Allergies)
            .MaximumLength(500).WithMessage("过敏史不能超过 500 个字符")
            .When(x => !string.IsNullOrEmpty(x.Allergies));

        RuleFor(x => x.History)
            .MaximumLength(2000).WithMessage("病史不能超过 2000 个字符")
            .When(x => !string.IsNullOrEmpty(x.History));

        RuleFor(x => x.ChronicTags)
            .MaximumLength(200).WithMessage("慢病标签不能超过 200 个字符")
            .When(x => !string.IsNullOrEmpty(x.ChronicTags));
    }
}

/// <summary>创建处方验证：患者/医生 ID 有效，诊断不能为空</summary>
public class CreatePrescriptionValidator : AbstractValidator<CreatePrescriptionRequest>
{
    public CreatePrescriptionValidator()
    {
        RuleFor(x => x.PatientId)
            .GreaterThan(0).WithMessage("无效的患者 ID");

        RuleFor(x => x.DoctorId)
            .GreaterThan(0).WithMessage("无效的医生 ID");

        RuleFor(x => x.DiagnosisText)
            .NotEmpty().WithMessage("诊断不能为空")
            .MaximumLength(500).WithMessage("诊断内容不能超过 500 个字符");

        RuleFor(x => x.ChiefComplaint)
            .MaximumLength(500).WithMessage("主诉不能超过 500 个字符")
            .When(x => !string.IsNullOrEmpty(x.ChiefComplaint));

        // P1：添加诊断编码长度限制
        RuleFor(x => x.DiagnosisCode)
            .MaximumLength(50).WithMessage("诊断编码不能超过 50 个字符")
            .When(x => !string.IsNullOrEmpty(x.DiagnosisCode));

        // P1：添加延长理由长度限制
        RuleFor(x => x.ExtendedReason)
            .MaximumLength(500).WithMessage("延长用药理由不能超过 500 个字符")
            .When(x => !string.IsNullOrEmpty(x.ExtendedReason));

        RuleFor(x => x.PrescriptionType)
            .InclusiveBetween(0, 2).WithMessage("处方类型无效（0=普通，1=急诊，2=儿科）");

        // 本次就诊体征验证（可选字段，填写时检查范围合理性）
        RuleFor(x => x.Weight)
            .InclusiveBetween(0.1m, 500m).WithMessage("体重应在 0.1~500 kg 之间")
            .When(x => x.Weight.HasValue);

        RuleFor(x => x.Temperature)
            .InclusiveBetween(30m, 45m).WithMessage("体温应在 30~45 °C 之间")
            .When(x => x.Temperature.HasValue);

        RuleFor(x => x.SystolicBP)
            .InclusiveBetween(40, 250).WithMessage("收缩压应在 40~250 mmHg 之间")
            .When(x => x.SystolicBP.HasValue);

        RuleFor(x => x.DiastolicBP)
            .InclusiveBetween(20, 150).WithMessage("舒张压应在 20~150 mmHg 之间")
            .When(x => x.DiastolicBP.HasValue);

        RuleFor(x => x.HeartRate)
            .InclusiveBetween(20, 250).WithMessage("心率应在 20~250 次/分之间")
            .When(x => x.HeartRate.HasValue);

        // ExtendedReason 为可选项：普通处方超过 7 天用药时需填写
        // 具体天数校验在 AddPrescriptionItemAsync 中根据处方类型动态检查
    }
}

/// <summary>添加处方明细验证：药品 ID、剂量、频次、数量等必须有效</summary>
public class AddPrescriptionItemValidator : AbstractValidator<AddPrescriptionItemRequest>
{
    public AddPrescriptionItemValidator()
    {
        RuleFor(x => x.PrescriptionId)
            .GreaterThan(0).WithMessage("无效的处方 ID");

        RuleFor(x => x.DrugId)
            .GreaterThan(0).WithMessage("无效的药品 ID");

        RuleFor(x => x.Dose)
            .GreaterThan(0).WithMessage("剂量必须大于 0")
            .LessThanOrEqualTo(9999).WithMessage("剂量不能超过 9999");

        RuleFor(x => x.DoseUnit)
            .NotEmpty().WithMessage("剂量单位不能为空")
            .MaximumLength(20).WithMessage("剂量单位不能超过 20 个字符");

        RuleFor(x => x.Frequency)
            .NotEmpty().WithMessage("用药频次不能为空")
            .MaximumLength(50).WithMessage("用药频次不能超过 50 个字符");

        RuleFor(x => x.Route)
            .NotEmpty().WithMessage("给药途径不能为空")
            .MaximumLength(50).WithMessage("给药途径不能超过 50 个字符");

        // P1：DurationDays 上限改为 84（延长用药最大天数）
        RuleFor(x => x.DurationDays)
            .InclusiveBetween(1, 84).WithMessage("疗程天数应在 1-84 天之间");

        // P1：添加 Qty 上限校验
        RuleFor(x => x.Qty)
            .GreaterThan(0).WithMessage("数量必须大于 0")
            .LessThanOrEqualTo(999).WithMessage("数量不能超过 999");
    }
}

/// <summary>入库验证：药品 ID、批号、效期、数量校验</summary>
public class StockInValidator : AbstractValidator<StockInRequest>
{
    public StockInValidator()
    {
        RuleFor(x => x.DrugId)
            .GreaterThan(0).WithMessage("无效的药品 ID");

        RuleFor(x => x.BatchNo)
            .NotEmpty().WithMessage("批号不能为空")
            .MaximumLength(50).WithMessage("批号不能超过 50 个字符");

        RuleFor(x => x.ExpiryDate)
            .GreaterThan(DateOnly.FromDateTime(DateTime.Today))
            .WithMessage("药品已过期，效期必须晚于今天");

        RuleFor(x => x.Qty)
            .GreaterThan(0).WithMessage("入库数量必须大于 0");

        RuleFor(x => x.CostPrice)
            .GreaterThanOrEqualTo(0).WithMessage("成本价不能为负数");

        // P1：添加供应商长度限制
        RuleFor(x => x.Supplier)
            .MaximumLength(200).WithMessage("供应商名称不能超过 200 个字符")
            .When(x => !string.IsNullOrEmpty(x.Supplier));

        RuleFor(x => x.OperatorId)
            .GreaterThan(0).WithMessage("无效的操作人 ID");
    }
}

/// <summary>收费验证：处方 ID、收费方式、金额校验</summary>
public class RecordPaymentValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentValidator()
    {
        RuleFor(x => x.PrescriptionId)
            .GreaterThan(0).WithMessage("无效的处方 ID");

        RuleFor(x => x.Method)
            .InclusiveBetween(0, 1).WithMessage("收费方式无效（0=现金，1=POS）");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("收费金额必须大于 0");

        RuleFor(x => x.OperatorId)
            .GreaterThan(0).WithMessage("无效的操作人 ID");

        // POS 收费必须有序列号
        RuleFor(x => x.PosSerialNo)
            .NotEmpty().WithMessage("POS 收费必须填写终端序列号")
            .MaximumLength(100).WithMessage("POS 终端序列号不能超过 100 个字符")
            .When(x => x.Method == 1);

        // P1：添加备注长度限制
        RuleFor(x => x.Note)
            .MaximumLength(500).WithMessage("备注不能超过 500 个字符")
            .When(x => !string.IsNullOrEmpty(x.Note));
    }
}
