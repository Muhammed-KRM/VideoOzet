using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VideoOzet.Business.Infrastructure.AI;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using Xunit;

namespace VideoOzet.UnitTests.Business.Services;

public class AiProviderSettingsTests : IDisposable
{
    private readonly string _sharedSettingsFilePath = Path.Combine(AppContext.BaseDirectory, "ai_provider_settings.json");
    private readonly string? _originalSettingsFileBackup;

    public AiProviderSettingsTests()
    {
        if (File.Exists(_sharedSettingsFilePath))
        {
            _originalSettingsFileBackup = File.ReadAllText(_sharedSettingsFilePath);
            File.Delete(_sharedSettingsFilePath);
        }
    }

    public void Dispose()
    {
        try
        {
            if (_originalSettingsFileBackup is not null)
            {
                File.WriteAllText(_sharedSettingsFilePath, _originalSettingsFileBackup);
            }
            else if (File.Exists(_sharedSettingsFilePath))
            {
                File.Delete(_sharedSettingsFilePath);
            }
        }
        catch
        {
            // Test temizliği sırasında dosya kilidi varsa yoksay
        }
    }

    private static (AiProviderSettingsService Service, Dictionary<string, string> Store) CreateSettingsService(
        IDictionary<string, string?>? configOverrides = null)
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        var cacheMock = new Mock<ICacheService>();

        cacheMock.Setup(c => c.GetAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => store.TryGetValue(key, out var val) ? val : null);

