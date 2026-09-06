using Clinic.Application.DTOs;
using Xunit;

namespace Clinic.UnitTests.DTOs;

/// <summary>
/// PrescriptionItemDto 金额计算逻辑单元测试。
/// 验证：按盒计价、按单位计价、包装数量解析、小计计算。
/// </summary>
public class PrescriptionItemDtoTests
{
    [Fact]
    public void ParsePackQuantity_标准规格_返回正确数量()
    {
        // 常见规格格式
        Assert.Equal(12m, PrescriptionItemDto.ParsePackQuantity("0.25g*12片/盒"));
        Assert.Equal(24m, PrescriptionItemDto.ParsePackQuantity("0.5g*24粒/盒"));
        Assert.Equal(10m, PrescriptionItemDto.ParsePackQuantity("10mg*10片/板*2板/盒"));
    }

    [Fact]
    public void ParsePackQuantity_无数字_返回默认1()
    {
        Assert.Equal(1m, PrescriptionItemDto.ParsePackQuantity(""));
        Assert.Equal(1m, PrescriptionItemDto.ParsePackQuantity("片剂"));
        Assert.Equal(1m, PrescriptionItemDto.ParsePackQuantity(null!));
    }

    [Fact]
    public void RecalculateSubtotalOnly_按盒计价_计算正确()
    {
        // 一盒10元，一盒12片，开9片 → 应该按1盒计价 = 10元
        var item = new PrescriptionItemDto
        {
            DrugName = "盐酸环丙沙星片",
            Spec = "0.25g*12片/盒",
            UnitPrice = 10m,
            PackQuantity = 12m,
            Qty = 9m,
            Dose = 1m,
            DurationDays = 3
        };

        item.RecalculateSubtotalOnly();

        // 9片需要1盒（向上取整），1盒 × 10元 = 10元
        Assert.Equal(10m, item.Subtotal);
    }

    [Fact]
    public void RecalculateSubtotalOnly_整盒数量_计算正确()
    {
        // 一盒10元，开24片 = 2盒 → 20元
        var item = new PrescriptionItemDto
        {
            DrugName = "阿莫西林胶囊",
            Spec = "0.25g*12粒/盒",
            UnitPrice = 10m,
            PackQuantity = 12m,
            Qty = 24m
        };

        item.RecalculateSubtotalOnly();

        Assert.Equal(20m, item.Subtotal);
    }

    [Fact]
    public void RecalculateSubtotalOnly_零数量_返回零()
    {
        var item = new PrescriptionItemDto
        {
            UnitPrice = 10m,
            PackQuantity = 12m,
            Qty = 0m
        };

        item.RecalculateSubtotalOnly();

        Assert.Equal(0m, item.Subtotal);
    }

    [Fact]
    public void RecalculateSubtotalOnly_包装数量为1_按实际数量计价()
    {
        // 包装数量为1（如中药饮片按克计价），单价1元/克，开10克 → 10元
        var item = new PrescriptionItemDto
        {
            DrugName = "黄芪",
            Spec = "饮片",
            UnitPrice = 1m,
            PackQuantity = 1m,
            Qty = 10m
        };

        item.RecalculateSubtotalOnly();

        Assert.Equal(10m, item.Subtotal);
    }

    [Fact]
    public void Recalculate_每日三次三天_数量计算正确()
    {
        var item = new PrescriptionItemDto
        {
            Dose = 1m,
            Frequency = "每日三次",
            DurationDays = 3,
            UnitPrice = 10m,
            PackQuantity = 12m
        };

        item.Recalculate();

        // 1片 × 3次/天 × 3天 = 9片
        Assert.Equal(9m, item.Qty);
        // 9片需要1盒，1盒 × 10元 = 10元
        Assert.Equal(10m, item.Subtotal);
    }

    [Fact]
    public void Recalculate_每日两次五天_数量计算正确()
    {
        var item = new PrescriptionItemDto
        {
            Dose = 2m,
            Frequency = "每日两次",
            DurationDays = 5,
            UnitPrice = 20m,
            PackQuantity = 24m
        };

        item.Recalculate();

        // 2片 × 2次/天 × 5天 = 20片
        Assert.Equal(20m, item.Qty);
        // 20片需要1盒（24片/盒），1盒 × 20元 = 20元
        Assert.Equal(20m, item.Subtotal);
    }

    [Fact]
    public void Recalculate_整盒数量_计算正确()
    {
        var item = new PrescriptionItemDto
        {
            Dose = 2m,
            Frequency = "每日两次",
            DurationDays = 6,
            UnitPrice = 15m,
            PackQuantity = 12m
        };

        item.Recalculate();

        // 2片 × 2次/天 × 6天 = 24片 = 2盒
        Assert.Equal(24m, item.Qty);
        // 2盒 × 15元 = 30元
        Assert.Equal(30m, item.Subtotal);
    }

    [Fact]
    public void UnitPricePerUnit_计算正确()
    {
        var item = new PrescriptionItemDto
        {
            UnitPrice = 10m,
            PackQuantity = 12m,
            Qty = 9m
        };

        item.RecalculateSubtotalOnly();

        // 每片价格 = 10/12 = 0.8333元
        Assert.Equal(0.8333m, item.UnitPricePerUnit);
    }
}
