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

public class OpenAIWhisperProvider : ISttProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIWhisperProvider> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAIWhisperProvider(HttpClient httpClient, IConfiguration configuration, ILogger<OpenAIWhisperProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        _apiKey = configuration["OpenAI:ApiKey"] ?? configuration["OPENAI_API_KEY"] ?? string.Empty;
        _model = configuration["OpenAI:WhisperModel"] ?? "whisper-1";
        
        _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }
    }

    public async Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("OpenAI ApiKey is not configured. Returning mock transcription for development/testing.");
            return "Bu eğitim videosunda temel kavramlar, metodoloji ve uygulama örnekleri ele alınmaktadır. İlgili konularda detaylı analizler yapılmış ve pratik bilgiler sunulmuştur.";
        }

        _logger.LogInformation("Sending audio stream to OpenAI Whisper for transcription. File: {FileName}", fileName);

        using var requestContent = new MultipartFormDataContent();
        
        // Add model
        requestContent.Add(new StringContent(_model), "model");
        
        // Add file
        var fileContent = new StreamContent(audioStream);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/mpeg"); // assuming mp3
        requestContent.Add(fileContent, "file", fileName);
        
        // Optional: response format
        requestContent.Add(new StringContent("json"), "response_format");

        var response = await _httpClient.PostAsync("audio/transcriptions", requestContent, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("OpenAI Whisper API returned {StatusCode}. Error: {ErrorBody}", response.StatusCode, errorBody);
            throw new Exception($"OpenAI Whisper API failed with status code {response.StatusCode}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        
        using var jsonDoc = JsonDocument.Parse(responseBody);
        if (jsonDoc.RootElement.TryGetProperty("text", out var textElement))
        {
            return textElement.GetString() ?? string.Empty;
        }

        _logger.LogWarning("OpenAI Whisper API response did not contain 'text' property. Response: {ResponseBody}", responseBody);
        return string.Empty;
    }
}
