using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;

namespace VideoOzet.Business.Services;

public class ExtractedTopicItem
{
    public string Baslik { get; set; } = string.Empty;
    public string Aciklama { get; set; } = string.Empty;
    public int TahminiSureDk { get; set; } = 3;
}

public class SourceTopicMapper : ISourceTopicMapper
{
    public const int CurrentPromptVersion = 1;
    public const int MaxWordsPerChunk = 25000;

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
        return false;
    }

    public async Task<string> BuildTopicDigestAsync(Guid egitimId, CancellationToken ct = default)
    {
        var videolar = await _dbContext.Videolar
            .Include(v => v.Summary)
            .Where(v => v.EgitimId == egitimId && v.Summary != null && !string.IsNullOrWhiteSpace(v.Summary.OzetMetni))
            .OrderBy(v => v.Sira)
            .ToListAsync(ct);

        var dokumanlar = await _dbContext.Dokumanlar
            .Include(d => d.DokumanMetin)
            .Where(d => d.EgitimId == egitimId && d.DokumanMetin != null && !string.IsNullOrWhiteSpace(d.DokumanMetin.HamMetin))
            .ToListAsync(ct);

        var videoIds = videolar.Select(v => v.Id).ToList();
        var docIds = dokumanlar.Select(d => d.Id).ToList();
        var allSourceIds = videoIds.Concat(docIds).ToList();

        var existingExtractions = await _dbContext.KaynakKonuCikarimlari
            .Where(k => allSourceIds.Contains(k.KaynakId))
            .ToListAsync(ct);

        var digestBuilder = new StringBuilder();

        // 1. Videoları MAP et (Sadece mock/placeholder olmayan gerçek videolar)
        foreach (var video in videolar)
        {
            var videoText = (video.Summary!.OzetMetni ?? "").Trim();
            if (IsPlaceholder(videoText))
            {
                _logger.LogInformation("Video {Baslik} placeholder/mock içerik içerdiği için konu çıkarımına dahil edilmedi.", video.Baslik);
                continue;
            }
            if (!string.IsNullOrWhiteSpace(video.Summary.KonuBasliklari) && video.Summary.KonuBasliklari != "[]")
            {
                videoText += "\nKonu Başlıkları:\n" + video.Summary.KonuBasliklari;
            }

            var hash = ComputeHash(videoText);
            var cached = existingExtractions.FirstOrDefault(x => x.KaynakId == video.Id && x.KaynakTuru == "Video");

            List<ExtractedTopicItem> topics;

            if (cached != null && cached.PromptVersiyonu == CurrentPromptVersion && cached.IcerikHash == hash)
            {
                _logger.LogInformation("Video {Baslik} konuları önbellekten alındı.", video.Baslik);
                topics = ParseTopics(cached.KonularJson);
            }
            else
            {
                _logger.LogInformation("Video {Baslik} için konular LLM ile çıkarılıyor (Map)...", video.Baslik);
                topics = await ExtractTopicsFromTextAsync(video.Baslik, videoText, ct);

                if (cached == null)
                {
                    cached = new KaynakKonuCikarimi
                    {
                        KaynakId = video.Id,
                        KaynakTuru = "Video",
                        PromptVersiyonu = CurrentPromptVersion,
                        IcerikHash = hash,
                        KonularJson = JsonSerializer.Serialize(topics)
                    };
                    _dbContext.KaynakKonuCikarimlari.Add(cached);
                }
                else
                {
                    cached.PromptVersiyonu = CurrentPromptVersion;
                    cached.IcerikHash = hash;
                    cached.KonularJson = JsonSerializer.Serialize(topics);
                }

                await _dbContext.SaveChangesAsync(ct);
            }

            if (topics.Any())
            {
                digestBuilder.AppendLine($"[VİDEO: {video.Baslik} (ID: {video.Id})]");
                foreach (var topic in topics)
                {
                    digestBuilder.AppendLine($"- {topic.Baslik}: {topic.Aciklama} (~{topic.TahminiSureDk} dk)");
                }
                digestBuilder.AppendLine();
            }
        }

        // 2. Dokümanları MAP et
        foreach (var doc in dokumanlar)
        {
            var docText = (doc.DokumanMetin!.HamMetin ?? "").Trim();
            if (string.IsNullOrWhiteSpace(docText)) continue;

            var hash = ComputeHash(docText);
            var cached = existingExtractions.FirstOrDefault(x => x.KaynakId == doc.Id && x.KaynakTuru == "Dokuman");

            List<ExtractedTopicItem> topics;

            if (cached != null && cached.PromptVersiyonu == CurrentPromptVersion && cached.IcerikHash == hash)
            {
                _logger.LogInformation("Doküman {DosyaAdi} konuları önbellekten alındı.", doc.DosyaAdi);
                topics = ParseTopics(cached.KonularJson);
            }
            else
            {
                _logger.LogInformation("Doküman {DosyaAdi} için konular LLM ile çıkarılıyor (Map)...", doc.DosyaAdi);
                topics = await ExtractTopicsFromTextAsync(doc.DosyaAdi, docText, ct);

                if (cached == null)
                {
                    cached = new KaynakKonuCikarimi
                    {
                        KaynakId = doc.Id,
                        KaynakTuru = "Dokuman",
                        PromptVersiyonu = CurrentPromptVersion,
                        IcerikHash = hash,
                        KonularJson = JsonSerializer.Serialize(topics)
                    };
                    _dbContext.KaynakKonuCikarimlari.Add(cached);
                }
                else
                {
                    cached.PromptVersiyonu = CurrentPromptVersion;
                    cached.IcerikHash = hash;
                    cached.KonularJson = JsonSerializer.Serialize(topics);
                }

                await _dbContext.SaveChangesAsync(ct);
            }

            if (topics.Any())
            {
                digestBuilder.AppendLine($"[DÖKÜMAN: {doc.DosyaAdi} (ID: {doc.Id})]");
                foreach (var topic in topics)
                {
                    digestBuilder.AppendLine($"- {topic.Baslik}: {topic.Aciklama} (~{topic.TahminiSureDk} dk)");
                }
                digestBuilder.AppendLine();
            }
        }

        return digestBuilder.ToString();
    }

    private async Task<List<ExtractedTopicItem>> ExtractTopicsFromTextAsync(string kaynakAdi, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<ExtractedTopicItem>();

        var chunks = _textChunker.ChunkText(text, maxTokens: MaxWordsPerChunk);
        if (!chunks.Any()) chunks = new List<string> { text };

        var allTopics = new Dictionary<string, ExtractedTopicItem>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunkText = chunks[i];
            var jsonResponse = await _synthesisProvider.ExtractSourceTopicsAsync(kaynakAdi, chunkText, i + 1, chunks.Count, ct);

            var chunkTopics = ParseTopicsFromLlmResponse(jsonResponse);
            foreach (var t in chunkTopics)
            {
                var key = t.Baslik.Trim();
                if (allTopics.TryGetValue(key, out var existing))
                {
                    // Süreyi topla, ilk açıklamayı koru
                    existing.TahminiSureDk += t.TahminiSureDk;
                }
                else
                {
                    allTopics[key] = t;
                }
            }
        }

        return allTopics.Values.ToList();
    }

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static List<ExtractedTopicItem> ParseTopicsFromLlmResponse(string jsonResponse)
    {
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
                            if (p.Value.ValueKind == JsonValueKind.Number)
                                sure = p.Value.GetInt32();
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
        catch { }

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
