using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.AI;

public class GroqWhisperSttProvider : ISttProvider
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GroqWhisperSttProvider> _logger;
    private readonly IAiProviderSettingsService? _settingsService;

    private const string GroqEndpoint = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const int MaxDirectAudioBytes = 20 * 1024 * 1024; // 20 MB
    private const int ChunkDurationSeconds = 15 * 60;         // 15 dakika (900 sn)
    private static readonly string[] CandidateModels = new[] { "whisper-large-v3-turbo", "whisper-large-v3" };

    public string? LastDetectedLanguage { get; private set; }
    public string? LastSegmentsJson { get; private set; }

    private sealed record WhisperSegmentItem(
        [property: JsonPropertyName("start")] double Start,
        [property: JsonPropertyName("end")] double End,
        [property: JsonPropertyName("text")] string Text);

    public GroqWhisperSttProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GroqWhisperSttProvider> logger,
        IAiProviderSettingsService? settingsService = null)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _settingsService = settingsService;
    }

    public async Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        LastDetectedLanguage = null;
        LastSegmentsJson = null;

        string? apiKey = null;
        if (_settingsService != null)
        {
            try
            {
                var settings = await _settingsService.GetSettingsAsync(cancellationToken);
                apiKey = settings?.GroqApiKey;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = _configuration["GROQ_API_KEY"] ?? _configuration["Groq:ApiKey"];
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("GROQ_API_KEY is not configured.");
            return string.Empty;
        }

        byte[] audioBytes;
        using (var ms = new MemoryStream())
        {
            await audioStream.CopyToAsync(ms, cancellationToken);
            audioBytes = ms.ToArray();
        }

        if (audioBytes.Length == 0)
        {
            _logger.LogWarning("Audio stream is empty for file: {FileName}", fileName);
            return string.Empty;
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant().TrimStart('.');
        var mediaType = ext == "wav" ? "audio/wav" : "audio/mpeg";

        if (audioBytes.Length > MaxDirectAudioBytes)
        {
            _logger.LogInformation(
                "Audio size is {SizeMb:F2} MB (>20 MB limit) for file: {FileName}. Splitting into 15-minute chunks via FFmpeg...",
                audioBytes.Length / (1024.0 * 1024.0), fileName);

            return await TranscribeLargeAudioInChunksAsync(audioBytes, fileName, ext, mediaType, apiKey, cancellationToken);
        }

        _logger.LogInformation("Uploading {SizeMb:F2} MB to Groq Whisper for file: {FileName}...", audioBytes.Length / (1024.0 * 1024.0), fileName);

        var (transcript, segments, _) = await TranscribeSingleBufferAsync(
            audioBytes,
            string.IsNullOrEmpty(fileName) ? "audio.mp3" : fileName,
            mediaType,
            apiKey,
            offsetSeconds: 0.0,
            cancellationToken);

        LastSegmentsJson = segments.Count > 0 ? JsonSerializer.Serialize(segments) : null;
        return transcript;
    }

    private async Task<string> TranscribeLargeAudioInChunksAsync(
        byte[] audioBytes,
        string fileName,
        string ext,
        string mediaType,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var ffmpegPath = ResolveFfmpegPath();
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            _logger.LogWarning("FFmpeg bulunamadı. Ses dosyası doğrudan Groq Whisper'a gönderiliyor...");
            var (directText, directSegs, _) = await TranscribeSingleBufferAsync(
                audioBytes,
                string.IsNullOrEmpty(fileName) ? "audio.mp3" : fileName,
                mediaType,
                apiKey,
                offsetSeconds: 0.0,
                cancellationToken);
            LastSegmentsJson = directSegs.Count > 0 ? JsonSerializer.Serialize(directSegs) : null;
            return directText;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "VideoOzet", "groq_stt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var safeExt = string.IsNullOrWhiteSpace(ext) ? "mp3" : ext;
        var inputAudioPath = Path.Combine(tempDir, $"input_audio.{safeExt}");
        var chunkPattern = Path.Combine(tempDir, "chunk_%04d.mp3");

        try
        {
            await File.WriteAllBytesAsync(inputAudioPath, audioBytes, cancellationToken);

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-loglevel error -i \"{inputAudioPath}\" -vn -ac 1 -ar 16000 -b:a 64k -f segment -segment_time {ChunkDurationSeconds} \"{chunkPattern}\" -y",
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = psi })
            {
                process.Start();
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var errorOutput = await errorTask;
                if (process.ExitCode != 0)
                {
                    _logger.LogWarning("FFmpeg 15-min segment exited with code {Code}: {Error}", process.ExitCode, errorOutput);
                }
            }

            var chunkFiles = Directory.GetFiles(tempDir, "chunk_*.mp3")
                                      .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                      .ToList();

            if (chunkFiles.Count == 0)
            {
                _logger.LogWarning("FFmpeg parça üretemedi, doğrudan Groq Whisper çağrısı deneniyor.");
                var (fallbackText, fallbackSegs, _) = await TranscribeSingleBufferAsync(
                    audioBytes,
                    string.IsNullOrEmpty(fileName) ? "audio.mp3" : fileName,
                    mediaType,
                    apiKey,
                    offsetSeconds: 0.0,
                    cancellationToken);
                LastSegmentsJson = fallbackSegs.Count > 0 ? JsonSerializer.Serialize(fallbackSegs) : null;
                return fallbackText;
            }

            _logger.LogInformation("FFmpeg split large audio into {Count} 15-minute chunks for sequential Groq Whisper transcription.", chunkFiles.Count);

            var allTranscriptParts = new List<string>(chunkFiles.Count);
            var allSegments = new List<WhisperSegmentItem>();
            double cumulativeOffsetSeconds = 0.0;

            for (int i = 0; i < chunkFiles.Count; i++)
            {
                var chunkFile = chunkFiles[i];
                var chunkBytes = await File.ReadAllBytesAsync(chunkFile, cancellationToken);
                _logger.LogInformation(
                    "Transcribing Groq Whisper chunk {Index}/{Total} ({SizeMb:F2} MB, offset: {Offset:F1}s)...",
                    i + 1, chunkFiles.Count, chunkBytes.Length / (1024.0 * 1024.0), cumulativeOffsetSeconds);

                var (chunkText, chunkSegments, chunkDurationSec) = await TranscribeSingleBufferAsync(
                    chunkBytes,
                    Path.GetFileName(chunkFile),
                    "audio/mpeg",
                    apiKey,
                    cumulativeOffsetSeconds,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    allTranscriptParts.Add(chunkText.Trim());
                }

                if (chunkSegments.Count > 0)
                {
                    allSegments.AddRange(chunkSegments);
                }

                if (chunkDurationSec > 0)
                {
                    cumulativeOffsetSeconds += chunkDurationSec;
                }
                else if (chunkSegments.Count > 0)
                {
                    cumulativeOffsetSeconds = Math.Max(cumulativeOffsetSeconds + ChunkDurationSeconds, chunkSegments[^1].End);
                }
                else
                {
                    cumulativeOffsetSeconds += ChunkDurationSeconds;
                }
            }

            LastSegmentsJson = allSegments.Count > 0 ? JsonSerializer.Serialize(allSegments) : null;
            return string.Join(" ", allTranscriptParts).Trim();
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up Groq Whisper temp chunk directory: {TempDir}", tempDir);
            }
        }
    }

    private async Task<(string Text, List<WhisperSegmentItem> Segments, double DurationSec)> TranscribeSingleBufferAsync(
        byte[] audioBytes,
        string fileName,
        string mediaType,
        string apiKey,
        double offsetSeconds,
        CancellationToken cancellationToken)
    {
        Exception? lastEx = null;

        foreach (var modelName in CandidateModels)
        {
            try
            {
                _logger.LogInformation("Sending audio to Groq Whisper API with model: {Model}", modelName);

                using var content = new MultipartFormDataContent();
                var audioContent = new ByteArrayContent(audioBytes);
                audioContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);

                content.Add(audioContent, "file", string.IsNullOrEmpty(fileName) ? "audio.mp3" : fileName);
                content.Add(new StringContent(modelName), "model");
                content.Add(new StringContent("verbose_json"), "response_format");

                using var request = new HttpRequestMessage(HttpMethod.Post, GroqEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                request.Headers.Add("User-Agent", "VideoOzet-App/1.0");
                request.Content = content;

                var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (responseString.Contains("rate_limit_exceeded") || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("Groq model {Model} hit rate limit. Trying next candidate model if available. Response: {Resp}", modelName, responseString);
                        lastEx = new Exception($"Groq Whisper rate limit on {modelName}: {responseString}");
                        continue;
                    }

                    throw new Exception($"Groq Whisper API error ({response.StatusCode}): {responseString}");
                }

                using var doc = JsonDocument.Parse(responseString);
                if (doc.RootElement.TryGetProperty("language", out var langProp))
                {
                    var detected = langProp.GetString()?.Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(detected))
                    {
                        LastDetectedLanguage = detected;
                    }
                }

                double durationSec = doc.RootElement.TryGetProperty("duration", out var durProp) && durProp.TryGetDouble(out var dVal)
                    ? dVal
                    : 0.0;

                var segments = new List<WhisperSegmentItem>();
                if (doc.RootElement.TryGetProperty("segments", out var segArray) && segArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var seg in segArray.EnumerateArray())
                    {
                        var segText = seg.TryGetProperty("text", out var tProp) ? tProp.GetString()?.Trim() : null;
                        if (string.IsNullOrWhiteSpace(segText))
                            continue;

                        double rawStart = seg.TryGetProperty("start", out var sProp) && sProp.TryGetDouble(out var sv) ? sv : 0.0;
                        double rawEnd = seg.TryGetProperty("end", out var eProp) && eProp.TryGetDouble(out var ev) ? ev : rawStart;

                        double adjStart = Math.Round(Math.Max(0.0, rawStart + offsetSeconds), 2);
                        double adjEnd = Math.Round(Math.Max(adjStart, rawEnd + offsetSeconds), 2);

                        segments.Add(new WhisperSegmentItem(adjStart, adjEnd, segText));
                    }
                }

                if (doc.RootElement.TryGetProperty("text", out var textProp))
                {
                    var transcript = textProp.GetString()?.Trim() ?? string.Empty;
                    _logger.LogInformation(
                        "Groq Whisper transcription completed successfully using {Model} (Language: {Lang}, Segments: {SegCount}). Characters: {Length}",
                        modelName, LastDetectedLanguage ?? "auto", segments.Count, transcript.Length);
                    return (transcript, segments, durationSec);
                }

                throw new Exception($"Unexpected Groq API response: {responseString}");
            }
            catch (Exception ex) when (ex.Message.Contains("rate limit") && modelName != CandidateModels.Last())
            {
                lastEx = ex;
            }
        }

        throw lastEx ?? new Exception("All Groq Whisper models failed.");
    }

    private static string? ResolveFfmpegPath()
    {
        var possiblePaths = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ffmpeg", "bin", "ffmpeg.exe")),
            Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg", "bin", "ffmpeg.exe"),
            @"D:\Hoca\ffmpeg\bin\ffmpeg.exe",
            @"D:\Hoca\VideoOzet\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Links\ffmpeg.exe"),
            "ffmpeg.exe"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
