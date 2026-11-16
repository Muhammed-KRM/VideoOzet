using System.Text.RegularExpressions;
using VideoOzet.Business.Services;

namespace VideoOzet.Business.Services;

public sealed record NumericAnomaly(string MatchedValue, string Sentence, bool ExistsInSources);

public interface IDeterministicClaimScanner
{
    IReadOnlyList<NumericAnomaly> FindUngroundedNumbersAndCitations(string generatedMarkdown, string contextData);
}

/// <summary>
/// Üretilen araştırma özeti veya video planı içindeki yüzde/oran ifadelerini tarar ve
/// kaynak bağlamında geçmeyen uydurma istatistikleri deterministik olarak yakalar.
/// </summary>
public partial class DeterministicClaimScanner : IDeterministicClaimScanner
{
    [GeneratedRegex(@"(?:%\s*(?<val>\d+(?:[.,]\d+)?)|(?<val2>\d+(?:[.,]\d+)?)\s*%|yüzde\s+(?<val3>\d+(?:[.,]\d+)?))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PercentageRegex();

    public IReadOnlyList<NumericAnomaly> FindUngroundedNumbersAndCitations(string generatedMarkdown, string contextData)
    {
        var anomalies = new List<NumericAnomaly>();
        if (string.IsNullOrWhiteSpace(generatedMarkdown))
            return anomalies;

        var safeContext = contextData ?? string.Empty;
        var seenMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Cümlelere veya satırlara böl
        var sentences = Regex.Split(generatedMarkdown, @"(?<=[\.!\?])\s+|\r?\n+")
            .Select(s => s.Trim())
            .Where(s => s.Length > 5);

        foreach (var sentence in sentences)
        {
            // Başlık veya sadece zaman/süre notlarını atla
            if (sentence.StartsWith("**Hedef Süre", StringComparison.OrdinalIgnoreCase))
                continue;

            var matches = PercentageRegex().Matches(sentence);
            foreach (Match m in matches)
            {
                var rawVal = !string.IsNullOrEmpty(m.Groups["val"].Value)
                    ? m.Groups["val"].Value
                    : (!string.IsNullOrEmpty(m.Groups["val2"].Value)
                        ? m.Groups["val2"].Value
                        : m.Groups["val3"].Value);

                if (string.IsNullOrWhiteSpace(rawVal)) continue;

                var normalizedVal = rawVal.Replace(',', '.');
                var altVal = rawVal.Replace('.', ',');

                bool exists = !string.IsNullOrEmpty(safeContext) && (
                    ContainsPercentageInContext(safeContext, normalizedVal) ||
                    (altVal != normalizedVal && ContainsPercentageInContext(safeContext, altVal)));

                if (!exists && seenMatches.Add($"{rawVal}|{sentence[..Math.Min(40, sentence.Length)]}"))
                {
                    var cleanSentence = sentence.Length > 240 ? sentence[..240] + "..." : sentence;
                    anomalies.Add(new NumericAnomaly(m.Value.Trim(), cleanSentence, ExistsInSources: false));
                }
            }
        }

        return anomalies;
    }

    private static bool ContainsPercentageInContext(string context, string num)
    {
        var esc = Regex.Escape(num);
        var pattern = $@"(?:%\s*{esc}(?!\d)|\b{esc}\s*%|\byüzde\s+{esc}(?!\d)|\b{esc}\s*(?:percent|pct)\b)";
        return Regex.IsMatch(context, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
