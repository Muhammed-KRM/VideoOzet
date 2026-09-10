using System.Text.RegularExpressions;

namespace VideoOzet.Business.Utils;

public static class SensitiveDataMasker
{
    public static string? MaskJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return json;

        return Regex.Replace(
            json,
            @"""(password|token|refreshToken|aesKey|ibanEncrypted|tcknEncrypted|apiKey)""\s*:\s*""[^""]*""",
            @"""$1"":""***""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
