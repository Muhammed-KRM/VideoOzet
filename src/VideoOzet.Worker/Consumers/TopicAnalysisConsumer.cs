using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Events;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Services;

namespace VideoOzet.Worker.Consumers;

public class TopicAnalysisConsumer : IConsumer<TopicAnalysisRequestedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ISourceTopicMapper _topicMapper;
    private readonly ISourceContextBuilder? _sourceContextBuilder;
    private readonly ILogService _logService;
    private readonly ILogger<TopicAnalysisConsumer> _logger;

    public TopicAnalysisConsumer(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        ISourceTopicMapper topicMapper,
        ILogService logService,
        ILogger<TopicAnalysisConsumer> logger,
        ISourceContextBuilder? sourceContextBuilder = null)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _topicMapper = topicMapper;
        _logService = logService;
        _logger = logger;
        _sourceContextBuilder = sourceContextBuilder;
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

        if (request.Durum == ContentRequestDurumu.Hata)
        {
            _logger.LogInformation("ContentRequest {Id} daha önce durdurulmuş/iptal edilmiş, TopicAnalysis atlanıyor.", message.ContentRequestId);
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

            await context.Publish(new PipelineProgressEvent
            {
                VideoId = Guid.Empty,
                EgitimId = message.EgitimId,
                Asama = "KonuAnalizi",
                Durum = "Basladi",
                Mesaj = "Eğitimdeki tüm kaynak kartları ve yazar tezi analiz ediliyor..."
            }, context.CancellationToken);

            // 2. Zengin Yapılandırılmış Kaynak Kartları (%100 Kaynak Görünürlüğü; boş dönerse BuildTopicDigestAsync fallback)
            var structuredCards = await _topicMapper.GetStructuredSourceCardsAsync(message.EgitimId, context.CancellationToken);
            string allSourcesData;
            if (structuredCards != null && structuredCards.Count > 0)
            {
                allSourcesData = FormatStructuredSourceCards(structuredCards);
            }
            else
            {
                allSourcesData = await _topicMapper.BuildTopicDigestAsync(message.EgitimId, context.CancellationToken);
            }

            var combinedSources = allSourcesData;
            if (_sourceContextBuilder != null)
            {
                var queries = new List<string> { request.Konu };
                if (!string.IsNullOrWhiteSpace(request.EkTonTalimati))
                    queries.Add(request.EkTonTalimati);

                var sourceContext = await _sourceContextBuilder.BuildContextAsync(
                    message.EgitimId,
                    queries,
                    maxChars: 75000,
                    ct: context.CancellationToken);

                if (sourceContext != null && !string.IsNullOrWhiteSpace(sourceContext.ContextText))
                {
                    combinedSources = $"--- KONU VE TÜM KAYNAK KARTLARI DÖKÜMÜ ---\n{allSourcesData}\n\n--- SEÇİLİ GERÇEK KAYNAK METİNLERİ ([K#]) ---\n{sourceContext.ContextText}";
                }
            }

            // İptal kontrolü (kullanıcı durdurmuşsa LLM sentezine geçmeden çık)
            var currentDurum = await _dbContext.ContentRequests.AsNoTracking()
                .Where(x => x.Id == request.Id)
                .Select(x => x.Durum)
                .FirstOrDefaultAsync(context.CancellationToken);
            if (currentDurum == ContentRequestDurumu.Hata)
            {
                _logger.LogInformation("ContentRequest {Id} kullanıcı tarafından durdurulduğu için analiz iptal edildi.", request.Id);
                return;
            }

            // 3. [YAZAR-TEZİ VE KURGU DİREKTİFİ] enjeksiyonu ile AI Analizi (Reduce)
            var authorThesisPromptInput = BuildAuthorThesisDirective(request.Konu, request.EkTonTalimati, request.HedefKitle);
            var analizJsonStr = await _synthesisProvider.AnalyzeTopicAsync(authorThesisPromptInput, combinedSources, context.CancellationToken);
            var analizData = LlmJson.Deserialize<JsonElement>(analizJsonStr);
            if (analizData.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Yapay zeka geçerli bir JSON objesi döndürmedi.");
            }

            // 4. Veritabanına kaydet
            konuAnalizi.AnaFikir = ReadString(analizData, "AnaFikir");
            konuAnalizi.OnerilenVideoSayisi = ReadInt(analizData, "OnerilenVideoSayisi", 1);
            konuAnalizi.OneriSuresiDk = ReadInt(analizData, "OneriSuresiDk", 10);
            konuAnalizi.OneriGerekcesi = ReadString(analizData, "OneriGerekcesi");

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
                Mesaj = "Konu ve kaynak kapsam analizi tamamlandı."
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

            await ConsumerFailureGuard.TryPersistFailureStateAsync(_dbContext, _logger, async (db, ct) =>
            {
                var konuAnalizi = await db.KonuAnalizleri.FirstOrDefaultAsync(x => x.ContentRequestId == request.Id, ct);
                if (konuAnalizi != null)
                {
                    konuAnalizi.Durum = AnalizDurumu.Hata;
                }
            });

            await ConsumerFailureGuard.TryRunAsync(_logger, "LogFunctionError",
                () => _logService.LogFunctionErrorAsync(nameof(TopicAnalysisConsumer), ex, message));

            await ConsumerFailureGuard.TryRunAsync(_logger, "Publish ContentErrorEvent",
                () => context.Publish(new ContentErrorEvent
                {
                    ContentRequestId = request.Id,
                    EgitimId = request.EgitimId,
                    HataMesaji = ex.Message
                }, System.Threading.CancellationToken.None));
        }
    }

    private static string BuildAuthorThesisDirective(string? konu, string? ekTonTalimati, string? hedefKitle)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[YAZAR-TEZİ VE KURGU DİREKTİFİ]: {(string.IsNullOrWhiteSpace(konu) ? "Genel Eğitim İçeriği" : konu.Trim())}");
        if (!string.IsNullOrWhiteSpace(ekTonTalimati))
        {
            sb.AppendLine($"[EK TON, ÜSLUP VE PERSPEKTİF TALİMATI]: {ekTonTalimati.Trim()}");
        }
        if (!string.IsNullOrWhiteSpace(hedefKitle))
        {
            sb.AppendLine($"[HEDEF KİTLE]: {hedefKitle.Trim()}");
        }
        sb.AppendLine("KRİTİK KURAL: Yazarın yukarıdaki özgün tezini ve kurgusunu kaynaklarda birebir geçmese dahi ASLA 'kaynakta yok' diyerek çöpe atma; ana fikrin omurgası yap ve kaynakta doğrudan ampirik karşılığı bulunmayan özgün yazar başlıklarını 'KaynaktaOlmayanlar' altında 'Yazar Tezi Olarak İşlenecek: ...' biçiminde işaretleyerek koru!");
        return sb.ToString().Trim();
    }

    private static string FormatStructuredSourceCards(IReadOnlyList<CourseSourceCardDto> cards)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Toplam Eğitim Kaynak Sayısı: {cards.Count}");
        int idx = 1;
        foreach (var card in cards)
        {
            sb.AppendLine($"[KAYNAK KARTI #{idx}] KaynakId={card.KaynakId} | Tür={card.KaynakTuru} | Başlık=\"{card.Baslik}\" | Dil={card.Dil} | Mod={card.KullanimTuru} | TahminiSüre=~{card.TahminiToplamSureDk}dk");
            if (card.Konular != null && card.Konular.Count > 0)
            {
                sb.AppendLine($"  Konular: {string.Join(" | ", card.Konular)}");
            }
            if (!string.IsNullOrWhiteSpace(card.OzetBilgi))
            {
                sb.AppendLine($"  Özet & Düşünürler: {card.OzetBilgi}");
            }
            idx++;
        }
        return sb.ToString();
    }

    private static int ReadInt(JsonElement obj, string name, int fallback)
    {
        if (!obj.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.Number when v.TryGetDouble(out var d) => (int)Math.Round(d),
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => fallback
        };
    }

    private static string ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return string.Empty;
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.ToString();
    }
}
