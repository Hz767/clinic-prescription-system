using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;

namespace Clinic.Infrastructure.Pdf;

/// <summary>
/// QuestPDF 处方 PDF 生成服务实现。
/// 使用 Community 许可证（单人诊所、非商业用途）。
/// 处方模板：诊所名称 + 处方编号 + 患者信息 + 诊断 + 药品明细表 + 金额 + 签名区。
/// </summary>
public class QuestPdfService : IPdfService
{
    static QuestPdfService()
    {
        // 配置 QuestPDF Community 许可证
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public string GeneratePrescriptionPdf(PrescriptionPdfData data, string outputPath)
    {
        var fullPath = Path.ChangeExtension(outputPath, ".pdf");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(20);
                page.DefaultTextStyle(ts => ts.FontFamily("Microsoft YaHei").FontSize(9));

                // 处方类型背景色：急诊淡黄(#FFFDE7) / 儿科淡绿(#F1F8E9) / 普通白色
                page.PageColor(data.PrescriptionType switch
                {
                    1 => Color.FromHex("FFFDE7"),
                    2 => Color.FromHex("F1F8E9"),
                    _ => Colors.White
                });

                page.Header().Element(compose => ComposeHeader(compose, data));
                page.Content().Element(compose => ComposeContent(compose, data));
                page.Footer().Element(compose => ComposeFooter(compose, data));
            });
        })
        .GeneratePdf(fullPath);

        return fullPath;
    }

    /// <summary>页眉：诊所名称 + 处方标题 + 处方编号</summary>
    private static void ComposeHeader(IContainer container, PrescriptionPdfData data)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text(data.ClinicName).FontSize(14).Bold();

                // 处方类型标签：急诊/儿科显示彩色标签，普通处方显示类型文本
                if (data.PrescriptionType == 1)
                {
                    row.ConstantItem(60).Background(Color.FromHex("FFF59D")).Padding(3)
                        .AlignCenter().AlignMiddle().Text("急诊").FontSize(10).Bold();
                }
                else if (data.PrescriptionType == 2)
                {
                    row.ConstantItem(60).Background(Color.FromHex("C5E1A5")).Padding(3)
                        .AlignCenter().AlignMiddle().Text("儿科").FontSize(10).Bold();
                }
                else
                {
                    row.ConstantItem(120).AlignRight().Text(data.PrescriptionTypeText).FontSize(10);
                }
            });

            column.Item().PaddingTop(4).Text("处 方 笺").FontSize(18).Bold().AlignCenter();

            column.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text($"编号：{data.PrescriptionNo}");
                row.ConstantItem(140).AlignRight().Text($"日期：{data.CreatedAt:yyyy-MM-dd HH:mm}");
            });

            column.Item().PaddingTop(2).LineHorizontal(1).LineColor(Colors.Grey.Medium);
        });
    }

    /// <summary>主体内容：患者信息 + 诊断 + 药品明细表 + 金额</summary>
    private static void ComposeContent(IContainer container, PrescriptionPdfData data)
    {
        container.PaddingVertical(6).Column(column =>
        {
            // 患者信息
            column.Item().Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("姓名：").Bold();
                    text.Span(data.PatientName);
                });
                row.ConstantItem(60).Text(text =>
                {
                    text.Span("性别：").Bold();
                    text.Span(data.PatientGender);
                });
                row.ConstantItem(60).Text(text =>
                {
                    text.Span("年龄：").Bold();
                    text.Span(data.PatientAge.HasValue ? $"{data.PatientAge}岁" : "—");
                });
            });

            column.Item().PaddingTop(2).Text(text =>
            {
                text.Span("电话：").Bold();
                text.Span(data.PatientPhone);
            });

            column.Item().PaddingTop(2).Text(text =>
            {
                text.Span("医生：").Bold();
                text.Span(data.DoctorName);
            });

            // 主诉
            if (!string.IsNullOrWhiteSpace(data.ChiefComplaint))
            {
                column.Item().PaddingTop(4).Text(text =>
                {
                    text.Span("主诉：").Bold();
                    text.Span(data.ChiefComplaint);
                });
            }

            // 诊断
            column.Item().PaddingTop(4).Text(text =>
            {
                text.Span("临床诊断：").Bold();
                text.Span(data.DiagnosisText);
            });

            // Rp 标示（处方正文标记，法规要求）
            column.Item().PaddingTop(4).Text("Rp").FontSize(12).Bold();

            // 药品明细表
            column.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(24);   // 序号
                    columns.RelativeColumn(2);    // 药品名称
                    columns.RelativeColumn(1);    // 规格
                    columns.RelativeColumn(1.5f); // 用法用量
                    columns.ConstantColumn(50);   // 数量
                    columns.ConstantColumn(55);   // 金额
                });

                // 表头
                table.Header(header =>
                {
                    header.Cell().Element(CellStyle).Text("序");
                    header.Cell().Element(CellStyle).Text("药品名称");
                    header.Cell().Element(CellStyle).Text("规格");
                    header.Cell().Element(CellStyle).Text("用法用量");
                    header.Cell().Element(CellStyle).AlignRight().Text("数量");
                    header.Cell().Element(CellStyle).AlignRight().Text("金额");
                });

                // 数据行
                foreach (var item in data.Items)
                {
                    table.Cell().Element(CellStyle).Text(item.Seq.ToString());
                    table.Cell().Element(CellStyle).Text(item.DrugName);
                    table.Cell().Element(CellStyle).Text(item.Spec);
                    table.Cell().Element(CellStyle).Text(text =>
                    {
                        text.Line($"{item.DoseText} {item.Frequency}");
                        text.Line($"{item.Route} ×{item.DurationDays}天");
                    });
                    table.Cell().Element(CellStyle).AlignRight().Text(
                        $"{item.Qty}{item.Unit}");
                    table.Cell().Element(CellStyle).AlignRight().Text(
                        $"¥{item.Subtotal:F2}");
                }

                static IContainer CellStyle(IContainer c) =>
                    c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2);
            });

            // 处方完毕标记 "/"
            column.Item().PaddingTop(2).AlignRight().Text("/").FontSize(14);

            // 合计金额
            column.Item().PaddingTop(4).AlignRight().Text(text =>
            {
                text.Span("合计：").Bold();
                text.Span($"¥{data.TotalAmount:F2}").FontSize(11).Bold();
            });

            // 延长用药理由
            if (!string.IsNullOrEmpty(data.Notes))
            {
                column.Item().PaddingTop(4).Text(data.Notes).FontSize(8).Italic();
            }

            // 签名区
            column.Item().PaddingTop(20).Row(row =>
            {
                row.RelativeItem().Text("医生签名：________________");
                row.ConstantItem(120).AlignRight().Text("审核/调配：________________");
            });
        });
    }

    /// <summary>页脚：打印时间</summary>
    private static void ComposeFooter(IContainer container, PrescriptionPdfData data)
    {
        container.AlignCenter().Text(t =>
        {
            t.Span("本处方有效期为 ").FontSize(7).FontColor(Colors.Grey.Darken2);
            t.Span(data.PrescriptionTypeText.Contains("急诊") ? "1 天" : "3 天").FontSize(7).FontColor(Colors.Grey.Darken2);
            t.Span($"　打印时间：{DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(7).FontColor(Colors.Grey.Darken2);
        });
    }
}
