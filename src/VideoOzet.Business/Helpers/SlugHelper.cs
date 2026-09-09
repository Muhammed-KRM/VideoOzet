using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VideoOzet.Business.Helpers;

public static class SlugHelper
{
    public static string GenerateSlug(string text)
    {
        // Türkçe karakterleri dönüştür
        var slug = text.ToLowerInvariant();
        slug = slug.Replace("ı", "i").Replace("ğ", "g").Replace("ü", "u")
                   .Replace("ş", "s").Replace("ö", "o").Replace("ç", "c");
        
        // Aksanları kaldır
        slug = RemoveDiacritics(slug);
        
        // Alfanümerik olmayan karakterleri tire ile değiştir
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"[\s-]+", "-").Trim('-');
        
        return slug;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
