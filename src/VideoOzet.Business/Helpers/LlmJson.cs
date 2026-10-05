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
}
