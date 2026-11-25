using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Services;

public class TranscriptQualityEvaluator : ITranscriptQualityEvaluator
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Regex BracketTagRegex = new(
        @"\[[^\]]{1,150}\]|\((?:music|müzik|applause|alkış|laughter|gülüşme|silence|sessizlik|cheering|sound|inaudible|anlaşılamıyor)[^\)]*\)|♪[^♪\r\n]*♪?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChevronMarkerRegex = new(
        @"(?:^|\s)>>(?=\s|$)",
        RegexOptions.Compiled);

    private static readonly Regex WordSplitRegex = new(
        @"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*",
        RegexOptions.Compiled);

    private static readonly HashSet<string> TurkishStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ve", "bir", "bu", "için", "icin", "ile", "olan", "olarak", "da", "de",
        "ama", "fakat", "gibi", "daha", "çok", "cok", "var", "yok", "ben", "sen",
        "biz", "siz", "ne", "nasıl", "nasil", "neden", "evet", "hayır", "hayir",
        "merhaba", "arkadaşlar", "arkadaslar", "şimdi", "simdi", "burada", "ancak",
        "çünkü", "cunku", "kadar", "sonra", "önce", "once", "veya", "ya", "hem",
        "şey", "sey", "şu", "su", "o", "bunlar", "şunlar", "neler", "hakkında", "hakkinda"
    };

    private static readonly HashSet<string> EnglishStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "is", "are", "was", "were", "this", "that", "with", "for",
        "from", "you", "have", "has", "not", "but", "what", "which", "they", "will",
        "would", "can", "about", "there", "their", "been", "into", "when", "where",
        "hello", "everyone", "today", "because", "should", "could", "these", "those"
    };

    private static readonly HashSet<char> TurkishSpecificChars = new("çğıöşüÇĞİÖŞÜ");

    public TranscriptQualityReport Evaluate(string text, int? durationSeconds, string? expectedLang, bool isManualCaption)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TranscriptQualityReport(0, false, "Transkript metni boş.", 0, 0);
        }

        // 1. Non-speech tag detection & stripping
        int bracketMatches = BracketTagRegex.Matches(text).Count;
        int chevronMatches = ChevronMarkerRegex.Matches(text).Count;
        int nonSpeechCount = bracketMatches + chevronMatches;

        string cleanedText = BracketTagRegex.Replace(text, " ");
        cleanedText = ChevronMarkerRegex.Replace(cleanedText, " ");

        var rawWords = WordSplitRegex.Matches(cleanedText)
            .Select(m => m.Value)
            .ToList();

        var words = rawWords
            .Select(w => w.Replace('\u0130', 'i').ToLowerInvariant())
            .ToList();

        int speechWordCount = words.Count;
        int totalUnits = speechWordCount + nonSpeechCount;
        double nonSpeechRatio = totalUnits > 0 ? (double)nonSpeechCount / totalUnits : 1.0;

        // Calculate 4-gram repetition ratio
        double repetitionRatio = CalculateFourGramRepetitionRatio(words);

        // Calculate WPM when duration is known
        double wpm = (durationSeconds.HasValue && durationSeconds.Value > 0)
            ? speechWordCount / (durationSeconds.Value / 60.0)
            : 0;

        var reasons = new List<string>();
        bool hardRejected = false;
        int score = isManualCaption ? 100 : 85;

        // Rule 1: Minimum length
        int minRequiredWords = (durationSeconds.HasValue && durationSeconds.Value > 0 && durationSeconds.Value < 60)
            ? 15
            : 50;

        if (speechWordCount < minRequiredWords)
        {
            hardRejected = true;
            reasons.Add($"yetersiz kelime sayısı ({speechWordCount} < {minRequiredWords})");
            score -= 50;
        }

        // Rule 2: WPM density
        if (durationSeconds.HasValue && durationSeconds.Value > 0)
        {
            if (wpm < 25)
            {
                hardRejected = true;
                reasons.Add($"çok düşük konuşma yoğunluğu ({wpm:F1} WPM < 25)");
                score -= 45;
            }
            else if (wpm < 40)
            {
                reasons.Add($"düşük konuşma yoğunluğu ({wpm:F1} WPM)");
                score -= 20;
            }
            else if (wpm > 260)
            {
                reasons.Add($"aşırı yüksek kelime yoğunluğu ({wpm:F1} WPM > 260)");
                score -= 25;
            }
        }

        // Rule 3: 4-gram repetition ratio
        if (repetitionRatio > 0.30)
        {
            hardRejected = true;
            reasons.Add($"yüksek 4-gram tekrar oranı (%{repetitionRatio * 100:F0})");
            score -= 45;
        }
        else if (repetitionRatio >= 0.20)
        {
            reasons.Add($"orta düzey tekrar oranı (%{repetitionRatio * 100:F0})");
            score -= 20;
        }

        // Rule 4: Non-speech / noise tag ratio
        if (nonSpeechRatio >= 0.50)
        {
            hardRejected = true;
            reasons.Add($"konuşma dışı etiket oranı çok yüksek (%{nonSpeechRatio * 100:F0})");
            score -= 45;
        }
        else if (nonSpeechRatio >= 0.25)
        {
            reasons.Add($"yüksek ses/müzik etiketi oranı (%{nonSpeechRatio * 100:F0})");
            score -= 25;
        }

        // Rule 5: Language match
        if (!string.IsNullOrWhiteSpace(expectedLang) && words.Count > 0)
        {
            if (!IsLanguageCompatible(cleanedText, words, rawWords, expectedLang, out var langReason))
            {
                hardRejected = true;
                reasons.Add(langReason);
                score -= 50;
            }
        }

        // Rule 6: Auto-generated basic coherence check (garbage single-char tokens)
        if (!isManualCaption && words.Count > 0)
        {
            double singleCharRatio = words.Count(w => w.Length == 1) / (double)words.Count;
            if (singleCharRatio > 0.35)
            {
                reasons.Add("parçalanmış otomatik kelime dizilimi");
                score -= 20;
            }
        }

        if (hardRejected)
        {
            score = Math.Min(score, 49);
        }

        int finalScore = Math.Clamp(score, 0, 100);
        bool passed = !hardRejected && finalScore >= 70;

        string summaryReason;
        if (passed)
        {
            var sourceLabel = isManualCaption ? "Manuel altyazı" : "Otomatik altyazı";
            summaryReason = reasons.Count == 0
                ? $"{sourceLabel} kalite kapısından geçti (Skor: {finalScore}/100)."
                : $"{sourceLabel} kalite kapısından geçti (Skor: {finalScore}/100; not: {string.Join(", ", reasons)}).";
        }
        else
        {
            summaryReason = reasons.Count > 0
                ? $"Altyazı kalite kontrolünü geçemedi: {string.Join("; ", reasons)}."
                : $"Altyazı kalite skoru yetersiz ({finalScore}/100 < 70).";
        }

        return new TranscriptQualityReport(
            finalScore,
            passed,
            summaryReason,
            Math.Round(wpm, 1),
            Math.Round(repetitionRatio, 3));
    }

    private static double CalculateFourGramRepetitionRatio(List<string> words)
    {
        if (words.Count < 8)
        {
            return 0;
        }

        int totalFourGrams = words.Count - 3;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < totalFourGrams; i++)
        {
            var gram = $"{words[i]} {words[i + 1]} {words[i + 2]} {words[i + 3]}";
            seen.Add(gram);
        }

        return 1.0 - ((double)seen.Count / totalFourGrams);
    }

    private static bool IsLanguageCompatible(string cleanedText, List<string> words, List<string> rawWords, string expectedLang, out string reason)
    {
        reason = string.Empty;
        var normLang = NormalizeLang(expectedLang);

        int letterCount = 0;
        int latinLetterCount = 0;
        int turkishCharCount = 0;

        foreach (char c in cleanedText)
        {
            if (char.IsLetter(c))
            {
                letterCount++;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || TurkishSpecificChars.Contains(c) || c >= '\u00C0' && c <= '\u024F')
                {
                    latinLetterCount++;
                }
                if (TurkishSpecificChars.Contains(c))
                {
                    turkishCharCount++;
                }
            }
        }

        int trHits = 0;
        for (int i = 0; i < words.Count; i++)
        {
            if (TurkishStopwords.Contains(words[i]) || TurkishStopwords.Contains(rawWords[i].ToLower(TurkishCulture)))
            {
                trHits++;
            }
        }
        int enHits = words.Count(w => EnglishStopwords.Contains(w));

        if (normLang == "tr")
        {
            if (letterCount > 20 && latinLetterCount < letterCount * 0.6)
            {
                reason = "beklenen dil (tr) ile alfabe uyuşmuyor";
                return false;
            }

            if ((enHits >= 3 && turkishCharCount <= 2 && enHits >= Math.Max(3, trHits * 3)) ||
                (turkishCharCount == 0 && trHits == 0 && words.Count >= 15 && (double)enHits / words.Count >= 0.10) ||
                (trHits == 0 && enHits >= 4) ||
                (enHits >= 6 && enHits >= Math.Max(6, trHits * 4) && turkishCharCount < Math.Max(6, words.Count / 6)))
            {
                reason = "beklenen dil (tr) yerine İngilizce ağırlıklı metin tespit edildi";
                return false;
            }

            if (words.Count >= 30 && turkishCharCount == 0 && trHits == 0 && enHits > 0)
            {
                reason = "beklenen dil (tr) ile metin profili uyuşmuyor";
                return false;
            }
        }
        else if (normLang == "en")
        {
            if (letterCount > 20 && latinLetterCount < letterCount * 0.6)
            {
                reason = "beklenen dil (en) ile alfabe uyuşmuyor";
                return false;
            }

            if (turkishCharCount >= 3 || (trHits >= 3 && trHits >= Math.Max(3, enHits * 3)))
            {
                reason = "beklenen dil (en) yerine Türkçe ağırlıklı metin tespit edildi";
                return false;
            }
        }

        return true;
    }

    private static string NormalizeLang(string lang)
    {
        var lower = lang.Trim().ToLowerInvariant();
        if (lower == "turkish" || lower.StartsWith("tr")) return "tr";
        if (lower == "english" || lower.StartsWith("en")) return "en";
        if (lower == "german" || lower.StartsWith("de")) return "de";
        if (lower == "french" || lower.StartsWith("fr")) return "fr";
        return lower.Length >= 2 ? lower[..2] : lower;
    }
}
