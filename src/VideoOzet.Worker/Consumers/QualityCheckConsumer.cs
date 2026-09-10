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

            // 1. İddiaları (Claims) çıkar
            var extractClaimsResponse = await _synthesisProvider.ExtractClaimsAsync(request.GeneratedContent.ArastirmaOzeti, context.CancellationToken);
            var claims = extractClaimsResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                              .Select(c => c.TrimStart('-', ' ', '*'))
                                              .Where(c => !string.IsNullOrWhiteSpace(c))
                                              .ToList();

            // 2. Kullanılan Kaynakları Getir
            var chunkIdsStr = request.GeneratedContent.KullanilanKaynaklar;
            var contextData = string.Empty;

            if (!string.IsNullOrEmpty(chunkIdsStr))
            {
                var chunkIds = chunkIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
                var chunks = await _dbContext.VideoChunkDocuments.Where(c => chunkIds.Contains(c.Id)).ToListAsync();
                
                var contextBuilder = new StringBuilder();
                foreach (var chunk in chunks)
                {
                    contextBuilder.AppendLine($"[VideoId={chunk.VideoId}, Zaman={chunk.StartTimeMs / 1000.0}-{chunk.EndTimeMs / 1000.0}s]");
                    contextBuilder.AppendLine(chunk.Text);
                }
                contextData = contextBuilder.ToString();
            }

            // 3. Her bir iddiayı doğrula
            var desteklenen = 0;
            var belirsiz = 0;
            var desteklenmeyen = 0;
            var detayliRaporBuilder = new StringBuilder();

            foreach (var claim in claims)
            {
                var verificationResultJson = await _synthesisProvider.VerifyClaimAsync(claim, contextData, context.CancellationToken);
                
                // Parse the JSON. We expect { "durum": "desteklendi", "aciklama": "..." }
                try
                {
                    using var doc = JsonDocument.Parse(verificationResultJson);
                    var durum = doc.RootElement.GetProperty("durum").GetString()?.ToLowerInvariant();
                    var aciklama = doc.RootElement.GetProperty("aciklama").GetString();

                    if (durum == "desteklendi") desteklenen++;
                    else if (durum == "desteklenmedi") desteklenmeyen++;
                    else belirsiz++;

                    detayliRaporBuilder.AppendLine($"İddia: {claim}");
                    detayliRaporBuilder.AppendLine($"Durum: {durum}");
                    detayliRaporBuilder.AppendLine($"Açıklama: {aciklama}");
                    detayliRaporBuilder.AppendLine("---");
                }
                catch (Exception)
                {
                    _logger.LogWarning("QC Parse Error. Yanıt JSON değildi. İddia: {Claim}, Yanıt: {Response}", claim, verificationResultJson);
                    belirsiz++;
                    detayliRaporBuilder.AppendLine($"İddia: {claim} \nDurum: belirsiz \nAçıklama: Model düzgün JSON dönmedi.\n---");
                }
            }

            var toplamIddia = claims.Count;
            decimal guvenYuzde = toplamIddia == 0 ? 100 : (decimal)desteklenen / toplamIddia * 100;

            // 4. QcResult Kaydı
            var qcResult = new QcResult
            {
                ContentRequestId = request.Id,
                ToplamIddiaSayisi = toplamIddia,
                DesteklenenSayisi = desteklenen,
                BelirsizSayisi = belirsiz,
                DesteklenmeyenSayisi = desteklenmeyen,
                DetayliRapor = detayliRaporBuilder.ToString(),
                GuvenSkorYuzde = guvenYuzde,
                OlusturmaTarihi = DateTime.UtcNow
            };

            _dbContext.QcResults.Add(qcResult);

            // 5. Durumu Tamamla
            request.Durum = ContentRequestDurumu.Tamamlandi;
            request.TamamlanmaTarihi = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // 6. SignalR Bildirimi için ContentReadyEvent fırlat
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
            await _logService.LogFunctionErrorAsync(nameof(QualityCheckConsumer), ex, message);
            await _logService.LogPipelineErrorAsync(logId, ex);

            var request = await _dbContext.ContentRequests.FindAsync(message.ContentRequestId);
            if (request != null)
            {
                request.Durum = ContentRequestDurumu.Hata;
                await _dbContext.SaveChangesAsync();
            }
        }
    }
}
