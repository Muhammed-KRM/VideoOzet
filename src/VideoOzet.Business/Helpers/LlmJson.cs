using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VideoOzet.Business.Helpers;

public static class LlmJson
{
    public static string ExtractJson(string llmResponse)
    {
        if (string.IsNullOrWhiteSpace(llmResponse))
            return string.Empty;

        var text = llmResponse.Trim();

        // 0. Reasoning (<think>...</think>) bloklarını temizle
        text = Regex.Replace(text, @"<think>.*?</think>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
        if (text.StartsWith("<think>", StringComparison.OrdinalIgnoreCase))
        {
            var idx = text.IndexOf('{');
            if (idx > 0) text = text.Substring(idx).Trim();
        }

        // 1. Markdown kod bloğu temizleme
        var match = Regex.Match(text, @"```(?:json)?\s*(.*?)\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match.Success)
        {
            text = match.Groups[1].Value.Trim();
        }

        // Eğer hala json blok işareti varsa (bazen bozuk gelebiliyor)
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(7).Trim();
        }
        if (text.StartsWith("```"))
        {
            text = text.Substring(3).Trim();
        }
        if (text.EndsWith("```"))
        {
            text = text.Substring(0, text.Length - 3).Trim();
        }

        return text;
    }

    public static T Deserialize<T>(string llmResponse)
    {
        var jsonText = ExtractJson(llmResponse);

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return default;
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        };

        try
        {
            return JsonSerializer.Deserialize<T>(jsonText, options);
        }
        catch (JsonException ex)
        {
            throw new Exception($"Geçersiz JSON formatı: {jsonText}", ex);
        }
    }

    /// <summary>
    /// SeriesVideoGenerationPrompt çıktısındaki ArastirmaOzeti, VideoPlani ve DevirNotu alanlarını;
    /// bölüm ayraçları (===ARASTIRMA_OZETI===), geçerli JSON veya tek/çift tırnaklı sözde-JSON formatlarından güvenle ayrıştırır.
    /// </summary>
    public static (string ArastirmaOzeti, string VideoPlani, string DevirNotu) ParseSeriesVideoContent(string llmResponse)
    {
        if (string.IsNullOrWhiteSpace(llmResponse))
            throw new InvalidOperationException("Yapay zeka boş yanıt döndürdü.");

        // 0. <think>...</think> bloklarını temizle
        var cleanResponse = Regex.Replace(llmResponse.Trim(), @"<think>.*?</think>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();

        // 1. Bölüm ayraçları (===ARASTIRMA_OZETI=== / ===VIDEO_PLANI=== / ===DEVIR_NOTU===) kontrolü
        var secAo = Regex.Match(cleanResponse, @"===\s*ARASTIRMA_OZETI\s*===", RegexOptions.IgnoreCase);
        var secVp = Regex.Match(cleanResponse, @"===\s*VIDEO_PLANI\s*===", RegexOptions.IgnoreCase);
        var secDn = Regex.Match(cleanResponse, @"===\s*DEVIR_NOTU\s*===", RegexOptions.IgnoreCase);

        if (secVp.Success)
        {
            string aoText = "";
            string vpText;
            string dnText = "";

            if (secAo.Success && secAo.Index < secVp.Index)
            {
                int aoStart = secAo.Index + secAo.Length;
                aoText = cleanResponse.Substring(aoStart, secVp.Index - aoStart).Trim();
            }
            else if (secVp.Index > 0)
            {
                aoText = cleanResponse.Substring(0, secVp.Index).Trim();
            }

            int vpStart = secVp.Index + secVp.Length;
            if (secDn.Success && secDn.Index > vpStart)
            {
                vpText = cleanResponse.Substring(vpStart, secDn.Index - vpStart).Trim();
                dnText = cleanResponse.Substring(secDn.Index + secDn.Length).Trim();
            }
            else
            {
                vpText = cleanResponse.Substring(vpStart).Trim();
            }

            return (aoText, vpText, dnText);
        }

        // 2. Standart JSON ayrıştırma (birim testler ve JSON döndüren modeller için)
        var jsonText = ExtractJson(cleanResponse);
        try
        {
            var elem = Deserialize<JsonElement>(jsonText);
            if (elem.ValueKind == JsonValueKind.Object)
            {
                var ao = elem.TryGetProperty("ArastirmaOzeti", out var aoProp) ? aoProp.GetString() ?? "" : "";
                var vp = elem.TryGetProperty("VideoPlani", out var vpProp) ? vpProp.GetString() ?? "" : "";
                var dn = elem.TryGetProperty("DevirNotu", out var dnProp) ? dnProp.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(ao) || !string.IsNullOrWhiteSpace(vp))
                    return (ao, vp, dn);
            }
        }
        catch
        {
            // Standart JSON parse başarısız olduysa (örn. metin içinde kaçırılmamış " veya ' tırnakları varsa)
            // alan sınırlarından doğrudan ayıkla.
        }

        // 3. Hem çift tırnak (") hem tek tırnak (') destekleyen alan sınırı ayrıştırıcısı
        var aoMatch = Regex.Match(jsonText, @"[""']?ArastirmaOzeti[""']?\s*:\s*[""']", RegexOptions.IgnoreCase);
        var vpMatch = Regex.Match(jsonText, @"[""']\s*,\s*[""']?VideoPlani[""']?\s*:\s*[""']", RegexOptions.IgnoreCase);
        var dnMatch = Regex.Match(jsonText, @"[""']\s*,\s*[""']?DevirNotu[""']?\s*:\s*[""']", RegexOptions.IgnoreCase);

        if (aoMatch.Success && vpMatch.Success && vpMatch.Index > aoMatch.Index)
        {
            int aoStart = aoMatch.Index + aoMatch.Length;
            string rawAo = jsonText.Substring(aoStart, vpMatch.Index - aoStart);

            int vpStart = vpMatch.Index + vpMatch.Length;
            string rawVp;
            string rawDn = "";

            if (dnMatch.Success && dnMatch.Index > vpStart)
            {
                rawVp = jsonText.Substring(vpStart, dnMatch.Index - vpStart);
                int dnStart = dnMatch.Index + dnMatch.Length;
                rawDn = jsonText.Substring(dnStart);
                rawDn = Regex.Replace(rawDn, @"[""']\s*\}\s*$", "", RegexOptions.Singleline);
            }
            else
            {
                rawVp = jsonText.Substring(vpStart);
                rawVp = Regex.Replace(rawVp, @"[""']\s*\}\s*$", "", RegexOptions.Singleline);
            }

            return (UnescapeJsonText(rawAo), UnescapeJsonText(rawVp), UnescapeJsonText(rawDn));
        }

        // 4. Son çare: Model doğrudan Markdown döndürdüyse içeriği kaybetme
        if (cleanResponse.Length > 100)
        {
            return (cleanResponse, cleanResponse, "");
        }

        throw new InvalidOperationException($"Yapay zeka yanıtından bölüm alanları ayrıştırılamadı: {jsonText}");
    }

    private static string UnescapeJsonText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        return raw
            .Replace("\\r\\n", "\n")
            .Replace("\\n", "\n")
            .Replace("\\t", "\t")
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\")
            .Trim();
    }
}