        cacheMock.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Callback<string, string, TimeSpan>((key, val, _) => store[key] = val)
            .Returns(Task.CompletedTask);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configOverrides ?? new Dictionary<string, string?>())
            .Build();

        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new System.Net.Http.HttpClient());

        var service = new AiProviderSettingsService(
            cacheMock.Object,
            config,
            httpFactoryMock.Object,
            NullLogger<AiProviderSettingsService>.Instance);

        return (service, store);
    }

    [Fact]
    public async Task DefaultSettings_ShouldHaveAllowFallbackToSecondaryProviderFalse()
    {
        var dto = new AiProviderSettingsDto();
        dto.AllowFallbackToSecondaryProvider.Should().BeFalse();

        var (service, store) = CreateSettingsService();
        // Bellek yedeğinden etkilenmemesi için önbelleğe varsayılan JSON yazıp okuma ve doğrudan varsayılan kontrolü
        store["videoozet:settings:ai_provider"] = """
            {
              "activeProvider": "antigravity",
              "antigravityProxyUrl": "http://localhost:8045/v1",
              "antigravityProxyModel": "gemini-3.8-flash-tiered"
            }
            """;

        var loaded = await service.GetSettingsAsync();
        loaded.AllowFallbackToSecondaryProvider.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAndGetSettings_ShouldSyncAntigravityProxyAndLegacyFrontendFields_Bidirectionally()
    {
        var (service, _) = CreateSettingsService();
        var camelCaseOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // 1. Yön: Frontend eski alan adlarıyla (antigravityVideoPlanModel, antigravitySummaryModel, antigravityQcModel) gönderdiğinde
        var legacyFrontendPayloadJson = """
            {
              "activeProvider": "antigravity",
              "antigravityProxyUrl": "http://localhost:8045/v1",
              "antigravityProxyKey": "sk-local-proxy",
              "antigravityProxyModel": "gemini-3.8-flash-tiered",
              "antigravitySummaryModel": "gemini-2.5-flash",
              "antigravityVideoPlanModel": "argon",
              "antigravityQcModel": "claude-3-7-sonnet",
              "allowFallbackToSecondaryProvider": false
            }
            """;

        var incomingLegacyDto = JsonSerializer.Deserialize<AiProviderSettingsDto>(legacyFrontendPayloadJson, camelCaseOptions)!;
        var savedFromLegacy = await service.UpdateSettingsAsync(incomingLegacyDto);

        savedFromLegacy.AntigravityProxySummaryModel.Should().Be("gemini-2.5-flash");
        savedFromLegacy.AntigravitySummaryModel.Should().Be("gemini-2.5-flash");
        savedFromLegacy.AntigravityProxyVideoPlanModel.Should().Be("argon");
        savedFromLegacy.AntigravityVideoPlanModel.Should().Be("argon");
        savedFromLegacy.AntigravityProxyQcModel.Should().Be("claude-3-7-sonnet");
        savedFromLegacy.AntigravityQcModel.Should().Be("claude-3-7-sonnet");
        savedFromLegacy.AllowFallbackToSecondaryProvider.Should().BeFalse();

        var reloaded1 = await service.GetSettingsAsync();
        reloaded1.GetEffectiveSummaryModel().Should().Be("gemini-2.5-flash");
        reloaded1.GetEffectiveVideoPlanModel().Should().Be("argon");
        reloaded1.GetEffectiveQcModel().Should().Be("claude-3-7-sonnet");
        reloaded1.AntigravityProxyVideoPlanModel.Should().Be("argon");
        reloaded1.AntigravityVideoPlanModel.Should().Be("argon");

        // 2. Yön: Yeni alan adlarıyla (AntigravityProxyVideoPlanModel vb.) güncellendiğinde (eski alias alanda önceki değer kalsa bile)
        var incomingProxyDto = new AiProviderSettingsDto
        {
            ActiveProvider = "antigravity",
            AntigravityProxyUrl = "http://localhost:8045/v1",
            AntigravityProxyKey = "sk-local-proxy",
            AntigravityProxyModel = "gemini-3.8-flash-tiered",
            AntigravityProxySummaryModel = "gemini-3.8-flash",
            AntigravitySummaryModel = "gemini-2.5-flash", // eski değer kalsa bile yeni seçilen korunmalı
            AntigravityProxyVideoPlanModel = "claude-3-7-sonnet-tiered",
            AntigravityVideoPlanModel = "argon",
            AntigravityProxyQcModel = "o3-mini",
            AntigravityQcModel = "claude-3-7-sonnet",
            AllowFallbackToSecondaryProvider = false
        };

        var savedFromProxy = await service.UpdateSettingsAsync(incomingProxyDto);
        savedFromProxy.AntigravityProxySummaryModel.Should().Be("gemini-3.8-flash");
        savedFromProxy.AntigravitySummaryModel.Should().Be("gemini-3.8-flash");
        savedFromProxy.AntigravityProxyVideoPlanModel.Should().Be("claude-3-7-sonnet-tiered");
        savedFromProxy.AntigravityVideoPlanModel.Should().Be("claude-3-7-sonnet-tiered");
        savedFromProxy.AntigravityProxyQcModel.Should().Be("o3-mini");
        savedFromProxy.AntigravityQcModel.Should().Be("o3-mini");

        var reloaded2 = await service.GetSettingsAsync();
        reloaded2.GetEffectiveSummaryModel().Should().Be("gemini-3.8-flash");
        reloaded2.GetEffectiveVideoPlanModel().Should().Be("claude-3-7-sonnet-tiered");
        reloaded2.GetEffectiveQcModel().Should().Be("o3-mini");
    }

    [Fact]
    public async Task ClaudeSynthesisProvider_WhenAllowFallbackIsFalse_ShouldNeverSilentlyFallbackToGroq_AndThrowExplicitError()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GROQ_API_KEY"] = "gsk_should_never_be_used_in_strict_mode"
            })
            .Build();

        // Senaryo A: OpenRouter seçili, OpenRouter anahtarı eksik ama Groq anahtarı dolu -> sessiz Groq fallback engellenmeli
        var openRouterSettingsMock = new Mock<IAiProviderSettingsService>();
        openRouterSettingsMock.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiProviderSettingsDto
            {
                ActiveProvider = "openrouter",
                OpenRouterApiKey = "",
                OpenRouterVideoPlanModel = "anthropic/claude-3.7-sonnet",
                GroqApiKey = "gsk_valid_looking_secondary_key",
                AllowFallbackToSecondaryProvider = false
            });

        var openRouterProvider = new ClaudeSynthesisProvider(
            NullLogger<ClaudeSynthesisProvider>.Instance,
            config,
            openRouterSettingsMock.Object);

        Func<Task> actOpenRouter = () => openRouterProvider.GenerateResearchSummaryAsync(
            "Özgür İrade",
            "Orta",
            "Üniversite",
            "=== [K1] ===\nKaynak metni");

        await actOpenRouter.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*OpenRouter*anthropic/claude-3.7-sonnet*AllowFallbackToSecondaryProvider=false*");

        // Senaryo B: Antigravity Yerel Proxy seçili, proxy erişilemez (port 1 anında reddeder) ve Groq anahtarı dolu -> 3 deneme sonrası açık hata fırlatmalı
        var proxySettingsMock = new Mock<IAiProviderSettingsService>();
        proxySettingsMock.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiProviderSettingsDto
            {
                ActiveProvider = "antigravity",
                AntigravityProxyUrl = "http://127.0.0.1:1/v1",
                AntigravityProxyKey = "sk-antigravity-local",
                AntigravityProxyVideoPlanModel = "argon",
                GroqApiKey = "gsk_valid_looking_secondary_key",
                AllowFallbackToSecondaryProvider = false
            });

        var proxyProvider = new ClaudeSynthesisProvider(
            NullLogger<ClaudeSynthesisProvider>.Instance,
            config,
            proxySettingsMock.Object);

        proxyProvider.ActiveModelName.Should().Be("argon");

        Func<Task> actProxy = () => proxyProvider.GenerateSeriesPlanAsync(
            topicAndThesis: "Modern algoritmalar ve irade",
            targetAudience: "Genel",
            toneInstruction: "Felsefi",
            konuAnaliziJson: "{}",
            structuredSourceCardsJson: "[]",
            formattedConstraintsText: "VideoSayisi=2");

        await actProxy.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Antigravity Yerel Proxy*argon*Groq*");
    }

    [Fact]
    public async Task GeminiSummarizer_WhenAllowFallbackIsFalse_ShouldNeverSilentlyFallbackToGroq_AndThrowExplicitError()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GROQ_API_KEY"] = "gsk_should_never_be_used_in_strict_mode"
            })
            .Build();

        // Senaryo A: Birincil API anahtarı boş, ancak yedek Groq anahtarı dolu ve AllowFallbackToSecondaryProvider = false
        var noKeySettingsMock = new Mock<IAiProviderSettingsService>();
        noKeySettingsMock.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiProviderSettingsDto
            {
                ActiveProvider = "openrouter",
                OpenRouterApiKey = "",
                OpenRouterSummaryModel = "google/gemini-2.5-pro",
                GroqApiKey = "gsk_secondary_key_present",
                AllowFallbackToSecondaryProvider = false
            });

        var summarizerNoKey = new GeminiSummarizer(
            config,
            NullLogger<GeminiSummarizer>.Instance,
            noKeySettingsMock.Object);

        Func<Task> actNoKey = () => summarizerNoKey.SummarizeAsync("Bu uzun bir ders transkriptidir ve özetlenmesi gerekmektedir.");
        await actNoKey.Should()
            .ThrowAsync<Exception>()
            .WithMessage("*API anahtarı girilmemiş*");

        // Senaryo B: Yerel Proxy seçili ama bağlantı hatası veriyor -> Groq yedeğine düşmeden açık hata fırlatmalı
        var unreachableProxySettingsMock = new Mock<IAiProviderSettingsService>();
        unreachableProxySettingsMock.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiProviderSettingsDto
            {
                ActiveProvider = "antigravity",
                AntigravityProxyUrl = "http://127.0.0.1:1/v1",
                AntigravityProxyKey = "sk-proxy-key",
                AntigravityProxySummaryModel = "gemini-2.5-pro",
                GroqApiKey = "gsk_secondary_key_present",
                AllowFallbackToSecondaryProvider = false
            });

        var summarizerProxy = new GeminiSummarizer(
            config,
            NullLogger<GeminiSummarizer>.Instance,
            unreachableProxySettingsMock.Object);

        summarizerProxy.ActiveModelName.Should().Be("gemini-2.5-pro");

        Func<Task> actProxy = () => summarizerProxy.SummarizeAsync("Bu metin özetlenirken seçili proxy hata verdiğinde Groq'a düşmemelidir.");
        await actProxy.Should()
            .ThrowAsync<Exception>()
            .WithMessage("*Yerel Proxy (Antigravity)*gemini-2.5-pro*AllowFallbackToSecondaryProvider=false*");
    }

    [Fact]
    public async Task UpdateAndGetSettings_ShouldTrimAndPersistYouTubeApiKey()
    {
        var (service, store) = CreateSettingsService();

        var updated = await service.UpdateSettingsAsync(new AiProviderSettingsDto
        {
            ActiveProvider = "openrouter",
            YouTubeApiKey = "  AIzaSyTestYouTubeKey123  "
        });

        updated.YouTubeApiKey.Should().Be("AIzaSyTestYouTubeKey123");
        store.Should().ContainKey("videoozet:settings:ai_provider");
        store["videoozet:settings:ai_provider"].Should().Contain("AIzaSyTestYouTubeKey123");

        var reloaded = await service.GetSettingsAsync();
        reloaded.YouTubeApiKey.Should().Be("AIzaSyTestYouTubeKey123");

        var cleared = await service.UpdateSettingsAsync(new AiProviderSettingsDto
        {
            ActiveProvider = "openrouter",
            YouTubeApiKey = "   "
        });
        cleared.YouTubeApiKey.Should().BeEmpty();
    }
}
