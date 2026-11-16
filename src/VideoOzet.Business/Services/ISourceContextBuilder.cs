using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Services;

public sealed record ChunkEvidenceItem(
    string KanitKodu,
    Guid ChunkId,
    Guid? VideoId,
    Guid? DokumanId,
    string KaynakBaslik,
    string? BolumBasligi,
    int StartTimeMs,
    int EndTimeMs,
    string KaynakDil,
    KaynakKullanimTuru KullanimTuru);

public class SourceContextResult
{
    public string ContextText { get; set; } = string.Empty;
    public List<Guid> UsedChunkIds { get; set; } = new();
    public List<string> UsedSourceTitles { get; set; } = new();
    public bool UsedFullDocumentFallback { get; set; }
    public List<ChunkEvidenceItem> Evidences { get; set; } = new();
    public List<string> UncoveredSubTopics { get; set; } = new();
}

public interface ISourceContextBuilder
{
    Task<SourceContextResult> BuildContextAsync(
        Guid egitimId,
        IEnumerable<string> queries,
        int maxChars = 90000,
        CancellationToken ct = default);

    Task<SourceContextResult> BuildContextAsync(
        Guid egitimId,
        IEnumerable<string> queries,
        int maxChars,
        IReadOnlyCollection<Guid>? preferredSourceIds,
        int? targetChunksOverride = null,
        CancellationToken ct = default)
        => BuildContextAsync(egitimId, queries, maxChars, ct);

    Task<SourceContextResult> BuildFromChunkIdsAsync(
        Guid egitimId,
        IEnumerable<Guid> chunkIds,
        CancellationToken ct = default);
}
