using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VideoOzet.Business.Services;

public class SourceContextResult
{
    public string ContextText { get; set; } = string.Empty;
    public List<Guid> UsedChunkIds { get; set; } = new();
    public List<string> UsedSourceTitles { get; set; } = new();
    public bool UsedFullDocumentFallback { get; set; }
}

public interface ISourceContextBuilder
{
    Task<SourceContextResult> BuildContextAsync(
        Guid egitimId,
        IEnumerable<string> searchQueries,
        int maxChars = 80000,
        CancellationToken ct = default);

    Task<SourceContextResult> BuildFromChunkIdsAsync(
        Guid egitimId,
        IEnumerable<Guid> chunkIds,
        CancellationToken ct = default);

    bool IsPlaceholder(string? text);
}
