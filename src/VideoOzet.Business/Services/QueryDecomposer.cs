using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Services;

/// <summary>
/// Numaralı/maddeli konu planlarını alt-sorgulara böler, Türkçe özel isim köklerini temizler
/// ve eğitimde yabancı dilde kaynak varsa hem deterministik akademik sözlük hem de dinamik LLM
/// (önbellekli, hata/timeout dayanıklı) üzerinden çapraz dil terim genişlemesi uygular.
/// </summary>
public partial class QueryDecomposer : IQueryDecomposer
{
    private const int DynamicExpansionTimeoutSeconds = 12;
    private const int MaxCacheEntries = 2048;

    private static readonly ConcurrentDictionary<string, string[]> _dynamicExpansionCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IGeminiProvider? _gemini;
    private readonly ILogger<QueryDecomposer>? _logger;

    public QueryDecomposer() : this(null, null)
    {
    }

    [ActivatorUtilitiesConstructor]
    public QueryDecomposer(
        IGeminiProvider? geminiProvider = null,
        ILogger<QueryDecomposer>? logger = null)
    {
        _gemini = geminiProvider;
        _logger = logger;
    }

    [GeneratedRegex(@"^\s*(?:[-*•]+|\d{1,2}[\.\):]|#{1,4}|[IVXLCDM]{1,4}\.)\s+", RegexOptions.CultureInvariant)]
    private static partial Regex BulletPrefixRegex();

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ve", "ile", "bir", "bu", "şu", "o", "için", "gibi", "daha", "en", "da", "de", "ki",
        "mi", "mı", "mu", "mü", "ama", "fakat", "ancak", "veya", "ya", "hem", "ise", "olarak",
        "üzerine", "hakkında", "nasıl", "neden", "nedir", "üzerinden", "kadar", "olan", "olup",
        "the", "and", "for", "with", "from", "that", "this", "how", "what", "why", "are", "was",
        "into", "over", "about", "their", "these", "those", "mock", "özet", "ozet"
    };

    private static readonly (string Pattern, string[] Expansions)[] CrossLingualDictionary =
    {
        // Mevcut temel kavramlar (Geriye uyumluluk)
        ("özgür irade", new[] { "free will", "agency", "volition", "freedom of will" }),
        ("irade", new[] { "free will", "will", "volition", "agency" }),
        ("determinizm", new[] { "determinism", "hard determinism", "causal determinism" }),
        ("belirlenimcilik", new[] { "determinism", "causality" }),
        ("uyumluluk", new[] { "compatibilism", "soft determinism", "compatibilist" }),
        ("bağdaşımcılık", new[] { "compatibilism", "soft determinism" }),
        ("cüzî", new[] { "kalam", "free choice", "acquisition", "ghazali", "maturidi" }),
        ("cuzi", new[] { "kalam", "free choice", "acquisition", "ghazali", "maturidi" }),
        ("kesb", new[] { "kasb", "acquisition", "ash'ari", "ghazali", "kalam" }),
        ("kader", new[] { "predestination", "determinism", "divine decree", "free will" }),
        ("gazzâlî", new[] { "ghazali", "al-ghazali", "gazzali", "gazali" }),
        ("gazzali", new[] { "ghazali", "al-ghazali", "gazzâlî", "gazali" }),
        ("mâtürîdî", new[] { "maturidi", "al-maturidi", "mâtürîdî" }),
        ("ibn sînâ", new[] { "avicenna", "ibn sina" }),
        ("ibn rüşd", new[] { "averroes", "ibn rushd" }),
        ("fârâbî", new[] { "al-farabi", "farabi" }),
        ("eş'arî", new[] { "ash'ari", "al-ash'ari", "asharite" }),
        ("mutezile", new[] { "mu'tazila", "mutazilite", "rationalism" }),
        ("biyolojik", new[] { "biological determinism", "genetics", "neurobiology", "Sapolsky", "determined" }),
        ("hormon", new[] { "hormones", "dopamine", "testosterone", "neurobiology" }),
        ("dopamin", new[] { "dopamine", "reward circuit", "neurobiology", "impulse" }),
        ("beyin", new[] { "brain", "neuroscience", "prefrontal cortex", "Libet" }),
        ("nörobilim", new[] { "neuroscience", "neurobiology", "Libet", "Sapolsky" }),
        ("nörobiyoloji", new[] { "neurobiology", "neuroscience", "Sapolsky", "prefrontal cortex" }),
        ("algoritma", new[] { "algorithm", "algorithmic", "recommender systems", "AI" }),
        ("öneri sistem", new[] { "recommender systems", "recommendation algorithms", "ethics of recommender" }),
        ("öneri algoritma", new[] { "recommender system", "recommender systems", "recommendation algorithm" }),
        ("tavsiye sistem", new[] { "recommender systems", "recommendation algorithms" }),
        ("dikkat ekonomisi", new[] { "attention economy", "hijacks minds", "persuasive design", "Tristan Harris" }),
        ("manipülasyon", new[] { "manipulation", "hypernudge", "algorithmic manipulation", "persuasion" }),
        ("seçim mimarisi", new[] { "choice architecture", "nudge", "hypernudge", "Karen Yeung" }),
        ("dürtme", new[] { "nudge", "hypernudge", "choice architecture" }),
        ("sosyal medya", new[] { "social media", "attention economy", "platform", "feed" }),
        ("ceza hukuku", new[] { "criminal law", "culpability", "mens rea", "liability", "responsibility" }),
        ("sorumluluk", new[] { "responsibility", "moral responsibility", "criminal liability", "accountability" }),
        ("hukuk", new[] { "law", "legal regulation", "algorithmic regulation", "Hildebrandt" }),
        ("yapay zeka", new[] { "artificial intelligence", "AI", "deep learning", "machine learning" }),
        ("mimetik", new[] { "mimetic desire", "Rene Girard", "imitation" }),
        ("rastgele", new[] { "randomness", "indeterminism", "quantum" }),
        ("istatistik", new[] { "statistics", "data", "empirical study", "percent" }),
        ("somut örnek", new[] { "case study", "real-world example", "empirical" }),
        ("free will", new[] { "özgür irade", "determinizm", "uyumluluk" }),
        ("recommender", new[] { "öneri sistemleri", "algoritma", "manipülasyon" }),

        // Çok disiplinli genel akademik & teknik genişletmeler (Felsefe, Psikoloji, Yazılım, Ekonomi, Bilim, Tarih, Eğitim)
        ("bilinç", new[] { "consciousness", "awareness", "qualia", "unconscious", "subconscious" }),
        ("bilinçdışı", new[] { "unconscious", "subconscious", "cognitive bias", "automaticity" }),
        ("bilişsel", new[] { "cognitive", "cognition", "cognitive bias", "heuristics" }),
        ("önyargı", new[] { "bias", "cognitive bias", "prejudice", "algorithmic bias" }),
        ("etik", new[] { "ethics", "moral philosophy", "normative ethics", "deontology", "utilitarianism" }),
        ("ahlak", new[] { "morality", "moral philosophy", "ethics", "virtue ethics" }),
        ("nedensellik", new[] { "causality", "causation", "causal chain", "sufficient reason" }),
        ("epistemoloji", new[] { "epistemology", "theory of knowledge", "justification", "skepticism" }),
        ("ontoloji", new[] { "ontology", "metaphysics", "being", "existence" }),
        ("varoluş", new[] { "existentialism", "existence", "phenomenology", "authenticity" }),
        ("fenomenoloji", new[] { "phenomenology", "intentionality", "subjective experience" }),
        ("kuantum", new[] { "quantum mechanics", "indeterminacy", "uncertainty principle", "probabilistic" }),
        ("evrim", new[] { "evolution", "natural selection", "evolutionary psychology", "adaptation" }),
        ("genetik", new[] { "genetics", "heritability", "epigenetics", "behavioral genetics" }),
        ("mizaç", new[] { "temperament", "disposition", "personality traits", "innate" }),
        ("karakter", new[] { "character", "virtue", "habitus", "personality" }),
        ("alışkanlık", new[] { "habit", "habit loop", "conditioning", "neuroplasticity" }),
        ("bağımlılık", new[] { "addiction", "compulsion", "behavioral addiction", "variable reward" }),
        ("mikroservis", new[] { "microservices", "distributed systems", "service mesh", "bounded context" }),
        ("mimari", new[] { "architecture", "system design", "clean architecture", "scalability" }),
        ("veri tabanı", new[] { "database", "indexing", "transaction", "consistency" }),
        ("vektör", new[] { "vector", "embedding", "similarity search", "retrieval" }),
        ("ekonomi", new[] { "economics", "market", "incentives", "behavioral economics" }),
        ("kapitalizm", new[] { "capitalism", "surveillance capitalism", "commodification", "Zuboff" }),
        ("gözetim", new[] { "surveillance", "surveillance capitalism", "privacy", "panopticon" }),
        ("mahremiyet", new[] { "privacy", "data protection", "autonomy", "consent" }),
        ("demokrasi", new[] { "democracy", "public sphere", "polarization", "echo chamber" }),
        ("kutuplaşma", new[] { "polarization", "filter bubble", "echo chamber", "tribalism" }),
        ("pedagoji", new[] { "pedagogy", "learning theory", "curriculum", "instructional design" }),
        ("deney", new[] { "experiment", "empirical study", "clinical trial", "laboratory" })
    };

    public async Task<IReadOnlyList<DecomposedQuery>> DecomposeAsync(
        string rawTopicOrOutline,
        IReadOnlyCollection<string> courseLanguages,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(rawTopicOrOutline))
            return Array.Empty<DecomposedQuery>();

        var foreignLanguages = (courseLanguages ?? Array.Empty<string>())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim().ToLowerInvariant())
            .Where(l => !string.Equals(l, "tr", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        bool hasForeignSource = foreignLanguages.Length > 0;
        var sections = SplitIntoSubSections(rawTopicOrOutline);
        if (sections.Count == 0)
            return Array.Empty<DecomposedQuery>();

        // 1. Deterministik kök ayıklama + sözlük eşleşmesi (her zaman çalışır, 0 gecikmeli taban)
        var perSectionDeterministic = new List<string[]>(sections.Count);
        for (int i = 0; i < sections.Count; i++)
        {
            perSectionDeterministic.Add(BuildLexicalTerms(sections[i], hasForeignSource));
        }

        // 2. Dinamik LLM çapraz dil genişletmesi (yabancı kaynak varsa ve _gemini enjekte edilmişse)
        Dictionary<int, string[]>? dynamicExpansions = null;
        if (hasForeignSource && _gemini != null)
        {
            dynamicExpansions = await TryExpandDynamicCrossLingualAsync(sections, foreignLanguages, ct);
        }

        var result = new List<DecomposedQuery>(sections.Count);
        for (int i = 0; i < sections.Count; i++)
        {
            var rawSection = sections[i];
            var title = ExtractSectionTitle(rawSection, i + 1);
            var baseTerms = perSectionDeterministic[i];

            string[] mergedTerms = baseTerms;
            if (dynamicExpansions != null &&
                dynamicExpansions.TryGetValue(i, out var dynTerms) &&
                dynTerms.Length > 0)
            {
                var combined = new List<string>(baseTerms.Length + dynTerms.Length);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var bt in baseTerms)
                {
                    if (seen.Add(bt))
                        combined.Add(bt);
                }

                foreach (var dt in dynTerms)
                {
                    if (seen.Add(dt))
                        combined.Add(dt);
                }

                mergedTerms = combined.Take(32).ToArray();
            }

            var expanded = BuildExpandedQueryText(rawSection, mergedTerms);

            result.Add(new DecomposedQuery(
                Index: i,
                Title: title,
                OriginalText: rawSection,
                ExpandedKeywordsText: expanded,
                LexicalTerms: mergedTerms));
        }

        return result;
    }

    private async Task<Dictionary<int, string[]>> TryExpandDynamicCrossLingualAsync(
        List<string> sections,
        string[] foreignLanguages,
        CancellationToken ct)
    {
        var resolved = new Dictionary<int, string[]>();
        if (_gemini == null || sections.Count == 0 || foreignLanguages.Length == 0)
            return resolved;

        var langKey = string.Join(",", foreignLanguages);
        var uncachedIndices = new List<int>();

        for (int i = 0; i < sections.Count; i++)
        {
            var cacheKey = BuildCacheKey(sections[i], langKey);
            if (_dynamicExpansionCache.TryGetValue(cacheKey, out var cachedTerms))
            {
                resolved[i] = cachedTerms;
            }
            else
            {
                uncachedIndices.Add(i);
            }
        }

        if (uncachedIndices.Count == 0)
            return resolved;

        try
        {
            var promptSb = new StringBuilder();
            promptSb.AppendLine($"[ACADEMIC CROSS-LINGUAL SEARCH EXPANSION — TARGET LANGUAGES: {string.Join(", ", foreignLanguages.Select(l => l.ToUpperInvariant()))}]");
            promptSb.AppendLine("For each numbered query item below, produce 4 to 8 exact academic, philosophical, scientific, technical, legal, or historical search keywords and proper-name transliterations in the target language(s) so hybrid vector+lexical search can match foreign-language papers and lectures.");
            promptSb.AppendLine("Keep " + "\"ozetMetni\"" + " strictly as plain lines in the exact format:");
            promptSb.AppendLine("Q0: term1, term2, term3, term4");
            promptSb.AppendLine("Q1: term1, term2, term3, term4");
            promptSb.AppendLine("Do NOT translate into Turkish; write ONLY the foreign-language academic terms for each Q# line.");
            promptSb.AppendLine();
            promptSb.AppendLine("QUERIES:");

            foreach (int secIdx in uncachedIndices)
            {
                var compactSection = sections[secIdx].Length > 260
                    ? sections[secIdx][..260]
                    : sections[secIdx];
                promptSb.AppendLine($"Q{secIdx}: {compactSection}");
            }

            var rawResponse = await _gemini
                .SummarizeAsync(promptSb.ToString())
                .WaitAsync(TimeSpan.FromSeconds(DynamicExpansionTimeoutSeconds), ct);

            var parsedByIndex = ParseDynamicExpansionResponse(rawResponse, uncachedIndices);

            if (_dynamicExpansionCache.Count > MaxCacheEntries)
            {
                _dynamicExpansionCache.Clear();
            }

            foreach (int secIdx in uncachedIndices)
            {
                parsedByIndex.TryGetValue(secIdx, out var extracted);
                var cleanTerms = extracted ?? Array.Empty<string>();
                _dynamicExpansionCache[BuildCacheKey(sections[secIdx], langKey)] = cleanTerms;
                if (cleanTerms.Length > 0)
                {
                    resolved[secIdx] = cleanTerms;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Dinamik çapraz dil sorgu genişletmesi zaman aşımına uğradı veya hata verdi; deterministik sözlüğe geri dönülüyor.");
        }

        return resolved;
    }

    private static Dictionary<int, string[]> ParseDynamicExpansionResponse(string? rawResponse, List<int> expectedIndices)
    {
        var map = new Dictionary<int, string[]>();
        if (string.IsNullOrWhiteSpace(rawResponse))
            return map;

        // Geliştirme ortamı mock yanıtlarını kesinlikle yok say
        if (rawResponse.Contains("Mock Özet", StringComparison.OrdinalIgnoreCase) ||
            rawResponse.Contains("Mock Baslik", StringComparison.OrdinalIgnoreCase))
        {
            return map;
        }

        string bodyToParse = rawResponse.Trim();
        var extraTagFallback = new List<string>();

        // Yanıt JSON sarmalındaysa ("ozetMetni" veya özel JSON nesnesi/dizisi) iç metni çıkar
        if (bodyToParse.StartsWith('{') || bodyToParse.StartsWith('[') || bodyToParse.Contains("\"ozetMetni\"", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var elem = LlmJson.Deserialize<JsonElement>(bodyToParse);
                if (elem.ValueKind == JsonValueKind.Object)
                {
                    if (elem.TryGetProperty("ozetMetni", out var ozetProp) && ozetProp.ValueKind == JsonValueKind.String)
                    {
                        bodyToParse = ozetProp.GetString() ?? string.Empty;
                    }
                    else
                    {
                        // Doğrudan { "Q0": [...], "0": [...] } formatında mock/LLM yanıtı desteği
                        foreach (var prop in elem.EnumerateObject())
                        {
                            var keyDigits = Regex.Match(prop.Name, @"\d+");
                            if (keyDigits.Success && int.TryParse(keyDigits.Value, out int parsedIdx))
                            {
                                if (prop.Value.ValueKind == JsonValueKind.Array)
                                {
                                    var arrTerms = prop.Value.EnumerateArray()
                                        .Where(x => x.ValueKind == JsonValueKind.String)
                                        .Select(x => x.GetString() ?? "")
                                        .ToList();
                                    map[parsedIdx] = SanitizeDynamicTerms(arrTerms);
                                }
                                else if (prop.Value.ValueKind == JsonValueKind.String)
                                {
                                    map[parsedIdx] = SanitizeDynamicTerms(SplitTermLine(prop.Value.GetString() ?? ""));
                                }
                            }
                        }

                        if (map.Count > 0)
                            return map;
                    }

                    if (elem.TryGetProperty("konuEtiketleri", out var etiketProp) && etiketProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in etiketProp.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                                extraTagFallback.Add(item.GetString() ?? "");
                        }
                    }
                }
                else if (elem.ValueKind == JsonValueKind.Array && expectedIndices.Count == 1)
                {
                    var arrTerms = elem.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString() ?? "")
                        .ToList();
                    map[expectedIndices[0]] = SanitizeDynamicTerms(arrTerms);
                    return map;
                }
            }
            catch
            {
                // Ham metin satır ayrıştırıcısına devam et
            }
        }

        // Q0: term1, term2, term3 satırlarını ayrıştır
        var lineMatches = Regex.Matches(bodyToParse, @"(?im)^\s*(?:[-*•]\s*)?Q?(\d{1,2})\s*[:\-]\s*(.+)$");
        foreach (Match m in lineMatches)
        {
            if (int.TryParse(m.Groups[1].Value, out int qIdx))
            {
                var terms = SanitizeDynamicTerms(SplitTermLine(m.Groups[2].Value));
                if (terms.Length > 0)
                {
                    map[qIdx] = terms;
                }
            }
        }

        // Tek alt-sorgu varsa ve Q0 etiketi olmadan doğrudan virgüllü terimler döndüyse yakala
        if (map.Count == 0 && expectedIndices.Count == 1)
        {
            var singleTerms = SanitizeDynamicTerms(SplitTermLine(bodyToParse).Concat(extraTagFallback));
            if (singleTerms.Length > 0)
            {
                map[expectedIndices[0]] = singleTerms;
            }
        }

        return map;
    }

    private static IEnumerable<string> SplitTermLine(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
            return Array.Empty<string>();

        return rawLine.Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] SanitizeDynamicTerms(IEnumerable<string> rawCandidates)
    {
        var cleaned = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawCandidates)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var t = Regex.Replace(raw, @"^[\s""'‘’“”`\[\]\(\)\-\d\.:]+|[\s""'‘’“”`\[\]\(\)\.]+$", "").Trim();
            if (t.Length < 3 || t.Length > 48)
                continue;

            if (t.StartsWith("Q", StringComparison.OrdinalIgnoreCase) && t.Length <= 3 && char.IsDigit(t[^1]))
                continue;

            if (StopWords.Contains(t))
                continue;

            var normalized = t.ToLowerInvariant();
            if (seen.Add(normalized))
            {
                cleaned.Add(normalized);
                if (cleaned.Count >= 10)
                    break;
            }
        }

        return cleaned.ToArray();
    }

    private static string BuildCacheKey(string section, string langKey)
    {
        var normalized = Regex.Replace(section.Trim().ToLowerInvariant(), @"\s+", " ");
        return $"{langKey}::{normalized}";
    }

    private static List<string> SplitIntoSubSections(string input)
    {
        var lines = input.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0)
            return new List<string>();

        var blocks = new List<string>();
        var currentLines = new List<string>();

        foreach (var line in lines)
        {
            bool startsNewItem = BulletPrefixRegex().IsMatch(line) ||
                                 (line.EndsWith(':') && line.Length < 100);

            if (startsNewItem && currentLines.Count > 0)
            {
                blocks.Add(string.Join(" ", currentLines));
                currentLines.Clear();
            }

            currentLines.Add(BulletPrefixRegex().Replace(line, string.Empty).Trim());
        }

        if (currentLines.Count > 0)
            blocks.Add(string.Join(" ", currentLines));

        // Tek blok geldiyse ve birden fazla satır/cümle içeriyorsa en fazla 3 odak sorgusuna böl
        if (blocks.Count == 1 && blocks[0].Length > 180)
        {
            var sentences = Regex.Split(blocks[0], @"(?<=[\.!\?;])\s+")
                .Select(s => s.Trim())
                .Where(s => s.Length >= 20)
                .ToList();

            if (sentences.Count >= 2 && sentences.Count <= 4)
                return sentences;
        }

        // Çok uzun listelerde 12 alt-sorgu tavanı uygula
        if (blocks.Count > 12)
        {
            var merged = new List<string>(12);
            int step = (int)Math.Ceiling(blocks.Count / 12.0);
            for (int i = 0; i < blocks.Count; i += step)
            {
                merged.Add(string.Join(" | ", blocks.Skip(i).Take(step)));
            }
            return merged;
        }

        return blocks;
    }

    private static string ExtractSectionTitle(string section, int number)
    {
        var colonIdx = section.IndexOf(':');
        if (colonIdx > 2 && colonIdx <= 80)
            return section[..colonIdx].Trim();

        return section.Length <= 80 ? section : $"{section[..77].Trim()}...";
    }

    private static string[] BuildLexicalTerms(string text, bool includeCrossLingual)
    {
        var orderedTerms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddTerm(string candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
                orderedTerms.Add(candidate);
            }
        }

        var lower = text.ToLowerInvariant();

        // Özel isim / iyelik ekli kelimeler (örn. Gazzâlî'nin -> gazzâlî, gazzali)
        var rawTokens = Regex.Matches(text, @"[\p{L}\p{N}âîûÂÎÛ'’\-]{3,}")
            .Select(m => m.Value)
            .ToList();

        foreach (var tok in rawTokens)
        {
            var clean = tok;
            int aposIdx = clean.IndexOfAny(new[] { '\'', '’' });
            if (aposIdx >= 3)
            {
                clean = clean[..aposIdx];
            }

            clean = clean.Trim('-', '_', '.', ',');
            if (clean.Length < 3 || StopWords.Contains(clean))
                continue;

            var cleanLower = clean.ToLowerInvariant();
            AddTerm(cleanLower);

            var ascii = FoldTurkishDiacritics(cleanLower);
            if (!string.Equals(ascii, cleanLower, StringComparison.Ordinal))
                AddTerm(ascii);

            // Türkçe yaygın isim çekim eklerini basitçe soyarak kök tahmini (yalnız >= 8 kr kelimelerde)
            if (cleanLower.Length >= 8)
            {
                foreach (var suffix in new[] { "larıyla", "leriyle", "larının", "lerinin", "ından", "inden", "sının", "sinin", "ların", "lerin", "daki", "deki", "ları", "leri" })
                {
                    if (cleanLower.EndsWith(suffix, StringComparison.Ordinal) && cleanLower.Length - suffix.Length >= 4)
                    {
                        var stem = cleanLower[..^suffix.Length];
                        AddTerm(stem);
                        AddTerm(FoldTurkishDiacritics(stem));
                        break;
                    }
                }
            }
        }

        if (includeCrossLingual)
        {
            var foldedFull = FoldTurkishDiacritics(lower);
            foreach (var (pattern, expansions) in CrossLingualDictionary)
            {
                if (lower.Contains(pattern, StringComparison.Ordinal) ||
                    foldedFull.Contains(FoldTurkishDiacritics(pattern), StringComparison.Ordinal))
                {
                    foreach (var exp in expansions)
                    {
                        AddTerm(exp.ToLowerInvariant());
                    }
                }
            }
        }

        return orderedTerms.Take(28).ToArray();
    }

    private static string BuildExpandedQueryText(string originalSection, string[] lexicalTerms)
    {
        if (lexicalTerms.Length == 0)
            return originalSection;

        var extra = lexicalTerms
            .Where(t => !originalSection.Contains(t, StringComparison.OrdinalIgnoreCase))
            .Take(16)
            .ToList();

        return extra.Count == 0
            ? originalSection
            : $"{originalSection} ({string.Join(", ", extra)})";
    }

    public static string FoldTurkishDiacritics(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input
            .Replace('â', 'a').Replace('Â', 'A')
            .Replace('î', 'i').Replace('Î', 'I')
            .Replace('û', 'u').Replace('Û', 'U')
            .Replace('ğ', 'g').Replace('Ğ', 'G')
            .Replace('ü', 'u').Replace('Ü', 'U')
            .Replace('ş', 's').Replace('Ş', 'S')
            .Replace('ö', 'o').Replace('Ö', 'O')
            .Replace('ç', 'c').Replace('Ç', 'C')
            .Replace('ı', 'i').Replace('İ', 'I');
    }
}
