using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.DTOs.YouTube;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Services;

public class YouTubeSearchService : IYouTubeSearchService
{
    private static readonly Regex VideoIdRegex = new("^[a-zA-Z0-9_-]{11}$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedYouTubeHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "music.youtube.com",
        "youtu.be",
        "youtube-nocookie.com",
        "www.youtube-nocookie.com"
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly IAiProviderSettingsService _settingsService;
    private readonly ILogger<YouTubeSearchService> _logger;

    public YouTubeSearchService(
        IHttpClientFactory httpFactory,
        IAiProviderSettingsService settingsService,
        ILogger<YouTubeSearchService> logger)
    {
        _httpFactory = httpFactory;
        _settingsService = settingsService;
        _logger = logger;
    }

    public static bool TryNormalizeVideoId(string? rawVideoId, out string videoId)
    {
        videoId = string.Empty;
        if (string.IsNullOrWhiteSpace(rawVideoId))
            return false;

        var trimmed = rawVideoId.Trim();
        if (VideoIdRegex.IsMatch(trimmed))
        {
            videoId = trimmed;
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !AllowedYouTubeHosts.Contains(uri.Host))
            return false;

        if (string.Equals(uri.Host, "youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var shortPath = uri.AbsolutePath.Trim('/');
            if (VideoIdRegex.IsMatch(shortPath))
            {
                videoId = shortPath;
                return true;
            }

            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 &&
            (string.Equals(segments[0], "shorts", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "embed", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "live", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "v", StringComparison.OrdinalIgnoreCase)))
        {
            if (VideoIdRegex.IsMatch(segments[1]))
            {
                videoId = segments[1];
                return true;
            }

            return false;
        }

        if (segments.Length == 0 ||
            (segments.Length == 1 && string.Equals(segments[0], "watch", StringComparison.OrdinalIgnoreCase)))
        {
            var query = uri.Query;
            if (!string.IsNullOrEmpty(query))
            {
                var trimmedQuery = query.TrimStart('?');
                foreach (var pair in trimmedQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eqIndex = pair.IndexOf('=');
                    if (eqIndex <= 0)
                        continue;

                    var key = pair.Substring(0, eqIndex);
                    if (!string.Equals(key, "v", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var rawVal = pair.Substring(eqIndex + 1);
                    var candidate = Uri.UnescapeDataString(rawVal);
                    if (VideoIdRegex.IsMatch(candidate))
                    {
                        videoId = candidate;
                        return true;
                    }

                    return false;
                }
            }
        }

        return false;
    }

    public async Task<YouTubeSearchResponseDto> SearchAsync(
        string query,
        int maxResults = 15,
        string duration = "any",
        CancellationToken ct = default)
    {
        var normalizedQuery = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return new YouTubeSearchResponseDto
            {
                Items = new List<YouTubeSearchItemDto>(),
                KullanilanKaynak = "yt-dlp"
            };
        }

        var clampedMax = Math.Clamp(maxResults, 1, 30);
        var normalizedDuration = NormalizeDurationFilter(duration);

        var settings = await _settingsService.GetSettingsAsync(ct);
        var apiKey = settings.YouTubeApiKey?.Trim();

        bool quotaExceeded = false;
        string? warningMessage = null;

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var apiAttempt = await TrySearchWithDataApiAsync(normalizedQuery, clampedMax, normalizedDuration, apiKey, ct);
            if (apiAttempt.Success)
            {
                return new YouTubeSearchResponseDto
                {
                    Items = apiAttempt.Items,
                    KullanilanKaynak = "youtube-api-v3",
                    KotaAsildiMi = false,
                    UyariMesaji = null
                };
            }

            quotaExceeded = apiAttempt.QuotaExceeded;
            warningMessage = apiAttempt.WarningMessage;
            _logger.LogWarning(
                "YouTube Data API v3 araması başarısız oldu, yt-dlp yedek motoruna düşülüyor. KotaAşıldı={QuotaExceeded}, Uyarı={Warning}",
                quotaExceeded,
                warningMessage);
        }

        var ytDlpItems = await SearchWithYtDlpAsync(normalizedQuery, clampedMax, normalizedDuration, ct);
        return new YouTubeSearchResponseDto
        {
            Items = ytDlpItems,
            KullanilanKaynak = "yt-dlp",
            KotaAsildiMi = quotaExceeded,
            UyariMesaji = warningMessage
        };
    }

    public async Task<YouTubeVideoDetailDto?> GetVideoDetailAsync(string videoId, CancellationToken ct = default)
    {
        if (!TryNormalizeVideoId(videoId, out var normalizedId))
            return null;

        YouTubeVideoDetailDto? detail = null;
        var ytDlpPath = TryFindYtDlpPath();

        if (!string.IsNullOrWhiteSpace(ytDlpPath))
        {
            detail = await TryGetVideoDetailWithYtDlpAsync(ytDlpPath, normalizedId, ct);
        }

        if (detail is null)
        {
            var settings = await _settingsService.GetSettingsAsync(ct);
            var apiKey = settings.YouTubeApiKey?.Trim();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                detail = await TryGetVideoDetailWithDataApiAsync(normalizedId, apiKey, ct);
            }
            else if (string.IsNullOrWhiteSpace(ytDlpPath))
            {
                throw new BusinessException(
                    "yt-dlp aracı bulunamadı ve YouTube Data API v3 anahtarı tanımlı değil. Lütfen Ayarlar'dan API anahtarı girin veya 'tools/yt-dlp' aracını ekleyin.");
            }
        }

        if (detail is null)
            return null;

        try
        {
            var related = await GetRelatedOrChannelVideosAsync(normalizedId, detail.KanalId, 8, ct);
            if (related.Count == 0 && !string.IsNullOrWhiteSpace(detail.Baslik))
            {
                var fallbackSearch = await SearchAsync(detail.Baslik, 9, "any", ct);
                related = fallbackSearch.Items
                    .Where(i => !string.Equals(i.VideoId, normalizedId, StringComparison.Ordinal))
                    .Take(8)
                    .ToList();
            }

            detail.IlgiliVideolar = related.ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "İlgili videolar alınırken hata oluştu (VideoId: {VideoId}).", normalizedId);
        }

        return detail;
    }

