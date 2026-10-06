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
    private readonly GeminiAudioSttProvider _geminiProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CompositeSttProvider> _logger;

    public Action<int, int>? OnProgress { get; set; }

    public CompositeSttProvider(
        GroqWhisperSttProvider groqProvider,
        GeminiAudioSttProvider geminiProvider,
        IConfiguration configuration,
        ILogger<CompositeSttProvider> logger)
    {
        _groqProvider = groqProvider;
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
                _logger.LogWarning(ex, "Groq Whisper STT failed. Falling back to Gemini Parallel STT.");
            }

            memoryStream.Position = 0;
        }

        _logger.LogInformation("Transcribing via Gemini Parallel STT Provider...");
        return await _geminiProvider.TranscribeAsync(memoryStream, fileName, progressCallback, cancellationToken);
    }
}
