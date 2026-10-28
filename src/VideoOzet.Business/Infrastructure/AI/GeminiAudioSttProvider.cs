using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.AI;

public class GeminiAudioSttProvider : ISttProvider, ISupportsProgress
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiAudioSttProvider> _logger;
    private readonly IAiProviderSettingsService? _settingsService;
    private static int _currentKeyIndex = 0;

    public Action<int, int>? OnProgress { get; set; }

    private const string ModelName = "gemini-3.8-flash-tiered";
    private const int MaxDirectAudioBytes = 500 * 1024; // 500 KB threshold (base64 ~666 KB < 1MB limit)

    public GeminiAudioSttProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiAudioSttProvider> logger)
        : this(httpClient, configuration, logger, null)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public GeminiAudioSttProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiAudioSttProvider> logger,
        IAiProviderSettingsService? settingsService)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _settingsService = settingsService;
    }

    public Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        return TranscribeAsync(audioStream, fileName, OnProgress, cancellationToken);
    }

    public async Task<string> TranscribeAsync(Stream audioStream, string fileName, Action<int, int>? onProgress, CancellationToken cancellationToken = default)
    {
        var progressCallback = onProgress ?? OnProgress;
        AiProviderSettingsDto? settings = null;
        if (_settingsService != null)
        {
            try
            {
                settings = await _settingsService.GetSettingsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI Provider ayarları STT için okunamadı, varsayılan yapılandırma kullanılacak.");
            }
        }

        bool isOpenRouter = settings != null &&
                            settings.ActiveProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase);
        bool isGroq = settings != null &&
                      settings.ActiveProvider.Equals("groq", StringComparison.OrdinalIgnoreCase);

        var apiKeyStr = isOpenRouter
            ? settings!.OpenRouterApiKey
            : isGroq
                ? settings!.GroqApiKey
                : (settings != null && !string.IsNullOrWhiteSpace(settings.AntigravityProxyKey)
                    ? settings.AntigravityProxyKey
                    : _configuration["GEMINI_API_KEY"]);

        if (string.IsNullOrWhiteSpace(apiKeyStr) && settings != null && !string.IsNullOrWhiteSpace(settings.GroqApiKey))
        {
            apiKeyStr = settings.GroqApiKey;
        }

        if (string.IsNullOrEmpty(apiKeyStr))
        {
            _logger.LogWarning("Aktif sağlayıcı için STT API anahtarı tanımlanmamış.");
            return string.Empty;
        }

        var apiKeys = apiKeyStr.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(k => k.Trim())
                               .ToArray();

        if (apiKeys.Length == 0)
        {
            _logger.LogWarning("No valid Gemini API keys found.");
            return string.Empty;
        }

        _logger.LogInformation("Reading audio stream for transcription. File: {FileName}", fileName);

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
        var format = ext == "wav" ? "wav" : "mp3";

        // If audio is small enough (< 500 KB), transcribe directly in one shot
        if (audioBytes.Length <= MaxDirectAudioBytes)
        {
            progressCallback?.Invoke(1, 1);
            return await TranscribeChunkWithRetryAsync(audioBytes, format, apiKeys, cancellationToken);
        }

        // For larger audio, split into ~60s chunks via FFmpeg and transcribe in parallel
        _logger.LogInformation("Audio size is {SizeMb:F2} MB (exceeds direct limit). Splitting into 60s chunks via FFmpeg...", audioBytes.Length / (1024.0 * 1024.0));
        return await TranscribeChunkedAudioAsync(audioBytes, apiKeys, progressCallback, cancellationToken);
    }

    private async Task<string> TranscribeChunkedAudioAsync(byte[] audioBytes, string[] apiKeys, Action<int, int>? onProgress, CancellationToken cancellationToken)
    {
        var ffmpegPath = ResolveFfmpegPath();
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            _logger.LogWarning("FFmpeg not found. Attempting direct transcription as fallback.");
            return await TranscribeChunkWithRetryAsync(audioBytes, "mp3", apiKeys, cancellationToken);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "VideoOzet", "stt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var inputAudioPath = Path.Combine(tempDir, "input_audio.mp3");
        var chunkPattern = Path.Combine(tempDir, "chunk_%04d.mp3");

        try
        {
            await File.WriteAllBytesAsync(inputAudioPath, audioBytes, cancellationToken);

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-loglevel error -i \"{inputAudioPath}\" -vn -ac 1 -ar 16000 -b:a 32k -f segment -segment_time 60 \"{chunkPattern}\" -y",
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
                    _logger.LogWarning("FFmpeg segment exited with code {Code}: {Error}", process.ExitCode, errorOutput);
                }
            }

            var chunkFiles = Directory.GetFiles(tempDir, "chunk_*.mp3")
                                      .OrderBy(f => f)
                                      .ToList();

            if (chunkFiles.Count == 0)
            {
                _logger.LogWarning("No chunks generated by FFmpeg. Attempting fallback direct transcription.");
                return await TranscribeChunkWithRetryAsync(audioBytes, "mp3", apiKeys, cancellationToken);
            }

            _logger.LogInformation("FFmpeg split audio into {Count} chunks. Transcribing chunks in parallel (max 6 concurrent)...", chunkFiles.Count);

            var transcriptParts = new string[chunkFiles.Count];
            using var semaphore = new SemaphoreSlim(6, 6);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var tasks = new List<Task>();
            var completedChunks = 0;
            Exception? firstFatalError = null;

            for (int i = 0; i < chunkFiles.Count; i++)
            {
                var index = i;
                var chunkFile = chunkFiles[index];

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await semaphore.WaitAsync(linkedCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    try
                    {
                        if (linkedCts.IsCancellationRequested)
                            return;

                        var chunkBytes = await File.ReadAllBytesAsync(chunkFile, linkedCts.Token);
                        _logger.LogInformation("Transcribing chunk {Index}/{Total} ({SizeKb} KB)...", index + 1, chunkFiles.Count, chunkBytes.Length / 1024);

                        var chunkText = await TranscribeChunkWithRetryAsync(chunkBytes, "mp3", apiKeys, linkedCts.Token);
                        if (!string.IsNullOrWhiteSpace(chunkText))
                        {
                            transcriptParts[index] = chunkText.Trim();
                        }
                    }
                    catch (OperationCanceledException) when (firstFatalError != null)
                    {
                        // Cancelled due to fatal error on another chunk
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref firstFatalError, ex, null);
                        _logger.LogWarning(ex, "Failed to transcribe chunk {Index}/{Total}.", index + 1, chunkFiles.Count);

                        // Bağlantı reddedildi veya 401/403 yetki hatası varsa diğer parçaları boşuna deneme, hemen durdur
                        if (ex is HttpRequestException ||
                            ex.Message.Contains("401") ||
                            ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
                            ex.Message.Contains("403") ||
                            ex.Message.Contains("402"))
                        {
                            try { linkedCts.Cancel(); } catch { }
                        }
                    }
                    finally
                    {
                        var done = Interlocked.Increment(ref completedChunks);
                        onProgress?.Invoke(done, chunkFiles.Count);
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);

            var fullTranscript = string.Join(" ", transcriptParts.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
            if (string.IsNullOrWhiteSpace(fullTranscript) && firstFatalError != null)
            {
                throw new Exception($"Ses deşifre (STT) hatası: {firstFatalError.Message}", firstFatalError);
            }

            _logger.LogInformation("Chunked transcription completed. Total chunks: {Total}, Result characters: {Length}", chunkFiles.Count, fullTranscript.Length);
            return fullTranscript;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temp chunk directory: {TempDir}", tempDir);
            }
        }
    }

    private async Task<string> TranscribeChunkWithRetryAsync(byte[] audioBytes, string format, string[] apiKeys, CancellationToken cancellationToken)
    {
        AiProviderSettingsDto? settings = null;
        if (_settingsService != null)
        {
            try
            {
                settings = await _settingsService.GetSettingsAsync(cancellationToken);
            }
            catch { }
        }

        bool isOpenRouter = settings != null &&
                            settings.ActiveProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(settings.OpenRouterApiKey);

        var effectiveModel = isOpenRouter
            ? (!string.IsNullOrWhiteSpace(settings!.OpenRouterModel)
                ? settings.OpenRouterModel.Replace(":batch", "", StringComparison.OrdinalIgnoreCase).TrimStart('~')
                : "google/gemini-3.8-flash")
            : (settings != null && !string.IsNullOrWhiteSpace(settings.AntigravityProxyModel)
                ? settings.AntigravityProxyModel
                : ModelName);

        var baseUrl = isOpenRouter
            ? settings!.OpenRouterBaseUrl.TrimEnd('/')
            : (settings != null && !string.IsNullOrWhiteSpace(settings.AntigravityProxyUrl)
                ? settings.AntigravityProxyUrl.TrimEnd('/')
                : "http://localhost:8045/v1");

        _logger.LogInformation("STT Request -> Provider: {Provider}, Endpoint: {BaseUrl}/chat/completions, Model: {Model}",
            isOpenRouter ? "OpenRouter" : "AntigravityProxy", baseUrl, effectiveModel);

        var audioBase64 = Convert.ToBase64String(audioBytes);
        var promptText = "Aşağıdaki ses kaydını dikkatle dinle ve içindeki tüm konuşmaları eksiksiz, kelimesi kelimesine Türkçe olarak yazıya dök (deşifre et). " +
                         "Sadece ve sadece deşifre edilmiş konuşma metnini döndür. Başka hiçbir açıklama, giriş veya yorum ekleme.";

        var requestBody = new
        {
            model = effectiveModel,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = promptText },
                        new { type = "input_audio", input_audio = new { data = audioBase64, format } }
                    }
                }
            }
        };

        var jsonPayload = JsonSerializer.Serialize(requestBody);
        Exception? lastException = null;

        for (int tryCount = 0; tryCount < apiKeys.Length; tryCount++)
        {
            var keyToUse = apiKeys[_currentKeyIndex % apiKeys.Length];
            try
            {
                var requestUrl = $"{baseUrl}/chat/completions";
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                requestMessage.Headers.Add("Authorization", $"Bearer {keyToUse}");
                if (isOpenRouter)
                {
                    requestMessage.Headers.Add("HTTP-Referer", "https://videoozet.local");
                    requestMessage.Headers.Add("X-Title", "VideoOzet");
                }
                requestMessage.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (responseString.Contains("429") || responseString.Contains("quota") || responseString.Contains("exhausted"))
                    {
                        throw new Exception($"Quota Exceeded: {responseString}");
                    }
                    if (responseString.Contains("503") || responseString.Contains("UNAVAILABLE") || responseString.Contains("high demand"))
                    {
                        throw new Exception($"High Demand: {responseString}");
                    }

                    throw new Exception($"Gemini STT API error: {response.StatusCode} - {responseString}");
                }

                using var jsonDoc = JsonDocument.Parse(responseString);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                    {
                        var text = content.GetString()?.Trim() ?? string.Empty;
                        return text;
                    }
                }

                throw new Exception($"Unexpected Gemini STT API response format: {responseString}");
            }
            catch (Exception ex) when (ex.Message.Contains("Quota Exceeded") || ex.Message.Contains("High Demand") || ex.Message.Contains("exhausted"))
            {
                _logger.LogWarning("API Key hit limit on proxy. Rotating... Error: {Error}", ex.Message);
                Interlocked.Increment(ref _currentKeyIndex);
                lastException = ex;
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning("Primary STT endpoint ({BaseUrl}) failed for chunk ({SizeKb} KB): {Message}. Trying OtoEdit Groq Whisper fallback...", baseUrl, audioBytes.Length / 1024, ex.Message);
                break;
            }
        }

        // Yalnızca arayüzden veya ortam değişkeninden girilmiş bir Groq API anahtarı varsa yedek olarak kullan
        var groqKey = settings != null && !string.IsNullOrWhiteSpace(settings.GroqApiKey)
            ? settings.GroqApiKey
            : (_configuration["GROQ_API_KEY"] ?? _configuration["Groq:ApiKey"]);

        if (!string.IsNullOrWhiteSpace(groqKey))
        {
            foreach (var groqModel in new[] { "whisper-large-v3-turbo", "whisper-large-v3" })
            {
                try
                {
                    using var content = new MultipartFormDataContent();
                    var audioContent = new ByteArrayContent(audioBytes);
                    audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(format == "wav" ? "audio/wav" : "audio/mpeg");
                    content.Add(audioContent, "file", $"chunk.{format}");
                    content.Add(new StringContent(groqModel), "model");
                    content.Add(new StringContent("tr"), "language");
                    content.Add(new StringContent("json"), "response_format");

                    using var groqReq = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/audio/transcriptions");
                    groqReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", groqKey.Trim());
                    groqReq.Headers.Add("User-Agent", "VideoOzet/1.0");
                    groqReq.Content = content;

                    var groqResp = await _httpClient.SendAsync(groqReq, cancellationToken);
                    var groqBody = await groqResp.Content.ReadAsStringAsync(cancellationToken);
                    if (groqResp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(groqBody);
                        if (doc.RootElement.TryGetProperty("text", out var textProp))
                        {
                            return textProp.GetString()?.Trim() ?? string.Empty;
                        }
                    }
                }
                catch (Exception gEx)
                {
                    _logger.LogWarning(gEx, "Groq Whisper fallback chunk error ({Model})", groqModel);
                }
            }
        }

        throw new Exception("Bütün Gemini / Bulut STT anahtarları tükendi veya STT çağrısı başarısız oldu.", lastException);
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
