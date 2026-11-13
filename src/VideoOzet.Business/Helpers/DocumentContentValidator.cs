using System;

namespace VideoOzet.Business.Helpers;

/// <summary>
/// Dokümanlardan çıkarılan metnin gerçek içerik olup olmadığını (boş/taranmış PDF veya
/// Cloudflare / WAF / 403 erişim engeli sayfası olup olmadığını) doğrular.
/// </summary>
public static class DocumentContentValidator
{
    public static bool IsBlockedOrInvalidContent(string? rawText, out string errorReason)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            errorReason = "Dokümandan okunabilir metin çıkarılamadı (boş veya taranmış görüntü tabanlı PDF olabilir).";
            return true;
        }

        int alnumCount = 0;
        foreach (char c in rawText)
        {
            if (char.IsLetterOrDigit(c))
            {
                alnumCount++;
                if (alnumCount >= 15) break;
            }
        }

        if (alnumCount < 15)
        {
            errorReason = "Dokümandan okunabilir metin çıkarılamadı (boş veya taranmış görüntü tabanlı PDF olabilir).";
            return true;
        }

        var words = rawText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 600)
        {
            var lower = rawText.ToLowerInvariant();

            bool hasCloudflareFooter = lower.Contains("cloudflare ray id") || lower.Contains("performance & security by cloudflare");
            bool hasBlockPhrase = lower.Contains("have been blocked") ||
                                  lower.Contains("unable to access") ||
                                  lower.Contains("attention required") ||
                                  lower.Contains("security service to protect itself");

            bool hasWhyBlocked = lower.Contains("why have i been blocked");

            bool hasAccessDenied = lower.Contains("access denied") || lower.Contains("403 forbidden") || lower.Contains("error 1020");
            bool hasWafVendor = lower.Contains("cloudflare") || lower.Contains("sucuri") || lower.Contains("akamai") || lower.Contains("incapsula");

            bool hasChallenge = lower.Contains("just a moment") &&
                                (lower.Contains("verifying you are human") || lower.Contains("enable javascript and cookies to continue"));

            if ((hasCloudflareFooter && hasBlockPhrase) || hasWhyBlocked || (hasAccessDenied && hasWafVendor) || hasChallenge)
            {
                errorReason = "Yüklenen PDF gerçek içerik yerine web güvenlik/erişim engeli (Cloudflare / 403) sayfası içeriyor. Lütfen geçerli makale/doküman dosyasını yükleyin.";
                return true;
            }
        }

        errorReason = string.Empty;
        return false;
    }
}
