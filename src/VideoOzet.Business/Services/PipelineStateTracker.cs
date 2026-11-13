using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Services;

public class PipelineStateTracker : IPipelineStateTracker
{
    private readonly ICacheService _cacheService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PipelineStateTracker> _logger;

    private readonly SemaphoreSlim _queueCacheLock = new(1, 1);
    private Dictionary<string, int> _cachedQueueCounts = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _queueCacheExpiresAtUtc = DateTime.MinValue;
    private bool _cachedRabbitAvailable;

    private static readonly TimeSpan StateTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan HeartbeatTtl = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PipelineStateTracker(
        ICacheService cacheService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PipelineStateTracker> logger)
    {
        _cacheService = cacheService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    private static string StateKey(Guid itemId) => $"pipeline:state:{itemId}";
    private static string HeartbeatKey(Guid itemId) => $"pipeline:heartbeat:{itemId}";

    public IAsyncDisposable StartHeartbeat(Guid itemId, string stageName, string? extraLockKeyToRenew = null)
    {
        return new HeartbeatHandle(this, itemId, stageName, extraLockKeyToRenew);
    }

    public async Task RecordQueuedAsync(Guid itemId, Guid? egitimId, string asama, string mesaj)
    {
        await _cacheService.RemoveAsync($"lock:stt:{itemId}");
        await _cacheService.RemoveAsync(HeartbeatKey(itemId));

        var state = new StoredPipelineState
        {
            SonAsama = asama,
            Durum = "Bekliyor",
            Mesaj = mesaj,
            HataMesaji = null,
            Yuzde = 2,
            MevcutAdim = null,
            ToplamAdim = null,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await SaveStateAsync(itemId, state);
    }

    public async Task RecordProgressAsync(PipelineProgressEvent evt)
    {
        if (evt.VideoId == null || evt.VideoId == Guid.Empty) return;

        var itemId = evt.VideoId.Value;
        var existing = await LoadStateAsync(itemId);

        bool isError = string.Equals(evt.Durum, "Hata", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(evt.Asama, "Hata", StringComparison.OrdinalIgnoreCase);
        bool isCompleted = string.Equals(evt.Durum, "Tamamlandi", StringComparison.OrdinalIgnoreCase) &&
                           (string.Equals(evt.Asama, "Tamamlandi", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(evt.Asama, "Doküman İndeksleme", StringComparison.OrdinalIgnoreCase));

        bool sameStage = existing != null && string.Equals(existing.SonAsama, evt.Asama, StringComparison.OrdinalIgnoreCase);

        var next = new StoredPipelineState
        {
            SonAsama = !string.IsNullOrWhiteSpace(evt.Asama) ? evt.Asama : existing?.SonAsama,
            Durum = !string.IsNullOrWhiteSpace(evt.Durum) ? evt.Durum : existing?.Durum,
            Mesaj = !string.IsNullOrWhiteSpace(evt.Mesaj) ? evt.Mesaj : existing?.Mesaj,
            HataMesaji = isError ? evt.Mesaj : null,
            Yuzde = isError
                ? 0
                : (isCompleted
                    ? 100
                    : (evt.Yuzde ?? (sameStage ? existing?.Yuzde : null))),
            MevcutAdim = evt.MevcutAdim ?? (sameStage ? existing?.MevcutAdim : null),
            ToplamAdim = evt.ToplamAdim ?? (sameStage ? existing?.ToplamAdim : null),
            UpdatedAtUtc = DateTime.UtcNow
        };

        await SaveStateAsync(itemId, next);
    }

    public async Task RecordErrorAsync(Guid itemId, Guid? egitimId, string asama, string hataMesaji)
    {
        await _cacheService.RemoveAsync(HeartbeatKey(itemId));

        var state = new StoredPipelineState
        {
            SonAsama = asama,
            Durum = "Hata",
            Mesaj = hataMesaji,
            HataMesaji = hataMesaji,
            Yuzde = 0,
            MevcutAdim = null,
            ToplamAdim = null,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await SaveStateAsync(itemId, state);
    }

    public async Task ClearStateAsync(Guid itemId)
    {
        await _cacheService.RemoveAsync(StateKey(itemId));
        await _cacheService.RemoveAsync(HeartbeatKey(itemId));
        await _cacheService.RemoveAsync($"lock:stt:{itemId}");
    }

    public async Task<PipelineItemHealth> EvaluateItemHealthAsync(
        Guid itemId,
        string itemType,
        VideoIslemDurumu islemDurumu,
        DateTime olusturmaTarihiUtc)
    {
        var state = await LoadStateAsync(itemId);

        if (islemDurumu == VideoIslemDurumu.Tamamlandi)
        {
            return new PipelineItemHealth
            {
                SonAsama = "Tamamlandi",
                Durum = "Tamamlandi",
                Mesaj = "İndekslendi (RAG Hazır)",
                HataMesaji = null,
                Yuzde = 100,
                MevcutAdim = state?.MevcutAdim,
                ToplamAdim = state?.ToplamAdim,
                TakiliMi = false,
                TakilmaNedeni = null,
                UpdatedAtUtc = state?.UpdatedAtUtc
            };
        }

        if (islemDurumu == VideoIslemDurumu.Hata)
        {
            var errMsg = !string.IsNullOrWhiteSpace(state?.HataMesaji)
                ? state.HataMesaji
                : (!string.IsNullOrWhiteSpace(state?.Mesaj) ? state.Mesaj : "İşlem sırasında hata oluştu. Yeniden başlatabilirsiniz.");

            return new PipelineItemHealth
            {
                SonAsama = state?.SonAsama ?? "Hata",
                Durum = "Hata",
                Mesaj = errMsg,
                HataMesaji = errMsg,
                Yuzde = 0,
                MevcutAdim = null,
                ToplamAdim = null,
                TakiliMi = false,
                TakilmaNedeni = null,
                UpdatedAtUtc = state?.UpdatedAtUtc
            };
        }

        var health = new PipelineItemHealth
        {
            SonAsama = state?.SonAsama,
            Durum = state?.Durum ?? islemDurumu.ToString(),
            Mesaj = state?.Mesaj,
            HataMesaji = null,
            Yuzde = state?.Yuzde,
            MevcutAdim = state?.MevcutAdim,
            ToplamAdim = state?.ToplamAdim,
            UpdatedAtUtc = state?.UpdatedAtUtc
        };

        bool hasHeartbeat = await _cacheService.GetAsync(HeartbeatKey(itemId)) != null;
        if (hasHeartbeat)
        {
            health.TakiliMi = false;
            return health;
        }

        var lastActivityUtc = EnsureUtc(state?.UpdatedAtUtc ?? olusturmaTarihiUtc);
        var idleSeconds = (DateTime.UtcNow - lastActivityUtc).TotalSeconds;

        var (rabbitAvailable, queueCounts) = await GetQueueMessageCountsAsync();
        int relevantQueueMessages = CountRelevantQueueMessages(queueCounts, itemType, islemDurumu);

        bool isActiveProcessingState =
            islemDurumu == VideoIslemDurumu.SesAyristirmaBasladi ||
            islemDurumu == VideoIslemDurumu.SttBasladi ||
            islemDurumu == VideoIslemDurumu.OzetlemeBasladi ||
            islemDurumu == VideoIslemDurumu.IndekslemeBasladi;

        if (isActiveProcessingState)
        {
            if (idleSeconds > 90 && (!rabbitAvailable || relevantQueueMessages == 0 || idleSeconds > 180))
            {
                health.TakiliMi = true;
                health.TakilmaNedeni = "Arka plan işçisi (Worker) yanıt vermiyor ve bu aşama yarıda kalmış görünüyor (zaman aşımı veya bağlantı kopması).";
            }
            return health;
        }

        // Bekleme / aşama geçiş durumları (0, 2, 4, 6)
        if (rabbitAvailable && relevantQueueMessages > 0)
        {
            health.TakiliMi = false;
            if (string.IsNullOrWhiteSpace(health.Mesaj))
            {
                health.Mesaj = "⏳ Sırada bekliyor (önündeki dosyalar işleniyor)...";
            }
            return health;
        }

        if (rabbitAvailable && relevantQueueMessages == 0 && idleSeconds > 90)
        {
            health.TakiliMi = true;
            health.TakilmaNedeni = "Dosya bekleme aşamasında kaldı ancak kuyrukta bekleyen görev bulunamadı.";
        }

        return health;
    }

    private static int CountRelevantQueueMessages(
        Dictionary<string, int> queueCounts,
        string itemType,
        VideoIslemDurumu status)
    {
        int GetCount(string name) => queueCounts.TryGetValue(name, out var c) ? c : 0;

        if (string.Equals(itemType, "dokuman", StringComparison.OrdinalIgnoreCase))
        {
            return status switch
            {
                VideoIslemDurumu.SttBasladi => GetCount("ExtractDokumanText"),
                VideoIslemDurumu.IndekslemeBasladi => GetCount("IndexDokuman"),
                _ => GetCount("ExtractDokumanText") + GetCount("IndexDokuman")
            };
        }

        return status switch
        {
            VideoIslemDurumu.SesAyristirmaBasladi or VideoIslemDurumu.SttBasladi => GetCount("ExtractTranscript"),
            VideoIslemDurumu.OzetlemeBasladi => GetCount("SummarizeVideo"),
            VideoIslemDurumu.IndekslemeBasladi => GetCount("IndexSummary"),
            _ => GetCount("ExtractTranscript") + GetCount("SummarizeVideo") + GetCount("IndexSummary")
        };
    }

    private async Task<(bool Available, Dictionary<string, int> Counts)> GetQueueMessageCountsAsync()
    {
        if (_configuration.GetValue<bool>("StandaloneMode"))
        {
            return (false, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        }

        if (DateTime.UtcNow < _queueCacheExpiresAtUtc)
        {
            return (_cachedRabbitAvailable, _cachedQueueCounts);
        }

        await _queueCacheLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow < _queueCacheExpiresAtUtc)
            {
                return (_cachedRabbitAvailable, _cachedQueueCounts);
            }

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool available = false;

            try
            {
                var rabbitHost = _configuration["RabbitMQ:Host"] ?? "localhost";
                var rabbitUser = _configuration["RabbitMQ:Username"] ?? "guest";
                var rabbitPass = _configuration["RabbitMQ:Password"] ?? "guest";
                var mgmtPort = _configuration["RabbitMQ:ManagementPort"] ?? "15673";

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMilliseconds(1500);

                var request = new HttpRequestMessage(HttpMethod.Get, $"http://{rabbitHost}:{mgmtPort}/api/queues/%2f");
                var authBytes = Encoding.ASCII.GetBytes($"{rabbitUser}:{rabbitPass}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

                using var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var doc = await JsonDocument.ParseAsync(stream);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var q in doc.RootElement.EnumerateArray())
                        {
                            var qName = q.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                            if (string.IsNullOrWhiteSpace(qName)) continue;

                            int total = 0;
                            if (q.TryGetProperty("messages", out var msgProp) && msgProp.TryGetInt32(out var m))
                            {
                                total = m;
                            }
                            else
                            {
                                int ready = q.TryGetProperty("messages_ready", out var rProp) && rProp.TryGetInt32(out var r) ? r : 0;
                                int unacked = q.TryGetProperty("messages_unacknowledged", out var uProp) && uProp.TryGetInt32(out var u) ? u : 0;
                                total = ready + unacked;
                            }
                            counts[qName] = total;
                        }
                        available = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "RabbitMQ yönetim API kuyruk bilgisi alınamadı.");
                available = false;
            }

            _cachedRabbitAvailable = available;
            _cachedQueueCounts = counts;
            _queueCacheExpiresAtUtc = DateTime.UtcNow.AddSeconds(3);
            return (available, counts);
        }
        finally
        {
            _queueCacheLock.Release();
        }
    }

    private async Task<StoredPipelineState?> LoadStateAsync(Guid itemId)
    {
        var json = await _cacheService.GetAsync(StateKey(itemId));
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<StoredPipelineState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveStateAsync(Guid itemId, StoredPipelineState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await _cacheService.SetAsync(StateKey(itemId), json, StateTtl);
    }

    private async Task WriteHeartbeatAsync(Guid itemId, string stageName, string? extraLockKey)
    {
        await _cacheService.SetAsync(HeartbeatKey(itemId), DateTime.UtcNow.ToString("O"), HeartbeatTtl);
        if (!string.IsNullOrWhiteSpace(extraLockKey))
        {
            await _cacheService.SetAsync(extraLockKey, "1", HeartbeatTtl);
        }

        // Aktif çalışma zaman damgasını state içinde de güncel tut (heartbeat düşse bile son canlı an bilinsin)
        var existing = await LoadStateAsync(itemId);
        if (existing != null && !string.Equals(existing.Durum, "Hata", StringComparison.OrdinalIgnoreCase))
        {
            existing.UpdatedAtUtc = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(existing.SonAsama))
                existing.SonAsama = stageName;
            await SaveStateAsync(itemId, existing);
        }
    }

    private static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

    private sealed class HeartbeatHandle : IAsyncDisposable
    {
        private readonly PipelineStateTracker _owner;
        private readonly Guid _itemId;
        private readonly string _stageName;
        private readonly string? _extraLockKey;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loopTask;

        public HeartbeatHandle(PipelineStateTracker owner, Guid itemId, string stageName, string? extraLockKey)
        {
            _owner = owner;
            _itemId = itemId;
            _stageName = stageName;
            _extraLockKey = extraLockKey;
            _loopTask = Task.Run(RunLoopAsync);
        }

        private async Task RunLoopAsync()
        {
            try
            {
                await _owner.WriteHeartbeatAsync(_itemId, _stageName, _extraLockKey);

                using var timer = new PeriodicTimer(HeartbeatInterval);
                while (await timer.WaitForNextTickAsync(_cts.Token))
                {
                    await _owner.WriteHeartbeatAsync(_itemId, _stageName, _extraLockKey);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal kapanış
            }
            catch (Exception ex)
            {
                _owner._logger.LogDebug(ex, "Heartbeat yenileme döngüsü sonlandı: {ItemId}", _itemId);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                await _loopTask;
            }
            catch
            {
                // Ignore
            }
            finally
            {
                _cts.Dispose();
                await _owner._cacheService.RemoveAsync(HeartbeatKey(_itemId));
                if (!string.IsNullOrWhiteSpace(_extraLockKey))
                {
                    await _owner._cacheService.RemoveAsync(_extraLockKey);
                }
            }
        }
    }

    private sealed class StoredPipelineState
    {
        public string? SonAsama { get; set; }
        public string? Durum { get; set; }
        public string? Mesaj { get; set; }
        public string? HataMesaji { get; set; }
        public int? Yuzde { get; set; }
        public int? MevcutAdim { get; set; }
        public int? ToplamAdim { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
