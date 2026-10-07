using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.AI;

public class LocalFasterWhisperSttProvider : ISttProvider, ISupportsProgress
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalFasterWhisperSttProvider> _logger;

    public Action<int, int>? OnProgress { get; set; }

    public LocalFasterWhisperSttProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<LocalFasterWhisperSttProvider> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = _configuration["LocalWhisper:BaseUrl"] ?? "http://127.0.0.1:5005";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var resp = await _httpClient.GetAsync($"{baseUrl}/health", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Local Faster-Whisper health check failed ({Message}) at {BaseUrl}", ex.Message, baseUrl);
            return false;
        }
    }

    public Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        return TranscribeAsync(audioStream, fileName, OnProgress, cancellationToken);
    }

    public async Task<string> TranscribeAsync(
        Stream audioStream,
        string fileName,
        Action<int, int>? onProgress,
        CancellationToken cancellationToken = default)
    {
        var progressCallback = onProgress ?? OnProgress;
        var baseUrl = _configuration["LocalWhisper:BaseUrl"] ?? "http://127.0.0.1:5005";

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".wav";
        var tempFile = Path.Combine(Path.GetTempPath(), $"whisper_local_{Guid.NewGuid():N}{ext}");

        try
        {
            progressCallback?.Invoke(1, 4);
            _logger.LogInformation("Saving audio stream to temp file for local Whisper: {TempFile}", tempFile);
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await audioStream.CopyToAsync(fs, cancellationToken);
            }

            progressCallback?.Invoke(2, 4);
            _logger.LogInformation("Sending transcription request to local Faster-Whisper service at {BaseUrl}...", baseUrl);

            var payload = new
            {
                file_path = tempFile,
                language = "tr",
                beam_size = 1,
                stream = true
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(30));

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/transcribe")
            {
                Content = jsonContent
            };

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            using var responseStream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(responseStream, Encoding.UTF8);

            string? finalText = null;
            double finalDuration = 0;
            double finalElapsed = 0;
            string finalDevice = "unknown";

            string? line;
            while ((line = await reader.ReadLineAsync(cts.Token)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("type", out var typeProp))
                    {
                        var eventType = typeProp.GetString();
                        if (eventType == "progress")
                        {
                            var curSec = root.GetProperty("current_sec").GetInt32();
                            var totSec = root.GetProperty("total_sec").GetInt32();
                            progressCallback?.Invoke(curSec, totSec);
                        }
                        else if (eventType == "done")
                        {
                            if (root.TryGetProperty("success", out var succ) && succ.GetBoolean())
                            {
                                finalText = root.GetProperty("text").GetString() ?? string.Empty;
                                finalDuration = root.TryGetProperty("duration", out var durProp) ? durProp.GetDouble() : 0;
                                finalElapsed = root.TryGetProperty("elapsed_seconds", out var elProp) ? elProp.GetDouble() : 0;
                                finalDevice = root.TryGetProperty("device", out var devProp) ? devProp.GetString() ?? "unknown" : "unknown";
                            }
                            else
                            {
                                var errMsg = root.TryGetProperty("error", out var errProp) ? errProp.GetString() : "Unknown error";
                                throw new InvalidOperationException($"Local Faster-Whisper reported error: {errMsg}");
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed partial lines
                }
            }

            if (finalText != null)
            {
                _logger.LogInformation(
                    "Local Faster-Whisper succeeded: {Length} chars transcribed on [{Device}] in {Elapsed:F1}s (audio duration: {Duration:F1}s).",
                    finalText.Length, finalDevice, finalElapsed, finalDuration
                );

                progressCallback?.Invoke((int)Math.Round(finalDuration), (int)Math.Round(finalDuration));
                return finalText;
            }

            throw new InvalidOperationException("Local Faster-Whisper closed connection without a completed transcription result.");
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temporary audio file: {TempFile}", tempFile);
            }
        }
    }
}
