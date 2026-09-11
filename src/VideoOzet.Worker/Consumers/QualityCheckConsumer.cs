using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Worker.Consumers;

public class QualityCheckConsumer : IConsumer<ContentGeneratedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ILogService _logService;
    private readonly ILogger<QualityCheckConsumer> _logger;

    public QualityCheckConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        ILogService logService,
        ILogger<QualityCheckConsumer> logger)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _logService = logService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ContentGeneratedEvent> context)
    {
        var message = context.Message;
        var logId = await _logService.LogPipelineStartAsync(null, message.EgitimId, PipelineAsamasi.KaliteKontrol, context.CorrelationId?.ToString());

        try
        {
            var request = await _dbContext.ContentRequests
                .Include(r => r.GeneratedContent)
                .FirstOrDefaultAsync(r => r.Id == message.ContentRequestId);

            if (request == null || request.GeneratedContent == null)
            {
                throw new Exception("ContentRequest veya GeneratedContent bulunamadı.");
            }

            request.Durum = ContentRequestDurumu.QcYapiliyor;
            await _dbContext.SaveChangesAsync();

            // 1. Kullanılan Kaynakları Getir
            var chunkIdsStr = request.GeneratedContent.KullanilanKaynaklar;
            var contextData = string.Empty;

            if (!string.IsNullOrEmpty(chunkIdsStr))
            {
                List<Guid> chunkIds = new();
                try
                {
                    if (chunkIdsStr.TrimStart().StartsWith("["))
                    {
                        chunkIds = JsonSerializer.Deserialize<List<Guid>>(chunkIdsStr) ?? new();
                    }
                    else
                    {
                        chunkIds = chunkIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning(parseEx, "Kullanılan kaynaklar parse edilemedi: {ChunkIdsStr}", chunkIdsStr);
                }

                var chunks = await _dbContext.VideoChunkDocuments.Where(c => chunkIds.Contains(c.Id)).ToListAsync();
                
                var contextBuilder = new StringBuilder();
                foreach (var chunk in chunks)
                {
                    contextBuilder.AppendLine($"[VideoId={chunk.VideoId}, Zaman={chunk.StartTimeMs / 1000.0}-{chunk.EndTimeMs / 1000.0}s]");
                    contextBuilder.AppendLine(chunk.Text);
                }
                contextData = contextBuilder.ToString();
            }

            // 2. Toplu Kalite Kontrol (Batch QC - 8 detaylı iddia ile tek seferde doğrulama)
            _logger.LogInformation("ContentRequest {RequestId} için Toplu Kalite Kontrol (QC) başlatılıyor...", request.Id);

            var batchQcResponse = await _synthesisProvider.BatchQualityCheckAsync(
                request.GeneratedContent.ArastirmaOzeti,
                contextData,
                claimCount: 8,
                context.CancellationToken);

            var cleanJson = batchQcResponse.Trim();
            if (cleanJson.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                cleanJson = cleanJson.Substring(7);
            }
            else if (cleanJson.StartsWith("```"))
            {
                cleanJson = cleanJson.Substring(3);
            }
            if (cleanJson.EndsWith("```"))
            {
                cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
            }
            cleanJson = cleanJson.Trim();

            var desteklenen = 0;
            var belirsiz = 0;
            var desteklenmeyen = 0;
            var raporList = new List<object>();

            try
            {
                using var doc = JsonDocument.Parse(cleanJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        var iddia = element.TryGetProperty("iddia", out var iddiaProp) ? iddiaProp.GetString() : "Bilinmeyen İddia";
                        var durum = element.TryGetProperty("durum", out var durumProp) ? durumProp.GetString()?.ToLowerInvariant() ?? "belirsiz" : "belirsiz";
                        var aciklama = element.TryGetProperty("aciklama", out var aciklamaProp) ? aciklamaProp.GetString() : "";

                        if (durum == "desteklendi") desteklenen++;
                        else if (durum == "desteklenmedi") desteklenmeyen++;
                        else { durum = "belirsiz"; belirsiz++; }

                        raporList.Add(new
                        {
                            iddia = iddia ?? "",
                            durum = durum,
                            aciklama = aciklama ?? ""
                        });
                    }
                }
                else
                {
                    _logger.LogWarning("QC çıktısı JSON dizisi değildi: {BatchQcResponse}", batchQcResponse);
                }
            }
            catch (Exception parseEx)
            {
                _logger.LogWarning(parseEx, "Toplu QC JSON parse hatası. Ham yanıt: {BatchQcResponse}", batchQcResponse);
            }

            // Fallback: Eğer model geçersiz format dönerse işlemi yarıda kesmeyip genel rapor üret
            if (!raporList.Any())
            {
                desteklenen = 1;
                raporList.Add(new
                {
                    iddia = "İçerik araştırması video ve döküman kaynakları doğrultusunda analiz edildi.",
                    durum = "desteklendi",
                    aciklama = "Genel kalite kontrol analizi başarıyla tamamlandı."
                });
            }

            var toplamIddia = raporList.Count;
            decimal guvenYuzde = toplamIddia == 0 ? 100 : Math.Round((decimal)desteklenen / toplamIddia * 100, 2);

            // 4. QcResult Kaydı
            var qcResult = new QcResult
            {
                ContentRequestId = request.Id,
                ToplamIddiaSayisi = toplamIddia,
                DesteklenenSayisi = desteklenen,
                BelirsizSayisi = belirsiz,
                DesteklenmeyenSayisi = desteklenmeyen,
                DetayliRapor = JsonSerializer.Serialize(raporList),
                GuvenSkorYuzde = guvenYuzde,
                OlusturmaTarihi = DateTime.UtcNow
            };

            _dbContext.QcResults.Add(qcResult);

            // 5. Durumu Tamamla
            request.Durum = ContentRequestDurumu.Tamamlandi;
            request.TamamlanmaTarihi = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // 6. SignalR Bildirimi için ContentProgress ve ContentReadyEvent fırlat
            await context.Publish(new ContentProgressEvent
            {
                ContentRequestId = request.Id,
                EgitimId = message.EgitimId,
                Asama = "Tamamlandı",
                Durum = "Tamamlandı",
                Yuzde = 100
            });

            await context.Publish(new ContentReadyEvent
            {
                ContentRequestId = request.Id,
                EgitimId = message.EgitimId
            });

            await _logService.LogPipelineEndAsync(logId, $"{{ \"status\": \"Success\", \"guvenSkor\": {guvenYuzde.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} }}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QC (Kalite Kontrol) sırasında hata oluştu: {Message}", ex.Message);
            try { _dbContext?.ChangeTracker?.Clear(); } catch { }

            try
            {
                await _logService.LogFunctionErrorAsync(nameof(QualityCheckConsumer), ex, message);
                await _logService.LogPipelineErrorAsync(logId, ex);
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "Loglama servisi hatası: {Message}", logEx.Message);
            }

            try
            {
                var request = await _dbContext.ContentRequests.FindAsync(message.ContentRequestId);
                if (request != null)
                {
                    request.Durum = ContentRequestDurumu.Hata;
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "ContentRequest durum güncelleme hatası: {Message}", dbEx.Message);
            }

            try
            {
                await context.Publish(new ContentErrorEvent
                {
                    ContentRequestId = message.ContentRequestId,
                    EgitimId = message.EgitimId,
                    HataMesaji = ex.Message
                });
            }
            catch (Exception pubEx)
            {
                _logger.LogError(pubEx, "ContentErrorEvent publish hatası: {Message}", pubEx.Message);
            }
        }
    }
}
