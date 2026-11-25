using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Infrastructure.Media;

public class YtDlpMediaService : IRemoteMediaService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YtDlpMediaService> _logger;
    private string? _ytDlpPath;

    private static readonly Regex ProgressRegex = new(@"\[download\]\s+(\d+(?:\.\d+)?)%", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex YouTubeVideoIdRegex = new(@"^[a-zA-Z0-9_-]{11}$", RegexOptions.Compiled);
    private static readonly Regex VttTimeRegex = new(
        @"(?:(\d{1,2}):)?(\d{2}):(\d{2})[.,](\d{3})\s*-->\s*(?:(\d{1,2}):)?(\d{2}):(\d{2})[.,](\d{3})",
        RegexOptions.Compiled);

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

    private static readonly HashSet<string> DirectMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mp3", ".m4a", ".wav", ".webm", ".mkv", ".mov", ".ogg", ".aac", ".m3u8"
    };

    private static readonly string[] DrmErrorMarkers =
    {
        "drm protected",
        "this video is drm",
        "widevine",
        "fairplay",
        "playready",
        "encrypted with",
        "sample-aes",
        "cenc",
        "edef8ba9-79d6-4ace-a3c8-27dcd51d21ed",
        "skd://"
    };

    public YtDlpMediaService(IHttpClientFactory httpClientFactory, ILogger<YtDlpMediaService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _ytDlpPath = DiscoverYtDlpBinary();
    }

    public bool IsYtDlpAvailable() => !string.IsNullOrWhiteSpace(GetYtDlpPath());

    private string? GetYtDlpPath()
    {
        if (!string.IsNullOrWhiteSpace(_ytDlpPath) && File.Exists(_ytDlpPath))
        {
            return _ytDlpPath;
        }

        _ytDlpPath = DiscoverYtDlpBinary();
        return _ytDlpPath;
    }

    private string? DiscoverYtDlpBinary()
    {
        var binaryName = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";

        var candidateDirs = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "tools"),
            AppContext.BaseDirectory,
            Path.Combine(Directory.GetCurrentDirectory(), "tools"),
            Directory.GetCurrentDirectory(),
            "/opt/homebrew/bin",
            "/usr/local/bin",
            "/usr/bin",
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools")),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Links")
        };

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            candidateDirs.AddRange(pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var dir in candidateDirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var fullPath = Path.Combine(dir.Trim(), binaryName);
                if (File.Exists(fullPath))
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        EnsureUnixExecutable(fullPath);
                    }
                    _logger.LogInformation("Configured yt-dlp binary at {Path}", fullPath);
                    return fullPath;
                }
            }
            catch
            {
                // Geçersiz path girdisini yoksay
            }
        }

        _logger.LogWarning("yt-dlp binary not found in standard paths.");
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

    private string RequireYtDlpPath()
    {
        var path = GetYtDlpPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        throw new BusinessException("yt-dlp ikili dosyası bulunamadı. Lütfen tools/yt-dlp kurulumunu kontrol edin.");
    }

    public async Task<RemoteVideoMetadataDto> GetMetadataAsync(string url, CancellationToken ct = default)
    {
        url = ValidateHttpUrl(url);

        if (!IsYouTubeUrl(url) && IsDirectMediaFileUrl(url))
        {
            if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                await CheckManifestForDrmAsync(url, null, ct);
            }

            return BuildDirectUrlMetadata(url, null, null, null, null);
        }

        var exe = RequireYtDlpPath();
        var args = new[] { "--dump-single-json", "--no-playlist", "--no-warnings", "--", url };
        var (exitCode, stdout, stderr) = await RunYtDlpProcessAsync(exe, args, null, ct);

        ThrowIfDrmDetected(stderr, stdout);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            throw new BusinessException($"Video meta verisi alınamadı: {ExtractYtDlpError(stderr)}");
        }

        return ParseSingleMetadataJson(stdout, url);
    }

    public async Task<IReadOnlyList<RemoteVideoMetadataDto>> ExpandPlaylistOrManifestAsync(string url, string? bearerToken, CancellationToken ct = default)
    {
        url = ValidateHttpUrl(url);
        bearerToken = ValidateAndNormalizeBearerToken(bearerToken);

        if (IsYouTubeUrl(url))
        {
            if (IsYouTubePlaylistUrl(url))
            {
                var exe = RequireYtDlpPath();
                var args = new[] { "--flat-playlist", "--dump-json", "--no-warnings", "--", url };
                var (exitCode, stdout, stderr) = await RunYtDlpProcessAsync(exe, args, null, ct);

                ThrowIfDrmDetected(stderr, stdout);

                if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                {
                    throw new BusinessException($"YouTube oynatma listesi okunamadı: {ExtractYtDlpError(stderr)}");
                }

                var list = ParseFlatPlaylistNdjson(stdout);
                if (list.Count == 0)
                {
                    throw new BusinessException("Oynatma listesinde işlenebilir video bulunamadı.");
                }
                return list;
            }

            var single = await GetMetadataAsync(url, ct);
            return new[] { single };
        }

        // Direct media extension (.mp4, .mp3, .m3u8, vb.)
        if (IsDirectMediaFileUrl(url))
        {
            if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                await CheckManifestForDrmAsync(url, bearerToken, ct);
            }
            return new[] { BuildDirectUrlMetadata(url, null, null, null, null) };
        }

        // Probe HTTP endpoint for JSON course manifest or media stream
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyBearerToken(req, bearerToken);

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            if ((int)resp.StatusCode == 401 || (int)resp.StatusCode == 403)
                throw new BusinessException($"Yetkilendirme hatası ({(int)resp.StatusCode}): Lütfen geçerli bir Bearer Token girin.");

            throw new BusinessException($"Kaynak URL okunamadı (HTTP {(int)resp.StatusCode}).");
        }

        var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { BuildDirectUrlMetadata(url, null, null, null, null) };
        }

        var body = await resp.Content.ReadAsStringAsync(ct);

        if (contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ||
            body.TrimStart().StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
        {
            ThrowIfHlsManifestHasDrm(body);
            return new[] { BuildDirectUrlMetadata(url, null, null, null, null) };
        }

        if (contentType.Contains("dash+xml", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("<MPD", StringComparison.OrdinalIgnoreCase))
        {
            ThrowIfDashManifestHasDrm(body);
            return new[] { BuildDirectUrlMetadata(url, null, null, null, null) };
        }

        var trimmed = body.TrimStart();
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            var manifestItems = ParseCourseManifestJson(trimmed, url);
            if (manifestItems.Count > 0)
                return manifestItems;
            throw new BusinessException("JSON manifesti geçerli video öğesi içermiyor.");
        }

        // Fallback: try yt-dlp for other supported public sites
        if (IsYtDlpAvailable())
        {
            var single = await GetMetadataAsync(url, ct);
            return new[] { single };
        }

        throw new BusinessException("URL tanınamadı. Doğrudan medya bağlantısı (.mp4/.mp3/.m3u8) veya JSON ders manifesti girin.");
    }

    public async Task<RemoteCaptionResult?> TryFetchCaptionsAsync(string url, string preferredLang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        url = ValidateHttpUrl(url);

        if (!IsYtDlpAvailable())
            return null;

        var exe = RequireYtDlpPath();
        var tempDir = Path.Combine(Path.GetTempPath(), "VideoOzet", "caps_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var langs = BuildOrderedLanguageList(preferredLang);
            var langArg = string.Join(",", langs);
            var outTemplate = Path.Combine(tempDir, "sub");
            var args = new[]
            {
                "--skip-download",
                "--write-subs",
                "--write-auto-subs",
                "--write-info-json",
                "--sub-format", "json3/vtt",
                "--sub-langs", langArg,
                "--retries", "3",
                "--socket-timeout", "15",
                "--no-playlist",
                "--no-warnings",
                "-o", outTemplate,
                "--",
                url
            };

            var (_, stdout, stderr) = await RunYtDlpProcessAsync(exe, args, null, ct);
            ThrowIfDrmDetected(stderr, stdout);

            var infoJsonPath = Directory.GetFiles(tempDir, "*.info.json").FirstOrDefault();
            int? durationSeconds = null;
            string? videoOriginalLang = null;
            var manualLangs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(infoJsonPath) && File.Exists(infoJsonPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(infoJsonPath, ct));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("duration", out var durProp) && durProp.TryGetDouble(out var dur))
                    {
                        durationSeconds = (int)Math.Round(dur);
                    }
                    if (root.TryGetProperty("language", out var videoLangProp) && videoLangProp.ValueKind == JsonValueKind.String)
                    {
                        videoOriginalLang = NormalizeLanguageCode(videoLangProp.GetString());
                    }
                    if (root.TryGetProperty("subtitles", out var subsProp) && subsProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in subsProp.EnumerateObject())
                        {
                            manualLangs.Add(prop.Name);
                        }
                    }
                }
                catch
                {
                    // info.json parse hatası kritik değil
                }
            }

            var subFiles = Directory.GetFiles(tempDir, "sub*")
                .Where(f => f.EndsWith(".json3", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (subFiles.Count == 0)
            {
                return null;
            }

            // Pick best subtitle file according to language priority, preferring json3 over vtt and manual over auto
            string? selectedFile = null;
            string selectedLang = langs[0];

            foreach (var lang in langs)
            {
                var match = subFiles
                    .Where(f => ExtractLangFromSubFilename(f).Equals(lang, StringComparison.OrdinalIgnoreCase) ||
                                ExtractLangFromSubFilename(f).StartsWith(lang + "-", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.EndsWith(".json3", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (match != null)
                {
                    selectedFile = match;
                    selectedLang = ExtractLangFromSubFilename(match);
                    break;
                }
            }

            selectedFile ??= subFiles
                .OrderByDescending(f => f.EndsWith(".json3", StringComparison.OrdinalIgnoreCase))
                .First();

            if (string.IsNullOrWhiteSpace(selectedLang))
            {
                selectedLang = ExtractLangFromSubFilename(selectedFile);
            }

            if (ShouldRejectAutoTranslatedCaption(videoOriginalLang, preferredLang, selectedLang))
            {
                _logger.LogWarning(
                    "Videonun orijinal dili ({OriginalLang}) tercih edilen dille ({PreferredLang}) aynı olmasına rağmen yalnızca farklı dilde ({SelectedLang}) altyazı indirilebildi. Yanlış otomatik çeviri yerine ses/STT fallback'ine geçiliyor.",
                    videoOriginalLang, preferredLang, selectedLang);
                return null;
            }

            bool isManual = manualLangs.Contains(selectedLang) ||
                            manualLangs.Any(ml => selectedLang.StartsWith(ml, StringComparison.OrdinalIgnoreCase) || ml.StartsWith(selectedLang, StringComparison.OrdinalIgnoreCase));

            var rawContent = await File.ReadAllTextAsync(selectedFile, ct);
            (string hamMetin, string segmentsJson) parsed;
            try
            {
                parsed = selectedFile.EndsWith(".json3", StringComparison.OrdinalIgnoreCase)
                    ? ParseJson3(rawContent)
                    : ParseVtt(rawContent);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Bozuk altyazı JSON içeriği atlandı: {File}", selectedFile);
                return null;
            }

            var (hamMetin, segmentsJson) = parsed;

            if (string.IsNullOrWhiteSpace(hamMetin))
            {
                return null;
            }

            var shortLang = NormalizeLanguageCode(selectedLang);
            if (string.IsNullOrWhiteSpace(shortLang)) shortLang = "tr";

            return new RemoteCaptionResult
            {
                HamMetin = hamMetin,
                ZamanDamgalariJson = segmentsJson,
                Dil = shortLang,
                IsManual = isManual,
                SureSaniye = durationSeconds
            };
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    public async Task<RemoteCaptionResult?> TryFetchTranscriptFromUrlAsync(string transcriptUrl, string? bearerToken, string preferredLang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transcriptUrl))
            return null;

        transcriptUrl = ValidateHttpUrl(transcriptUrl);
        bearerToken = ValidateAndNormalizeBearerToken(bearerToken);

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var req = new HttpRequestMessage(HttpMethod.Get, transcriptUrl);
        ApplyBearerToken(req, bearerToken);

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return null;

        var content = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var trimmed = content.TrimStart();
        string hamMetin;
        string segmentsJson = "[]";

        try
        {
            if (trimmed.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            {
                (hamMetin, segmentsJson) = ParseVtt(content);
            }
            else if (trimmed.StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("events", out _))
                {
                    (hamMetin, segmentsJson) = ParseJson3(trimmed);
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                         TryGetStringProperty(doc.RootElement, out var txt, "hamMetin", "text", "transcript"))
                {
                    hamMetin = txt!;
                }
                else
                {
                    hamMetin = string.Empty;
                }
            }
            else if (trimmed.StartsWith('['))
            {
                (hamMetin, segmentsJson) = ParseSegmentArrayJson(trimmed);
            }
            else
            {
                hamMetin = content.Trim();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Uzak transkript URL'sinden bozuk JSON alındı: {Url}", transcriptUrl);
            return null;
        }

        if (string.IsNullOrWhiteSpace(hamMetin))
            return null;

        return new RemoteCaptionResult
        {
            HamMetin = hamMetin,
            ZamanDamgalariJson = segmentsJson,
            Dil = string.IsNullOrWhiteSpace(preferredLang) ? "tr" : preferredLang.Trim().ToLowerInvariant(),
            IsManual = true
        };
    }

    public async Task<string> DownloadAudioAsync(
        string url,
        string outputDir,
        string? bearerToken,
        Action<int, string>? onProgress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new BusinessException("İndirilecek medya URL'si boş olamaz.");

        url = ValidateHttpUrl(url);
        var cleanToken = ValidateAndNormalizeBearerToken(bearerToken);

        Directory.CreateDirectory(outputDir);

        // Direct HTTP download for plain media files (.mp4, .mp3, .m4a, .wav, .webm, etc. — excluding .m3u8)
        if (!IsYouTubeUrl(url) && IsDirectHttpDownloadableFile(url))
        {
            return await DownloadDirectMediaHttpAsync(url, outputDir, cleanToken, onProgress, ct);
        }

        if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            await CheckManifestForDrmAsync(url, cleanToken, ct);
        }

        var exe = RequireYtDlpPath();
        var outTemplate = Path.Combine(outputDir, "audio.%(ext)s");
        var args = new List<string>
        {
            "-f", "bestaudio/best",
            "--no-playlist",
            "--newline",
            "--no-warnings"
        };

        if (!string.IsNullOrEmpty(cleanToken))
        {
            args.Add("--add-header");
            args.Add($"Authorization: Bearer {cleanToken}");
        }

        args.Add("-o");
        args.Add(outTemplate);
        args.Add("--");
        args.Add(url);

        int lastReportedPct = -1;
        DateTime lastReportedAt = DateTime.MinValue;

        var (exitCode, stdout, stderr) = await RunYtDlpProcessAsync(exe, args, line =>
        {
            var m = ProgressRegex.Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            {
                int rounded = Math.Clamp((int)Math.Round(pct), 0, 99);
                var now = DateTime.UtcNow;
                if (rounded > lastReportedPct && (rounded - lastReportedPct >= 5 || (now - lastReportedAt).TotalSeconds >= 2))
                {
                    lastReportedPct = rounded;
                    lastReportedAt = now;
                    onProgress?.Invoke(rounded, $"Ses akışı indiriliyor (%{rounded})...");
                }
            }
        }, ct);

        ThrowIfDrmDetected(stderr, stdout);

        if (exitCode != 0)
        {
            throw new BusinessException($"Medya akışı indirilemedi: {ExtractYtDlpError(stderr)}");
        }

        var downloadedFile = Directory.GetFiles(outputDir)
            .FirstOrDefault(f =>
                !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                !f.EndsWith(".info.json", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(downloadedFile) || !File.Exists(downloadedFile))
        {
            throw new BusinessException("yt-dlp tamamlandı ancak indirilen ses dosyası bulunamadı.");
        }

        onProgress?.Invoke(100, "Ses akışı indirildi, depolamaya aktarılıyor...");
        return downloadedFile;
    }

    private async Task<string> DownloadDirectMediaHttpAsync(
        string url,
        string outputDir,
        string? bearerToken,
        Action<int, string>? onProgress,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(30);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyBearerToken(req, bearerToken);

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            if ((int)resp.StatusCode == 401 || (int)resp.StatusCode == 403)
                throw new BusinessException($"Yetkilendirme hatası ({(int)resp.StatusCode}): Medya bağlantısı için geçerli Bearer Token gerekli.");

            throw new BusinessException($"Medya dosyası indirilemedi (HTTP {(int)resp.StatusCode}).");
        }

        var ext = GetSafeExtensionFromUrl(url, resp.Content.Headers.ContentType?.MediaType);
        var targetPath = Path.Combine(outputDir, $"media{ext}");
        var totalBytes = resp.Content.Headers.ContentLength;

        await using var source = await resp.Content.ReadAsStreamAsync(ct);
        await using var dest = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int lastPct = -1;
        DateTime lastAt = DateTime.MinValue;

        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), ct);
            totalRead += read;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                int pct = Math.Clamp((int)Math.Round((double)totalRead / totalBytes.Value * 100), 0, 99);
                var now = DateTime.UtcNow;
                if (pct > lastPct && (pct - lastPct >= 5 || (now - lastAt).TotalSeconds >= 2))
                {
                    lastPct = pct;
                    lastAt = now;
                    onProgress?.Invoke(pct, $"Medya dosyası indiriliyor (%{pct})...");
                }
            }
        }

        onProgress?.Invoke(100, "Medya dosyası indirildi, depolamaya aktarılıyor...");
        return targetPath;
    }

    private async Task CheckManifestForDrmAsync(string url, string? bearerToken, CancellationToken ct)
    {
        url = ValidateHttpUrl(url);
        bearerToken = ValidateAndNormalizeBearerToken(bearerToken);

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyBearerToken(req, bearerToken);
            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return;

            var body = await resp.Content.ReadAsStringAsync(ct);
            ThrowIfHlsManifestHasDrm(body);
            ThrowIfDashManifestHasDrm(body);
        }
        catch (BusinessException)
        {
            throw;
        }
        catch
        {
            // Ağ zaman aşımı vb. durumlarda yt-dlp denemesine bırak
        }
    }

    public static void ThrowIfHlsManifestHasDrm(string manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest)) return;

        var upper = manifest.ToUpperInvariant();
        if (upper.Contains("SKD://", StringComparison.Ordinal) ||
            ((upper.Contains("#EXT-X-KEY") || upper.Contains("#EXT-X-SESSION-KEY")) &&
             (upper.Contains("SAMPLE-AES") ||
              upper.Contains("COM.APPLE.FPS") ||
              upper.Contains("COM.APPLE.STREAMINGKEYDELIVERY") ||
              upper.Contains("EDEF8BA9-79D6-4ACE-A3C8-27DCD51D21ED") ||
              upper.Contains("9A04F079-9840-4286-AB92-E65BE0885F95") ||
              upper.Contains("WIDEVINE") ||
              upper.Contains("FAIRPLAY") ||
              upper.Contains("PLAYREADY"))))
        {
            throw new DrmProtectedContentException();
        }
    }

    public static void ThrowIfDashManifestHasDrm(string manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest)) return;

        var upper = manifest.ToUpperInvariant();
        if (upper.Contains("CONTENTPROTECTION") &&
            (upper.Contains("CENC") ||
             upper.Contains("EDEF8BA9-79D6-4ACE-A3C8-27DCD51D21ED") ||
             upper.Contains("9A04F079-9840-4286-AB92-E65BE0885F95") ||
             upper.Contains("WIDEVINE") ||
             upper.Contains("PLAYREADY") ||
             upper.Contains("FAIRPLAY")))
        {
            throw new DrmProtectedContentException();
        }
    }

    public static void ThrowIfDrmDetected(string? stderr, string? stdout = null)
    {
        var combined = $"{stderr}\n{stdout}";
        if (string.IsNullOrWhiteSpace(combined)) return;

        var lower = combined.ToLowerInvariant();
        foreach (var marker in DrmErrorMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
            {
                throw new DrmProtectedContentException();
            }
        }
    }

    public static (string HamMetin, string ZamanDamgalariJson) ParseJson3(string json3Content)
    {
        if (string.IsNullOrWhiteSpace(json3Content))
            return (string.Empty, "[]");

        try
        {
            using var doc = JsonDocument.Parse(json3Content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("events", out var eventsProp) ||
                eventsProp.ValueKind != JsonValueKind.Array)
            {
                return (string.Empty, "[]");
            }

            var segments = new List<CaptionSegment>();
            var lastText = string.Empty;

            foreach (var ev in eventsProp.EnumerateArray())
            {
                if (ev.ValueKind != JsonValueKind.Object)
                    continue;

                if (!ev.TryGetProperty("segs", out var segsProp) || segsProp.ValueKind != JsonValueKind.Array)
                    continue;

                var sb = new StringBuilder();
                foreach (var seg in segsProp.EnumerateArray())
                {
                    if (seg.ValueKind != JsonValueKind.Object)
                        continue;

                    if (seg.TryGetProperty("utf8", out var uProp) && uProp.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(uProp.GetString());
                    }
                }

                var text = NormalizeCaptionText(sb.ToString());
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (string.Equals(text, lastText, StringComparison.OrdinalIgnoreCase))
                    continue;

                double startMs = ev.TryGetProperty("tStartMs", out var tProp) && tProp.ValueKind == JsonValueKind.Number && tProp.TryGetDouble(out var ts) ? ts : 0;
                double durMs = ev.TryGetProperty("dDurationMs", out var dProp) && dProp.ValueKind == JsonValueKind.Number && dProp.TryGetDouble(out var ds) ? ds : 2000;

                double start = Math.Round(startMs / 1000.0, 2);
                double end = Math.Round((startMs + Math.Max(durMs, 200)) / 1000.0, 2);

                segments.Add(new CaptionSegment(start, end, text));
                lastText = text;
            }

            var hamMetin = string.Join(" ", segments.Select(s => s.text));
            var segmentsJson = JsonSerializer.Serialize(segments);
            return (hamMetin, segmentsJson);
        }
        catch (JsonException)
        {
            return (string.Empty, "[]");
        }
    }

    public static (string HamMetin, string ZamanDamgalariJson) ParseVtt(string vttContent)
    {
        if (string.IsNullOrWhiteSpace(vttContent))
            return (string.Empty, "[]");

        const int dedupeWindowSize = 8;
        var lines = vttContent.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var segments = new List<CaptionSegment>();
        var recentLines = new Queue<string>(dedupeWindowSize);

        int i = 0;
        while (i < lines.Length)
        {
            var match = VttTimeRegex.Match(lines[i]);
            if (!match.Success)
            {
                i++;
                continue;
            }

            double start = ParseVttTimestamp(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value);
            double end = ParseVttTimestamp(match.Groups[5].Value, match.Groups[6].Value, match.Groups[7].Value, match.Groups[8].Value);

            i++;
            var cueLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            {
                // Strip inline cue tags with empty string so intra-word tags (e.g. mer<00:00:07.500><c>haba</c>) stay intact
                var cleaned = NormalizeCaptionText(HtmlTagRegex.Replace(lines[i], string.Empty));
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    if (!recentLines.Any(r => string.Equals(r, cleaned, StringComparison.OrdinalIgnoreCase)))
                    {
                        cueLines.Add(cleaned);
                        recentLines.Enqueue(cleaned);
                        if (recentLines.Count > dedupeWindowSize)
                        {
                            recentLines.Dequeue();
                        }
                    }
                }
                i++;
            }

            var cueText = NormalizeCaptionText(string.Join(" ", cueLines));
            if (!string.IsNullOrWhiteSpace(cueText))
            {
                segments.Add(new CaptionSegment(Math.Round(start, 2), Math.Round(Math.Max(start, end), 2), cueText));
            }
        }

        var hamMetin = string.Join(" ", segments.Select(s => s.text));
        var segmentsJson = JsonSerializer.Serialize(segments);
        return (hamMetin, segmentsJson);
    }

    public static List<RemoteVideoMetadataDto> ParseCourseManifestJson(string json, string manifestUrl)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new BusinessException("Geçersiz veya bozuk JSON ders manifesti: içerik boş.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new BusinessException("Geçersiz veya bozuk JSON ders manifesti. Lütfen geçerli bir JSON yanıtı sağlayın.", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            JsonElement itemsArray;

            if (root.ValueKind == JsonValueKind.Array)
            {
                itemsArray = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (!TryGetArrayProperty(root, out itemsArray, "videolar", "videos", "items", "lessons", "bolumler"))
                {
                    return new List<RemoteVideoMetadataDto>();
                }
            }
            else
            {
                return new List<RemoteVideoMetadataDto>();
            }

            var result = new List<RemoteVideoMetadataDto>();
            int idx = 1;
            foreach (var item in itemsArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                if (!TryGetStringProperty(item, out var mediaUrl, "mediaUrl", "videoUrl", "url", "kaynakUrl") ||
                    string.IsNullOrWhiteSpace(mediaUrl))
                {
                    continue;
                }

                TryGetStringProperty(item, out var baslik, "baslik", "title", "ad", "name");
                TryGetStringProperty(item, out var disKaynakId, "disKaynakId", "id", "externalId");
                TryGetStringProperty(item, out var transcriptUrl, "transcriptUrl", "captionUrl", "subtitlesUrl");
                TryGetStringProperty(item, out var thumbnailUrl, "thumbnailUrl", "thumbnail", "kapakUrl");
                TryGetStringProperty(item, out var kanalAdi, "kanalAdi", "egitmen", "author", "channel");

                int? sureSaniye = null;
                if (TryGetIntProperty(item, out var dur, "sureSaniye", "durationSeconds", "duration"))
                {
                    sureSaniye = dur;
                }

                var resolvedMediaUrl = ValidateHttpUrl(ResolveRelativeUrl(manifestUrl, mediaUrl));
                var resolvedTranscriptUrl = !string.IsNullOrWhiteSpace(transcriptUrl)
                    ? ValidateHttpUrl(ResolveRelativeUrl(manifestUrl, transcriptUrl))
                    : null;

                result.Add(new RemoteVideoMetadataDto
                {
                    DisKaynakId = string.IsNullOrWhiteSpace(disKaynakId) ? ComputeShortHash(resolvedMediaUrl) : disKaynakId.Trim(),
                    Baslik = string.IsNullOrWhiteSpace(baslik) ? $"Ders {idx}" : baslik.Trim(),
                    KaynakUrl = resolvedMediaUrl,
                    KaynakPlatform = KaynakPlatform.CustomApiManifest,
                    KanalAdi = kanalAdi,
                    ThumbnailUrl = thumbnailUrl,
                    SureSaniye = sureSaniye,
                    TranscriptUrl = resolvedTranscriptUrl,
                    AltyaziMevcutMu = !string.IsNullOrWhiteSpace(resolvedTranscriptUrl)
                });
                idx++;
            }

            return result;
        }
    }

    private static (string HamMetin, string ZamanDamgalariJson) ParseSegmentArrayJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return (string.Empty, "[]");

        var segments = new List<CaptionSegment>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            if (!TryGetStringProperty(item, out var text, "text", "metin") || string.IsNullOrWhiteSpace(text))
                continue;
            double start = item.TryGetProperty("start", out var sProp) && sProp.ValueKind == JsonValueKind.Number && sProp.TryGetDouble(out var s) ? s : 0;
            double end = item.TryGetProperty("end", out var eProp) && eProp.ValueKind == JsonValueKind.Number && eProp.TryGetDouble(out var e) ? e : start + 2;
            segments.Add(new CaptionSegment(start, Math.Max(start, end), NormalizeCaptionText(text)));
        }

        var hamMetin = string.Join(" ", segments.Select(s => s.text));
        return (hamMetin, JsonSerializer.Serialize(segments));
    }

    public static RemoteVideoMetadataDto ParseSingleMetadataJson(string json, string fallbackUrl)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return BuildDirectUrlMetadata(fallbackUrl, null, null, null, null);

        var id = root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String ? idProp.GetString() ?? string.Empty : string.Empty;
        var title = root.TryGetProperty("title", out var tProp) && tProp.ValueKind == JsonValueKind.String ? tProp.GetString() ?? "Uzaktan Video" : "Uzaktan Video";
        var webpageUrl = root.TryGetProperty("webpage_url", out var wProp) && wProp.ValueKind == JsonValueKind.String ? wProp.GetString() ?? fallbackUrl : fallbackUrl;
        var channel = root.TryGetProperty("channel", out var chProp) && chProp.ValueKind == JsonValueKind.String ? chProp.GetString() :
                      (root.TryGetProperty("uploader", out var upProp) && upProp.ValueKind == JsonValueKind.String ? upProp.GetString() : null);
        var thumbnail = root.TryGetProperty("thumbnail", out var thProp) && thProp.ValueKind == JsonValueKind.String ? thProp.GetString() : null;
        int? duration = root.TryGetProperty("duration", out var dProp) && dProp.ValueKind == JsonValueKind.Number && dProp.TryGetDouble(out var dVal)
            ? (int)Math.Round(dVal)
            : null;

        var langs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectSubtitleLanguages(root, "subtitles", filterAutoTranslate: false, langs);
        CollectSubtitleLanguages(root, "automatic_captions", filterAutoTranslate: true, langs);

        var platform = IsYouTubeUrl(webpageUrl) ? KaynakPlatform.YouTube : KaynakPlatform.DirectUrl;
        if (string.IsNullOrWhiteSpace(id))
        {
            id = ExtractYouTubeVideoId(webpageUrl) ?? ComputeShortHash(webpageUrl);
        }

        return new RemoteVideoMetadataDto
        {
            DisKaynakId = id,
            Baslik = title,
            KaynakUrl = webpageUrl,
            KaynakPlatform = platform,
            KanalAdi = channel,
            ThumbnailUrl = thumbnail,
            SureSaniye = duration,
            AltyaziMevcutMu = langs.Count > 0,
            MevcutAltyaziDilleri = langs.Take(15).ToList()
        };
    }

    private static List<RemoteVideoMetadataDto> ParseFlatPlaylistNdjson(string ndjson)
    {
        var list = new List<RemoteVideoMetadataDto>();
        foreach (var rawLine in ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith('{')) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var title = root.TryGetProperty("title", out var tProp) ? tProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.Equals(title, "[Private video]", StringComparison.OrdinalIgnoreCase) || string.Equals(title, "[Deleted video]", StringComparison.OrdinalIgnoreCase))
                    continue;

                var videoUrl = root.TryGetProperty("url", out var uProp) ? uProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(videoUrl) || !videoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    videoUrl = $"https://www.youtube.com/watch?v={id}";
                }

                var channel = root.TryGetProperty("channel", out var cProp) ? cProp.GetString() :
                              (root.TryGetProperty("uploader", out var upProp) ? upProp.GetString() : null);

                string? thumbnail = root.TryGetProperty("thumbnail", out var thProp) ? thProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(thumbnail) && root.TryGetProperty("thumbnails", out var thArr) && thArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var th in thArr.EnumerateArray())
                    {
                        if (th.ValueKind == JsonValueKind.Object &&
                            th.TryGetProperty("url", out var tu) &&
                            tu.ValueKind == JsonValueKind.String)
                        {
                            thumbnail = tu.GetString();
                        }
                    }
                }
                thumbnail ??= $"https://i.ytimg.com/vi/{id}/hqdefault.jpg";

                int? duration = root.TryGetProperty("duration", out var dProp) && dProp.ValueKind == JsonValueKind.Number && dProp.TryGetDouble(out var dVal)
                    ? (int)Math.Round(dVal)
                    : null;

                list.Add(new RemoteVideoMetadataDto
                {
                    DisKaynakId = id,
                    Baslik = string.IsNullOrWhiteSpace(title) ? id : title,
                    KaynakUrl = videoUrl,
                    KaynakPlatform = KaynakPlatform.YouTube,
                    KanalAdi = channel,
                    ThumbnailUrl = thumbnail,
                    SureSaniye = duration
                });
            }
            catch
            {
                // Bozuk satırı atla
            }
        }

        return list;
    }

    private static void CollectSubtitleLanguages(JsonElement root, string propertyName, bool filterAutoTranslate, HashSet<string> langs)
    {
        if (!root.TryGetProperty(propertyName, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return;

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
                    if (fmt.ValueKind == JsonValueKind.Object &&
                        fmt.TryGetProperty("url", out var urlProp) &&
                        urlProp.ValueKind == JsonValueKind.String)
                    {
                        var trackUrl = urlProp.GetString();
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
                }

                if (hasAnyUrl && !hasNonTranslatedTrack)
                    continue;
            }

            langs.Add(lang);
        }
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunYtDlpProcessAsync(
        string exePath,
        IReadOnlyList<string> arguments,
        Action<string>? onStdoutLine,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };

        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stdoutSb.AppendLine(e.Data);
            onStdoutLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stderrSb.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return (process.ExitCode, stdoutSb.ToString(), stderrSb.ToString());
    }

    private static RemoteVideoMetadataDto BuildDirectUrlMetadata(
        string url,
        string? title,
        string? channel,
        string? thumbnail,
        int? duration)
    {
        var fileName = "Uzaktan Medya";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var seg = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(seg))
            {
                fileName = Uri.UnescapeDataString(seg);
            }
        }

        return new RemoteVideoMetadataDto
        {
            DisKaynakId = ComputeShortHash(url),
            Baslik = string.IsNullOrWhiteSpace(title) ? fileName : title,
            KaynakUrl = url,
            KaynakPlatform = KaynakPlatform.DirectUrl,
            KanalAdi = channel,
            ThumbnailUrl = thumbnail,
            SureSaniye = duration
        };
    }

    public static string? ExtractYouTubeVideoId(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!string.IsNullOrEmpty(uri.UserInfo) || !AllowedYouTubeHosts.Contains(uri.Host))
            return null;

        if (string.Equals(uri.Host, "youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var shortPath = uri.AbsolutePath.Trim('/');
            return YouTubeVideoIdRegex.IsMatch(shortPath) ? shortPath : null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 &&
            (string.Equals(segments[0], "shorts", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "embed", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "live", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "v", StringComparison.OrdinalIgnoreCase)))
        {
            return YouTubeVideoIdRegex.IsMatch(segments[1]) ? segments[1] : null;
        }

        if (segments.Length == 0 ||
            (segments.Length == 1 && string.Equals(segments[0], "watch", StringComparison.OrdinalIgnoreCase)))
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in query)
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && string.Equals(kv[0], "v", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv[1]))
                {
                    var candidate = Uri.UnescapeDataString(kv[1]);
                    return YouTubeVideoIdRegex.IsMatch(candidate) ? candidate : null;
                }
            }
        }

        return null;
    }

    public static bool IsYouTubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        return AllowedYouTubeHosts.Contains(uri.Host);
    }

    private static bool IsYouTubePlaylistUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.AbsolutePath.Contains("/playlist", StringComparison.OrdinalIgnoreCase) ||
         uri.Query.Contains("list=", StringComparison.OrdinalIgnoreCase) && !uri.Query.Contains("v=", StringComparison.OrdinalIgnoreCase));

    private static bool IsDirectMediaFileUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var ext = Path.GetExtension(uri.AbsolutePath);
        return !string.IsNullOrWhiteSpace(ext) && DirectMediaExtensions.Contains(ext);
    }

    private static bool IsDirectHttpDownloadableFile(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var ext = Path.GetExtension(uri.AbsolutePath);
        return !string.IsNullOrWhiteSpace(ext) &&
               DirectMediaExtensions.Contains(ext) &&
               !ext.Equals(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSafeExtensionFromUrl(string url, string? contentType)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var ext = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(ext) && DirectMediaExtensions.Contains(ext))
                return ext.ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.Contains("mp3", StringComparison.OrdinalIgnoreCase) || contentType.Contains("mpeg", StringComparison.OrdinalIgnoreCase)) return ".mp3";
            if (contentType.Contains("mp4", StringComparison.OrdinalIgnoreCase)) return ".mp4";
            if (contentType.Contains("webm", StringComparison.OrdinalIgnoreCase)) return ".webm";
            if (contentType.Contains("wav", StringComparison.OrdinalIgnoreCase)) return ".wav";
        }

        return ".mp4";
    }

    public static string NormalizeLanguageCode(string? rawLang)
    {
        if (string.IsNullOrWhiteSpace(rawLang))
            return string.Empty;

        var clean = rawLang.Trim().ToLowerInvariant();
        var parts = clean.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    public static bool ShouldRejectAutoTranslatedCaption(string? videoOriginalLang, string? preferredLang, string? selectedLang)
    {
        var origNorm = NormalizeLanguageCode(videoOriginalLang);
        var prefNorm = NormalizeLanguageCode(preferredLang);
        var selNorm = NormalizeLanguageCode(selectedLang);

        if (string.IsNullOrWhiteSpace(origNorm) || string.IsNullOrWhiteSpace(prefNorm) || string.IsNullOrWhiteSpace(selNorm))
            return false;

        // Video orijinal dili tercih edilen dille aynıyken (ör. Türkçe videoda tercih "tr"),
        // yalnızca farklı dilde otomatik çeviri (ör. "en") indirilebildiyse reddet.
        return string.Equals(origNorm, prefNorm, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(selNorm, origNorm, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> BuildOrderedLanguageList(string? preferredLang)
    {
        var list = new List<string>();
        void AddWithOrig(string? l)
        {
            var norm = NormalizeLanguageCode(l);
            if (string.IsNullOrWhiteSpace(norm)) return;

            var origTag = $"{norm}-orig";
            if (!list.Contains(origTag)) list.Add(origTag);
            if (!list.Contains(norm)) list.Add(norm);
        }

        AddWithOrig(preferredLang);
        AddWithOrig("tr");
        AddWithOrig("en");
        return list;
    }

    private static string ExtractLangFromSubFilename(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var dotIdx = name.LastIndexOf('.');
        return dotIdx >= 0 && dotIdx < name.Length - 1 ? name[(dotIdx + 1)..] : string.Empty;
    }

    private static double ParseVttTimestamp(string hStr, string mStr, string sStr, string msStr)
    {
        int h = string.IsNullOrEmpty(hStr) ? 0 : int.Parse(hStr, CultureInfo.InvariantCulture);
        int m = int.Parse(mStr, CultureInfo.InvariantCulture);
        int s = int.Parse(sStr, CultureInfo.InvariantCulture);
        int ms = int.Parse(msStr, CultureInfo.InvariantCulture);
        return h * 3600 + m * 60 + s + ms / 1000.0;
    }

    private static string NormalizeCaptionText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var decoded = System.Net.WebUtility.HtmlDecode(input);
        return Regex.Replace(decoded.Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
    }

    private static string ResolveRelativeUrl(string baseUrl, string candidate)
    {
        if (Uri.TryCreate(candidate, UriKind.Absolute, out _)) return candidate;
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
            Uri.TryCreate(baseUri, candidate, out var resolved))
        {
            return resolved.ToString();
        }
        return candidate;
    }

    private static string ComputeShortHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes[..12]).ToLowerInvariant();
    }

    public static string ValidateHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new BusinessException("Geçerli bir URL belirtilmelidir.");

        var trimmed = url.Trim();
        if (trimmed.StartsWith('-'))
            throw new BusinessException("Geçersiz URL: '-' ile başlayan değerler kabul edilmez.");

        if (trimmed.Any(ch => char.IsControl(ch) || char.IsWhiteSpace(ch) || ch == '"'))
            throw new BusinessException("Geçersiz URL: boşluk, tırnak veya kontrol karakterleri içeremez.");

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new BusinessException("Yalnızca geçerli http:// veya https:// URL'leri desteklenir.");
        }

        return trimmed;
    }

    public static string? ValidateAndNormalizeBearerToken(string? bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return null;

        foreach (var ch in bearerToken)
        {
            if (ch == '\r' || ch == '\n' || char.IsControl(ch))
            {
                throw new BusinessException("Bearer Token geçersiz kontrol karakterleri (CR/LF) içeremez.");
            }
        }

        var token = bearerToken.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token["Bearer ".Length..].Trim();

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static void ApplyBearerToken(HttpRequestMessage req, string? bearerToken)
    {
        var token = ValidateAndNormalizeBearerToken(bearerToken);
        if (!string.IsNullOrEmpty(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static bool TryGetStringProperty(JsonElement obj, out string? value, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = null;
            return false;
        }
        foreach (var prop in obj.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                {
                    value = prop.Value.GetString();
                    return true;
                }
            }
        }
        value = null;
        return false;
    }

    private static bool TryGetIntProperty(JsonElement obj, out int value, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = 0;
            return false;
        }
        foreach (var prop in obj.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out value))
                        return true;
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var d))
                    {
                        value = (int)Math.Round(d);
                        return true;
                    }
                }
            }
        }
        value = 0;
        return false;
    }

    private static bool TryGetArrayProperty(JsonElement obj, out JsonElement value, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }
        foreach (var prop in obj.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    value = prop.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string ExtractYtDlpError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return "Bilinmeyen yt-dlp hatası.";
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return lines.Count > 0 ? string.Join(" ", lines) : stderr.Trim();
    }

    private sealed record CaptionSegment(double start, double end, string text);
}
