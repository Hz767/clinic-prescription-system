using Clinic.Application.DTOs;

namespace Clinic.Application.Interfaces;

/// <summary>
/// PDF 生成服务接口。定义在应用层，由基础设施层实现（QuestPDF）。
/// </summary>
public interface IPdfService
{
    /// <summary>
    /// 生成处方 PDF 文件。
    /// </summary>
    /// <param name="data">处方 PDF 数据（含患者、医生、药品明细等）</param>
    /// <param name="outputPath">输出文件路径（含文件名，不含扩展名，自动追加 .pdf）</param>
    /// <returns>实际生成的 PDF 文件完整路径</returns>
    string GeneratePrescriptionPdf(PrescriptionPdfData data, string outputPath);
}
