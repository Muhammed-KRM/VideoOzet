using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Services;

public class ExtractedTopicItem
{
    public string Baslik { get; set; } = string.Empty;
    public string Aciklama { get; set; } = string.Empty;
    public int TahminiSureDk { get; set; } = 3;
}

public class SourceTopicMapper : ISourceTopicMapper
{
    public const int CurrentPromptVersion = 2;
    public const int MaxWordsPerChunk = 18000;

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ITextChunker _textChunker;
    private readonly ILogger<SourceTopicMapper> _logger;

    public SourceTopicMapper(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        ITextChunker textChunker,
        ILogger<SourceTopicMapper> logger)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _textChunker = textChunker;
        _logger = logger;
    }

    private static bool IsPlaceholder(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (text.Contains("Bu eğitim videosunda temel kavramlar, metodoloji ve uygulama örnekleri", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("temel kavramlar, metodoloji ve uygulama örnekleri ele alınmakta", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("[Mock", StringComparison.OrdinalIgnoreCase) || text.Contains("Mock özet", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public async Task<string> BuildTopicDigestAsync(Guid egitimId, CancellationToken ct = default)
    {
        var videolar = await _dbContext.Videolar
            .Include(v => v.Summary)
            .Include(v => v.Transcript)
            .Where(v => v.EgitimId == egitimId &&
                        v.IslemDurumu != VideoIslemDurumu.Hata &&
                        ((v.Summary != null && !string.IsNullOrWhiteSpace(v.Summary.OzetMetni)) ||
                         (v.IslemDurumu == VideoIslemDurumu.Tamamlandi && v.Transcript != null && !string.IsNullOrWhiteSpace(v.Transcript.HamMetin))))
            .OrderBy(v => v.Sira)
            .ThenBy(v => v.OlusturmaTarihi)
            .ToListAsync(ct);

        var dokumanlar = await _dbContext.Dokumanlar
            .Include(d => d.DokumanMetin)
            .Where(d => d.EgitimId == egitimId &&
                        d.IslemDurumu != VideoIslemDurumu.Hata &&
                        d.DokumanMetin != null &&
                        (!string.IsNullOrWhiteSpace(d.DokumanMetin.HamMetin) ||
                         !string.IsNullOrWhiteSpace(d.DokumanMetin.DamitilmisOzetMetni) ||
                         !string.IsNullOrWhiteSpace(d.DokumanMetin.CevrilmisOzetMetni)))
            .OrderBy(d => d.OlusturmaTarihi)
            .ThenBy(d => d.DosyaAdi)
            .ToListAsync(ct);

        var allSourceIds = videolar.Select(v => v.Id)
            .Concat(dokumanlar.Select(d => d.Id))
            .ToList();

        var existingExtractions = allSourceIds.Count > 0
            ? await _dbContext.KaynakKonuCikarimlari
                .Where(k => allSourceIds.Contains(k.KaynakId))
                .ToListAsync(ct)
            : new List<KaynakKonuCikarimi>();

        var digestBuilder = new StringBuilder();

        // 1. Videoları MAP et (Sadece mock/placeholder olmayan gerçek videolar)
        foreach (var video in videolar)
        {
            var videoText = BuildVideoExtractionText(video);
            if (IsPlaceholder(videoText))
            {
                _logger.LogInformation("Video {Baslik} placeholder/mock içerik içerdiği için konu çıkarımına dahil edilmedi.", video.Baslik);
                continue;
            }

            var topics = await GetOrExtractVideoTopicsAsync(video, videoText, existingExtractions, swallowLlmErrors: false, ct);
            if (topics.Count > 0)
            {
                var kullanimEtiketi = video.KullanimTuru == KaynakKullanimTuru.SadeceArgumanVeMantik
                    ? " [DIŞ KAYNAK - KOPYALAMAK YASAK, SADECE ARGÜMAN/MANTIK AL]"
                    : " [DOĞRUDAN REFERANS]";
                digestBuilder.AppendLine($"[VİDEO: {video.Baslik} (ID: {video.Id}){kullanimEtiketi}]");
                foreach (var topic in topics)
                {
                    digestBuilder.AppendLine($"- {topic.Baslik}: {topic.Aciklama} (~{topic.TahminiSureDk} dk)");
                }
                digestBuilder.AppendLine();
            }
        }

        // 2. Dokümanları MAP et (Tam HamMetin üzerinden 18.000 kelimelik pencerelerle eksiksiz tarama)
        foreach (var doc in dokumanlar)
        {
            var docText = BuildDocumentFullExtractionText(doc);
            if (string.IsNullOrWhiteSpace(docText))
                continue;

            var topics = await GetOrExtractDocumentTopicsAsync(doc, docText, existingExtractions, swallowLlmErrors: false, ct);
            if (topics.Count > 0)
            {
                var kullanimEtiketi = doc.KullanimTuru == KaynakKullanimTuru.SadeceArgumanVeMantik
                    ? " [DIŞ KAYNAK - KOPYALAMAK YASAK, SADECE ARGÜMAN/MANTIK AL]"
                    : " [DOĞRUDAN REFERANS]";
                digestBuilder.AppendLine($"[DÖKÜMAN: {doc.DosyaAdi} (ID: {doc.Id}){kullanimEtiketi}]");
                foreach (var topic in topics)
                {
                    digestBuilder.AppendLine($"- {topic.Baslik}: {topic.Aciklama} (~{topic.TahminiSureDk} dk)");
                }
                digestBuilder.AppendLine();
            }
        }

        return digestBuilder.ToString();
    }

    public async Task<IReadOnlyList<CourseSourceCardDto>> GetStructuredSourceCardsAsync(Guid egitimId, CancellationToken ct = default)
    {
        var videolar = await _dbContext.Videolar
            .Include(v => v.Summary)
            .Include(v => v.Transcript)
            .Where(v => v.EgitimId == egitimId &&
                        v.IslemDurumu != VideoIslemDurumu.Hata &&
                        (v.IslemDurumu == VideoIslemDurumu.Tamamlandi ||
                         (v.Summary != null && !string.IsNullOrWhiteSpace(v.Summary.OzetMetni)) ||
                         (v.Transcript != null && !string.IsNullOrWhiteSpace(v.Transcript.HamMetin))))
            .OrderBy(v => v.Sira)
            .ThenBy(v => v.OlusturmaTarihi)
            .ToListAsync(ct);

        var dokumanlar = await _dbContext.Dokumanlar
            .Include(d => d.DokumanMetin)
            .Where(d => d.EgitimId == egitimId &&
                        d.IslemDurumu != VideoIslemDurumu.Hata &&
                        (d.IslemDurumu == VideoIslemDurumu.Tamamlandi ||
                         (d.DokumanMetin != null &&
                          (!string.IsNullOrWhiteSpace(d.DokumanMetin.HamMetin) ||
                           !string.IsNullOrWhiteSpace(d.DokumanMetin.DamitilmisOzetMetni) ||
                           !string.IsNullOrWhiteSpace(d.DokumanMetin.CevrilmisOzetMetni)))))
            .OrderBy(d => d.OlusturmaTarihi)
            .ThenBy(d => d.DosyaAdi)
            .ToListAsync(ct);

        var allSourceIds = videolar.Select(v => v.Id)
            .Concat(dokumanlar.Select(d => d.Id))
            .ToList();

        if (allSourceIds.Count == 0)
            return Array.Empty<CourseSourceCardDto>();

        var existingExtractions = await _dbContext.KaynakKonuCikarimlari
            .Where(k => allSourceIds.Contains(k.KaynakId))
            .ToListAsync(ct);

        var cards = new List<CourseSourceCardDto>(allSourceIds.Count);

        // 1. Tamamlanmış videoları yapılandırılmış kaynak kartına dönüştür
        foreach (var video in videolar)
        {
            var videoText = BuildVideoExtractionText(video);
            if (IsPlaceholder(videoText) && (video.Transcript == null || IsPlaceholder(video.Transcript.HamMetin)))
            {
                _logger.LogInformation("Video {Baslik} placeholder içerik nedeniyle yapılandırılmış kart listesinden atlandı.", video.Baslik);
                continue;
            }

            if (IsPlaceholder(videoText) && video.Transcript != null && !string.IsNullOrWhiteSpace(video.Transcript.HamMetin))
            {
                videoText = video.Transcript.HamMetin.Trim();
            }

            var topics = !string.IsNullOrWhiteSpace(videoText)
                ? await GetOrExtractVideoTopicsAsync(video, videoText, existingExtractions, swallowLlmErrors: true, ct)
                : new List<ExtractedTopicItem>();

            var dil = ResolveVideoLanguage(video);
            var konularList = FormatTopicsForCard(topics, video.Summary?.KonuBasliklari, videoText, video.Baslik);
            var sureDk = EstimateVideoDurationMinutes(video, topics, videoText);
            var ozetBilgi = BuildSourceCardSummary(
                topics,
                primarySummary: !string.IsNullOrWhiteSpace(video.Summary?.CevrilmisOzetMetni)
                    ? video.Summary!.CevrilmisOzetMetni
                    : video.Summary?.OzetMetni,
                fallbackRaw: video.Transcript?.HamMetin);

            cards.Add(new CourseSourceCardDto(
                kaynakId: video.Id,
                kaynakTuru: "Video",
                baslik: video.Baslik,
                dil: dil,
                kullanimTuru: video.KullanimTuru.ToString(),
                tahminiToplamSureDk: sureDk,
                konular: konularList,
                ozetBilgi: ozetBilgi));
        }

        // 2. Tamamlanmış dokümanları tam HamMetin taramasıyla yapılandırılmış kaynak kartına dönüştür
        foreach (var doc in dokumanlar)
        {
            var docText = BuildDocumentFullExtractionText(doc);
            var topics = !string.IsNullOrWhiteSpace(docText)
                ? await GetOrExtractDocumentTopicsAsync(doc, docText, existingExtractions, swallowLlmErrors: true, ct)
                : new List<ExtractedTopicItem>();

            var dil = ResolveDocumentLanguage(doc);
            var konularList = FormatTopicsForCard(topics, null, docText, doc.DosyaAdi);
            var sureDk = EstimateDocumentDurationMinutes(doc, topics, docText);
            var ozetBilgi = BuildSourceCardSummary(
                topics,
                primarySummary: !string.IsNullOrWhiteSpace(doc.DokumanMetin?.CevrilmisOzetMetni)
                    ? doc.DokumanMetin!.CevrilmisOzetMetni
                    : doc.DokumanMetin?.DamitilmisOzetMetni,
                fallbackRaw: doc.DokumanMetin?.HamMetin);

            cards.Add(new CourseSourceCardDto(
                kaynakId: doc.Id,
                kaynakTuru: "Dokuman",
                baslik: doc.DosyaAdi,
                dil: dil,
                kullanimTuru: doc.KullanimTuru.ToString(),
                tahminiToplamSureDk: sureDk,
                konular: konularList,
                ozetBilgi: ozetBilgi));
        }

        return cards;
    }

    private static string BuildVideoExtractionText(Video video)
    {
        var baseText = (!string.IsNullOrWhiteSpace(video.Summary?.CevrilmisOzetMetni)
            ? video.Summary!.CevrilmisOzetMetni
            : !string.IsNullOrWhiteSpace(video.Summary?.OzetMetni)
                ? video.Summary!.OzetMetni
                : video.Transcript?.HamMetin ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(baseText) &&
            video.Summary != null &&
            !string.IsNullOrWhiteSpace(video.Summary.KonuBasliklari) &&
            video.Summary.KonuBasliklari != "[]")
        {
            baseText += "\nKonu Başlıkları:\n" + video.Summary.KonuBasliklari;
        }

        return baseText;
    }

    /// <summary>
    /// Kesik/kısa özete bakma hatasını önlemek için öncelikle doğrudan TAM HamMetin döner;
    /// yalnızca HamMetin boşsa damıtılmış/çevrilmiş metne geri döner.
    /// </summary>
    private static string BuildDocumentFullExtractionText(Dokuman doc)
    {
        if (doc.DokumanMetin == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(doc.DokumanMetin.HamMetin))
            return doc.DokumanMetin.HamMetin.Trim();

        if (!string.IsNullOrWhiteSpace(doc.DokumanMetin.CevrilmisOzetMetni))
            return doc.DokumanMetin.CevrilmisOzetMetni.Trim();

        if (!string.IsNullOrWhiteSpace(doc.DokumanMetin.DamitilmisOzetMetni))
            return doc.DokumanMetin.DamitilmisOzetMetni.Trim();

        return string.Empty;
    }

    private async Task<List<ExtractedTopicItem>> GetOrExtractVideoTopicsAsync(
        Video video,
        string videoText,
        List<KaynakKonuCikarimi> existingExtractions,
        bool swallowLlmErrors,
        CancellationToken ct)
    {
        var hash = ComputeHash(videoText);
        var cached = existingExtractions.FirstOrDefault(x => x.KaynakId == video.Id && x.KaynakTuru == "Video");

        if (cached != null &&
            (cached.PromptVersiyonu == CurrentPromptVersion || cached.PromptVersiyonu == 1) &&
            cached.IcerikHash == hash)
        {
            _logger.LogInformation("Video {Baslik} konuları önbellekten alındı.", video.Baslik);
            return ParseTopics(cached.KonularJson);
        }

        try
        {
            _logger.LogInformation("Video {Baslik} için konular LLM ile çıkarılıyor (Map)...", video.Baslik);
            var topics = await ExtractTopicsFromTextAsync(video.Baslik, videoText, ct);
            await UpsertTopicCacheAsync(video.Id, "Video", hash, topics, cached, existingExtractions, swallowLlmErrors, ct);
            return topics;
        }
        catch (Exception ex) when (swallowLlmErrors && !ct.IsCancellationRequested && !IsFatalProviderException(ex))
        {
            _logger.LogWarning(ex, "Video {Baslik} için LLM konu çıkarımı başarısız oldu; yedek başlıklarla devam ediliyor.", video.Baslik);
            return cached != null ? ParseTopics(cached.KonularJson) : new List<ExtractedTopicItem>();
        }
    }

    private async Task<List<ExtractedTopicItem>> GetOrExtractDocumentTopicsAsync(
        Dokuman doc,
        string docText,
        List<KaynakKonuCikarimi> existingExtractions,
        bool swallowLlmErrors,
        CancellationToken ct)
    {
        var hash = ComputeHash(docText);
        var cached = existingExtractions.FirstOrDefault(x => x.KaynakId == doc.Id && x.KaynakTuru == "Dokuman");

        // Dokümanlarda kesinlikle CurrentPromptVersion (v2) ve tam HamMetin hash eşleşmesi aranır;
        // eski v1 yarım/kesik özet önbellekleri otomatik yenilenir.
        if (cached != null &&
            cached.PromptVersiyonu == CurrentPromptVersion &&
            cached.IcerikHash == hash)
        {
            _logger.LogInformation("Doküman {DosyaAdi} konuları önbellekten alındı (v{Ver}).", doc.DosyaAdi, cached.PromptVersiyonu);
            return ParseTopics(cached.KonularJson);
        }

        try
        {
            _logger.LogInformation("Doküman {DosyaAdi} için tam metin ({Chars} kr) üzerinden konular çıkarılıyor (Map, Pencere={MaxWords} kelime)...",
                doc.DosyaAdi, docText.Length, MaxWordsPerChunk);
            var topics = await ExtractTopicsFromTextAsync(doc.DosyaAdi, docText, ct);
            await UpsertTopicCacheAsync(doc.Id, "Dokuman", hash, topics, cached, existingExtractions, swallowLlmErrors, ct);
            return topics;
        }
        catch (Exception ex) when (swallowLlmErrors && !ct.IsCancellationRequested && !IsFatalProviderException(ex))
        {
            _logger.LogWarning(ex, "Doküman {DosyaAdi} için LLM konu çıkarımı başarısız oldu; yedek başlıklarla devam ediliyor.", doc.DosyaAdi);
            return cached != null ? ParseTopics(cached.KonularJson) : new List<ExtractedTopicItem>();
        }
    }

    private static bool IsFatalProviderException(Exception ex)
    {
        for (var cur = ex; cur != null; cur = cur.InnerException)
        {
            if (cur is System.Net.Sockets.SocketException)
                return true;

            if (cur is System.Net.Http.HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var code = (int)httpEx.StatusCode.Value;
                if (code is 401 or 402 or 403)
                    return true;
            }

            var msg = cur.Message ?? string.Empty;
            if (msg.Contains("402", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("401", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("403", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("prepayment credits", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Invalid API key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task UpsertTopicCacheAsync(
        Guid kaynakId,
        string kaynakTuru,
        string hash,
        List<ExtractedTopicItem> topics,
        KaynakKonuCikarimi? cached,
        List<KaynakKonuCikarimi> existingExtractions,
        bool ignoreSaveConflict,
        CancellationToken ct)
    {
        if (ignoreSaveConflict && topics.Count == 0)
            return;

        var serialized = JsonSerializer.Serialize(topics);
        if (cached == null)
        {
            cached = new KaynakKonuCikarimi
            {
                Id = Guid.NewGuid(),
                KaynakId = kaynakId,
                KaynakTuru = kaynakTuru,
                PromptVersiyonu = CurrentPromptVersion,
                IcerikHash = hash,
                KonularJson = serialized
            };
            _dbContext.KaynakKonuCikarimlari.Add(cached);
            existingExtractions.Add(cached);
        }
        else
        {
            cached.PromptVersiyonu = CurrentPromptVersion;
            cached.IcerikHash = hash;
            cached.KonularJson = serialized;
        }

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException dbEx) when (ignoreSaveConflict)
        {
            _logger.LogWarning(dbEx, "KaynakKonuCikarimi ({KaynakId}) eşzamanlı yazım çakışması yok sayıldı.", kaynakId);
            _dbContext.Entry(cached).State = EntityState.Detached;
        }
    }

    private async Task<List<ExtractedTopicItem>> ExtractTopicsFromTextAsync(string kaynakAdi, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<ExtractedTopicItem>();

        var chunks = _textChunker.ChunkText(text, maxTokens: MaxWordsPerChunk);
        if (chunks == null || chunks.Count == 0)
        {
            chunks = FallbackWordWindowSplit(text, MaxWordsPerChunk);
        }

        var allTopics = new Dictionary<string, ExtractedTopicItem>(StringComparer.OrdinalIgnoreCase);

        // Hiçbir bölümü atlamadan TÜM pencereleri sırayla tara
        for (int i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var chunkText = chunks[i];
            if (string.IsNullOrWhiteSpace(chunkText))
                continue;

            var jsonResponse = await _synthesisProvider.ExtractSourceTopicsAsync(kaynakAdi, chunkText, i + 1, chunks.Count, ct);
            var chunkTopics = ParseTopicsFromLlmResponse(jsonResponse);

            foreach (var t in chunkTopics)
            {
                var key = t.Baslik.Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (allTopics.TryGetValue(key, out var existing))
                {
                    existing.TahminiSureDk = Math.Clamp(existing.TahminiSureDk + t.TahminiSureDk, 1, 90);
                    if (!string.IsNullOrWhiteSpace(t.Aciklama) &&
                        !existing.Aciklama.Contains(t.Aciklama, StringComparison.OrdinalIgnoreCase) &&
                        existing.Aciklama.Length < 420)
                    {
                        existing.Aciklama = string.IsNullOrWhiteSpace(existing.Aciklama)
                            ? t.Aciklama
                            : $"{existing.Aciklama}; {t.Aciklama}";
                    }
                }
                else
                {
                    allTopics[key] = t;
                }
            }
        }

        return allTopics.Values.ToList();
    }

    private static List<string> FallbackWordWindowSplit(string text, int maxWords)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return new List<string> { text };

        var windows = new List<string>();
        for (int i = 0; i < words.Length; i += maxWords)
        {
            windows.Add(string.Join(' ', words.Skip(i).Take(maxWords)));
        }
        return windows;
    }

    private static string ResolveVideoLanguage(Video video)
    {
        if (!string.IsNullOrWhiteSpace(video.Summary?.OrijinalDil))
            return video.Summary!.OrijinalDil.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(video.Transcript?.Dil))
            return video.Transcript!.Dil.Trim().ToLowerInvariant();
        return "tr";
    }

    private static string ResolveDocumentLanguage(Dokuman doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.DokumanMetin?.OrijinalDil))
            return doc.DokumanMetin!.OrijinalDil.Trim().ToLowerInvariant();
        return "tr";
    }

    private static int EstimateVideoDurationMinutes(Video video, List<ExtractedTopicItem> topics, string fallbackText)
    {
        if (video.Sure.HasValue && video.Sure.Value.TotalSeconds >= 30)
            return Math.Max(1, (int)Math.Round(video.Sure.Value.TotalMinutes));

        if (topics.Count > 0)
            return Math.Clamp(topics.Sum(t => t.TahminiSureDk), 2, 120);

        int words = video.Transcript?.KelimeSayisi > 0
            ? video.Transcript.KelimeSayisi
            : CountWordsQuick(fallbackText);
        return Math.Clamp(Math.Max(2, words / 140), 2, 90);
    }

    private static int EstimateDocumentDurationMinutes(Dokuman doc, List<ExtractedTopicItem> topics, string fullText)
    {
        if (topics.Count > 0)
            return Math.Clamp(topics.Sum(t => t.TahminiSureDk), 3, 120);

        int words = doc.DokumanMetin?.KelimeSayisi > 0
            ? doc.DokumanMetin.KelimeSayisi
            : CountWordsQuick(fullText);
        return Math.Clamp(Math.Max(3, words / 180), 3, 90);
    }

    private static List<string> FormatTopicsForCard(
        List<ExtractedTopicItem> topics,
        string? rawKonuBasliklariJson,
        string? sourceText,
        string fallbackTitle)
    {
        if (topics.Count > 0)
        {
            return topics
                .Select(t => string.IsNullOrWhiteSpace(t.Aciklama)
                    ? t.Baslik
                    : $"{t.Baslik}: {t.Aciklama}")
                .ToList();
        }

        var fallbackList = new List<string>();
        if (!string.IsNullOrWhiteSpace(rawKonuBasliklariJson) && rawKonuBasliklariJson != "[]")
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(rawKonuBasliklariJson, _jsonOptions);
                if (parsed != null)
                {
                    fallbackList.AddRange(parsed.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
                }
            }
            catch { }
        }

        if (fallbackList.Count == 0 && !string.IsNullOrWhiteSpace(sourceText))
        {
            var headingMatches = Regex.Matches(sourceText, @"(?m)^(?:#{1,4}\s+|(?:\d{1,2}[\.\)]\s+))([^\r\n]{4,90})$");
            foreach (Match m in headingMatches)
            {
                var h = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(h) && !fallbackList.Contains(h, StringComparer.OrdinalIgnoreCase))
                {
                    fallbackList.Add(h);
                    if (fallbackList.Count >= 8) break;
                }
            }
        }

        if (fallbackList.Count == 0 && !string.IsNullOrWhiteSpace(fallbackTitle))
        {
            fallbackList.Add(fallbackTitle);
        }

        return fallbackList;
    }

    private static string BuildSourceCardSummary(
        List<ExtractedTopicItem> topics,
        string? primarySummary,
        string? fallbackRaw)
    {
        var sb = new StringBuilder();

        if (topics.Count > 0)
        {
            var topicDescriptions = topics
                .Where(t => !string.IsNullOrWhiteSpace(t.Aciklama))
                .Take(6)
                .Select(t => $"{t.Baslik} ({t.Aciklama})")
                .ToList();

            if (topicDescriptions.Count > 0)
            {
                sb.Append(string.Join(" | ", topicDescriptions));
            }
        }

        var extraText = !string.IsNullOrWhiteSpace(primarySummary) && !IsPlaceholder(primarySummary)
            ? primarySummary!.Trim()
            : (!string.IsNullOrWhiteSpace(fallbackRaw) && !IsPlaceholder(fallbackRaw) ? fallbackRaw!.Trim() : string.Empty);

        if (!string.IsNullOrWhiteSpace(extraText) && sb.Length < 450)
        {
            var cleanSnippet = Regex.Replace(extraText, @"\s+", " ").Trim();
            int remaining = 750 - sb.Length;
            if (remaining > 80)
            {
                if (cleanSnippet.Length > remaining)
                    cleanSnippet = cleanSnippet[..remaining].TrimEnd() + "...";

                if (sb.Length > 0)
                    sb.Append(" — Özet: ");
                sb.Append(cleanSnippet);
            }
        }

        var result = sb.ToString().Trim();
        return result.Length <= 800 ? result : result[..797].TrimEnd() + "...";
    }

    private static int CountWordsQuick(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        int count = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) inWord = false;
            else if (!inWord) { inWord = true; count++; }
        }
        return count;
    }

    private static List<ExtractedTopicItem> ParseTopicsFromLlmResponse(string jsonResponse)
    {
        if (string.IsNullOrWhiteSpace(jsonResponse))
            return new List<ExtractedTopicItem>();

        try
        {
            var elem = LlmJson.Deserialize<JsonElement>(jsonResponse);
            JsonElement arrayElem;

            if (elem.ValueKind == JsonValueKind.Array)
            {
                arrayElem = elem;
            }
            else if (elem.ValueKind == JsonValueKind.Object)
            {
                if (elem.TryGetProperty("Konular", out var prop) || elem.TryGetProperty("konular", out prop))
                {
                    arrayElem = prop;
                }
                else
                {
                    return new List<ExtractedTopicItem>();
                }
            }
            else
            {
                return new List<ExtractedTopicItem>();
            }

            if (arrayElem.ValueKind == JsonValueKind.Array)
            {
                var list = new List<ExtractedTopicItem>();
                foreach (var item in arrayElem.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    string baslik = "";
                    string aciklama = "";
                    int sure = 3;

                    foreach (var p in item.EnumerateObject())
                    {
                        if (p.Name.Equals("Baslik", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
                            baslik = p.Value.GetString() ?? "";
                        else if (p.Name.Equals("Aciklama", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("description", StringComparison.OrdinalIgnoreCase))
                            aciklama = p.Value.GetString() ?? "";
                        else if (p.Name.Equals("TahminiSureDk", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("tahmini_sure_dk", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("duration", StringComparison.OrdinalIgnoreCase))
                        {
                            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int numVal))
                                sure = numVal;
                            else if (p.Value.ValueKind == JsonValueKind.String && int.TryParse(p.Value.GetString(), out int strVal))
                                sure = strVal;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(baslik))
                    {
                        list.Add(new ExtractedTopicItem
                        {
                            Baslik = baslik.Trim(),
                            Aciklama = aciklama.Trim(),
                            TahminiSureDk = Math.Clamp(sure, 1, 60)
                        });
                    }
                }
                return list;
            }
        }
        catch
        {
        }

        return new List<ExtractedTopicItem>();
    }

    private static List<ExtractedTopicItem> ParseTopics(string storedJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ExtractedTopicItem>>(storedJson, _jsonOptions) ?? new List<ExtractedTopicItem>();
        }
        catch
        {
            return new List<ExtractedTopicItem>();
        }
    }

    private static string ComputeHash(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
