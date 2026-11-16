using System;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Interfaces;

public interface ISynthesisProvider
{
    /// <summary>
    /// Mevcut yapılandırmaya göre LLM çağrılarında fiilen kullanılan model adı (kayıtlardaki LlmModel alanı için).
    /// </summary>
    string ActiveModelName => string.Empty;

    /// <summary>
    /// RAG sonuçlarını kullanarak araştırma özeti üretir.
    /// </summary>
    Task<string> GenerateResearchSummaryAsync(string topic, string targetLength, string targetAudience, string contextData, CancellationToken ct = default);

    /// <summary>
    /// RAG sonuçlarını kullanarak video planı üretir.
    /// </summary>
    Task<string> GenerateVideoPlanAsync(string topic, string targetLength, string contextData, CancellationToken ct = default);

    /// <summary>
    /// Doğrulanmış araştırma özetine sıkı bağlı stüdyo video çekim ve prompter planı üretir.
    /// </summary>
    Task<string> GenerateGroundedVideoPlanAsync(string topic, string targetLength, string contextData, string researchSummary, CancellationToken ct = default)
        => GenerateVideoPlanAsync(topic, targetLength, contextData, ct);

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

    /// <summary>
    /// Çoklu video senaryosunda tüm içerikleri tarayarak ana konuyu analiz eder.
    /// JSON formatında döner (AnaFikir, BeklenenKonular vs).
    /// </summary>
    Task<string> AnalyzeTopicAsync(string topic, string allSourcesData, CancellationToken ct = default);

    /// <summary>
    /// Konu analizine ve mevcut kaynaklara göre bir çoklu video serisi planı oluşturur.
    /// JSON formatında döner.
    /// </summary>
    Task<string> GenerateSeriesPlanAsync(string topic, string targetAudience, string konuAnaliziJson, string allSourcesData, string userConstraintsJson = "", CancellationToken ct = default);

    /// <summary>
    /// Fark-bazlı (Diff-Aware), yazar tezi, ton ve yapılandırılmış kaynak kartları ile zengin çoklu video serisi planı oluşturur.
    /// </summary>
    Task<string> GenerateSeriesPlanAsync(
        string topicAndThesis,
        string targetAudience,
        string toneInstruction,
        string konuAnaliziJson,
        string structuredSourceCardsJson,
        string formattedConstraintsText,
        string previousPlanJson = "",
        CancellationToken ct = default)
        => GenerateSeriesPlanAsync(topicAndThesis, targetAudience, konuAnaliziJson, structuredSourceCardsJson, formattedConstraintsText, ct);

    /// <summary>
    /// Çoklu video serisindeki belirli bir bölümün içeriğini ve sonraki videoya devir notunu üretir.
    /// JSON formatında döner.
    /// </summary>
    Task<string> GenerateSeriesVideoContentAsync(string bolumBasligi, string bolumKonulariJson, string oncekiVideoDevirNotuJson, string targetAudience, string contextData, string userInstructions = "", CancellationToken ct = default);

    /// <summary>
    /// Sahne-sahne (Iterative Scene-by-Scene) derin bölüm araştırması, çekim/prompter planı ve devir defteri üretir.
    /// </summary>
    Task<(string ArastirmaOzeti, string VideoPlani, string DevirDefteriJson)> GenerateSeriesEpisodeAsync(
        SeriesEpisodeGenerationRequest request,
        Func<string, int, Task>? onProgress = null,
        CancellationToken ct = default)
        => Task.FromResult((string.Empty, string.Empty, "{}"));

    /// <summary>
    /// Kaynak metninden (video veya doküman) anlatılan temel konuları ve tahmini süreleri çıkarır (Map adımı).
    /// JSON formatında döner.
    /// </summary>
    Task<string> ExtractSourceTopicsAsync(string kaynakAdi, string metin, int parcaNo, int parcaSayisi, CancellationToken ct = default);

    /// <summary>
    /// Stüdyo Soru-Cevap (Q&A) sistemi için yapılandırılmış bağlamla soruyu yanıtlar.
    /// </summary>
    Task<string> AskStudioQuestionAsync(string prompt, CancellationToken ct = default)
        => Task.FromResult("{}");
}
