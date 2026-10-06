using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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

    private const string GroqEndpoint = "https://api.groq.com/openai/v1/audio/transcriptions";
    private static readonly string[] CandidateModels = new[] { "whisper-large-v3", "whisper-large-v3-turbo" };

    public GroqWhisperSttProvider(HttpClient httpClient, IConfiguration configuration, ILogger<GroqWhisperSttProvider> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["GROQ_API_KEY"] ?? _configuration["Groq:ApiKey"];
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

        _logger.LogInformation("Uploading {SizeMb:F2} MB to Groq Whisper for file: {FileName}...", audioBytes.Length / (1024.0 * 1024.0), fileName);

        var ext = Path.GetExtension(fileName).ToLowerInvariant().TrimStart('.');
        var mediaType = ext == "wav" ? "audio/wav" : "audio/mpeg";

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
                content.Add(new StringContent("tr"), "language");
                content.Add(new StringContent("json"), "response_format");

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
                if (doc.RootElement.TryGetProperty("text", out var textProp))
                {
                    var transcript = textProp.GetString()?.Trim() ?? string.Empty;
                    _logger.LogInformation("Groq Whisper transcription completed successfully using {Model}. Characters: {Length}", modelName, transcript.Length);
                    return transcript;
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
}
