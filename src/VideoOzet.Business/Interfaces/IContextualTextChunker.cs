using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Interfaces;

public sealed record ContextualChunkOptions(
    string KaynakBaslik,
    string KaynakTuru,
    string Dil,
    KaynakKullanimTuru KullanimTuru,
    int TargetWords = 500,
    int OverlapWords = 75,
    int? VideoDurationSeconds = null);

public sealed record ContextualChunk(
    int ChunkIndex,
    ChunkTipi ChunkTipi,
    string? BolumBasligi,
    int StartTimeMs,
    int EndTimeMs,
    string RawText,
    string ContextualizedText);

/// <summary>
/// Doküman ham metinlerini ve zaman damgalı video transkriptlerini
/// bağlamsal üstbilgi ve bölüm/zaman metadata'sı ile parçalara böler.
/// </summary>
public interface IContextualTextChunker
{
    IReadOnlyList<ContextualChunk> ChunkDocument(string hamMetin, ContextualChunkOptions options);
    IReadOnlyList<ContextualChunk> ChunkTranscript(string hamMetin, string? zamanDamgalariJson, ContextualChunkOptions options);
}
