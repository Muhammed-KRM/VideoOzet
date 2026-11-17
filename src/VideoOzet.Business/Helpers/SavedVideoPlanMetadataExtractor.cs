using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Helpers;

public sealed class ExtractedSavedPlanMetadata
{
    public string? CalismaBasligi { get; set; }
    public int? HedefSureDk { get; set; }
    public string? Kanca { get; set; }
    public string? AnaFikir { get; set; }
    public List<string> Konular { get; set; } = new();
    public List<SeriesSceneOutlineItemDto> Sahneler { get; set; } = new();
}

/// <summary>
/// Kullanıcının düzenleyip kaydettiği son VideoPlanı Markdown metninden
/// gerçek başlık, süre (dk), kanca, tez ve sahne iskeletini çıkarır.
/// </summary>
public static class SavedVideoPlanMetadataExtractor
{
    private static readonly Regex TitleRegex = new(
        @"(?m)^#\s+(?:Bölüm\s*\d+\s*[:—–-]\s*)?(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex TargetDurationLineRegex = new(
        @"(?mi)^\s*(?:[-*]\s*)?\*\*Hedef\s+Süre:\*\*\s*(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex HookLineRegex = new(
        @"(?mi)^\s*(?:[-*]\s*)?\*\*Açılış\s+Kancası(?:\s*\([^)]*\))?:\*\*\s*(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex ThesisLineRegex = new(
        @"(?mi)^\s*(?:[-*]\s*)?\*\*(?:Bölüm\s+Ana\s+Tezi|Ana\s+Fikir(?:\s*&\s*Bölüm\s+Tezi)?):\*\*\s*(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex ThemeOrScopeLineRegex = new(
        @"(?mi)^\s*(?:[-*]\s*)?\*\*(?:Bölüm\s+Teması|Kapsam):\*\*\s*(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex SceneHeadingRegex = new(
        @"(?m)^#{2,3}\s+Sahne\s*(\d+)\s*[:—–-]\s*(.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TimeRangeRegex = new(
        @"(\d{1,2}):(\d{2})\s*[-–—]\s*(\d{1,2}):(\d{2})",
        RegexOptions.Compiled);

    private static readonly Regex MmSsRegex = new(
        @"\b(\d{1,2}):(\d{2})\b",
        RegexOptions.Compiled);

    public static ExtractedSavedPlanMetadata? Extract(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return null;

        var normalized = markdown.Replace("\r\n", "\n").Trim();
        var result = new ExtractedSavedPlanMetadata();

        // 1. Başlık
        foreach (Match tm in TitleRegex.Matches(normalized))
        {
            var rawTitle = tm.Groups[1].Value.Trim().Trim('*').Trim();
            if (!string.IsNullOrWhiteSpace(rawTitle) &&
                !rawTitle.Equals("Video Planı", StringComparison.OrdinalIgnoreCase) &&
                !rawTitle.StartsWith("Video Planı ve", StringComparison.OrdinalIgnoreCase))
            {
                result.CalismaBasligi = rawTitle;
                break;
            }
        }

        // 2. Kanca ve Ana Tez
        var hookMatch = HookLineRegex.Match(normalized);
        if (hookMatch.Success)
        {
            result.Kanca = hookMatch.Groups[1].Value.Trim().Trim('"');
        }

        var thesisMatch = ThesisLineRegex.Match(normalized);
        if (thesisMatch.Success)
        {
            result.AnaFikir = thesisMatch.Groups[1].Value.Trim();
        }
        else
        {
            var themeParts = ThemeOrScopeLineRegex.Matches(normalized)
                .Select(m => m.Groups[1].Value.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            if (themeParts.Count > 0)
            {
                result.AnaFikir = string.Join(" — ", themeParts);
            }
        }

        // 3. Sahneler (## Sahne N: veya ### Sahne N:)
        var sceneMatches = SceneHeadingRegex.Matches(normalized);
        double maxSceneEndMinutes = 0;

        for (int i = 0; i < sceneMatches.Count; i++)
        {
            var sm = sceneMatches[i];
            int startIdx = sm.Index;
            int endIdx = (i + 1 < sceneMatches.Count) ? sceneMatches[i + 1].Index : normalized.Length;
            var block = normalized.Substring(startIdx, endIdx - startIdx);

            string sceneTitle = sm.Groups[2].Value.Trim().Trim('*').Trim();
            int sceneDurDk = 2;

            var trMatch = TimeRangeRegex.Match(block);
            if (trMatch.Success &&
                int.TryParse(trMatch.Groups[1].Value, out int m1) &&
                int.TryParse(trMatch.Groups[2].Value, out int s1) &&
                int.TryParse(trMatch.Groups[3].Value, out int m2) &&
                int.TryParse(trMatch.Groups[4].Value, out int s2))
            {
                double startMin = m1 + (s1 / 60.0);
                double endMin = m2 + (s2 / 60.0);
                if (endMin > maxSceneEndMinutes)
                    maxSceneEndMinutes = endMin;

                double delta = Math.Max(0.75, endMin - startMin);
                sceneDurDk = Math.Max(1, (int)Math.Round(delta, MidpointRounding.AwayFromZero));
            }

            string summary = ExtractSceneSummary(block);
            if (string.IsNullOrWhiteSpace(result.Kanca) && i == 0 && !string.IsNullOrWhiteSpace(summary))
            {
                int firstSentenceEnd = summary.IndexOfAny(new[] { '?', '.', '!' });
                result.Kanca = firstSentenceEnd > 20 && firstSentenceEnd < 260
                    ? summary.Substring(0, firstSentenceEnd + 1).Trim()
                    : (summary.Length > 220 ? summary.Substring(0, 220).Trim() + "..." : summary);
            }

            result.Sahneler.Add(new SeriesSceneOutlineItemDto
            {
                SahneNo = i + 1,
                Baslik = sceneTitle,
                TahminiSureDk = sceneDurDk,
                AciklamaVeArgumanlar = string.IsNullOrWhiteSpace(summary) ? sceneTitle : summary,
                AtananKaynakKodlari = new List<string>()
            });

            result.Konular.Add($"{sceneTitle}: {(summary.Length > 180 ? summary.Substring(0, 177).TrimEnd() + "..." : summary)}");
        }

        // 4. Gerçek Hedef Süre (Dk)
        int? parsedDuration = ParseTargetDurationMinutes(normalized, maxSceneEndMinutes);
        if (parsedDuration.HasValue && parsedDuration.Value >= 2)
        {
            result.HedefSureDk = parsedDuration.Value;
        }

        return result;
    }

    private static int? ParseTargetDurationMinutes(string normalized, double maxSceneEndMinutes)
    {
        var durLine = TargetDurationLineRegex.Match(normalized);
        if (durLine.Success)
        {
            var raw = durLine.Groups[1].Value;

            // Önce mm:ss zaman damgalarına bak (örn: "00:00 - 07:40" veya "~08:30 – 09:00 Dakika")
            var mmssMatches = MmSsRegex.Matches(raw);
            if (mmssMatches.Count > 0)
            {
                double maxMmSs = 0;
                foreach (Match m in mmssMatches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int mins) &&
                        int.TryParse(m.Groups[2].Value, out int secs))
                    {
                        double val = mins + (secs / 60.0);
                        if (val > maxMmSs) maxMmSs = val;
                    }
                }

                if (maxMmSs >= 1.5)
                    return Math.Max(2, (int)Math.Round(maxMmSs, MidpointRounding.AwayFromZero));
            }

            // Ondalıklı veya aralıklı dakika ifadesi (örn: "~7,5 Dakika", "~8 Dakika", "10-12 dk")
            var numMatches = Regex.Matches(raw, @"(\d+(?:[.,]\d+)?)\s*(?:[-–—]\s*(\d+(?:[.,]\d+)?))?\s*(?:dk|dakika)\b", RegexOptions.IgnoreCase);
            if (numMatches.Count > 0)
            {
                var nm = numMatches[0];
                if (TryParseDouble(nm.Groups[1].Value, out double d1))
                {
                    if (nm.Groups[2].Success && TryParseDouble(nm.Groups[2].Value, out double d2) && d2 >= d1)
                    {
                        return Math.Max(2, (int)Math.Round((d1 + d2) / 2.0, MidpointRounding.AwayFromZero));
                    }
                    return Math.Max(2, (int)Math.Round(d1, MidpointRounding.AwayFromZero));
                }
            }
        }

