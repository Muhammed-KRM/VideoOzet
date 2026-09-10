using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;
using Xunit;

namespace VideoOzet.UnitTests.Business.Infrastructure;

public class PollyResilienceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _initialStatusCode;
        private readonly int _failCount;
        public int Attempts { get; private set; }

        public MockHttpMessageHandler(HttpStatusCode initialStatusCode, int failCount)
        {
            _initialStatusCode = initialStatusCode;
            _failCount = failCount;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts <= _failCount)
            {
                return Task.FromResult(new HttpResponseMessage(_initialStatusCode));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"success\"}")
            });
        }
    }

    [Fact]
    public async Task HttpClient_WithResilience_ShouldRetry_On429TooManyRequests()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.TooManyRequests, failCount: 2);

        var services = new ServiceCollection();
        services.AddHttpClient("ResilientClient")
            .ConfigurePrimaryHttpMessageHandler(() => mockHandler)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromMilliseconds(50);
                options.Retry.BackoffType = DelayBackoffType.Constant;
            });

        var provider = services.BuildServiceProvider();
        var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
        var client = clientFactory.CreateClient("ResilientClient");

        // Act
        var response = await client.GetAsync("https://api.testai.com/v1/test");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, mockHandler.Attempts); // 2 failed attempts (429) + 1 successful attempt (200)
    }

    [Fact]
    public async Task HttpClient_WithResilience_ShouldRetry_On504GatewayTimeout()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.GatewayTimeout, failCount: 1);

        var services = new ServiceCollection();
        services.AddHttpClient("ResilientClient504")
            .ConfigurePrimaryHttpMessageHandler(() => mockHandler)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromMilliseconds(50);
                options.Retry.BackoffType = DelayBackoffType.Constant;
            });

        var provider = services.BuildServiceProvider();
        var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
        var client = clientFactory.CreateClient("ResilientClient504");

        // Act
        var response = await client.GetAsync("https://api.testai.com/v1/timeout");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, mockHandler.Attempts); // 1 failure + 1 success
    }

    [Fact]
    public async Task HttpClient_WithResilience_ShouldExhaustRetries_WhenErrorsPersist()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.TooManyRequests, failCount: 10);

        var services = new ServiceCollection();
        services.AddHttpClient("ResilientClientExhaust")
            .ConfigurePrimaryHttpMessageHandler(() => mockHandler)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.Delay = TimeSpan.FromMilliseconds(20);
                options.Retry.BackoffType = DelayBackoffType.Constant;
            });

        var provider = services.BuildServiceProvider();
        var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
        var client = clientFactory.CreateClient("ResilientClientExhaust");

        // Act
        var response = await client.GetAsync("https://api.testai.com/v1/exhaust");

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(3, mockHandler.Attempts); // 1 initial + 2 retries = 3 total attempts
    }
}
