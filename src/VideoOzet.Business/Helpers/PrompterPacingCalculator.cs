using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VideoOzet.Business.Helpers;

public sealed record PrompterWordBudget(
    int Minutes,
    int MinWords,
    int TargetWords,
    int MaxWords,
    string ParagraphGuidance);

/// <summary>
/// Türkçe stüdyo prompter anlatım temposuna (~105 kelime/dk) göre süre, sahne sayısı ve kelime bütçesi hesaplar.
/// </summary>
public static class PrompterPacingCalculator
{
    public const int TargetWordsPerMinute = 105;
    public const int MinWordsPerMinute = 90;
    public const int MaxWordsPerMinute = 115;
    public const double HardOvershootTriggerRatio = 1.15;

    private static readonly Regex DurationRangeRegex = new(
        @"(\d+)\s*(?:[-–—]\s*(\d+))?\s*(?:dk|dakika)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BracketTagRegex = new(
        @"\[[^\]]+\]",
        RegexOptions.Compiled);

    public static PrompterWordBudget CalculateBudget(int minutes)
    {
        int safeMin = Math.Max(1, minutes);
        int min = Math.Max(75, safeMin * MinWordsPerMinute);
        int target = safeMin * TargetWordsPerMinute;
        int max = Math.Max(115, safeMin * MaxWordsPerMinute);

        string paragraphGuidance = safeMin switch
        {
            1 => "1–2 kısa, net paragraf",
            2 => "2 dengeli paragraf",
            3 => "2–3 dengeli paragraf",
            4 => "3–4 dengeli paragraf",
            _ => $"{Math.Clamp(min / 115, 3, 5)}–{Math.Clamp(max / 95, 4, 6)} dengeli paragraf"
        };

        return new PrompterWordBudget(safeMin, min, target, max, paragraphGuidance);
    }

    public static int CalculateTargetSceneCount(int targetMinutes)
    {
        int safeMin = Math.Max(3, targetMinutes);
        return safeMin switch
        {
            <= 6 => Math.Clamp(safeMin, 3, 4),
            <= 10 => 4,
            <= 15 => 5,
            <= 24 => Math.Clamp((int)Math.Ceiling(safeMin / 3.2), 6, 8),
            _ => Math.Clamp((int)Math.Ceiling(safeMin / 3.5), 8, 12)
        };
    }

    public static int CountPrompterWords(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return 0;

        int count = 0;
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.TrimStart();
            if (!trimmed.StartsWith('>'))
                continue;

            var content = trimmed.TrimStart('>', ' ', '\t');
            if (string.IsNullOrWhiteSpace(content))
                continue;

            content = BracketTagRegex.Replace(content, " ");
            content = content
                .Replace("\"", " ")
                .Replace("“", " ")
                .Replace("”", " ")
                .Replace("**", " ")
                .Replace("*", " ")
                .Replace("_", " ")
                .Replace("`", " ");

            var words = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            count += words.Length;
        }

        return count;
    }

    public static double EstimateDurationMinutes(int prompterWordCount)
    {
        if (prompterWordCount <= 0)
            return 0.0;

        return Math.Round(prompterWordCount / (double)TargetWordsPerMinute, 1);
    }

    public static bool IsOverBudget(int actualPrompterWords, PrompterWordBudget budget)
    {
        if (budget == null)
            throw new ArgumentNullException(nameof(budget));

        return actualPrompterWords > (int)Math.Ceiling(budget.MaxWords * HardOvershootTriggerRatio);
    }

    public static int? TryParseTargetMinutes(string? targetLength)
    {
        if (string.IsNullOrWhiteSpace(targetLength))
            return null;

        var match = DurationRangeRegex.Match(targetLength);
        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int m1) && m1 > 0)
        {
            if (match.Groups[2].Success &&
                int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int m2) &&
                m2 >= m1)
            {
                return (int)Math.Round((m1 + m2) / 2.0, MidpointRounding.AwayFromZero);
            }

            return m1;
        }

        var normalized = targetLength.Trim().ToLowerInvariant();
        if (normalized.Contains("kısa") || normalized.Contains("kisa"))
            return 6;
        if (normalized.Contains("orta"))
            return 10;
        if (normalized.Contains("uzun"))
            return 16;

        return null;
    }
}