        if (maxSceneEndMinutes >= 1.5)
        {
            return Math.Max(2, (int)Math.Round(maxSceneEndMinutes, MidpointRounding.AwayFromZero));
        }

        // Fallback: Prompter (>) satırlarının kelime sayısından (~130 kelime/dk) hesapla
        var prompterWords = normalized.Split('\n')
            .Select(l => l.TrimStart())
            .Where(l => l.StartsWith(">"))
            .Sum(l => l.TrimStart('>').Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length);

        if (prompterWords >= 200)
        {
            return Math.Max(2, (int)Math.Round(prompterWords / 130.0, MidpointRounding.AwayFromZero));
        }

        return null;
    }

    private static bool TryParseDouble(string raw, out double value)
    {
        var clean = raw.Trim().Replace(',', '.');
        return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string ExtractSceneSummary(string sceneBlock)
    {
        var lines = sceneBlock.Split('\n');
        var prompterLines = new List<string>();
        var proseLines = new List<string>();

        foreach (var rawLine in lines.Skip(1))
        {
            var t = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(t) || t == "---")
                continue;

            if (t.StartsWith(">"))
            {
                var content = t.TrimStart('>', ' ', '"', '“', '”').TrimEnd('"', '“', '”').Trim();
                if (!string.IsNullOrWhiteSpace(content))
                    prompterLines.Add(content);
            }
            else if (!t.StartsWith("#") &&
                     !t.StartsWith("**Zaman Aralığı", StringComparison.OrdinalIgnoreCase) &&
                     !t.Contains("Kamera Açısı", StringComparison.OrdinalIgnoreCase) &&
                     !t.Contains("Görsel &", StringComparison.OrdinalIgnoreCase) &&
                     !t.Contains("Ses & SFX", StringComparison.OrdinalIgnoreCase))
            {
                var clean = Regex.Replace(t, @"^[-*]\s*(\*\*[^*]+\*\*\s*)?", string.Empty).Trim();
                if (clean.Length > 20)
                    proseLines.Add(clean);
            }
        }

        var combined = prompterLines.Count > 0
            ? string.Join(" ", prompterLines)
            : string.Join(" ", proseLines);

        combined = Regex.Replace(combined, @"\s+", " ").Trim();
        if (combined.Length > 340)
            combined = combined.Substring(0, 337).TrimEnd() + "...";

        return combined;
    }
}
