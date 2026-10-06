using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;

namespace VideoOzet.Business.Services;

public class SourceContextBuilder : ISourceContextBuilder
{
    private readonly AppDbContext _dbContext;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<SourceContextBuilder> _logger;

    public SourceContextBuilder(
        AppDbContext dbContext,
        IEmbeddingProvider embeddingProvider,
        ILogger<SourceContextBuilder> logger)
    {
        _dbContext = dbContext;
        _embeddingProvider = embeddingProvider;
        _logger = logger;
    }

    public bool IsPlaceholder(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (text.Contains("Bu eğitim videosunda temel kavramlar, metodoloji ve uygulama örnekleri", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("temel kavramlar, metodoloji ve uygulama örnekleri ele alınmakta", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public async Task<SourceContextResult> BuildContextAsync(
        Guid egitimId,
        IEnumerable<string> searchQueries,
        int maxChars = 80000,
        CancellationToken ct = default)
    {
        var result = new SourceContextResult();

        // 1. Eğitime ait gerçek dokümanları al
        var dokumanlar = await _dbContext.Dokumanlar
            .Include(d => d.DokumanMetin)
            .Where(d => d.EgitimId == egitimId && d.DokumanMetin != null && !string.IsNullOrWhiteSpace(d.DokumanMetin.HamMetin))
            .ToListAsync(ct);

        // 2. Eğitime ait gerçek (mock olmayan) videoları al
        var videolar = await _dbContext.Videolar
            .Include(v => v.Summary)
            .Include(v => v.Transcript)
            .Where(v => v.EgitimId == egitimId)
            .ToListAsync(ct);

        var validVideolar = videolar.Where(v =>
            (!string.IsNullOrWhiteSpace(v.Transcript?.HamMetin) && !IsPlaceholder(v.Transcript.HamMetin)) ||
            (!string.IsNullOrWhiteSpace(v.Summary?.OzetMetni) && !IsPlaceholder(v.Summary.OzetMetni))
        ).ToList();

        // 3. Dokümanların ve geçerli videoların toplam karakter boyutunu hesapla
        int totalDocChars = dokumanlar.Sum(d => d.DokumanMetin!.HamMetin.Length);
        int totalVideoChars = validVideolar.Sum(v => (v.Transcript?.HamMetin?.Length ?? 0) + (v.Summary?.OzetMetni?.Length ?? 0));
        int totalRealChars = totalDocChars + totalVideoChars;

        _logger.LogInformation("SourceContextBuilder: Egitim {EgitimId} için {DocCount} doküman ({DocChars} kr) ve {VidCount} geçerli video ({VidChars} kr) bulundu.",
            egitimId, dokumanlar.Count, totalDocChars, validVideolar.Count, totalVideoChars);

        // KÜÇÜK / ORTA EĞİTİM KISAYOLU:
        // Eğer toplam gerçek içerik maxChars (örn. 80.000) içindeyse, hiçbir bilgiyi kaybetmemek için
        // doğrudan tüm doküman ve geçerli video metinlerini yapılandırılmış şekilde veriyoruz!
        if (totalRealChars > 0 && totalRealChars <= maxChars)
        {
            var sb = new StringBuilder();
            int idx = 1;

            foreach (var doc in dokumanlar)
            {
                var text = doc.DokumanMetin!.HamMetin.Trim();
                sb.AppendLine($"[KAYNAK #{idx++} · DOKÜMAN: {doc.DosyaAdi}]");
                sb.AppendLine(text);
                sb.AppendLine("---");
                result.UsedSourceTitles.Add(doc.DosyaAdi);
            }

            foreach (var vid in validVideolar)
            {
                var text = (!string.IsNullOrWhiteSpace(vid.Transcript?.HamMetin) && !IsPlaceholder(vid.Transcript.HamMetin))
                    ? vid.Transcript.HamMetin.Trim()
                    : vid.Summary?.OzetMetni?.Trim() ?? "";
                sb.AppendLine($"[KAYNAK #{idx++} · VİDEO: {vid.Baslik}]");
                sb.AppendLine(text);
                sb.AppendLine("---");
                result.UsedSourceTitles.Add(vid.Baslik);
            }

            if (_dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
            {
                var docIds = dokumanlar.Select(d => d.Id).ToList();
                result.UsedChunkIds = await _dbContext.VideoChunkDocuments
                    .Where(c => c.EgitimId == egitimId && c.DokumanId != null && docIds.Contains(c.DokumanId.Value))
                    .Select(c => c.Id)
                    .ToListAsync(ct);
            }

            result.ContextText = sb.ToString();
            result.UsedFullDocumentFallback = true;
            return result;
        }

        // BÜYÜK EĞİTİMLER veya maxChars üstü:
        // Vektör araması ile en alakalı chunk'ları bul (Placeholder olanlar hariç!)
        var validChunks = new List<VideoChunkDocument>();
        var cleanQueries = (searchQueries ?? Enumerable.Empty<string>())
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct()
            .ToList();

        if (_dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            return result;
        }

        if (cleanQueries.Any())
        {
            foreach (var q in cleanQueries)
            {
                try
                {
                    var emb = await _embeddingProvider.GenerateEmbeddingAsync(q);
                    if (emb != null && emb.Length > 0 && emb.Any(v => v != 0))
                    {
                        var vec = new Pgvector.Vector(emb);
                        var chunks = await _dbContext.VideoChunkDocuments
                            .Where(c => c.EgitimId == egitimId)
                            .OrderBy(c => c.Embedding!.CosineDistance(vec))
                            .Take(10)
                            .ToListAsync(ct);

                        // Mock içerik içermeyenleri filtrele
                        validChunks.AddRange(chunks.Where(c => !IsPlaceholder(c.Text)));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Vektör araması sırasında hata. Sorgu: {Query}", q);
                }
            }
        }

        var distinctChunks = validChunks
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .ToList();

        // Eğer vektör araması hiçbir geçerli chunk bulamadıysa, doküman chunk'larını doğrudan sırayla al!
        if (!distinctChunks.Any())
        {
            _logger.LogInformation("Vektör araması geçerli chunk döndürmedi. Doküman chunk'ları doğrudan alınıyor...");
            var allEgitimChunks = await _dbContext.VideoChunkDocuments
                .Where(c => c.EgitimId == egitimId && c.DokumanId != null)
                .ToListAsync(ct);

            distinctChunks = allEgitimChunks.Where(c => !IsPlaceholder(c.Text)).ToList();
        }

        // Metinleri maxChars sınırına kadar topla
        var builder = new StringBuilder();
        int currentLength = 0;
        int chunkIdx = 1;

        foreach (var chunk in distinctChunks)
        {
            if (currentLength + chunk.Text.Length > maxChars && currentLength > 0)
                break;

            string label = chunk.DokumanId != null ? $"Doküman Chunk #{chunkIdx++}" : $"Video #{chunk.VideoId}";
            builder.AppendLine($"[KAYNAK: {label}]");
            builder.AppendLine(chunk.Text.Trim());
            builder.AppendLine("---");

            currentLength += chunk.Text.Length;
            result.UsedChunkIds.Add(chunk.Id);
        }

        result.ContextText = builder.ToString();
        result.UsedFullDocumentFallback = false;

        // Son emniyet: Eğer hâlâ metin oluşmadıysa ama doküman metinleri varsa, dokümanların başından bir parça al
        if (string.IsNullOrWhiteSpace(result.ContextText) && dokumanlar.Any())
        {
            var fallbackSb = new StringBuilder();
            foreach (var doc in dokumanlar)
            {
                var sub = doc.DokumanMetin!.HamMetin.Length > maxChars / dokumanlar.Count
                    ? doc.DokumanMetin.HamMetin.Substring(0, maxChars / dokumanlar.Count)
                    : doc.DokumanMetin.HamMetin;
                fallbackSb.AppendLine($"[KAYNAK: {doc.DosyaAdi}]");
                fallbackSb.AppendLine(sub.Trim());
                fallbackSb.AppendLine("---");
            }
            result.ContextText = fallbackSb.ToString();
            result.UsedFullDocumentFallback = true;
        }

        return result;
    }

    public async Task<SourceContextResult> BuildFromChunkIdsAsync(
        Guid egitimId,
        IEnumerable<Guid> chunkIds,
        CancellationToken ct = default)
    {
        var result = new SourceContextResult();
        if (_dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            return result;
        }
        var idList = (chunkIds ?? Enumerable.Empty<Guid>()).Distinct().ToList();

        if (idList.Any())
        {
            var chunks = await _dbContext.VideoChunkDocuments
                .Where(c => idList.Contains(c.Id))
                .ToListAsync(ct);

            var valid = chunks.Where(c => !IsPlaceholder(c.Text)).ToList();
            if (valid.Any())
            {
                var sb = new StringBuilder();
                int idx = 1;
                foreach (var chunk in valid)
                {
                    sb.AppendLine($"[KAYNAK #{idx++}]");
                    sb.AppendLine(chunk.Text.Trim());
                    sb.AppendLine("---");
                    result.UsedChunkIds.Add(chunk.Id);
                }
                result.ContextText = sb.ToString();
                return result;
            }
        }

        // Eğer verilen chunk ID'ler boş veya placeholder ise, tüm eğitime ait bağlamı yeniden oluştur
        return await BuildContextAsync(egitimId, new[] { "" }, 80000, ct);
    }
}
