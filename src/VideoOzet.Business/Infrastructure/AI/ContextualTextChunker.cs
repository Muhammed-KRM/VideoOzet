using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Infrastructure.AI;

/// <summary>
/// Paragraf ve cümle sınırlarını koruyarak örtüşmeli bağlamsal parçalar (chunks) üretir.
/// Akademik PDF sonlarındaki Kaynakça/References bloklarını ChunkTipi.Kaynakca olarak işaretler;
/// video transkriptlerinde Whisper zaman damgalarını kullanır veya süre üzerinden oransal aralık üretir.
/// </summary>
public partial class ContextualTextChunker : IContextualTextChunker
{
    private const double DefaultWordsPerSecond = 2.5; // ~150 kelime/dakika

    [GeneratedRegex(@"^\s*(?:#{1,6}\s+)?(?<title>(?:KAYNAKÇA|KAYNAKLAR|BİBLİYOGRAFYA|BIBLIYOGRAFYA|BIBLIOGRAPHY|REFERENCES|WORKS\s+CITED|FURTHER\s+READING))\s*:?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceSectionHeaderRegex();

    [GeneratedRegex(@"^\s*(?:#{1,6}\s+(?<md>.+)|(?<num>\d+(?:\.\d+){0,3}\.?\s+\p{L}.{2,95}))$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingLineRegex();

    [GeneratedRegex(@"(?<=[\.!\?])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundaryRegex();

    private sealed record TextUnit(string Text, string? BolumBasligi, bool IsReference);
    private sealed record TranscriptSegment(double StartSec, double EndSec, string Text);

    public IReadOnlyList<ContextualChunk> ChunkDocument(string hamMetin, ContextualChunkOptions options)
    {
        if (string.IsNullOrWhiteSpace(hamMetin))
            return Array.Empty<ContextualChunk>();

        var targetWords = Math.Clamp(options.TargetWords, 120, 1200);
        var overlapWords = Math.Clamp(options.OverlapWords, 0, targetWords / 2);

        var units = ExtractDocumentUnits(hamMetin);
        if (units.Count == 0)
            return Array.Empty<ContextualChunk>();

        var result = new List<ContextualChunk>();
        int idx = 0;

        // İçerik ve Kaynakça bloklarını ayrı akışlarda parçala ki kaynakça gövdeye karışmasın
        foreach (var group in GroupByReferenceFlag(units))
        {
            var isRef = group[0].IsReference;
            var sentences = ExpandToSentences(group, targetWords);
            if (sentences.Count == 0) continue;

            int i = 0;
            while (i < sentences.Count)
            {
                int currentWords = 0;
                int startIdx = i;
                string? activeHeading = sentences[i].BolumBasligi;
                var sb = new StringBuilder();

                while (i < sentences.Count)
                {
                    var s = sentences[i];
                    var w = CountWords(s.Text);
                    if (currentWords > 0 && currentWords + w > targetWords)
                        break;

                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(s.Text);
                    currentWords += w;

                    if (!string.IsNullOrWhiteSpace(s.BolumBasligi))
                        activeHeading ??= s.BolumBasligi;

                    i++;
                }

                var rawText = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(rawText))
                {
                    var chunkTipi = isRef ? ChunkTipi.Kaynakca : ChunkTipi.HamMetin;
                    var contextHeader = BuildHeader(
                        options,
                        activeHeading ?? (isRef ? "Kaynakça" : "Genel"),
                        isRef ? "KAYNAKÇA" : options.KaynakTuru);

                    result.Add(new ContextualChunk(
                        ChunkIndex: idx++,
                        ChunkTipi: chunkTipi,
                        BolumBasligi: Truncate(activeHeading ?? (isRef ? "Kaynakça" : null), 300),
                        StartTimeMs: 0,
                        EndTimeMs: 0,
                        RawText: rawText,
                        ContextualizedText: $"{contextHeader}\n{rawText}"));
                }

                if (i >= sentences.Count)
                    break;

                // Örtüşme (overlap): sondan overlapWords kelime kadar tam cümleyi sonraki chunk başına taşı
                if (overlapWords > 0)
                {
                    int backWords = 0;
                    int nextStart = i;
                    while (nextStart - 1 > startIdx)
                    {
                        var candidateWords = CountWords(sentences[nextStart - 1].Text);
                        if (backWords > 0 && backWords + candidateWords > overlapWords)
                            break;
                        backWords += candidateWords;
                        nextStart--;
                    }
                    // Sonsuz döngüyü önlemek için en az 1 cümle ilerleme garantisi
                    i = Math.Max(startIdx + 1, nextStart);
                }
            }
        }

        return result;
    }

    public IReadOnlyList<ContextualChunk> ChunkTranscript(string hamMetin, string? zamanDamgalariJson, ContextualChunkOptions options)
    {
        var segments = TryParseSegments(zamanDamgalariJson);
        if (segments.Count > 0)
        {
            return ChunkTimestampedSegments(segments, options);
        }

        if (string.IsNullOrWhiteSpace(hamMetin))
            return Array.Empty<ContextualChunk>();

        var docChunks = ChunkDocument(hamMetin, options with { KaynakTuru = "VİDEO" });
        if (docChunks.Count == 0)
            return Array.Empty<ContextualChunk>();

        var wordCounts = docChunks.Select(c => Math.Max(1, CountWords(c.RawText))).ToList();
        var totalWords = Math.Max(1, CountWords(hamMetin));
        long totalDurationMs = options.VideoDurationSeconds is > 0
            ? options.VideoDurationSeconds.Value * 1000L
            : Math.Max(1000L, (long)Math.Round(totalWords / DefaultWordsPerSecond * 1000.0));

        // Örtüşmeli chunk'larda toplam kelime sum(wordCounts) > totalWords olabileceği için sum'a göre orantıla
        double sumChunkWords = Math.Max(1, wordCounts.Sum());
        double cumulative = 0;

        var result = new List<ContextualChunk>(docChunks.Count);
        for (int idx = 0; idx < docChunks.Count; idx++)
        {
            var c = docChunks[idx];
            var wc = wordCounts[idx];
            int startMs = (int)Math.Round((cumulative / sumChunkWords) * totalDurationMs);
            cumulative += wc;
            int endMs = (int)Math.Round((cumulative / sumChunkWords) * totalDurationMs);
            if (endMs <= startMs) endMs = startMs + 1000;

            var timeLabel = $"{FormatTimestamp(startMs)}–{FormatTimestamp(endMs)}";
            var header = BuildHeader(options, timeLabel, "VİDEO");

            result.Add(new ContextualChunk(
                ChunkIndex: idx,
                ChunkTipi: ChunkTipi.Transkript,
                BolumBasligi: timeLabel,
                StartTimeMs: startMs,
                EndTimeMs: endMs,
                RawText: c.RawText,
                ContextualizedText: $"{header}\n{c.RawText}"));
        }

        return result;
    }

    private IReadOnlyList<ContextualChunk> ChunkTimestampedSegments(List<TranscriptSegment> segments, ContextualChunkOptions options)
    {
        var targetWords = Math.Clamp(options.TargetWords, 120, 1000);
        var overlapWords = Math.Clamp(options.OverlapWords, 0, targetWords / 2);

        var result = new List<ContextualChunk>();
        int idx = 0;
        int i = 0;

        while (i < segments.Count)
        {
            int startIdx = i;
            int currentWords = 0;
            var sb = new StringBuilder();

            while (i < segments.Count)
            {
                var segText = segments[i].Text;
                var w = CountWords(segText);
                if (currentWords > 0 && currentWords + w > targetWords)
                    break;

                if (sb.Length > 0) sb.Append(' ');
                sb.Append(segText);
                currentWords += w;
                i++;
            }

            var rawText = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(rawText))
            {
                int startMs = Math.Max(0, (int)Math.Round(segments[startIdx].StartSec * 1000.0));
                int endMs = Math.Max(startMs + 500, (int)Math.Round(segments[i - 1].EndSec * 1000.0));
                var timeLabel = $"{FormatTimestamp(startMs)}–{FormatTimestamp(endMs)}";
                var header = BuildHeader(options, timeLabel, "VİDEO");

                result.Add(new ContextualChunk(
                    ChunkIndex: idx++,
                    ChunkTipi: ChunkTipi.Transkript,
                    BolumBasligi: timeLabel,
                    StartTimeMs: startMs,
                    EndTimeMs: endMs,
                    RawText: rawText,
                    ContextualizedText: $"{header}\n{rawText}"));
            }

            if (i >= segments.Count)
                break;

            if (overlapWords > 0)
            {
                int backWords = 0;
                int nextStart = i;
                while (nextStart - 1 > startIdx)
                {
                    var cw = CountWords(segments[nextStart - 1].Text);
                    if (backWords > 0 && backWords + cw > overlapWords)
                        break;
                    backWords += cw;
                    nextStart--;
                }
                i = Math.Max(startIdx + 1, nextStart);
            }
        }

        return result;
    }

    private static List<TranscriptSegment> TryParseSegments(string? json)
    {
        var list = new List<TranscriptSegment>();
        if (string.IsNullOrWhiteSpace(json))
            return list;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var tProp) ? tProp.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;

                double start = item.TryGetProperty("start", out var sProp) && sProp.TryGetDouble(out var sv) ? sv : 0;
                double end = item.TryGetProperty("end", out var eProp) && eProp.TryGetDouble(out var ev) ? ev : start;
                list.Add(new TranscriptSegment(start, Math.Max(start, end), text));
            }
        }
        catch (JsonException)
        {
            list.Clear();
        }

        return list;
    }

    private static List<TextUnit> ExtractDocumentUnits(string hamMetin)
    {
        var normalized = hamMetin.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var units = new List<TextUnit>();

        string? currentHeading = null;
        bool inReferenceSection = false;
        int processedChars = 0;
        int totalChars = Math.Max(1, normalized.Length);
        var paraSb = new StringBuilder();

        void FlushParagraph()
        {
            if (paraSb.Length == 0) return;
            var text = paraSb.ToString().Trim();
            paraSb.Clear();
            if (!string.IsNullOrWhiteSpace(text))
            {
                units.Add(new TextUnit(text, currentHeading, inReferenceSection));
            }
        }

        foreach (var rawLine in lines)
        {
            processedChars += rawLine.Length + 1;
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            double relativePos = (double)processedChars / totalChars;

            // Dokümanın son %40'ında başlayan Kaynakça/References başlığını yakala
            if (!inReferenceSection && relativePos >= 0.60 && line.Length <= 60 && ReferenceSectionHeaderRegex().IsMatch(line))
            {
                FlushParagraph();
                inReferenceSection = true;
                currentHeading = "Kaynakça";
                continue;
            }

            if (!inReferenceSection && IsLikelyHeading(line))
            {
                FlushParagraph();
                currentHeading = CleanHeading(line);
            }

            if (paraSb.Length > 0) paraSb.Append(' ');
            paraSb.Append(line);
        }

        FlushParagraph();
        return units;
    }

    private static bool IsLikelyHeading(string line)
    {
        if (line.Length < 3 || line.Length > 100)
            return false;

        if (HeadingLineRegex().IsMatch(line))
            return true;

        // Tamamı büyük harfli ve en az 2 harf içeren kısa başlık satırı
        int letters = 0, upper = 0;
        foreach (var ch in line)
        {
            if (char.IsLetter(ch))
            {
                letters++;
                if (char.IsUpper(ch)) upper++;
            }
        }

        return letters >= 4 && upper == letters && !line.EndsWith('.');
    }

    private static string CleanHeading(string line)
    {
        var cleaned = line.Trim().TrimStart('#').Trim();
        return Truncate(cleaned, 120) ?? cleaned;
    }

    private static List<List<TextUnit>> GroupByReferenceFlag(List<TextUnit> units)
    {
        var groups = new List<List<TextUnit>>();
        if (units.Count == 0) return groups;

        var current = new List<TextUnit> { units[0] };
        for (int i = 1; i < units.Count; i++)
        {
            if (units[i].IsReference == current[0].IsReference)
            {
                current.Add(units[i]);
            }
            else
            {
                groups.Add(current);
                current = new List<TextUnit> { units[i] };
            }
        }
        groups.Add(current);
        return groups;
    }

    private static List<(string Text, string? BolumBasligi)> ExpandToSentences(List<TextUnit> units, int targetWords)
    {
        var result = new List<(string Text, string? BolumBasligi)>();
        int maxSentenceWords = Math.Max(80, (int)(targetWords * 1.25));

        foreach (var u in units)
        {
            var rawSentences = SentenceBoundaryRegex().Split(u.Text);
            foreach (var rawS in rawSentences)
            {
                var s = rawS.Trim();
                if (string.IsNullOrWhiteSpace(s)) continue;

                if (CountWords(s) <= maxSentenceWords)
                {
                    result.Add((s, u.BolumBasligi));
                }
                else
                {
                    // Çok uzun tek cümle/liste satırını kelime sınırından güvenli böl
                    var words = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    for (int k = 0; k < words.Length; k += maxSentenceWords)
                    {
                        var slice = string.Join(' ', words.Skip(k).Take(maxSentenceWords));
                        if (!string.IsNullOrWhiteSpace(slice))
                            result.Add((slice, u.BolumBasligi));
                    }
                }
            }
        }

        return result;
    }

    private static string BuildHeader(ContextualChunkOptions options, string sectionOrTime, string turLabel)
    {
        var safeTitle = Truncate(string.IsNullOrWhiteSpace(options.KaynakBaslik) ? "Bilinmeyen Kaynak" : options.KaynakBaslik.Trim(), 120);
        var safeSection = Truncate(string.IsNullOrWhiteSpace(sectionOrTime) ? "Genel" : sectionOrTime.Trim(), 80);
        var safeLang = string.IsNullOrWhiteSpace(options.Dil) ? "tr" : options.Dil.Trim().ToUpperInvariant();
        return $"[Kaynak: {safeTitle} | Tür: {turLabel} | {safeSection} | Dil: {safeLang}]";
    }

    private static string FormatTimestamp(int ms)
    {
        var ts = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        int count = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }
        return count;
    }

    private static string? Truncate(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLen ? value : value[..maxLen];
    }
}
