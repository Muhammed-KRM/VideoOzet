using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;

namespace VideoOzet.IntegrationTests.Endpoints;

public class EndpointTestResult
{
    public string Group { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string ExpectedStatus { get; set; } = string.Empty;
    public string ActualStatus { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public static class EndpointTestReporter
{
    private static readonly ConcurrentBag<EndpointTestResult> Results = new();

    public static void RecordResult(EndpointTestResult result)
    {
        Results.Add(result);
    }

    public static void GenerateReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# API Endpoint Test Raporu");
        sb.AppendLine($"Tarih: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        
        var groupedResults = Results.GroupBy(x => x.Group).OrderBy(g => g.Key);

        foreach (var group in groupedResults)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();

            foreach (var result in group.OrderBy(x => x.Path))
            {
                string icon = result.IsSuccess ? "✅" : "❌";
                sb.AppendLine($"{icon} **[{result.HttpMethod}]** `{result.Path}`");
            }
            sb.AppendLine();
        }

        var failedTests = Results.Where(x => !x.IsSuccess).ToList();
        if (failedTests.Any())
        {
            sb.AppendLine("## ⚠️ Hata Detayları:");
            sb.AppendLine();
            foreach (var fail in failedTests.OrderBy(x => x.Path))
            {
                sb.AppendLine($"- **[{fail.HttpMethod}] {fail.Path}**");
                sb.AppendLine($"  - Beklenen: {fail.ExpectedStatus}");
                sb.AppendLine($"  - Alınan Durum (Status Code): {fail.ActualStatus}");
                if (!string.IsNullOrEmpty(fail.ErrorMessage))
                {
                    sb.AppendLine($"  - Hata İçeriği: {fail.ErrorMessage}");
                }
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("## 🎉 Tüm testler başarıyla geçti!");
        }

        // Proje kök dizini (tests/VideoOzet.IntegrationTests) veya bin output folder
        string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "EndpointTestReport.md");
        File.WriteAllText(reportPath, sb.ToString());

        // Ayrıca görünürlüğü artırmak için bin dışına, src yanına da yazalım
        try 
        {
            string rootPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../../../")); 
            string rootReportPath = Path.Combine(rootPath, "EndpointTestReport.md");
            File.WriteAllText(rootReportPath, sb.ToString());
        }
        catch { /* ignore */ }
    }
}