    public async Task<IReadOnlyList<YouTubeSearchItemDto>> GetRelatedOrChannelVideosAsync(
        string videoId,
        string? channelId = null,
        int maxResults = 8,
        CancellationToken ct = default)
    {
        if (!TryNormalizeVideoId(videoId, out var normalizedId))
            return Array.Empty<YouTubeSearchItemDto>();

        var clampedMax = Math.Clamp(maxResults, 1, 20);
        var settings = await _settingsService.GetSettingsAsync(ct);
        var apiKey = settings.YouTubeApiKey?.Trim();

        if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(channelId))
        {
            var apiItems = await TryGetChannelVideosWithDataApiAsync(channelId.Trim(), normalizedId, clampedMax, apiKey, ct);
            if (apiItems is not null)
                return apiItems;
        }

        var ytDlpPath = TryFindYtDlpPath();
        if (!string.IsNullOrWhiteSpace(ytDlpPath) && !string.IsNullOrWhiteSpace(channelId))
        {
            var channelUrl = $"https://www.youtube.com/channel/{Uri.EscapeDataString(channelId.Trim())}/videos";
            var channelArgs = new[]
            {
                "--flat-playlist",
                "--dump-json",
                "--no-warnings",
                "--ignore-errors",
                "--playlist-items",
                $"1:{clampedMax + 2}",
                channelUrl
            };

            var (exitCode, stdout, _) = await RunYtDlpAsync(ytDlpPath, channelArgs, ct);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                var parsed = ParseYtDlpSearchNdjson(stdout)
                    .Where(item => !string.Equals(item.VideoId, normalizedId, StringComparison.Ordinal))
                    .Take(clampedMax)
                    .ToList();
                if (parsed.Count > 0)
                    return parsed;
            }
        }

        return Array.Empty<YouTubeSearchItemDto>();
    }

    private async Task<(bool Success, List<YouTubeSearchItemDto> Items, bool QuotaExceeded, string? WarningMessage)> TrySearchWithDataApiAsync(
        string query,
        int maxResults,
        string duration,
        string apiKey,
        CancellationToken ct)
    {
        try
        {
            using var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var searchUrl = new StringBuilder("https://www.googleapis.com/youtube/v3/search")
                .Append("?part=snippet&type=video")
                .Append("&q=").Append(Uri.EscapeDataString(query))
                .Append("&maxResults=").Append(maxResults)
                .Append("&key=").Append(Uri.EscapeDataString(apiKey));

            if (duration is "short" or "medium" or "long")
            {
                searchUrl.Append("&videoDuration=").Append(duration);
            }

            using var searchRes = await client.GetAsync(searchUrl.ToString(), ct);
            var searchBody = await searchRes.Content.ReadAsStringAsync(ct);

            if (!searchRes.IsSuccessStatusCode)
            {
                var (isQuota, reason) = AnalyzeYouTubeApiError(searchRes.StatusCode, searchBody);
                var warning = isQuota
                    ? "YouTube Data API v3 günlük arama kotası aşıldı; otomatik olarak yt-dlp yedek arama motoruna geçildi."
                    : $"YouTube Data API v3 hatası (HTTP {(int)searchRes.StatusCode}{(string.IsNullOrWhiteSpace(reason) ? "" : $": {reason}")}); otomatik olarak yt-dlp yedek arama motoruna geçildi.";

                return (false, new List<YouTubeSearchItemDto>(), isQuota, warning);
            }

            var snippetFallbacks = new Dictionary<string, YouTubeSearchItemDto>(StringComparer.Ordinal);
            var orderedIds = new List<string>();

            using (var searchDoc = JsonDocument.Parse(searchBody))
            {
                if (searchDoc.RootElement.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var itemEl in itemsProp.EnumerateArray())
                    {
                        string? vid = null;
                        if (itemEl.TryGetProperty("id", out var idEl))
                        {
                            if (idEl.ValueKind == JsonValueKind.Object && idEl.TryGetProperty("videoId", out var vidProp))
                                vid = vidProp.GetString();
                            else if (idEl.ValueKind == JsonValueKind.String)
                                vid = idEl.GetString();
                        }

                        if (string.IsNullOrWhiteSpace(vid) || !TryNormalizeVideoId(vid, out var normalizedVid))
                            continue;

                        if (!snippetFallbacks.ContainsKey(normalizedVid))
                        {
                            orderedIds.Add(normalizedVid);
                            snippetFallbacks[normalizedVid] = ParseDataApiSnippetItem(normalizedVid, itemEl);
                        }
                    }
                }
            }

            if (orderedIds.Count == 0)
            {
                return (true, new List<YouTubeSearchItemDto>(), false, null);
            }

            var enriched = await EnrichWithVideosListAsync(client, orderedIds, snippetFallbacks, apiKey, ct);
            return (true, enriched, false, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "YouTube Data API v3 isteği sırasında beklenmeyen hata oluştu.");
            return (false, new List<YouTubeSearchItemDto>(), false, "YouTube Data API v3 bağlantısı kurulamadı; otomatik olarak yt-dlp yedek arama motoruna geçildi.");
        }
    }

    private async Task<List<YouTubeSearchItemDto>> EnrichWithVideosListAsync(
        HttpClient client,
        List<string> orderedIds,
        Dictionary<string, YouTubeSearchItemDto> snippetFallbacks,
        string apiKey,
        CancellationToken ct)
    {
        var byId = new Dictionary<string, YouTubeSearchItemDto>(snippetFallbacks, StringComparer.Ordinal);

        try
        {
            var videosUrl =
                $"https://www.googleapis.com/youtube/v3/videos?part=contentDetails,statistics,snippet&id={Uri.EscapeDataString(string.Join(",", orderedIds))}&key={Uri.EscapeDataString(apiKey)}";

            using var videosRes = await client.GetAsync(videosUrl, ct);
            if (videosRes.IsSuccessStatusCode)
            {
                var videosBody = await videosRes.Content.ReadAsStringAsync(ct);
                using var videosDoc = JsonDocument.Parse(videosBody);
                if (videosDoc.RootElement.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var vEl in itemsEl.EnumerateArray())
                    {
                        var vid = vEl.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                        if (string.IsNullOrWhiteSpace(vid) || !TryNormalizeVideoId(vid, out var normalizedVid))
                            continue;

                        var dto = ParseDataApiVideoItem(normalizedVid, vEl);
                        byId[normalizedVid] = dto;
                    }
                }
            }
            else
            {
                _logger.LogWarning("YouTube Data API videos.list zenginleştirmesi başarısız oldu: HTTP {StatusCode}", (int)videosRes.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "YouTube Data API videos.list zenginleştirmesi başarısız oldu.");
        }

        return orderedIds
            .Where(id => byId.ContainsKey(id))
            .Select(id => byId[id])
            .ToList();
    }

    private async Task<IReadOnlyList<YouTubeSearchItemDto>?> TryGetChannelVideosWithDataApiAsync(
        string channelId,
        string excludeVideoId,
        int maxResults,
        string apiKey,
        CancellationToken ct)
    {
        try
        {
            using var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var url =
                $"https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&channelId={Uri.EscapeDataString(channelId)}&order=date&maxResults={maxResults + 1}&key={Uri.EscapeDataString(apiKey)}";

            using var res = await client.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
                return null;

            var body = await res.Content.ReadAsStringAsync(ct);
            var snippetFallbacks = new Dictionary<string, YouTubeSearchItemDto>(StringComparer.Ordinal);
            var orderedIds = new List<string>();

            using (var doc = JsonDocument.Parse(body))
            {
                if (doc.RootElement.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var itemEl in itemsProp.EnumerateArray())
                    {
                        string? vid = null;
                        if (itemEl.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Object && idEl.TryGetProperty("videoId", out var vidProp))
                        {
                            vid = vidProp.GetString();
                        }

                        if (!TryNormalizeVideoId(vid, out var normalizedVid) ||
                            string.Equals(normalizedVid, excludeVideoId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (!snippetFallbacks.ContainsKey(normalizedVid))
                        {
                            orderedIds.Add(normalizedVid);
                            snippetFallbacks[normalizedVid] = ParseDataApiSnippetItem(normalizedVid, itemEl);
                        }
                    }
                }
            }

            if (orderedIds.Count == 0)
                return Array.Empty<YouTubeSearchItemDto>();

            var enriched = await EnrichWithVideosListAsync(client, orderedIds.Take(maxResults).ToList(), snippetFallbacks, apiKey, ct);
            return enriched;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "YouTube Data API ile kanal videoları alınamadı.");
            return null;
        }
    }

    private async Task<List<YouTubeSearchItemDto>> SearchWithYtDlpAsync(
        string query,
        int maxResults,
        string duration,
        CancellationToken ct)
    {
        var ytDlpPath = FindRequiredYtDlpPath();
        var fetchCount = duration == "any"
            ? maxResults
            : Math.Min(50, Math.Max(maxResults * 3, 30));

        var args = new[]
        {
            "--flat-playlist",
            "--dump-json",
            "--no-warnings",
            "--ignore-errors",
            $"ytsearch{fetchCount}:{query}"
        };

        var (exitCode, stdout, stderr) = await RunYtDlpAsync(ytDlpPath, args, ct);
        if (exitCode != 0 && string.IsNullOrWhiteSpace(stdout))
        {
            _logger.LogWarning("yt-dlp arama komutu çıkış kodu {ExitCode} ile döndü: {Stderr}", exitCode, stderr);
            throw new BusinessException($"YouTube araması yt-dlp ile tamamlanamadı: {(string.IsNullOrWhiteSpace(stderr) ? $"Çıkış kodu {exitCode}" : stderr.Trim())}");
        }

        var items = ParseYtDlpSearchNdjson(stdout);

        IEnumerable<YouTubeSearchItemDto> filtered = duration switch
        {
            "short" => items.Where(i => i.SureSaniye is > 0 and < 240),
            "medium" => items.Where(i => i.SureSaniye is >= 240 and <= 1200),
            "long" => items.Where(i => i.SureSaniye is > 1200),
            _ => items
        };

        return filtered.Take(maxResults).ToList();
    }

    private async Task<YouTubeVideoDetailDto?> TryGetVideoDetailWithYtDlpAsync(string ytDlpPath, string videoId, CancellationToken ct)
    {
        var args = new[]
        {
            "--dump-single-json",
            "--no-warnings",
            "--skip-download",
            "--no-playlist",
            $"https://www.youtube.com/watch?v={videoId}"
        };

        var (exitCode, stdout, stderr) = await RunYtDlpAsync(ytDlpPath, args, ct);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            _logger.LogWarning("yt-dlp video detayı alınamadı ({VideoId}, ExitCode={ExitCode}): {Stderr}", videoId, exitCode, stderr);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            var durationSeconds = ReadDurationSeconds(root);
            var manualLangs = ExtractSubtitleLanguages(root, "subtitles", filterAutoTranslate: false);
            var autoLangs = ExtractSubtitleLanguages(root, "automatic_captions", filterAutoTranslate: true);

            return new YouTubeVideoDetailDto
            {
                VideoId = videoId,
                Baslik = ReadString(root, "title") ?? string.Empty,
                Aciklama = ReadString(root, "description") ?? string.Empty,
                KanalAdi = ReadString(root, "channel") ?? ReadString(root, "uploader") ?? string.Empty,
                KanalId = ReadString(root, "channel_id") ?? ReadString(root, "uploader_id"),
                ThumbnailUrl = ExtractYtDlpThumbnail(root, videoId),
                SureSaniye = durationSeconds,
                SureMetni = FormatDuration(durationSeconds),
                YayinTarihi = ExtractYtDlpPublishDate(root),
                GoruntulenmeSayisi = ReadInt64(root, "view_count"),
                ManuelAltyaziDilleri = manualLangs,
                OtomatikAltyaziDilleri = autoLangs,
                IlgiliVideolar = new List<YouTubeSearchItemDto>(),
                KaynakUrl = ReadString(root, "webpage_url") ?? $"https://www.youtube.com/watch?v={videoId}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "yt-dlp video detay JSON çıktısı ayrıştırılamadı (VideoId: {VideoId}).", videoId);
            return null;
        }
    }

    private async Task<YouTubeVideoDetailDto?> TryGetVideoDetailWithDataApiAsync(string videoId, string apiKey, CancellationToken ct)
    {
        try
        {
            using var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var url =
                $"https://www.googleapis.com/youtube/v3/videos?part=contentDetails,statistics,snippet&id={Uri.EscapeDataString(videoId)}&key={Uri.EscapeDataString(apiKey)}";

            using var res = await client.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
                return null;

            var body = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("items", out var itemsProp) ||
                itemsProp.ValueKind != JsonValueKind.Array ||
                itemsProp.GetArrayLength() == 0)
            {
                return null;
            }

            var item = ParseDataApiVideoItem(videoId, itemsProp[0]);
            var manualLangs = new List<string>();
            var autoLangs = new List<string>();

            try
            {
                var captionsUrl =
                    $"https://www.googleapis.com/youtube/v3/captions?part=snippet&videoId={Uri.EscapeDataString(videoId)}&key={Uri.EscapeDataString(apiKey)}";
                using var capRes = await client.GetAsync(captionsUrl, ct);
                if (capRes.IsSuccessStatusCode)
                {
                    var capBody = await capRes.Content.ReadAsStringAsync(ct);
                    using var capDoc = JsonDocument.Parse(capBody);
                    if (capDoc.RootElement.TryGetProperty("items", out var capItems) && capItems.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cap in capItems.EnumerateArray())
                        {
                            if (!cap.TryGetProperty("snippet", out var snip)) continue;
                            var lang = ReadString(snip, "language");
                            var kind = ReadString(snip, "trackKind");
                            if (string.IsNullOrWhiteSpace(lang)) continue;

                            if (string.Equals(kind, "ASR", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!autoLangs.Contains(lang, StringComparer.OrdinalIgnoreCase))
                                    autoLangs.Add(lang);
                            }
                            else
                            {
                                if (!manualLangs.Contains(lang, StringComparer.OrdinalIgnoreCase))
                                    manualLangs.Add(lang);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "YouTube Data API captions.list okunamadı (VideoId: {VideoId}).", videoId);
            }

            return new YouTubeVideoDetailDto
            {
                VideoId = item.VideoId,
                Baslik = item.Baslik,
                Aciklama = item.Aciklama ?? string.Empty,
                KanalAdi = item.KanalAdi,
                KanalId = item.KanalId,
                ThumbnailUrl = item.ThumbnailUrl,
                SureSaniye = item.SureSaniye,
                SureMetni = item.SureMetni,
                YayinTarihi = item.YayinTarihi,
                GoruntulenmeSayisi = item.GoruntulenmeSayisi,
                ManuelAltyaziDilleri = manualLangs,
                OtomatikAltyaziDilleri = autoLangs,
                IlgiliVideolar = new List<YouTubeSearchItemDto>(),
                KaynakUrl = item.KaynakUrl
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "YouTube Data API video detayı alınamadı (VideoId: {VideoId}).", videoId);
            return null;
        }
    }

    private List<YouTubeSearchItemDto> ParseYtDlpSearchNdjson(string stdout)
    {
        var results = new List<YouTubeSearchItemDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        using var reader = new StringReader(stdout);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var rawId = ReadString(root, "id") ?? ReadString(root, "url") ?? ReadString(root, "webpage_url");
                if (!TryNormalizeVideoId(rawId, out var videoId) || !seen.Add(videoId))
                    continue;

                var durationSeconds = ReadDurationSeconds(root);
                results.Add(new YouTubeSearchItemDto
                {
                    VideoId = videoId,
                    Baslik = ReadString(root, "title") ?? string.Empty,
                    KanalAdi = ReadString(root, "channel") ?? ReadString(root, "uploader") ?? string.Empty,
                    KanalId = ReadString(root, "channel_id") ?? ReadString(root, "uploader_id"),
                    ThumbnailUrl = ExtractYtDlpThumbnail(root, videoId),
                    SureSaniye = durationSeconds,
                    SureMetni = FormatDuration(durationSeconds),
                    YayinTarihi = ExtractYtDlpPublishDate(root),
                    GoruntulenmeSayisi = ReadInt64(root, "view_count"),
                    Aciklama = ReadString(root, "description"),
                    KaynakUrl = $"https://www.youtube.com/watch?v={videoId}"
                });
            }
            catch (JsonException)
            {
                // Bozuk tekil satırı atla
            }
        }

        return results;
    }

    private static YouTubeSearchItemDto ParseDataApiSnippetItem(string videoId, JsonElement itemEl)
    {
        itemEl.TryGetProperty("snippet", out var snippet);
        return new YouTubeSearchItemDto
        {
            VideoId = videoId,
            Baslik = WebUtility.HtmlDecode(ReadString(snippet, "title") ?? string.Empty),
            KanalAdi = WebUtility.HtmlDecode(ReadString(snippet, "channelTitle") ?? string.Empty),
            KanalId = ReadString(snippet, "channelId"),
            ThumbnailUrl = ExtractDataApiThumbnail(snippet, videoId),
            SureSaniye = null,
            SureMetni = string.Empty,
            YayinTarihi = ReadString(snippet, "publishedAt"),
            GoruntulenmeSayisi = null,
            Aciklama = WebUtility.HtmlDecode(ReadString(snippet, "description") ?? string.Empty),
            KaynakUrl = $"https://www.youtube.com/watch?v={videoId}"
        };
    }

    private static YouTubeSearchItemDto ParseDataApiVideoItem(string videoId, JsonElement vEl)
    {
        vEl.TryGetProperty("snippet", out var snippet);
        vEl.TryGetProperty("contentDetails", out var contentDetails);
        vEl.TryGetProperty("statistics", out var statistics);

        var isoDuration = ReadString(contentDetails, "duration");
        var durationSeconds = ParseIso8601DurationSeconds(isoDuration);

        return new YouTubeSearchItemDto
        {
            VideoId = videoId,
            Baslik = WebUtility.HtmlDecode(ReadString(snippet, "title") ?? string.Empty),
            KanalAdi = WebUtility.HtmlDecode(ReadString(snippet, "channelTitle") ?? string.Empty),
            KanalId = ReadString(snippet, "channelId"),
            ThumbnailUrl = ExtractDataApiThumbnail(snippet, videoId),
            SureSaniye = durationSeconds,
            SureMetni = FormatDuration(durationSeconds),
            YayinTarihi = ReadString(snippet, "publishedAt"),
            GoruntulenmeSayisi = ReadInt64(statistics, "viewCount"),
            Aciklama = WebUtility.HtmlDecode(ReadString(snippet, "description") ?? string.Empty),
            KaynakUrl = $"https://www.youtube.com/watch?v={videoId}"
        };
    }

    private static (bool IsQuotaExceeded, string Reason) AnalyzeYouTubeApiError(HttpStatusCode statusCode, string responseBody)
    {
        var reasons = new List<string>();
        string? topMessage = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("error", out var errorEl))
                {
                    topMessage = ReadString(errorEl, "message");
                    if (errorEl.TryGetProperty("errors", out var errorsArr) && errorsArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var err in errorsArr.EnumerateArray())
                        {
                            var r = ReadString(err, "reason");
                            if (!string.IsNullOrWhiteSpace(r))
                                reasons.Add(r);
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        var combinedReason = reasons.Count > 0 ? string.Join(", ", reasons) : (topMessage ?? string.Empty);
        var isQuota = statusCode == HttpStatusCode.Forbidden &&
                      (reasons.Any(r =>
                           r.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase) ||
                           r.Contains("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
                           r.Contains("rateLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
                           r.Contains("userRateLimitExceeded", StringComparison.OrdinalIgnoreCase)) ||
                       responseBody.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase) ||
                       responseBody.Contains("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase));

        return (isQuota, combinedReason);
    }

    private static List<string> ExtractSubtitleLanguages(JsonElement root, string propertyName, bool filterAutoTranslate)
    {
        var langs = new List<string>();
        if (!root.TryGetProperty(propertyName, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return langs;

        foreach (var prop in obj.EnumerateObject())
        {
            var lang = prop.Name;
            if (string.IsNullOrWhiteSpace(lang) || string.Equals(lang, "live_chat", StringComparison.OrdinalIgnoreCase))
                continue;

            if (filterAutoTranslate && prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0)
            {
                bool hasAnyUrl = false;
                bool hasNonTranslatedTrack = false;
                foreach (var fmt in prop.Value.EnumerateArray())
                {
                    var trackUrl = ReadString(fmt, "url");
                    if (!string.IsNullOrWhiteSpace(trackUrl))
                    {
                        hasAnyUrl = true;
                        if (!trackUrl.Contains("tlang=", StringComparison.OrdinalIgnoreCase))
                        {
                            hasNonTranslatedTrack = true;
                            break;
                        }
                    }
                }

                if (hasAnyUrl && !hasNonTranslatedTrack)
                    continue;
            }

            if (!langs.Contains(lang, StringComparer.OrdinalIgnoreCase))
                langs.Add(lang);
        }

        return langs;
    }

    private static string ExtractDataApiThumbnail(JsonElement snippet, string videoId)
    {
        if (snippet.ValueKind == JsonValueKind.Object &&
            snippet.TryGetProperty("thumbnails", out var thumbs) &&
            thumbs.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "maxres", "standard", "high", "medium", "default" })
            {
                if (thumbs.TryGetProperty(key, out var thumbObj))
                {
                    var url = ReadString(thumbObj, "url");
                    if (!string.IsNullOrWhiteSpace(url))
                        return url;
                }
            }
        }

        return $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg";
    }

    private static string ExtractYtDlpThumbnail(JsonElement root, string videoId)
    {
        var direct = ReadString(root, "thumbnail");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        if (root.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array)
        {
            string? best = null;
            foreach (var t in thumbs.EnumerateArray())
            {
                var url = ReadString(t, "url");
                if (!string.IsNullOrWhiteSpace(url))
                    best = url;
            }
            if (!string.IsNullOrWhiteSpace(best))
                return best;
        }

        return $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg";
    }

    private static string? ExtractYtDlpPublishDate(JsonElement root)
    {
        var uploadDate = ReadString(root, "upload_date");
        if (!string.IsNullOrWhiteSpace(uploadDate) &&
            uploadDate.Length == 8 &&
            DateTime.TryParseExact(uploadDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var timestamp = ReadInt64(root, "timestamp") ?? ReadInt64(root, "release_timestamp");
        if (timestamp is > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return uploadDate;
    }

    private static int? ReadDurationSeconds(JsonElement root)
    {
        if (!root.TryGetProperty("duration", out var durProp))
            return null;

        if (durProp.ValueKind == JsonValueKind.Number)
        {
            if (durProp.TryGetInt32(out var intVal))
                return intVal >= 0 ? intVal : null;
            if (durProp.TryGetDouble(out var dblVal))
                return dblVal >= 0 ? (int)Math.Round(dblVal) : null;
        }
        else if (durProp.ValueKind == JsonValueKind.String &&
                 double.TryParse(durProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed >= 0 ? (int)Math.Round(parsed) : null;
        }

        return null;
    }

    public static int? ParseIso8601DurationSeconds(string? isoDuration)
    {
        if (string.IsNullOrWhiteSpace(isoDuration))
            return null;

        try
        {
            var ts = XmlConvert.ToTimeSpan(isoDuration.Trim());
            return ts.TotalSeconds >= 0 ? (int)Math.Round(ts.TotalSeconds) : null;
        }
        catch
        {
            return null;
        }
    }

    public static string FormatDuration(int? totalSeconds)
    {
        if (totalSeconds is null or <= 0)
            return string.Empty;

        var ts = TimeSpan.FromSeconds(totalSeconds.Value);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";

        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private static string NormalizeDurationFilter(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return "any";

        return duration.Trim().ToLowerInvariant() switch
        {
            "short" => "short",
            "medium" => "medium",
            "long" => "long",
            _ => "any"
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
            return null;

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            _ => null
        };
    }

    private static long? ReadInt64(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
            return null;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var num))
            return num;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var dbl))
            return (long)Math.Round(dbl);

        if (prop.ValueKind == JsonValueKind.String &&
            long.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }

    private string FindRequiredYtDlpPath()
    {
        var path = TryFindYtDlpPath();
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        throw new BusinessException(
            "yt-dlp aracı bulunamadı. Lütfen 'tools/yt-dlp' ikilisini yükleyin veya Ayarlar'dan YouTube Data API v3 anahtarı girin.");
    }

    private string? TryFindYtDlpPath()
    {
        var binaryName = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";

        var candidateDirectories = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools"),
            AppContext.BaseDirectory,
            Path.Combine(Directory.GetCurrentDirectory(), "tools"),
            Directory.GetCurrentDirectory(),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", OperatingSystem.IsWindows() ? "win-x64" : "mac")),
            "/opt/homebrew/bin",
            "/usr/local/bin",
            "/usr/bin",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Links")
        };

        foreach (var dir in candidateDirectories)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var fullPath = Path.Combine(dir, binaryName);
            if (File.Exists(fullPath))
            {
                EnsureUnixExecutable(fullPath);
                _logger.LogDebug("yt-dlp ikilisi bulundu: {Path}", fullPath);
                return fullPath;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var pathDir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fullPath = Path.Combine(pathDir, binaryName);
                if (File.Exists(fullPath))
                {
                    EnsureUnixExecutable(fullPath);
                    _logger.LogDebug("yt-dlp ikilisi PATH üzerinde bulundu: {Path}", fullPath);
                    return fullPath;
                }
            }
        }

        return null;
    }

    private static void EnsureUnixExecutable(string filePath)
    {
        try
        {
            if (!OperatingSystem.IsWindows() && File.Exists(filePath))
            {
                var mode = File.GetUnixFileMode(filePath);
                var execFlags = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                if ((mode & execFlags) == 0)
                {
                    File.SetUnixFileMode(filePath, mode | UnixFileMode.UserRead | UnixFileMode.UserWrite | execFlags);
                }
            }
        }
        catch
        {
            // Sistem dizinlerinde (/opt/homebrew/bin vb.) yetki gerekirse sessizce geç
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunYtDlpAsync(
        string ytDlpPath,
        IEnumerable<string> arguments,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(45));

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return (process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            throw;
        }
    }
}
