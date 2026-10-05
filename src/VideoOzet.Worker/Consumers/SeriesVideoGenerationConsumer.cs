using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Worker.Consumers;

public class SeriesVideoGenerationConsumer : IConsumer<SeriesVideoGenerationCommand>
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ILogService _logService;
    private readonly ILogger<SeriesVideoGenerationConsumer> _logger;

    public SeriesVideoGenerationConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        ILogService logService,
        ILogger<SeriesVideoGenerationConsumer> logger)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _logService = logService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SeriesVideoGenerationCommand> context)
    {
        var message = context.Message;
        var logId = await _logService.LogPipelineStartAsync(null, message.EgitimId, PipelineAsamasi.Sentez, context.CorrelationId?.ToString());

        try
        {
            var request = await _dbContext.ContentRequests
                .Include(r => r.Egitim)
                .FirstOrDefaultAsync(r => r.Id == message.ContentRequestId, context.CancellationToken);

            if (request == null)
            {
                _logger.LogWarning("ContentRequest {Id} bulunamadı.", message.ContentRequestId);
                return;
            }

            var seriPlani = await _dbContext.SeriPlanlari
                .Include(p => p.SeriBolumler)
                .ThenInclude(b => b.Revizyonlar)
                .FirstOrDefaultAsync(p => p.Id == message.SeriPlaniId, context.CancellationToken);
                
            var bolum = seriPlani?.SeriBolumler.FirstOrDefault(b => b.Id == message.SeriBolumId);

            if (seriPlani == null || bolum == null)
            {
                throw new Exception("Seri planı veya bölüm bulunamadı.");
            }

            bolum.Durum = BolumDurumu.Isleniyor;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = $"BolumUretimi-{bolum.BolumNo}",
                Durum = "Basladi",
                Mesaj = $"{bolum.BolumNo}. bölüm içeriği üretiliyor..."
            }, context.CancellationToken);

            var contextBuilder = new StringBuilder();
            var videolar = await _dbContext.Videolar.Include(v => v.Summary).Where(v => v.EgitimId == message.EgitimId && v.Summary != null).ToListAsync(context.CancellationToken);
            var dokumanlar = await _dbContext.Dokumanlar.Include(d => d.DokumanMetin).Where(d => d.EgitimId == message.EgitimId && d.DokumanMetin != null).ToListAsync(context.CancellationToken);
            
            foreach (var video in videolar) { contextBuilder.AppendLine($"[VİDEO: {video.Baslik}]\n{video.Summary!.OzetMetni}\n"); }
            foreach (var doc in dokumanlar) { contextBuilder.AppendLine($"[DÖKÜMAN: {doc.DosyaAdi}]\n{doc.DokumanMetin!.HamMetin}\n"); }

            var allSourcesData = contextBuilder.ToString();

            // Önceki bölümün devir notunu bul
            string devirNotu = "";
            if (bolum.BolumNo > 1)
            {
                var oncekiBolum = seriPlani.SeriBolumler.FirstOrDefault(b => b.BolumNo == bolum.BolumNo - 1);
                if (oncekiBolum != null)
                {
                    // En son revizyonu al
                    var sonRevizyon = oncekiBolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault();
                    devirNotu = sonRevizyon?.DevirNotuJson ?? "";
                }
            }

            var contentJsonStr = await _synthesisProvider.GenerateSeriesVideoContentAsync(
                bolum.CalismaBasligi,
                bolum.KonularJson,
                devirNotu,
                request.HedefKitle ?? "Genel",
                allSourcesData,
                context.CancellationToken);

            var contentData = LlmJson.Deserialize<System.Text.Json.JsonElement>(contentJsonStr);

            var yeniVersiyonNo = (bolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault()?.RevizyonNo ?? 0) + 1;

            var revizyon = new BolumRevizyonu
            {
                SeriBolumId = bolum.Id,
                RevizyonNo = yeniVersiyonNo,
                Tip = yeniVersiyonNo == 1 ? RevizyonTipi.IlkUretim : RevizyonTipi.KullaniciRevizyonu,
                Talimat = yeniVersiyonNo == 1 ? "Sistem tarafından ilk üretim" : "",
                ArastirmaOzeti = contentData.TryGetProperty("ArastirmaOzeti", out var ao) ? ao.GetString() ?? "" : "",
                VideoPlani = contentData.TryGetProperty("VideoPlani", out var vp) ? vp.GetString() ?? "" : "",
                DevirNotuJson = contentData.TryGetProperty("DevirNotu", out var dn) ? dn.GetString() ?? "" : "",
                LlmModel = _synthesisProvider.ActiveModelName
            };

            bolum.Revizyonlar.Add(revizyon);
            bolum.Durum = BolumDurumu.Tamamlandi;
            
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = $"BolumUretimi-{bolum.BolumNo}",
                Durum = "Tamamlandi",
                Mesaj = $"{bolum.BolumNo}. bölüm başarıyla üretildi."
            }, context.CancellationToken);

            await context.Publish(new SeriesVideoGeneratedEvent
            {
                ContentRequestId = request.Id,
                EgitimId = request.EgitimId,
                SeriBolumId = bolum.Id,
                BolumRevizyonuId = revizyon.Id
            }, context.CancellationToken);
            
            await _logService.LogPipelineEndAsync(logId, $"{{ \"status\": \"Success\", \"bolumNo\": {bolum.BolumNo} }}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeriesVideoGenerationConsumer hata fırlattı: {Message}", ex.Message);
            
            var bolum = await _dbContext.SeriBolumler.FirstOrDefaultAsync(x => x.Id == message.SeriBolumId, context.CancellationToken);
            if (bolum != null)
            {
                bolum.Durum = BolumDurumu.Hata;
                await _dbContext.SaveChangesAsync(context.CancellationToken);
            }
            
            await _logService.LogFunctionErrorAsync(nameof(SeriesVideoGenerationConsumer), ex, message);
            await _logService.LogPipelineErrorAsync(logId, ex);

            await context.Publish(new ContentErrorEvent
            {
                ContentRequestId = message.ContentRequestId,
                EgitimId = message.EgitimId,
                HataMesaji = $"Bölüm {message.BolumNo} üretim hatası: " + ex.Message
            }, context.CancellationToken);
        }
    }
}
