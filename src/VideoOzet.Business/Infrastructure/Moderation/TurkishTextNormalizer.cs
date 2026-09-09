using System.Text;

namespace VideoOzet.Business.Infrastructure.Moderation;

public static class TurkishTextNormalizer
{
    // Kiril ve Yunan harflerini Latin karşılıklarına çevirir (homoglyph bypass engeli)
    private static readonly Dictionary<char, char> HomoglyphMap = new()
    {
        ['\u0430'] = 'a', // Kiril а → Latin a
        ['\u0435'] = 'e', // Kiril е → Latin e
        ['\u043E'] = 'o', // Kiril о → Latin o
        ['\u0440'] = 'p', // Kiril р → Latin p
        ['\u0441'] = 'c', // Kiril с → Latin c
        ['\u0445'] = 'x', // Kiril х → Latin x
        ['\u03BF'] = 'o', // Yunan ο → Latin o
        ['\u03B1'] = 'a', // Yunan α → Latin a
    };

    // Türkçe rakam kelimeleri → rakam
    private static readonly Dictionary<string, string> NumberWords = new()
    {
        ["sıfır"] = "0",
        ["bir"]   = "1",
        ["iki"]   = "2",
        ["üç"]    = "3",
        ["dört"]  = "4",
        ["beş"]   = "5",
        ["altı"]  = "6",
        ["yedi"]  = "7",
        ["sekiz"] = "8",
        ["dokuz"] = "9",
    };

    public static string Normalize(string text)
    {
        // 1. Homoglyph temizle
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(HomoglyphMap.TryGetValue(c, out var clean) ? clean : c);
        var result = sb.ToString().ToLower(new System.Globalization.CultureInfo("tr-TR"));

        // 2. Türkçe rakam kelimelerini sayıya çevir
        foreach (var (word, digit) in NumberWords)
            result = result.Replace(word, digit);

        return result;
    }
}
