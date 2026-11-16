using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Services;

public partial class SourceContextBuilder : ISourceContextBuilder
{
    private const double RrfConstant = 60.0;
    private const double MmrLambda = 0.72;
    private const int BasePerQueryDenseLimit = 32;
    private const int BasePerQueryLexicalLimit = 32;
    private const int DefaultMaxChars = 120000;

    [GeneratedRegex(@"\s*\|\s*Mod:\s*[^\]\r\n]+(?=\])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedHeaderModSegmentRegex();

    [GeneratedRegex(@"^\s*\[Mod:\s*[^\]\r\n]+\]\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneLeadingModTagRegex();

    private readonly AppDbContext _dbContext;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<SourceContextBuilder> _logger;
    private readonly IQueryDecomposer _queryDecomposer;

    public SourceContextBuilder(
        AppDbContext dbContext,
        IEmbeddingProvider embeddingProvider,
        ILogger<SourceContextBuilder> logger,
        IQueryDecomposer? queryDecomposer = null)
    {
        _dbContext = dbContext;
        _embeddingProvider = embeddingProvider;
        _logger = logger;
        _queryDecomposer = queryDecomposer ?? new QueryDecomposer();
    }

    public bool IsPlaceholder(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        return text.Contains("[Mock", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Mock özet", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Transkript mevcut değil", StringComparison.OrdinalIgnoreCase)
            || text.Trim().Length < 15;
    }

    public Task<SourceContextResult> BuildContextAsync(
        Guid egitimId,
        IEnumerable<string> queries,
        int maxChars = DefaultMaxChars,
        CancellationToken ct = default)
        => BuildContextAsync(
            egitimId,
            queries,
            maxChars,
            preferredSourceIds: null,
            targetChunksOverride: null,
            ct: ct);

    public async Task<SourceContextResult> BuildContextAsync(
        Guid egitimId,
        IEnumerable<string> queries,
        int maxChars,
        IReadOnlyCollection<Guid>? preferredSourceIds,
        int? targetChunksOverride = null,
        CancellationToken ct = default)
    {
        var result = new SourceContextResult();
        if (maxChars <= 0)
            maxChars = DefaultMaxChars;

        var normalizedPreferredIds = (preferredSourceIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var preferredSet = normalizedPreferredIds.ToHashSet();

        bool isNpgsql = string.Equals(
            _dbContext.Database?.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.Ordinal);

        // 1. Eğitim kaynak meta verilerini (başlık, dil, güncel kullanım modu) topla
        var sourceMeta = await LoadCourseSourceMetadataAsync(egitimId, ct);
        if (sourceMeta.TotalSources == 0 && _dbContext.VideoChunkDocuments == null)
        {
            return result;
        }

        bool hasIndexedChunks = _dbContext.VideoChunkDocuments != null &&
                                await _dbContext.VideoChunkDocuments
                                    .AsNoTracking()
                                    .AnyAsync(c => c.EgitimId == egitimId && c.ChunkTipi != ChunkTipi.Kaynakca, ct);

        // 2. Küçük eğitim kontrolü: Yalnızca özel kaynak/parça kısıtı yokken ve ya hiç indeks yoksa
        // ya da çok az kaynaklı küçük bir eğitim bütçeye rahatça sığıyorsa tam metin ver
        if (preferredSet.Count == 0 &&
            !targetChunksOverride.HasValue &&
            sourceMeta.TotalRawChars > 0 &&
            sourceMeta.TotalRawChars <= maxChars &&
            (!hasIndexedChunks || (sourceMeta.TotalSources <= 3 && sourceMeta.TotalRawChars <= 35000)))
        {
            var smallCourseResult = BuildSmallCourseFullTextResult(sourceMeta, maxChars, null);
            if (!string.IsNullOrWhiteSpace(smallCourseResult.ContextText))
            {
                _logger.LogInformation(
                    "Küçük eğitim tam-metin modu aktif (Egitim={EgitimId}, ToplamHamKr={Total}, Kaynak={Count}).",
                    egitimId, sourceMeta.TotalRawChars, smallCourseResult.UsedSourceTitles.Count);
                return smallCourseResult;
            }
        }

        if (!hasIndexedChunks)
        {
            if (sourceMeta.TotalRawChars > 0)
            {
                return BuildSmallCourseFullTextResult(sourceMeta, maxChars, preferredSet.Count > 0 ? preferredSet : null);
            }
            return result;
        }

        // 3. Alt-sorgulara ayır + çapraz dil genişlet
        var rawQueryList = (queries ?? Enumerable.Empty<string>())
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => q.Trim())
            .ToList();

        var combinedQuery = string.Join("\n", rawQueryList);
        if (string.IsNullOrWhiteSpace(combinedQuery))
        {
            return await BuildBalancedOverviewContextAsync(
                egitimId,
                sourceMeta,
                maxChars,
                preferredSet,
                targetChunksOverride,
                ct);
        }

        var decomposedQueries = await _queryDecomposer.DecomposeAsync(combinedQuery, sourceMeta.Languages, ct);
        if (decomposedQueries.Count == 0)
        {
            decomposedQueries = new List<DecomposedQuery>
            {
                new(0, combinedQuery, combinedQuery, combinedQuery, Array.Empty<string>())
            };
        }

        if (_dbContext.VideoChunkDocuments == null)
        {
            return result;
        }

        // 4. Tüm alt-sorguların embedding vektörlerini tek toplu çağrıda üret
        var queryVectors = new List<float[]?>();
        try
        {
            var textsToEmbed = decomposedQueries.Select(q => q.ExpandedKeywordsText).ToList();
            var embs = await _embeddingProvider.GenerateEmbeddingsAsync(textsToEmbed);
            for (int i = 0; i < decomposedQueries.Count; i++)
            {
                queryVectors.Add(i < embs.Count && embs[i] != null && embs[i].Length > 0 ? embs[i] : null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Alt-sorgu embedding üretimi başarısız oldu, yalnızca sözcüksel RRF ile devam ediliyor.");
            while (queryVectors.Count < decomposedQueries.Count) queryVectors.Add(null);
        }

        // Non-PostgreSQL test ortamlarında tüm kurs parçalarını bir kez bellek içi aday havuzuna çek
        List<VideoChunkDocument>? inMemoryPool = null;
        if (!isNpgsql)
        {
            inMemoryPool = await _dbContext.VideoChunkDocuments
                .AsNoTracking()
                .Where(c => c.EgitimId == egitimId && c.ChunkTipi != ChunkTipi.Kaynakca)
                .ToListAsync(ct);

            if (inMemoryPool.Count == 0)
                return result;
        }

        int estimatedBudgetForLimits = targetChunksOverride.HasValue && targetChunksOverride.Value > 0
            ? targetChunksOverride.Value
            : Math.Clamp(Math.Max(32, (preferredSet.Count > 0 ? preferredSet.Count : Math.Max(1, sourceMeta.TotalSources)) * 3), 30, 64);

        int perQueryDenseLimit = Math.Max(BasePerQueryDenseLimit, estimatedBudgetForLimits);
        int perQueryLexicalLimit = Math.Max(BasePerQueryLexicalLimit, estimatedBudgetForLimits);

        // 5. Her alt-sorgu için Dense + Lexical adaylarını bul ve RRF skorla
        var perQueryRanked = new Dictionary<int, List<(VideoChunkDocument Chunk, double Score)>>();
        var globalCandidates = new Dictionary<Guid, (VideoChunkDocument Chunk, double MaxRrf, double SumRrf)>();

        for (int qi = 0; qi < decomposedQueries.Count; qi++)
        {
            var dq = decomposedQueries[qi];
            var qVec = queryVectors[qi];

            var denseList = await GetDenseCandidatesAsync(egitimId, qVec, perQueryDenseLimit, isNpgsql, inMemoryPool, ct);
            var lexList = await GetLexicalCandidatesAsync(egitimId, dq.LexicalTerms, perQueryLexicalLimit, isNpgsql, inMemoryPool, ct);

            var rrfByChunkId = new Dictionary<Guid, (VideoChunkDocument Chunk, double Score)>();

            for (int r = 0; r < denseList.Count; r++)
            {
                var c = denseList[r];
                double rrf = 1.0 / (RrfConstant + r + 1);
                rrfByChunkId[c.Id] = (c, rrf);
            }

            for (int r = 0; r < lexList.Count; r++)
            {
                var c = lexList[r];
                double lexRrf = 1.0 / (RrfConstant + r + 1);
                if (rrfByChunkId.TryGetValue(c.Id, out var cur))
                {
                    rrfByChunkId[c.Id] = (c, cur.Score + lexRrf);
                }
                else
                {
                    rrfByChunkId[c.Id] = (c, lexRrf);
                }
            }

            // OzetKart cezası + Doğrudan başlık/yazar adı eşleşme bonusu + Atanmış kaynak (preferredSourceIds) önceliği
            var scoredList = new List<(VideoChunkDocument Chunk, double Score)>(rrfByChunkId.Count);
            foreach (var kv in rrfByChunkId.Values)
            {
                double weight = kv.Chunk.ChunkTipi == ChunkTipi.OzetKart ? 0.75 : 1.0;
                var title = ResolveTitle(kv.Chunk, sourceMeta);
                if (dq.LexicalTerms.Any(t => t.Length >= 4 &&
                    (title.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                     QueryDecomposer.FoldTurkishDiacritics(title).Contains(QueryDecomposer.FoldTurkishDiacritics(t), StringComparison.OrdinalIgnoreCase))))
                {
                    weight *= 1.25;
                }

                if (preferredSet.Count > 0 && preferredSet.Contains(GetSourceKey(kv.Chunk)))
                {
                    weight *= 1.15;
                }

                double finalScore = kv.Score * weight;
                scoredList.Add((kv.Chunk, finalScore));

                if (globalCandidates.TryGetValue(kv.Chunk.Id, out var g))
                {
                    globalCandidates[kv.Chunk.Id] = (kv.Chunk, Math.Max(g.MaxRrf, finalScore), g.SumRrf + finalScore);
                }
                else
                {
                    globalCandidates[kv.Chunk.Id] = (kv.Chunk, finalScore, finalScore);
                }
            }

            scoredList.Sort((a, b) => b.Score.CompareTo(a.Score));
            perQueryRanked[qi] = scoredList;

            if (lexList.Count == 0 && denseList.Count == 0)
            {
                result.UncoveredSubTopics.Add(dq.Title);
            }
        }

        // 5b. preferredSourceIds verildiğinde aday havuzuna hiç girememiş veya tek parçada kalmış atanmış kaynakları
        // doğrudan kaynak bazlı sorgulayarak havuza dahil et (Recall@AssignedSource = %100 garantisi)
        if (preferredSet.Count > 0)
        {
            var allLexTerms = decomposedQueries
                .SelectMany(q => q.LexicalTerms)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var primaryVec = queryVectors.FirstOrDefault(v => v != null && v.Length > 0);

            await EnsurePreferredSourcesInCandidatePoolAsync(
                egitimId,
                normalizedPreferredIds,
                primaryVec,
                allLexTerms,
                isNpgsql,
                inMemoryPool,
                globalCandidates,
                ct);
        }

        if (globalCandidates.Count == 0)
        {
            return result;
        }

        // 6. 2 Aşamalı Kota-Kısıtlı RRF + MMR Seçimi
        int distinctCourseSources = Math.Max(
            sourceMeta.TotalSources,
            globalCandidates.Values.Select(v => GetSourceKey(v.Chunk)).Distinct().Count());

        int? preferredCountOrNull = preferredSet.Count > 0 ? preferredSet.Count : null;
        int computedTargetChunks = targetChunksOverride.HasValue && targetChunksOverride.Value > 0
            ? targetChunksOverride.Value
            : Math.Clamp(Math.Max(32, (preferredCountOrNull ?? distinctCourseSources) * 3), 30, 64);

        if (!targetChunksOverride.HasValue && preferredSet.Count > computedTargetChunks)
        {
            computedTargetChunks = preferredSet.Count;
        }

        int targetChunkCount = Math.Min(computedTargetChunks, globalCandidates.Count);

        // preferredSourceIds == null ve targetChunksOverride == null iken distinctCourseSources >= 8 ise kaynak başı <= 3 kuralı korunur
        int defaultMaxChunksPerSource = (preferredSet.Count == 0 && !targetChunksOverride.HasValue)
            ? (distinctCourseSources >= 8
                ? 3
                : Math.Max(3, (int)Math.Ceiling((double)computedTargetChunks / Math.Max(1, distinctCourseSources))))
            : Math.Max(3, (int)Math.Ceiling((double)computedTargetChunks / Math.Max(1, preferredCountOrNull ?? distinctCourseSources)));

        int preferredSourceCap = preferredSet.Count > 0
            ? Math.Max(4, (int)Math.Ceiling((double)computedTargetChunks / Math.Max(1, preferredSet.Count)) + 1)
            : defaultMaxChunksPerSource;

        int nonPreferredSourceCap = preferredSet.Count > 0
            ? 2
            : defaultMaxChunksPerSource;

        int GetPerSourceCap(Guid srcKey)
            => preferredSet.Count > 0
                ? (preferredSet.Contains(srcKey) ? preferredSourceCap : nonPreferredSourceCap)
                : defaultMaxChunksPerSource;

        var selected = new List<VideoChunkDocument>(targetChunkCount);
        var selectedIds = new HashSet<Guid>();
        var sourceCounts = new Dictionary<Guid, int>();
        int currentChars = 0;

        bool CanAdd(VideoChunkDocument chunk, bool ignorePerSourceCap = false)
        {
            if (selectedIds.Contains(chunk.Id)) return false;
            var srcKey = GetSourceKey(chunk);
            if (!ignorePerSourceCap)
            {
                sourceCounts.TryGetValue(srcKey, out int sc);
                if (sc >= GetPerSourceCap(srcKey)) return false;
            }
            int estLen = SanitizeChunkBodyText(chunk.Text).Length + 160;
            if (selected.Count > 0 && currentChars + estLen > maxChars) return false;
            return true;
        }

        void AddSelected(VideoChunkDocument chunk)
        {
            selected.Add(chunk);
            selectedIds.Add(chunk.Id);
            var srcKey = GetSourceKey(chunk);
            sourceCounts[srcKey] = sourceCounts.TryGetValue(srcKey, out int sc) ? sc + 1 : 1;
            currentChars += SanitizeChunkBodyText(chunk.Text).Length + 160;
        }

        // Aşama 1 (Taban Kota Garantisi — Floor Quota >= 1..2):
        // preferredSourceIds içindeki HER kaynaktan en yüksek RRF skorlu en az 1 (genel bütçe müsaitse 2) parçayı öncelikle seç!
        if (preferredSet.Count > 0)
        {
            var candidatesBySource = globalCandidates.Values
                .Where(v => preferredSet.Contains(GetSourceKey(v.Chunk)))
                .GroupBy(v => GetSourceKey(v.Chunk))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.MaxRrf + 0.35 * x.SumRrf)
                          .ThenBy(x => x.Chunk.ChunkTipi == ChunkTipi.OzetKart ? 1 : 0)
                          .ThenBy(x => x.Chunk.ChunkIndex)
                          .ToList());

            // Tur 1: Atanan her kaynaktan kesinlikle en iyi 1 parça (Recall@AssignedSource = %100)
            foreach (var prefId in normalizedPreferredIds)
            {
                if (targetChunksOverride.HasValue && selected.Count >= targetChunkCount)
                    break;

                if (!candidatesBySource.TryGetValue(prefId, out var srcList))
                    continue;

                foreach (var item in srcList)
                {
                    if (CanAdd(item.Chunk, ignorePerSourceCap: true))
                    {
                        AddSelected(item.Chunk);
                        break;
                    }
                }
            }

            // Tur 2: Genel parça ve karakter bütçesi müsaitse atanan her kaynaktan en iyi 2. parçayı da garanti et
            bool canAffordSecondFloorPass = targetChunkCount >= normalizedPreferredIds.Count * 2 + 4 &&
                                            currentChars < (int)(maxChars * 0.65);
            if (canAffordSecondFloorPass)
            {
                foreach (var prefId in normalizedPreferredIds)
                {
                    if (selected.Count >= targetChunkCount - 4 || currentChars >= (int)(maxChars * 0.72))
                        break;

                    if (!candidatesBySource.TryGetValue(prefId, out var srcList))
                        continue;

                    foreach (var item in srcList)
                    {
                        if (CanAdd(item.Chunk))
                        {
                            AddSelected(item.Chunk);
                            break;
                        }
                    }
                }
            }

            if (selected.Count > targetChunkCount && !targetChunksOverride.HasValue)
            {
                targetChunkCount = selected.Count;
            }
        }

        // Aşama 2A: Alt-sorgulardan Round-Robin ile her alt-başlığın temsilini garanti altına al
        for (int pass = 0; pass < 2 && selected.Count < targetChunkCount; pass++)
        {
            for (int qi = 0; qi < decomposedQueries.Count && selected.Count < targetChunkCount; qi++)
            {
                if (!perQueryRanked.TryGetValue(qi, out var ranked)) continue;
                foreach (var item in ranked)
                {
                    if (CanAdd(item.Chunk))
                    {
                        AddSelected(item.Chunk);
                        break;
                    }
                }
            }
        }

        // Aşama 2B: Kalan bütçeyi çeşitlilik cezalı Global MMR (λ = 0.72) ile doldur
        double maxGlobalRrf = Math.Max(1e-9, globalCandidates.Values.Max(v => v.MaxRrf + 0.35 * v.SumRrf));
        var remaining = globalCandidates.Values.ToList();

        while (selected.Count < targetChunkCount && currentChars < maxChars)
        {
            VideoChunkDocument? bestCandidate = null;
            double bestMmrScore = double.NegativeInfinity;

            foreach (var cand in remaining)
            {
                if (!CanAdd(cand.Chunk)) continue;

                double relevance = (cand.MaxRrf + 0.35 * cand.SumRrf) / maxGlobalRrf;
                var srcKey = GetSourceKey(cand.Chunk);
                if (preferredSet.Count > 0 && preferredSet.Contains(srcKey))
                {
                    relevance = Math.Min(1.05, relevance + 0.06);
                }

                double maxSim = 0.0;
                var candVec = cand.Chunk.Embedding?.ToArray();
                sourceCounts.TryGetValue(srcKey, out int sameSourceCount);

                foreach (var sel in selected)
                {
                    double sim;
                    var selVec = sel.Embedding?.ToArray();
                    if (candVec != null && selVec != null)
                    {
                        sim = Math.Max(0.0, 1.0 - ComputeCosineDistance(candVec, selVec));
                    }
                    else
                    {
                        sim = ComputeQuickJaccard(cand.Chunk.Text, sel.Text);
                    }

                    if (GetSourceKey(sel) == srcKey)
                    {
                        sim = Math.Min(1.0, sim + 0.12);
                    }

                    if (sim > maxSim) maxSim = sim;
                }

                double sourceQuotaPenalty = sameSourceCount * 0.08;
                double mmr = MmrLambda * relevance - (1.0 - MmrLambda) * maxSim - sourceQuotaPenalty;

                if (mmr > bestMmrScore)
                {
                    bestMmrScore = mmr;
                    bestCandidate = cand.Chunk;
                }
            }

            if (bestCandidate == null)
                break;

            AddSelected(bestCandidate);
        }

        PopulateFormattedResult(result, selected, sourceMeta, maxChars);

        _logger.LogInformation(
            "2 Aşamalı Kota-Kısıtlı RRF+MMR tamamlandı: {SubQueries} alt-sorgu -> {Chunks}/{Target} parça, {Sources} benzersiz kaynak (AtananKaynak={Preferred}, ToplamKr={Chars}).",
            decomposedQueries.Count, result.UsedChunkIds.Count, targetChunkCount, result.UsedSourceTitles.Count, preferredSet.Count, result.ContextText.Length);

        return result;
    }

    public async Task<SourceContextResult> BuildFromChunkIdsAsync(
        Guid egitimId,
        IEnumerable<Guid> chunkIds,
        CancellationToken ct = default)
    {
        var result = new SourceContextResult();
        if (_dbContext.VideoChunkDocuments == null)
            return result;

        var idList = (chunkIds ?? Enumerable.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var sourceMeta = await LoadCourseSourceMetadataAsync(egitimId, ct);

        if (idList.Count > 0)
        {
            var chunks = await _dbContext.VideoChunkDocuments
                .AsNoTracking()
                .Where(c => c.EgitimId == egitimId && idList.Contains(c.Id))
                .ToListAsync(ct);

            var orderMap = idList
                .Select((id, idx) => new { id, idx })
                .ToDictionary(x => x.id, x => x.idx);

            var ordered = chunks
                .OrderBy(c => orderMap.TryGetValue(c.Id, out int idx) ? idx : int.MaxValue)
                .ToList();

            PopulateFormattedResult(result, ordered, sourceMeta, int.MaxValue);
            return result;
        }

        // Yalnız chunkIds tamamen boşsa ve eğitim küçükse (<= 80K kr) tüm non-Kaynakca parçaları döndür
        if (sourceMeta.TotalRawChars > 0 && sourceMeta.TotalRawChars <= 80000)
        {
            return BuildSmallCourseFullTextResult(sourceMeta, 80000, null);
        }

        var smallChunks = await _dbContext.VideoChunkDocuments
            .AsNoTracking()
            .Where(c => c.EgitimId == egitimId && c.ChunkTipi != ChunkTipi.Kaynakca)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);

        int chunkCharSum = smallChunks.Sum(c => c.Text?.Length ?? 0);
        if (smallChunks.Count > 0 && chunkCharSum <= 80000)
        {
            result.UsedFullDocumentFallback = true;
            PopulateFormattedResult(result, smallChunks, sourceMeta, 80000);
        }

        return result;
    }

    private async Task EnsurePreferredSourcesInCandidatePoolAsync(
        Guid egitimId,
        List<Guid> orderedPreferredIds,
        float[]? primaryQueryVector,
        string[] lexicalTerms,
        bool isNpgsql,
        List<VideoChunkDocument>? inMemoryPool,
        Dictionary<Guid, (VideoChunkDocument Chunk, double MaxRrf, double SumRrf)> globalCandidates,
        CancellationToken ct)
    {
        var countsInPool = new Dictionary<Guid, int>();
        foreach (var cand in globalCandidates.Values)
        {
            var sk = GetSourceKey(cand.Chunk);
            countsInPool[sk] = countsInPool.TryGetValue(sk, out int c) ? c + 1 : 1;
        }

        var missingOrUnderrepresented = orderedPreferredIds
            .Where(id => !countsInPool.TryGetValue(id, out int cnt) || cnt < 2)
            .ToList();

        if (missingOrUnderrepresented.Count == 0)
            return;

        List<VideoChunkDocument> fetchedPool;
        if (isNpgsql)
        {
            var missingSet = missingOrUnderrepresented.ToList();
            if (primaryQueryVector != null && primaryQueryVector.Length > 0)
            {
                var vec = new Vector(primaryQueryVector);
                fetchedPool = new List<VideoChunkDocument>();
                // Tek toplu sorgu veya küçük gruplarla eksik atanmış kaynakların en yakın parçalarını çek
                foreach (var prefId in missingSet)
                {
                    var topForSource = await _dbContext.VideoChunkDocuments
                        .AsNoTracking()
                        .Where(c => c.EgitimId == egitimId &&
                                    c.ChunkTipi != ChunkTipi.Kaynakca &&
                                    (c.DokumanId == prefId || c.VideoId == prefId))
                        .OrderBy(c => c.Embedding != null ? c.Embedding.CosineDistance(vec) : 1.0)
                        .ThenBy(c => c.ChunkIndex)
                        .Take(4)
                        .ToListAsync(ct);
                    fetchedPool.AddRange(topForSource);
                }
            }
            else
            {
                fetchedPool = await _dbContext.VideoChunkDocuments
                    .AsNoTracking()
                    .Where(c => c.EgitimId == egitimId &&
                                c.ChunkTipi != ChunkTipi.Kaynakca &&
                                ((c.DokumanId.HasValue && missingSet.Contains(c.DokumanId.Value)) ||
                                 (c.VideoId.HasValue && missingSet.Contains(c.VideoId.Value))))
                    .OrderBy(c => c.ChunkIndex)
                    .ToListAsync(ct);
            }
        }
        else
        {
            var missingHash = missingOrUnderrepresented.ToHashSet();
            fetchedPool = (inMemoryPool ?? Enumerable.Empty<VideoChunkDocument>())
                .Where(c => missingHash.Contains(GetSourceKey(c)))
                .ToList();
        }

        foreach (var grp in fetchedPool.GroupBy(GetSourceKey))
        {
            var rankedForSource = grp
                .Select(c =>
                {
                    int lexHits = CountLexicalHits(c, lexicalTerms);
                    double denseSim = (primaryQueryVector != null && c.Embedding != null)
                        ? Math.Max(0.0, 1.0 - ComputeCosineDistance(c.Embedding.ToArray(), primaryQueryVector))
                        : 0.25;
                    double typeFactor = c.ChunkTipi == ChunkTipi.OzetKart ? 0.8 : 1.0;
                    double syntheticRrf = ((1.0 / (RrfConstant + 12)) + 0.004 * Math.Min(lexHits, 5) + 0.006 * denseSim) * typeFactor;
                    return (Chunk: c, Score: syntheticRrf);
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Chunk.ChunkIndex)
                .Take(3)
                .ToList();

            foreach (var item in rankedForSource)
            {
                if (!globalCandidates.ContainsKey(item.Chunk.Id))
                {
                    globalCandidates[item.Chunk.Id] = (item.Chunk, item.Score, item.Score);
                }
            }
        }
    }

    private async Task<List<VideoChunkDocument>> GetDenseCandidatesAsync(
        Guid egitimId,
        float[]? queryVector,
        int limit,
        bool isNpgsql,
        List<VideoChunkDocument>? inMemoryPool,
        CancellationToken ct)
    {
        if (queryVector == null || queryVector.Length == 0)
            return new List<VideoChunkDocument>();

        if (isNpgsql)
        {
            var vec = new Vector(queryVector);
            return await _dbContext.VideoChunkDocuments
                .AsNoTracking()
                .Where(c => c.EgitimId == egitimId && c.ChunkTipi != ChunkTipi.Kaynakca && c.Embedding != null)
                .OrderBy(c => c.Embedding!.CosineDistance(vec))
                .Take(limit)
                .ToListAsync(ct);
        }

        return (inMemoryPool ?? Enumerable.Empty<VideoChunkDocument>())
            .Where(c => c.Embedding != null)
            .OrderBy(c => ComputeCosineDistance(c.Embedding!.ToArray(), queryVector))
            .Take(limit)
            .ToList();
    }

    private async Task<List<VideoChunkDocument>> GetLexicalCandidatesAsync(
        Guid egitimId,
        string[] lexicalTerms,
        int limit,
        bool isNpgsql,
        List<VideoChunkDocument>? inMemoryPool,
        CancellationToken ct)
    {
        if (lexicalTerms == null || lexicalTerms.Length == 0)
            return new List<VideoChunkDocument>();

        if (isNpgsql)
        {
            var candidateMap = new Dictionary<Guid, (VideoChunkDocument Chunk, int Hits)>();

            // 1. PostgreSQL WebSearch OR sorgusu ('simple' sözlük ile özel isim bozulmadan)
            var safeFtsTerms = lexicalTerms
                .Select(t => Regex.Replace(t, @"[^\p{L}\p{N}]+", " ").Trim())
                .Where(t => t.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            if (safeFtsTerms.Count > 0)
            {
                try
                {
                    var orQuery = string.Join(" OR ", safeFtsTerms);
                    var ftsHits = await _dbContext.VideoChunkDocuments
                        .AsNoTracking()
                        .Where(c => c.EgitimId == egitimId &&
                                    c.ChunkTipi != ChunkTipi.Kaynakca &&
                                    EF.Functions.ToTsVector("simple", c.Text).Matches(EF.Functions.WebSearchToTsQuery("simple", orQuery)))
                        .Take(limit)
                        .ToListAsync(ct);

                    foreach (var hit in ftsHits)
                    {
                        int score = CountLexicalHits(hit, lexicalTerms);
                        candidateMap[hit.Id] = (hit, Math.Max(1, score));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "WebSearchToTsQuery hatası, ILIKE yedeğine geçiliyor.");
                }
            }

            // 2. Özel isim / Türkçe ek kökleri ve başlıklar için ILIKE taraması (en kritik 6 terim)
            foreach (var term in lexicalTerms.Where(t => t.Length >= 4).Take(6))
            {
                var pattern = $"%{term}%";
                var likeHits = await _dbContext.VideoChunkDocuments
                    .AsNoTracking()
                    .Where(c => c.EgitimId == egitimId &&
                                c.ChunkTipi != ChunkTipi.Kaynakca &&
                                (EF.Functions.ILike(c.Text, pattern) || EF.Functions.ILike(c.KaynakBaslik, pattern)))
                    .Take(12)
                    .ToListAsync(ct);

                foreach (var hit in likeHits)
                {
                    int score = CountLexicalHits(hit, lexicalTerms);
                    candidateMap[hit.Id] = (hit, Math.Max(1, score));
                }
            }

            return candidateMap.Values
                .OrderByDescending(x => x.Hits)
                .Take(limit)
                .Select(x => x.Chunk)
                .ToList();
        }

        // InMemory / SQLite test ortamında deterministik sözcüksel eşleşme
        return (inMemoryPool ?? Enumerable.Empty<VideoChunkDocument>())
            .Select(c => new { Chunk = c, Hits = CountLexicalHits(c, lexicalTerms) })
            .Where(x => x.Hits > 0)
            .OrderByDescending(x => x.Hits)
            .ThenBy(x => x.Chunk.ChunkIndex)
            .Take(limit)
            .Select(x => x.Chunk)
            .ToList();
    }

    private static int CountLexicalHits(VideoChunkDocument chunk, string[] terms)
    {
        if (terms == null || terms.Length == 0)
            return 0;

        var haystack = $"{chunk.KaynakBaslik} {chunk.BolumBasligi} {chunk.Text}".ToLowerInvariant();
        var foldedHaystack = QueryDecomposer.FoldTurkishDiacritics(haystack);
        int hits = 0;

        foreach (var t in terms)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            var tl = t.ToLowerInvariant();
            if (haystack.Contains(tl, StringComparison.Ordinal) ||
                foldedHaystack.Contains(QueryDecomposer.FoldTurkishDiacritics(tl), StringComparison.Ordinal))
            {
                hits++;
                if (!string.IsNullOrEmpty(chunk.KaynakBaslik) &&
                    chunk.KaynakBaslik.Contains(tl, StringComparison.OrdinalIgnoreCase))
                {
                    hits++;
                }
            }
        }

        return hits;
    }

    private async Task<CourseSourceMetadata> LoadCourseSourceMetadataAsync(Guid egitimId, CancellationToken ct)
    {
        var meta = new CourseSourceMetadata();
        var langSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_dbContext.Videolar != null)
        {
            var videos = await _dbContext.Videolar
                .AsNoTracking()
                .Where(v => v.EgitimId == egitimId)
                .ToListAsync(ct);

            var videoIds = videos.Select(v => v.Id).ToList();
            var transcripts = (videoIds.Count > 0 && _dbContext.VideoTranscripts != null)
                ? await _dbContext.VideoTranscripts
                    .AsNoTracking()
                    .Where(t => videoIds.Contains(t.VideoId))
                    .ToDictionaryAsync(t => t.VideoId, ct)
                : new Dictionary<Guid, VideoTranscript>();

            var summaries = (videoIds.Count > 0 && _dbContext.VideoSummaries != null)
                ? await _dbContext.VideoSummaries
                    .AsNoTracking()
                    .Where(s => videoIds.Contains(s.VideoId))
                    .ToDictionaryAsync(s => s.VideoId, ct)
                : new Dictionary<Guid, VideoSummary>();

            foreach (var v in videos)
            {
                transcripts.TryGetValue(v.Id, out var tr);
                summaries.TryGetValue(v.Id, out var sum);
                var lang = !string.IsNullOrWhiteSpace(sum?.OrijinalDil)
                    ? sum!.OrijinalDil.Trim()
                    : (string.IsNullOrWhiteSpace(tr?.Dil) ? "tr" : tr!.Dil.Trim());
                langSet.Add(lang);
                meta.VideoTitles[v.Id] = v.Baslik;
                meta.VideoLangs[v.Id] = lang;
                meta.VideoModes[v.Id] = v.KullanimTuru;

                if (tr != null && !string.IsNullOrWhiteSpace(tr.HamMetin) && !IsPlaceholder(tr.HamMetin))
                {
                    meta.SmallCourseItems.Add(new SmallCourseRawItem(
                        Id: v.Id, IsVideo: true, Title: v.Baslik, Lang: lang, Mode: v.KullanimTuru, Text: tr.HamMetin));
                    meta.TotalRawChars += tr.HamMetin.Length;
                }
            }
        }

        if (_dbContext.Dokumanlar != null)
        {
            var docs = await _dbContext.Dokumanlar
                .AsNoTracking()
                .Where(d => d.EgitimId == egitimId)
                .ToListAsync(ct);

            var docIds = docs.Select(d => d.Id).ToList();
            var docMetins = (docIds.Count > 0 && _dbContext.DokumanMetinleri != null)
                ? await _dbContext.DokumanMetinleri
                    .AsNoTracking()
                    .Where(m => docIds.Contains(m.DokumanId))
                    .ToDictionaryAsync(m => m.DokumanId, ct)
                : new Dictionary<Guid, DokumanMetin>();

            foreach (var d in docs)
            {
                docMetins.TryGetValue(d.Id, out var dm);
                var lang = string.IsNullOrWhiteSpace(dm?.OrijinalDil) ? "tr" : dm!.OrijinalDil.Trim();
                langSet.Add(lang);
                meta.DocTitles[d.Id] = d.DosyaAdi;
                meta.DocLangs[d.Id] = lang;
                meta.DocModes[d.Id] = d.KullanimTuru;

                if (dm != null && !string.IsNullOrWhiteSpace(dm.HamMetin))
                {
                    meta.SmallCourseItems.Add(new SmallCourseRawItem(
                        Id: d.Id, IsVideo: false, Title: d.DosyaAdi, Lang: lang, Mode: d.KullanimTuru, Text: dm.HamMetin));
                    meta.TotalRawChars += dm.HamMetin.Length;
                }
            }
        }

        meta.TotalSources = meta.VideoTitles.Count + meta.DocTitles.Count;
        meta.Languages = langSet.ToArray();
        return meta;
    }

    private static SourceContextResult BuildSmallCourseFullTextResult(
        CourseSourceMetadata meta,
        int maxChars,
        HashSet<Guid>? preferredSet)
    {
        var result = new SourceContextResult { UsedFullDocumentFallback = true };
        var sb = new StringBuilder();
        var titleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var orderedItems = preferredSet != null && preferredSet.Count > 0
            ? meta.SmallCourseItems
                .OrderByDescending(i => preferredSet.Contains(i.Id))
                .ThenBy(i => i.Mode)
                .ToList()
            : meta.SmallCourseItems
                .OrderBy(i => i.Mode)
                .ToList();

        int idx = 1;
        foreach (var item in orderedItems)
        {
            if (sb.Length >= maxChars) break;
            var code = $"K{idx++}";
            var tur = item.IsVideo ? "VİDEO" : "DOKÜMAN";
            var mod = item.Mode == KaynakKullanimTuru.SadeceArgumanVeMantik
                ? "DIŞ KAYNAK - KOPYALAMAK KESİNLİKLE YASAKTIR"
                : "DOĞRUDAN REFERANS (Prompter Dahil)";
            sb.AppendLine($"=== [{code}] | {tur}: {item.Title} | Dil: {item.Lang.ToUpperInvariant()} | Mod: {mod} ===");
            sb.AppendLine(SanitizeChunkBodyText(item.Text));
            sb.AppendLine();

            titleSet.Add(item.Title);
            result.Evidences.Add(new ChunkEvidenceItem(
                KanitKodu: code,
                ChunkId: Guid.Empty,
                VideoId: item.IsVideo ? item.Id : null,
                DokumanId: item.IsVideo ? null : item.Id,
                KaynakBaslik: item.Title,
                BolumBasligi: "Tam Metin",
                StartTimeMs: 0,
                EndTimeMs: 0,
                KaynakDil: item.Lang,
                KullanimTuru: item.Mode));
        }

        result.ContextText = sb.ToString();
        result.UsedSourceTitles = titleSet.ToList();
        return result;
    }

    private async Task<SourceContextResult> BuildBalancedOverviewContextAsync(
        Guid egitimId,
        CourseSourceMetadata meta,
        int maxChars,
        HashSet<Guid> preferredSet,
        int? targetChunksOverride,
        CancellationToken ct)
    {
        var result = new SourceContextResult();
        if (_dbContext.VideoChunkDocuments == null) return result;

        var chunks = await _dbContext.VideoChunkDocuments
            .AsNoTracking()
            .Where(c => c.EgitimId == egitimId && c.ChunkTipi != ChunkTipi.Kaynakca)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);

        int distinctSources = Math.Max(meta.TotalSources, chunks.Select(GetSourceKey).Distinct().Count());
        int? preferredCountOrNull = preferredSet.Count > 0 ? preferredSet.Count : null;
        int targetBudget = targetChunksOverride.HasValue && targetChunksOverride.Value > 0
            ? targetChunksOverride.Value
            : Math.Clamp(Math.Max(32, (preferredCountOrNull ?? distinctSources) * 3), 30, 64);

        IEnumerable<IGrouping<Guid, VideoChunkDocument>> grouped = chunks.GroupBy(GetSourceKey);
        if (preferredSet.Count > 0)
        {
            grouped = grouped.OrderByDescending(g => preferredSet.Contains(g.Key));
        }

        var picked = grouped
            .SelectMany(g => g.Take(preferredSet.Count > 0 && preferredSet.Contains(g.Key) ? 3 : 2))
            .Take(Math.Max(targetBudget, preferredSet.Count))
            .ToList();

        PopulateFormattedResult(result, picked, meta, maxChars);
        return result;
    }

    /// <summary>
    /// Eski indekslenmiş parçalarda metnin başında kalmış olabilecek "| Mod: ...]" veya "[Mod: ...]"
    /// etiketlerini temizler; güncel kullanım modu yalnızca üst başlığa dinamik basılır.
    /// </summary>
    private static string SanitizeChunkBodyText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        var cleaned = EmbeddedHeaderModSegmentRegex().Replace(rawText, string.Empty);
        cleaned = StandaloneLeadingModTagRegex().Replace(cleaned, string.Empty);
        return cleaned.Trim();
    }

    private static void PopulateFormattedResult(
        SourceContextResult result,
        List<VideoChunkDocument> chunks,
        CourseSourceMetadata meta,
        int maxChars)
    {
        var sb = new StringBuilder();
        var titleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var cleanBody = SanitizeChunkBodyText(chunk.Text);
            if (sb.Length > 0 && sb.Length + cleanBody.Length + 160 > maxChars)
                break;

            string code = $"K{i + 1}";
            string title = ResolveTitle(chunk, meta);
            string lang = ResolveLang(chunk, meta);
            var mode = ResolveMode(chunk, meta);
            string tur = chunk.VideoId.HasValue ? "VİDEO" : "DOKÜMAN";
            string modLabel = mode == KaynakKullanimTuru.SadeceArgumanVeMantik
                ? "DIŞ KAYNAK - KOPYALAMAK KESİNLİKLE YASAKTIR"
                : "DOĞRUDAN REFERANS (Prompter Dahil)";

            string locLabel = !string.IsNullOrWhiteSpace(chunk.BolumBasligi)
                ? chunk.BolumBasligi!
                : (chunk.EndTimeMs > chunk.StartTimeMs
                    ? $"{FormatMs(chunk.StartTimeMs)}–{FormatMs(chunk.EndTimeMs)}"
                    : "Genel");

            sb.AppendLine($"=== [{code}] | {tur}: {title} | Konum: {locLabel} | Dil: {lang.ToUpperInvariant()} | Mod: {modLabel} ===");
            sb.AppendLine(cleanBody);
            sb.AppendLine();

            result.UsedChunkIds.Add(chunk.Id);
            titleSet.Add(title);
            result.Evidences.Add(new ChunkEvidenceItem(
                KanitKodu: code,
                ChunkId: chunk.Id,
                VideoId: chunk.VideoId,
                DokumanId: chunk.DokumanId,
                KaynakBaslik: title,
                BolumBasligi: locLabel,
                StartTimeMs: chunk.StartTimeMs,
                EndTimeMs: chunk.EndTimeMs,
                KaynakDil: lang,
                KullanimTuru: mode));
        }

        result.ContextText = sb.ToString();
        result.UsedSourceTitles = titleSet.ToList();
    }

    private static Guid GetSourceKey(VideoChunkDocument chunk)
    {
        if (chunk.DokumanId.HasValue && chunk.DokumanId.Value != Guid.Empty) return chunk.DokumanId.Value;
        if (chunk.VideoId.HasValue && chunk.VideoId.Value != Guid.Empty) return chunk.VideoId.Value;
        return chunk.Id;
    }

    private static string ResolveTitle(VideoChunkDocument c, CourseSourceMetadata meta)
    {
        if (!string.IsNullOrWhiteSpace(c.KaynakBaslik)) return c.KaynakBaslik;
        if (c.DokumanId.HasValue && meta.DocTitles.TryGetValue(c.DokumanId.Value, out var dt)) return dt;
        if (c.VideoId.HasValue && meta.VideoTitles.TryGetValue(c.VideoId.Value, out var vt)) return vt;
        return c.VideoId.HasValue ? "Video Kaynağı" : "Doküman Kaynağı";
    }

    private static string ResolveLang(VideoChunkDocument c, CourseSourceMetadata meta)
    {
        if (!string.IsNullOrWhiteSpace(c.KaynakDil)) return c.KaynakDil;
        if (c.DokumanId.HasValue && meta.DocLangs.TryGetValue(c.DokumanId.Value, out var dl)) return dl;
        if (c.VideoId.HasValue && meta.VideoLangs.TryGetValue(c.VideoId.Value, out var vl)) return vl;
        return "tr";
    }

    private static KaynakKullanimTuru ResolveMode(VideoChunkDocument c, CourseSourceMetadata meta)
    {
        if (c.DokumanId.HasValue && meta.DocModes.TryGetValue(c.DokumanId.Value, out var dm)) return dm;
        if (c.VideoId.HasValue && meta.VideoModes.TryGetValue(c.VideoId.Value, out var vm)) return vm;
        return c.KullanimTuru;
    }

    private static string FormatMs(int ms)
    {
        var ts = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private static double ComputeCosineDistance(float[]? a, float[]? b)
    {
        if (a == null || b == null || a.Length != b.Length || a.Length == 0) return 1.0;
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA <= 0 || normB <= 0) return 1.0;
        return 1.0 - (dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }

    private static double ComputeQuickJaccard(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
        var setA = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4), StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4), StringComparer.OrdinalIgnoreCase);
        if (setA.Count == 0 || setB.Count == 0) return 0;
        int inter = setA.Count(w => setB.Contains(w));
        int union = setA.Count + setB.Count - inter;
        return union > 0 ? (double)inter / union : 0;
    }

    private sealed record SmallCourseRawItem(Guid Id, bool IsVideo, string Title, string Lang, KaynakKullanimTuru Mode, string Text);

    private sealed class CourseSourceMetadata
    {
        public Dictionary<Guid, string> VideoTitles { get; } = new();
        public Dictionary<Guid, string> VideoLangs { get; } = new();
        public Dictionary<Guid, KaynakKullanimTuru> VideoModes { get; } = new();
        public Dictionary<Guid, string> DocTitles { get; } = new();
        public Dictionary<Guid, string> DocLangs { get; } = new();
        public Dictionary<Guid, KaynakKullanimTuru> DocModes { get; } = new();
        public List<SmallCourseRawItem> SmallCourseItems { get; } = new();
        public int TotalRawChars { get; set; }
        public int TotalSources { get; set; }
        public string[] Languages { get; set; } = Array.Empty<string>();
    }
}
