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

    public Action<int, int>? OnProgress { get; set; }

    public CompositeSttProvider(
        GroqWhisperSttProvider groqProvider,
        LocalFasterWhisperSttProvider localWhisperProvider,
        GeminiAudioSttProvider geminiProvider,
        IConfiguration configuration,
        ILogger<CompositeSttProvider> logger)
    {
        _groqProvider = groqProvider;
        _localWhisperProvider = localWhisperProvider;
        _geminiProvider = geminiProvider;
        _configuration = configuration;
        _logger = logger;
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

        var providerPref = _configuration["STT_PROVIDER"] ?? _configuration["Stt:Provider"] ?? "Gemini";

        // 1. Direct Gemini Mode (Default: Maximum speed, parallel cloud chunks, no hourly audio limit)
        if (providerPref.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Transcribing via Gemini Parallel STT Provider (Direct Gemini Mode)...");
            return await _geminiProvider.TranscribeAsync(memoryStream, fileName, progressCallback, cancellationToken);
        }

        // 2. Groq-first mode (Auto): Try Groq ultra-fast LPU; if rate limited, fallback IMMEDIATELY to Gemini
        if (providerPref.Equals("Groq", StringComparison.OrdinalIgnoreCase) || providerPref.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            var groqKey = _configuration["GROQ_API_KEY"] ?? _configuration["Groq:ApiKey"];
            if (!string.IsNullOrWhiteSpace(groqKey))
            {
                try
                {
                    _logger.LogInformation("Attempting transcription via Groq Whisper API...");
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
                    _logger.LogWarning(ex, "Groq Whisper STT unavailable or rate limited. Falling back immediately to Gemini Parallel STT...");
                }

                memoryStream.Position = 0;
            }

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
