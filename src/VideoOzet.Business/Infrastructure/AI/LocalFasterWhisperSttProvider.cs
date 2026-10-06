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
        try
        {
            var baseUrl = _configuration["LocalWhisper:BaseUrl"] ?? "http://127.0.0.1:5005";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var resp = await _httpClient.GetAsync($"{baseUrl}/health", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
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
                beam_size = 5
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            // Large audio transcription can take up to several minutes
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(10));

            var response = await _httpClient.PostAsync($"{baseUrl}/transcribe", jsonContent, cts.Token);
            response.EnsureSuccessStatusCode();

            progressCallback?.Invoke(3, 4);
            var responseJson = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(responseJson);

            if (doc.RootElement.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
            {
                var text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
                var duration = doc.RootElement.TryGetProperty("duration", out var durProp) ? durProp.GetDouble() : 0;
                var elapsed = doc.RootElement.TryGetProperty("elapsed_seconds", out var elProp) ? elProp.GetDouble() : 0;
                var device = doc.RootElement.TryGetProperty("device", out var devProp) ? devProp.GetString() : "unknown";

                _logger.LogInformation(
                    "Local Faster-Whisper succeeded: {Length} chars transcribed on [{Device}] in {Elapsed:F1}s (audio duration: {Duration:F1}s).",
                    text.Length, device, elapsed, duration
                );

                progressCallback?.Invoke(4, 4);
                return text;
            }

            var errMsg = doc.RootElement.TryGetProperty("error", out var errProp) ? errProp.GetString() : "Unknown error";
            throw new InvalidOperationException($"Local Faster-Whisper reported error: {errMsg}");
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
