using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace VideoOzet.Business.Helpers;

public static class TextNormalizer
{
    private static readonly Dictionary<char, char> LegacyCharMap = new()
    {
        { 'ø', 'i' },
        { 'Ø', 'İ' },
        { 'Õ', 'ı' },
        { 'ú', 'ş' },
        { 'ù', 'Ş' },
        { '÷', 'ğ' },
        { 'Ý', 'İ' },
        { 'ý', 'ı' },
        { 'Þ', 'Ş' },
        { 'þ', 'ş' },
        { 'Ð', 'Ğ' },
        { 'ð', 'ğ' },
    };

    /// <summary>
    /// Eski font/PDF encoding hatalarını ve bozuk Türkçe karakterleri düzeltir.
    /// </summary>
    public static string NormalizeTurkishText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // Kontrol karakterlerini temizle (CR, LF, TAB hariç)
            if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
            {
                sb.Append(' ');
                continue;
            }

            // Harf haritasında varsa değiştir
            if (LegacyCharMap.TryGetValue(c, out var mappedChar))
            {
                // Eğer ø harfi büyük harfli bir kelime içindeyse (örn. ÜNøTE -> ÜNİTE) büyük 'İ' yap
                if (c == 'ø')
                {
                    bool prevIsUpper = i > 0 && char.IsUpper(text[i - 1]);
                    bool nextIsUpper = i + 1 < text.Length && char.IsUpper(text[i + 1]);
                    if (prevIsUpper || nextIsUpper)
                    {
                        mappedChar = 'İ';
                    }
                }
                sb.Append(mappedChar);
            }
            else
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString();

        // Bazı eski PDF fontlarında görülen spesifik kelime kalıplarını düzelt
        result = Regex.Replace(result, @"\bTan[ıi]P[ıi]\b", "Tanımı", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\bBak[ıi]P[ıi]ndan\b", "Bakımından", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\bayr[ıi]P[ıi]\b", "ayrımı", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\bara[sş]W[ıi]U[ıi]n\b", "araştırın", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"çal[ıi]şW[ıi]ktan", "çalıştıktan", RegexOptions.IgnoreCase);

        // Ardışık boşlukları normalize et (satır başı ve sonu boşluklarını temizle)
        result = Regex.Replace(result, @"[ \t]+", " ");
        result = Regex.Replace(result, @"\r?\n(\s*\r?\n)+", "\n\n");

        return result.Trim();
    }
}
