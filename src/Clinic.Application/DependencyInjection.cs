using Clinic.Application.Interfaces;
using Clinic.Application.Services;
using Clinic.Application.Session;
using Clinic.Application.Validators;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Application;

/// <summary>
/// 应用层 DI 注册扩展方法。
/// 在 App.xaml.cs 中通过 services.AddApplication() 调用。
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // 会话状态（Singleton：跨操作范围保持登录状态）
        services.AddSingleton<IUserSession, UserSession>();

        // 权限检查器（Scoped：依赖 IUserSession，跟随服务生命周期）
        services.AddScoped<IPermissionChecker, PermissionChecker>();

        // 审计日志服务（Scoped：依赖 IUserSession + IUnitOfWork）
        services.AddScoped<IAuditService, AuditService>();

        // 应用服务（Scoped：每个操作范围独立实例，共享 DbContext）
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPatientService, PatientService>();
        services.AddScoped<IPrescriptionService, PrescriptionService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IBillingService, BillingService>();

        // FluentValidation 验证器（Scoped：无状态，跟随服务生命周期）
        services.AddScoped<IValidator<LoginRequest>, LoginValidator>();
        services.AddScoped<IValidator<CreatePatientRequest>, CreatePatientValidator>();
        services.AddScoped<IValidator<CreatePrescriptionRequest>, CreatePrescriptionValidator>();
        services.AddScoped<IValidator<AddPrescriptionItemRequest>, AddPrescriptionItemValidator>();
        services.AddScoped<IValidator<StockInRequest>, StockInValidator>();
        services.AddScoped<IValidator<RecordPaymentRequest>, RecordPaymentValidator>();

        return services;
    }
}
