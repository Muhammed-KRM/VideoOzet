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
}
