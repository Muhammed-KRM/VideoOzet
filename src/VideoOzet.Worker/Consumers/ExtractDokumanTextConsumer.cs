using DocumentFormat.OpenXml.Packaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using VideoOzet.Business.Events;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Worker.Services;

namespace VideoOzet.Worker.Consumers;

public class ExtractDokumanTextConsumer : IConsumer<DocumentUploadedEvent>
{
    private readonly ILogger<ExtractDokumanTextConsumer> _logger;
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IGeminiProvider? _geminiProvider;
    private readonly IPipelineStateTracker? _stateTracker;

    public ExtractDokumanTextConsumer(
        ILogger<ExtractDokumanTextConsumer> logger,
        AppDbContext dbContext,
        IFileStorageService fileStorageService,
        IPublishEndpoint publishEndpoint,
        IGeminiProvider? geminiProvider = null,
        IPipelineStateTracker? stateTracker = null)
    {
        _logger = logger;
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _publishEndpoint = publishEndpoint;
        _geminiProvider = geminiProvider;
        _stateTracker = stateTracker;
    }

    public async Task Consume(ConsumeContext<DocumentUploadedEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("Extracting text for DokumanId: {DokumanId}", evt.DokumanId);

        var dokuman = await _dbContext.Dokumanlar.FindAsync(new object[] { evt.DokumanId }, context.CancellationToken);
        if (dokuman == null)
        {
            _logger.LogError("Doküman bulunamadı: {DokumanId}", evt.DokumanId);
            return;
        }

        if (dokuman.IslemDurumu == VideoIslemDurumu.Tamamlandi)
        {
            _logger.LogInformation("Doküman zaten tamamlanmış: {DokumanId}", evt.DokumanId);
            return;
        }

        await using var heartbeat = _stateTracker?.StartHeartbeat(evt.DokumanId, "Doküman Çıkarımı");

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{evt.DokumanId}{evt.Uzanti}");

        try
        {
            dokuman.IslemDurumu = VideoIslemDurumu.SttBasladi;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            var startEvt = new PipelineProgressEvent
            {
                VideoId = evt.DokumanId,
                EgitimId = evt.EgitimId,
                Asama = "Doküman Çıkarımı",
                Durum = VideoIslemDurumu.SttBasladi.ToString(),
                Mesaj = "Dokümandan metin çıkarılıyor...",
                Yuzde = 20
            };
            if (_stateTracker != null) await _stateTracker.RecordProgressAsync(startEvt);
            await _publishEndpoint.Publish(startEvt, context.CancellationToken);

            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
            {
                var storageStream = await _fileStorageService.DownloadFileAsync(evt.DosyaYolu);
                await storageStream.CopyToAsync(fileStream, context.CancellationToken);
                storageStream.Dispose();
            }

            string extractedText;
            if (evt.Uzanti == ".pdf")
            {
                extractedText = ExtractTextFromPdf(tempFilePath);
            }
            else if (evt.Uzanti == ".docx")
            {
                extractedText = ExtractTextFromDocx(tempFilePath);
            }
            else if (evt.Uzanti == ".txt")
            {
                extractedText = await File.ReadAllTextAsync(tempFilePath, context.CancellationToken);
            }
            else if (evt.Uzanti == ".pptx")
            {
                extractedText = ExtractTextFromPptx(tempFilePath);
            }
            else
            {
                _logger.LogWarning("Desteklenmeyen döküman formatı: {Uzanti}", evt.Uzanti);
                throw new NotSupportedException($"Format desteklenmiyor: {evt.Uzanti}");
            }

            extractedText = TextNormalizer.NormalizeTurkishText(extractedText);

            if (DocumentContentValidator.IsBlockedOrInvalidContent(extractedText, out var validationError))
            {
                _logger.LogWarning("Doküman içerik doğrulaması başarısız (DokumanId={DokumanId}): {Reason}", evt.DokumanId, validationError);
                throw new BusinessException(validationError);
            }

            var kelimeSayisi = extractedText.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

            string? damitilmisOzet = null;
            string? cevrilmisOzet = null;
            string orijinalDil = "tr";

            if (_geminiProvider != null && extractedText.Length > 100)
            {
                try
                {
                    var sumEvt = new PipelineProgressEvent
                    {
                        VideoId = evt.DokumanId,
                        EgitimId = evt.EgitimId,
                        Asama = "Doküman Çıkarımı",
                        Durum = VideoIslemDurumu.SttBasladi.ToString(),
                        Mesaj = "Doküman metni analiz ediliyor ve damıtılmış özet hazırlanıyor...",
                        Yuzde = 45
                    };
                    if (_stateTracker != null) await _stateTracker.RecordProgressAsync(sumEvt);
                    await _publishEndpoint.Publish(sumEvt, context.CancellationToken);

                    var sampleText = extractedText.Length > 20000 ? extractedText.Substring(0, 20000) : extractedText;
                    var summaryJson = await _geminiProvider.SummarizeAsync(sampleText);
                    var parsed = LlmJson.Deserialize<SummaryDocLlmResponse>(summaryJson);
                    if (parsed != null)
                    {
                        damitilmisOzet = parsed.OzetMetni;
                        if (!string.IsNullOrWhiteSpace(parsed.Dil))
                        {
                            var l = parsed.Dil.Trim().ToLowerInvariant();
                            if (l.StartsWith("en") || l == "english") orijinalDil = "en";
                            else if (l.StartsWith("de") || l == "german") orijinalDil = "de";
                            else if (l.StartsWith("tr") || l == "turkish") orijinalDil = "tr";
                            else if (l.Length >= 2) orijinalDil = l.Substring(0, 2);
                        }

                        if (!orijinalDil.Equals("tr", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(damitilmisOzet))
                        {
                            cevrilmisOzet = await _geminiProvider.TranslateDistilledSummaryToTurkishAsync(damitilmisOzet, orijinalDil);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Doküman için damıtılmış özet/çeviri adımı atlandı (Ham metin kullanılacak): {Msg}", ex.Message);
                }
            }

            var existingMetin = await _dbContext.DokumanMetinleri
                .FirstOrDefaultAsync(m => m.DokumanId == evt.DokumanId, context.CancellationToken);

            DokumanMetin dokumanMetin;
            if (existingMetin != null)
            {
                existingMetin.HamMetin = extractedText;
                existingMetin.DamitilmisOzetMetni = damitilmisOzet;
                existingMetin.CevrilmisOzetMetni = cevrilmisOzet;
                existingMetin.OrijinalDil = orijinalDil;
                existingMetin.KelimeSayisi = kelimeSayisi;
                existingMetin.OlusturmaTarihi = DateTime.UtcNow;
                dokumanMetin = existingMetin;
            }
            else
            {
                dokumanMetin = new DokumanMetin
                {
                    DokumanId = evt.DokumanId,
                    HamMetin = extractedText,
                    DamitilmisOzetMetni = damitilmisOzet,
                    CevrilmisOzetMetni = cevrilmisOzet,
                    OrijinalDil = orijinalDil,
                    KelimeSayisi = kelimeSayisi,
                    OlusturmaTarihi = DateTime.UtcNow
                };
                _dbContext.DokumanMetinleri.Add(dokumanMetin);
            }

            dokuman.IslemDurumu = VideoIslemDurumu.SttTamamlandi;
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            var doneEvt = new PipelineProgressEvent
            {
                VideoId = evt.DokumanId,
                EgitimId = evt.EgitimId,
                Asama = "Doküman Çıkarımı",
                Durum = VideoIslemDurumu.SttTamamlandi.ToString(),
                Mesaj = "Dokümandan metin çıkarıldı, indeksleme kuyruğuna alındı.",
                Yuzde = 60
            };
            if (_stateTracker != null) await _stateTracker.RecordProgressAsync(doneEvt);
            await _publishEndpoint.Publish(doneEvt, context.CancellationToken);

            _logger.LogInformation("Text extracted and saved successfully for DokumanId: {DokumanId}", evt.DokumanId);

            await _publishEndpoint.Publish(new DocumentTextReadyEvent
            {
                DokumanId = evt.DokumanId,
                EgitimId = evt.EgitimId,
                DokumanMetinId = dokumanMetin.Id
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text for DokumanId: {DokumanId}", evt.DokumanId);

            await ConsumerFailureGuard.TryPersistFailureStateAsync(_dbContext, _logger, async (db, ct) =>
            {
                var docEntity = await db.Dokumanlar.FindAsync(new object[] { evt.DokumanId }, ct);
                if (docEntity != null)
                {
                    docEntity.IslemDurumu = VideoIslemDurumu.Hata;
                }
            });

            var errMsg = ex is BusinessException ? ex.Message : $"Doküman işlenirken hata oluştu: {ex.Message}";
            if (_stateTracker != null)
            {
                await ConsumerFailureGuard.TryRunAsync(_logger, "RecordErrorAsync",
                    () => _stateTracker.RecordErrorAsync(evt.DokumanId, evt.EgitimId, "Doküman Çıkarımı", errMsg));
            }

            await ConsumerFailureGuard.TryRunAsync(_logger, "PublishDocExtractError", () =>
                _publishEndpoint.Publish(new PipelineProgressEvent
                {
                    VideoId = evt.DokumanId,
                    EgitimId = evt.EgitimId,
                    Asama = "Doküman Çıkarımı",
                    Durum = VideoIslemDurumu.Hata.ToString(),
                    Mesaj = errMsg
                }, CancellationToken.None));

            throw;
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
        }
    }

    private string ExtractTextFromPdf(string filePath)
    {
        var sb = new StringBuilder();
        using (PdfDocument document = PdfDocument.Open(filePath))
        {
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(page.Text);
            }
        }
        return sb.ToString();
    }

    private string ExtractTextFromDocx(string filePath)
    {
        var sb = new StringBuilder();
        using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(filePath, false))
        {
            var body = wordDoc.MainDocumentPart?.Document.Body;
            if (body != null)
            {
                sb.AppendLine(body.InnerText);
            }
        }
        return sb.ToString();
    }

    private string ExtractTextFromPptx(string filePath)
    {
        var sb = new StringBuilder();
        using (var presentationDoc = PresentationDocument.Open(filePath, false))
        {
            var presentationPart = presentationDoc.PresentationPart;
            if (presentationPart?.Presentation?.SlideIdList != null)
            {
                foreach (var slideIdEntry in presentationPart.Presentation.SlideIdList.Elements<DocumentFormat.OpenXml.Presentation.SlideId>())
                {
                    if (slideIdEntry.RelationshipId != null)
                    {
                        var slidePart = presentationPart.GetPartById(slideIdEntry.RelationshipId!) as SlidePart;
                        if (slidePart?.Slide != null)
                        {
                            var texts = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>();
                            foreach (var text in texts)
                            {
                                if (!string.IsNullOrWhiteSpace(text.Text))
                                {
                                    sb.AppendLine(text.Text);
                                }
                            }
                            sb.AppendLine();
                        }
                    }
                }
            }
            else if (presentationPart?.SlideParts != null)
            {
                foreach (var slidePart in presentationPart.SlideParts)
                {
                    if (slidePart.Slide != null)
                    {
                        var texts = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>();
                        foreach (var text in texts)
                        {
                            if (!string.IsNullOrWhiteSpace(text.Text))
                            {
                                sb.AppendLine(text.Text);
                            }
                        }
                        sb.AppendLine();
                    }
                }
            }
        }
        return sb.ToString();
    }

    private class SummaryDocLlmResponse
    {
        public string? Dil { get; set; }
        public string OzetMetni { get; set; } = string.Empty;
    }
}

public class ExtractDokumanTextConsumerDefinition : ConsumerDefinition<ExtractDokumanTextConsumer>
{
    public ExtractDokumanTextConsumerDefinition()
    {
        ConcurrentMessageLimit = 2;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ExtractDokumanTextConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.PrefetchCount = 2;
    }
}
