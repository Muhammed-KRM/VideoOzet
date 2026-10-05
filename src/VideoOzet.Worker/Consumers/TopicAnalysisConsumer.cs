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

public class TopicAnalysisConsumer : IConsumer<TopicAnalysisRequestedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ISourceTopicMapper _topicMapper;
    private readonly ILogService _logService;
    private readonly ILogger<TopicAnalysisConsumer> _logger;

    public TopicAnalysisConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        ISourceTopicMapper topicMapper,
        ILogService logService,
        ILogger<TopicAnalysisConsumer> logger)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _topicMapper = topicMapper;
        _logService = logService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TopicAnalysisRequestedEvent> context)
    {
        var message = context.Message;
        
        var request = await _dbContext.ContentRequests
            .Include(x => x.Egitim)
            .FirstOrDefaultAsync(x => x.Id == message.ContentRequestId, context.CancellationToken);
            
        if (request == null)
        {
            _logger.LogWarning("ContentRequest {Id} bulunamadı.", message.ContentRequestId);
            return;
        }

        try
        {
            // 1. Durum Güncelle
            var konuAnalizi = await _dbContext.KonuAnalizleri.FirstOrDefaultAsync(x => x.ContentRequestId == request.Id, context.CancellationToken);
            if (konuAnalizi == null)
            {
                konuAnalizi = new KonuAnalizi
                {
                    ContentRequestId = request.Id,
                    Durum = AnalizDurumu.Isleniyor
                };
                _dbContext.KonuAnalizleri.Add(konuAnalizi);
            }
            else
            {
                konuAnalizi.Durum = AnalizDurumu.Isleniyor;
            }
            
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            // Sinyal gönder
            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = "KonuAnalizi",
                Durum = "Basladi",
                Mesaj = "Eğitim içerikleri analiz ediliyor..."
            }, context.CancellationToken);

            // 2. Map-Reduce: Kaynak başına konu çıkarımı ve özet haritası (Önbellekli)
            var allSourcesData = await _topicMapper.BuildTopicDigestAsync(message.EgitimId, context.CancellationToken);

            // 3. AI ile Analiz (Reduce)
            var analizJsonStr = await _synthesisProvider.AnalyzeTopicAsync(request.Konu, allSourcesData, context.CancellationToken);
            var analizData = LlmJson.Deserialize<System.Text.Json.JsonElement>(analizJsonStr);
            if (analizData.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new InvalidOperationException("Yapay zeka geçerli bir JSON objesi döndürmedi.");
            }
            
            // 4. Veritabanına kaydet
            konuAnalizi.AnaFikir = analizData.TryGetProperty("AnaFikir", out var af) ? af.GetString() ?? "" : "";
            konuAnalizi.OnerilenVideoSayisi = analizData.TryGetProperty("OnerilenVideoSayisi", out var videoSayisi) ? videoSayisi.GetInt32() : 1;
            konuAnalizi.OneriSuresiDk = analizData.TryGetProperty("OneriSuresiDk", out var sure) ? sure.GetInt32() : 10;
            konuAnalizi.OneriGerekcesi = analizData.TryGetProperty("OneriGerekcesi", out var gerekce) ? gerekce.GetString() ?? "" : "";
            
            konuAnalizi.BeklenenKonularJson = analizData.TryGetProperty("BeklenenKonular", out var beklenen) ? beklenen.ToString() : "[]";
            konuAnalizi.KonuHaritasiJson = analizData.TryGetProperty("KonuHaritasi", out var harita) ? harita.ToString() : "[]";
            konuAnalizi.KaynaktaOlmayanlarJson = analizData.TryGetProperty("KaynaktaOlmayanlar", out var olmayan) ? olmayan.ToString() : "[]";
            
            konuAnalizi.Durum = AnalizDurumu.Tamamlandi;
            konuAnalizi.LlmModel = _synthesisProvider.ActiveModelName;
            
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            // 5. Tamamlandı Sinyali
            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = "KonuAnalizi",
                Durum = "Tamamlandi",
                Mesaj = "Konu analizi tamamlandı."
            }, context.CancellationToken);
            
            await context.Publish(new TopicAnalysisCompletedEvent
            {
                ContentRequestId = request.Id,
                EgitimId = request.EgitimId,
                KonuAnaliziId = konuAnalizi.Id
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TopicAnalysisConsumer hata fırlattı: {Message}", ex.Message);
            
            var konuAnalizi = await _dbContext.KonuAnalizleri.FirstOrDefaultAsync(x => x.ContentRequestId == request.Id, context.CancellationToken);
            if (konuAnalizi != null)
            {
                konuAnalizi.Durum = AnalizDurumu.Hata;
                await _dbContext.SaveChangesAsync(context.CancellationToken);
            }
            
            await _logService.LogFunctionErrorAsync(nameof(TopicAnalysisConsumer), ex, message);
            
            await context.Publish(new ContentErrorEvent
            {
                ContentRequestId = request.Id,
                EgitimId = request.EgitimId,
                HataMesaji = ex.Message
            }, context.CancellationToken);
        }
    }
}
