using System.Threading;
using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public interface ISynthesisProvider
{
    /// <summary>
    /// RAG sonuçlarını kullanarak araştırma özeti üretir.
    /// </summary>
    Task<string> GenerateResearchSummaryAsync(string topic, string targetLength, string targetAudience, string contextData, CancellationToken ct = default);

    /// <summary>
    /// RAG sonuçlarını kullanarak video planı üretir.
    /// </summary>
    Task<string> GenerateVideoPlanAsync(string topic, string targetLength, string contextData, CancellationToken ct = default);

    /// <summary>
    /// Çıkarılan bir iddianın kaynaklar tarafından desteklenip desteklenmediğini kontrol eder (QC).
    /// </summary>
    Task<string> VerifyClaimAsync(string claim, string contextData, CancellationToken ct = default);

    /// <summary>
    /// Metinden ana iddiaları çıkarır (QC işlemi için).
    /// </summary>
    Task<string> ExtractClaimsAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Metindeki ana iddiaları çıkarıp kaynaklarla tek seferde toplu olarak doğrular (Batch QC).
    /// </summary>
    Task<string> BatchQualityCheckAsync(string summaryText, string contextData, int claimCount = 8, CancellationToken ct = default);

    /// <summary>
    /// Mevcut içerik üzerinde kullanıcının revize talimatına göre sadece ilgili kısımları günceller, değinilmeyen kısımları aynen korur.
    /// </summary>
    Task<string> ReviseContentAsync(string originalContent, string revisionInstruction, string contentType, string topic, string contextData = "", CancellationToken ct = default);

    /// <summary>
    /// Varsa önceki QC raporundaki hataların düzeltilip düzeltilmediğini ve güncel içeriğin kaynaklarla uyumunu toplu olarak denetler.
    /// </summary>
    Task<string> ReQualityCheckAsync(string currentContent, string previousQcReportJson, string contextData, int claimCount = 8, CancellationToken ct = default);
}
