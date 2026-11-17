using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Helpers;

public static class LlmJson
{
    public static string ExtractJson(string llmResponse)
    {
        if (string.IsNullOrWhiteSpace(llmResponse))
            return string.Empty;

        var text = llmResponse.Trim();

        // 0. Reasoning (<think>...</think>, <thought>...</thought>, <thinking>...</thinking>) bloklarını temizle
        text = Regex.Replace(text, @"<(think|thought|thinking)>.*?</\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
        if (Regex.IsMatch(text, @"^<(think|thought|thinking)>", RegexOptions.IgnoreCase))
        {
            var idxObj = text.IndexOf('{');
            var idxArr = text.IndexOf('[');
            int idx = idxObj >= 0 && idxArr >= 0 ? Math.Min(idxObj, idxArr) : Math.Max(idxObj, idxArr);
            if (idx > 0) text = text.Substring(idx).Trim();
        }

        // 1. Markdown kod bloğu temizleme
        var match = Regex.Match(text, @"```(?:json)?\s*(.*?)\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match.Success)
        {
            text = match.Groups[1].Value.Trim();
        }

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

        // 2. Metnin başında/sonunda açıklama metni kalmışsa en dıştaki JSON nesnesini/dizisini ayıkla
        if (!text.StartsWith("{") && !text.StartsWith("["))
        {
            int firstBrace = text.IndexOf('{');
            int firstBracket = text.IndexOf('[');
            int start = firstBrace >= 0 && firstBracket >= 0
                ? Math.Min(firstBrace, firstBracket)
                : Math.Max(firstBrace, firstBracket);

            if (start >= 0)
            {
                char openChar = text[start];
                char closeChar = openChar == '{' ? '}' : ']';
                int end = text.LastIndexOf(closeChar);
                if (end > start)
                {
                    text = text.Substring(start, end - start + 1).Trim();
                }
            }
        }
        else
        {
            char openChar = text[0];
            char closeChar = openChar == '{' ? '}' : ']';
            int end = text.LastIndexOf(closeChar);
            if (end > 0 && end < text.Length - 1)
            {
                text = text.Substring(0, end + 1).Trim();
            }
        }

        return text;
    }

    public static T Deserialize<T>(string llmResponse)
    {
        var jsonText = ExtractJson(llmResponse);

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return default!;
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        };

        try
        {
            return JsonSerializer.Deserialize<T>(jsonText, options)!;
        }
        catch (JsonException ex)
        {
            if (jsonText.StartsWith("{") || jsonText.StartsWith("["))
            {
                try
                {
                    var repaired = RepairMalformedLlmJson(jsonText);
                    return JsonSerializer.Deserialize<T>(repaired, options)!;
                }
                catch
                {
                    // Onarım sonrası da ayrıştırılamazsa orijinal hatayı fırlat
                }
            }

            throw new Exception($"Geçersiz JSON formatı: {jsonText}", ex);
        }
    }

    /// <summary>
    /// LLM akış kesintisi veya dize içi kaçışsız alt satır (0x0A / 0x0D / 0x09) kaynaklı
    /// bozuk JSON metinlerini deterministik durum makinesiyle onarır.
    /// </summary>
    public static string RepairMalformedLlmJson(string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
            return jsonText;

        var sb = new StringBuilder(jsonText.Length + 32);
        var closingStack = new Stack<char>();
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < jsonText.Length; i++)
        {
            char c = jsonText[i];

            if (inString)
            {
                if (escaped)
                {
                    sb.Append(c);
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    sb.Append(c);
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    sb.Append(c);
                    inString = false;
                    continue;
                }

                if (c == '\r')
                {
                    continue;
                }

                if (c == '\n')
                {
                    sb.Append("\\n");
                    continue;
                }

                if (c == '\t')
                {
                    sb.Append("\\t");
                    continue;
                }

                if (c < 0x20)
                {
                    sb.Append(' ');
                    continue;
                }

                sb.Append(c);
                continue;
            }

            if (c == '"')
            {
                inString = true;
                sb.Append(c);
            }
            else if (c == '{')
            {
                closingStack.Push('}');
                sb.Append(c);
            }
            else if (c == '[')
            {
                closingStack.Push(']');
                sb.Append(c);
            }
            else if (c == '}' || c == ']')
            {
                if (closingStack.Count > 0 && closingStack.Peek() == c)
                {
                    closingStack.Pop();
                    sb.Append(c);
                }
                else if (closingStack.Count >= 2)
                {
                    // Örn: [ { ... } yerine [ ... } geldiyse aradaki eksik kapanışı tamamla
                    var arr = closingStack.ToArray(); // üstten alta
                    int matchIdx = Array.IndexOf(arr, c);
                    if (matchIdx > 0)
                    {
                        for (int k = 0; k < matchIdx; k++)
                        {
                            sb.Append(closingStack.Pop());
                        }
                        closingStack.Pop();
                        sb.Append(c);
                    }
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (inString)
        {
            if (escaped) sb.Append('\\');
            sb.Append('"');
        }

        // Kapanışlardan önce kalmış olabilecek yarım virgül/iki noktayı temizle
        while (sb.Length > 0 && char.IsWhiteSpace(sb[sb.Length - 1]))
        {
            sb.Length--;
        }
        if (sb.Length > 0 && (sb[sb.Length - 1] == ',' || sb[sb.Length - 1] == ':'))
        {
            if (sb[sb.Length - 1] == ':')
            {
                sb.Append("null");
            }
            else
            {
                sb.Length--;
            }
        }

        while (closingStack.Count > 0)
        {
            sb.Append(closingStack.Pop());
        }

        return sb.ToString();
    }

    /// <summary>
    /// AgentWrite Aşama 1 çıktısından Araştırma Özeti Markdown metnini ve Sahne Planı JSON dizisini ayrıştırır.
    /// </summary>
    public static (string ArastirmaOzeti, string SahnePlaniJson) ParseStage1ResearchAndOutline(string llmResponse)
    {
        if (string.IsNullOrWhiteSpace(llmResponse))
            return (string.Empty, "[]");

        var clean = Regex.Replace(llmResponse.Trim(), @"<(think|thought|thinking)>.*?(</\1>|$)", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();

        var secAo = Regex.Match(clean, @"===\s*ARASTIRMA_OZETI\s*===", RegexOptions.IgnoreCase);
        var secSp = Regex.Match(clean, @"===\s*SAHNE_PLANI_JSON\s*===", RegexOptions.IgnoreCase);

        if (secSp.Success)
        {
            int aoStart = secAo.Success && secAo.Index < secSp.Index ? secAo.Index + secAo.Length : 0;
            string aoText = clean.Substring(aoStart, secSp.Index - aoStart).Trim();
            string rawSp = clean.Substring(secSp.Index + secSp.Length).Trim();
            string spJson = ExtractJson(rawSp);
            return (aoText, string.IsNullOrWhiteSpace(spJson) ? "[]" : spJson);
        }

        if (secAo.Success)
        {
            string body = clean.Substring(secAo.Index + secAo.Length).Trim();
            int lastArrStart = body.LastIndexOf("\n[", StringComparison.Ordinal);
            int lastArrEnd = body.LastIndexOf(']');
            if (lastArrStart > 0 && lastArrEnd > lastArrStart)
            {
                var candidateJson = ExtractJson(body.Substring(lastArrStart).Trim());
                if (candidateJson.StartsWith("[") && candidateJson.Contains("SahneNo", StringComparison.OrdinalIgnoreCase))
                {
                    return (body.Substring(0, lastArrStart).Trim(), candidateJson);
                }
            }
            return (body, "[]");
        }

        return (clean, "[]");
    }

    /// <summary>
    /// AgentWrite Aşama 3 çıktısından gerçek EpisodeContinuityLedgerDto nesnesini güvenle ayrıştırır ve JSON olarak döndürür.
    /// </summary>
    public static string NormalizeContinuityLedgerJson(
        string? rawResponse,
        int bolumNo,
        string calismaBasligi,
        string arastirmaOzetiFallback = "",
        string videoPlaniFallback = "")
    {
        var dto = new EpisodeContinuityLedgerDto
        {
            BolumNo = bolumNo,
            CalismaBasligi = calismaBasligi ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(rawResponse))
        {
            try
            {
                var json = ExtractJson(rawResponse);
                if (!string.IsNullOrWhiteSpace(json) && json.StartsWith("{"))
                {
                    using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        dto.IslenenDusunurlerVeKavramlar = ReadStringList(root, "IslenenDusunurlerVeKavramlar", "IslenenKavramVeDusunurler", "IslenenDusunurler");
                        dto.VerilenSomutOrnekler = ReadStringList(root, "VerilenSomutOrnekler", "SomutOrnekler");
                        dto.AcikBirakilanSorular = ReadStringList(root, "AcikBirakilanSorular", "AcikSorular");
                        dto.SonCumleGecisNotu = ReadFirstString(root, "SonCumleGecisNotu", "SonCumleGecisi", "GecisNotu");
                    }
                }
            }
            catch
            {
                // Aşağıdaki yapılandırılmış fallback ile doldurulacak
            }
        }

        if (dto.IslenenDusunurlerVeKavramlar.Count == 0 && !string.IsNullOrWhiteSpace(arastirmaOzetiFallback))
        {
            var headings = Regex.Matches(arastirmaOzetiFallback, @"(?m)^#{2,3}\s+(?:\d+(?:\.\d+)*\.?\s*)?(.+)$")
                .Select(m => m.Groups[1].Value.Trim())
                .Where(h => !string.IsNullOrWhiteSpace(h) &&
                            !h.Contains("Giriş", StringComparison.OrdinalIgnoreCase) &&
                            !h.Contains("Sonuç", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
            dto.IslenenDusunurlerVeKavramlar = headings;
        }

        if (string.IsNullOrWhiteSpace(dto.SonCumleGecisNotu) && !string.IsNullOrWhiteSpace(videoPlaniFallback))
        {
            var lastQuotes = videoPlaniFallback.Replace("\r\n", "\n")
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith(">") && l.Length > 15)
                .Select(l => l.TrimStart('>').Trim().Trim('"'))
                .TakeLast(2)
                .ToList();

            if (lastQuotes.Count > 0)
            {
                var joined = string.Join(" ", lastQuotes);
                dto.SonCumleGecisNotu = joined.Length > 450 ? joined.Substring(joined.Length - 450) : joined;
            }
        }

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = false });
    }

    private static List<string> ReadStringList(JsonElement root, params string[] names)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (names.Any(n => string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase)) &&
                prop.Value.ValueKind == JsonValueKind.Array)
            {
                return prop.Value.EnumerateArray()
                    .Select(x => x.ValueKind == JsonValueKind.String ? (x.GetString() ?? "").Trim() : x.ToString().Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
        }
        return new List<string>();
    }

    private static string ReadFirstString(JsonElement root, params string[] names)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (names.Any(n => string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase)))
            {
                return prop.Value.ValueKind == JsonValueKind.String
                    ? (prop.Value.GetString() ?? string.Empty).Trim()
                    : prop.Value.ToString().Trim();
            }
        }
        return string.Empty;
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
        var cleanResponse = Regex.Replace(llmResponse.Trim(), @"<(think|thought|thinking)>.*?</\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();

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
                string dn = "";
                if (elem.TryGetProperty("DevirNotu", out var dnProp))
                {
                    dn = dnProp.ValueKind == JsonValueKind.String ? (dnProp.GetString() ?? "") : dnProp.ToString();
                }
                if (!string.IsNullOrWhiteSpace(ao) || !string.IsNullOrWhiteSpace(vp))
                    return (ao, vp, dn);
            }
        }
        catch
        {
            // Standart JSON parse başarısız olduysa alan sınırlarından doğrudan ayıkla.
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
