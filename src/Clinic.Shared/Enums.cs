namespace Clinic.Shared.Enums;

/// <summary>用户角色</summary>
public enum UserRole
{
    Doctor = 0,
    Nurse = 1,
    Readonly = 2
}

/// <summary>收费方式</summary>
public enum PaymentMethod
{
    Cash = 0,
    Pos = 1
}

/// <summary>处方类型</summary>
public enum PrescriptionType
{
    Normal = 0,
    Emergency = 1,
    Pediatric = 2
}

/// <summary>处方状态</summary>
public enum PrescriptionStatus
{
    /// <summary>草稿：处方创建后未保存，可编辑明细</summary>
    Draft = 0,
    /// <summary>已保存：处方已确认，库存已扣减，不可再修改</summary>
    Saved = 1,
    /// <summary>已收费：处方已完成收费</summary>
    Paid = 2,
    /// <summary>已作废：处方已作废</summary>
    Voided = 3,
    /// <summary>已审核：药师已审核通过，可收费（《处方管理办法》要求药师审核）</summary>
    Reviewed = 4
}

/// <summary>药物交互风险等级（DDInter 2.0）</summary>
public enum DrugInteractionLevel
{
    Major = 0,
    Moderate = 1,
    Minor = 2,
    Unknown = 3
}

/// <summary>抗菌药物分级</summary>
public enum AntibioticLevel
{
    None = 0,
    NonRestricted = 1,
    Restricted = 2,
    Special = 3
}

/// <summary>盘点模式</summary>
public enum StockCheckMode
{
    Spot = 0,
    Full = 1
}

/// <summary>随访状态</summary>
public enum FollowupStatus
{
    Pending = 0,
    Done = 1,
    Cancelled = 2
}

/// <summary>随访渠道</summary>
public enum FollowupChannel
{
    Onsite = 0,
    Phone = 1
}

/// <summary>打印模板类型</summary>
public enum TemplateKind
{
    Prescription = 0,
    Receipt = 1,
    MedicalRecord = 2
}
