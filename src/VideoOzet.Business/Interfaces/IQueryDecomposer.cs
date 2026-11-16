namespace VideoOzet.Business.Interfaces;

public sealed record DecomposedQuery(
    int Index,
    string Title,
    string OriginalText,
    string ExpandedKeywordsText,
    string[] LexicalTerms);

/// <summary>
/// Çok maddeli içerik taslaklarını ayrı alt-sorgulara böler ve yabancı dildeki kaynakları
/// yakalayabilmek için anahtar terimleri çapraz dille genişletir.
/// </summary>
public interface IQueryDecomposer
{
    Task<IReadOnlyList<DecomposedQuery>> DecomposeAsync(
        string rawTopicOrOutline,
        IReadOnlyCollection<string> courseLanguages,
        CancellationToken ct = default);
}
