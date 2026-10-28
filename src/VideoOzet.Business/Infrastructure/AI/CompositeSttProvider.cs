using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.AI;

public class CompositeSttProvider : ISttProvider, ISupportsProgress
{
    private readonly GroqWhisperSttProvider _groqProvider;
    private readonly LocalFasterWhisperSttProvider _localWhisperProvider;
    private readonly GeminiAudioSttProvider _geminiProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CompositeSttProvider> _logger;
    private readonly IAiProviderSettingsService? _settingsService;

    public Action<int, int>? OnProgress { get; set; }

    public CompositeSttProvider(
        GroqWhisperSttProvider groqProvider,
        LocalFasterWhisperSttProvider localWhisperProvider,
        GeminiAudioSttProvider geminiProvider,
        IConfiguration configuration,
        ILogger<CompositeSttProvider> logger,
        IAiProviderSettingsService? settingsService = null)
    {
        _groqProvider = groqProvider;
        _localWhisperProvider = localWhisperProvider;
        _geminiProvider = geminiProvider;
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
        // Read into memory stream so that multiple providers can read it if fallback is needed
        MemoryStream memoryStream;
        if (audioStream is MemoryStream ms)
        {
            memoryStream = ms;
            memoryStream.Position = 0;
        }
        else
        {
            memoryStream = new MemoryStream();
            await audioStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
        }

        var providerPref = _configuration["STT_PROVIDER"] ?? _configuration["Stt:Provider"] ?? "Auto";

        string? groqKey = null;
        if (_settingsService != null)
        {
            try
            {
                var settings = await _settingsService.GetSettingsAsync(cancellationToken);
                groqKey = settings?.GroqApiKey;
            }
            catch { }
        }
        if (string.IsNullOrWhiteSpace(groqKey))
        {
            groqKey = _configuration["GROQ_API_KEY"] ?? _configuration["Groq:ApiKey"];
        }

        // 1. Groq-first mode: Eğer arayüzden Groq API Anahtarı girilmişse öncelikle Groq Whisper kullan
        if (!providerPref.Equals("Local", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(groqKey))
        {
            try
            {
                _logger.LogInformation("Attempting primary transcription via Groq Cloud Whisper API (whisper-large-v3-turbo)...");
                progressCallback?.Invoke(1, 2);
                var result = await _groqProvider.TranscribeAsync(memoryStream, fileName, cancellationToken);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    _logger.LogInformation("Transcription succeeded via Groq Whisper API ({Length} chars).", result.Length);
                    progressCallback?.Invoke(2, 2);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Groq Whisper STT unavailable or rate limited. Falling back to secondary STT providers...");
            }

            memoryStream.Position = 0;

            try
            {
                if (await _localWhisperProvider.IsAvailableAsync(cancellationToken))
                {
                    _logger.LogInformation("Attempting transcription via Local Faster-Whisper service...");
                    var localRes = await _localWhisperProvider.TranscribeAsync(memoryStream, fileName, progressCallback, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(localRes))
                    {
                        _logger.LogInformation("Transcription succeeded via Local Faster-Whisper ({Length} chars).", localRes.Length);
                        return localRes;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local Faster-Whisper unavailable. Falling back to Gemini Parallel STT...");
            }

            memoryStream.Position = 0;
            _logger.LogInformation("Transcribing via Gemini Parallel STT Provider...");
            return await _geminiProvider.TranscribeAsync(memoryStream, fileName, progressCallback, cancellationToken);
        }

        // 3. Local Whisper mode (Only if explicitly requested)
        if (providerPref.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (await _localWhisperProvider.IsAvailableAsync(cancellationToken))
                {
                    _logger.LogInformation("Attempting transcription via Local Faster-Whisper service...");
                    var result = await _localWhisperProvider.TranscribeAsync(memoryStream, fileName, progressCallback, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        _logger.LogInformation("Transcription succeeded via Local Faster-Whisper ({Length} chars).", result.Length);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local Faster-Whisper failed. Falling back to Gemini Parallel STT.");
            }

            memoryStream.Position = 0;
        }

        _logger.LogInformation("Transcribing via Gemini Parallel STT Provider (Final Fallback)...");
        return await _geminiProvider.TranscribeAsync(memoryStream, fileName, progressCallback, cancellationToken);
    }
}
