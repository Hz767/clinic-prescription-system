using System.Text;
using Clinic.Application.Interfaces;

namespace Clinic.Infrastructure.Data;

/// <summary>
/// 国内药品清单数据源实现。
/// 从 GitHub 公开仓库 lrpopeyou/MedicalInsuranceKG 下载《药品信息.csv》
/// （国家医保目录 + 各省增补），解析为条目列表。
/// CSV 结构：表头 Med_Name,Med_Kind,Med_Plc；支持引号字段（字段内逗号）。
/// </summary>
public class NationalDrugListSource : INationalDrugListSource
{
    private const string DefaultUrl =
        "https://raw.githubusercontent.com/lrpopeyou/MedicalInsuranceKG/master/药品信息.csv";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public string DefaultSourceUrl => DefaultUrl;

    public async Task<IReadOnlyList<NationalDrugEntryDto>> DownloadAndParseAsync(
        string? url = null, CancellationToken ct = default)
    {
        var u = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url;
        string csv;
        try
        {
            csv = await _http.GetStringAsync(u, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"下载国内药品清单失败（{u}）：{ex.Message}", ex);
        }

        return ParseCsv(csv);
    }

    /// <summary>
    /// 从本地 CSV 文件读取并解析药品清单。
    /// 自动检测 UTF-8 BOM，支持医保目录格式（Med_Name,Med_Kind,Med_Plc）。
    /// </summary>
    public async Task<IReadOnlyList<NationalDrugEntryDto>> ParseFromFileAsync(
        string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"药品清单文件不存在：{filePath}", filePath);

        // 自动检测编码：优先 UTF-8（含 BOM），失败回退 GBK
        string csv;
        var bytes = await File.ReadAllBytesAsync(filePath, ct);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            csv = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        else
        {
            try
            {
                csv = Encoding.UTF8.GetString(bytes);
                // 简单检测：如果包含大量替换字符，尝试 GBK
                if (csv.Count(c => c == '\uFFFD') > 10)
                {
                    csv = Encoding.GetEncoding("GBK").GetString(bytes);
                }
            }
            catch
            {
                csv = Encoding.GetEncoding("GBK").GetString(bytes);
            }
        }

        return ParseCsv(csv);
    }

    /// <summary>
    /// 解析医保目录 CSV 文本。
    /// 自动跳过表头行（Med_Name 开头）与坏行（列数不足）；返回条目列表。
    /// </summary>
    public static IReadOnlyList<NationalDrugEntryDto> ParseCsv(string csvText)
    {
        var result = new List<NationalDrugEntryDto>();
        if (string.IsNullOrWhiteSpace(csvText)) return result;

        foreach (var line in csvText.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var fields = SplitCsvLine(trimmed);
            if (fields.Count < 3) continue;

            var name = fields[0].Trim();
            // 跳过表头
            if (name.Equals("Med_Name", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(name)) continue;

            result.Add(new NationalDrugEntryDto(
                name,
                fields[1].Trim(),
                fields[2].Trim()));
        }

        return result;
    }

    /// <summary>按 CSV 规则拆分一行：双引号包裹字段、引号内逗号不拆分、"" 转义为 "。</summary>
    internal static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        fields.Add(sb.ToString());
        return fields;
    }
}
