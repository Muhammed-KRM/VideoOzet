using DocumentFormat.OpenXml.Packaging;
using MassTransit;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using VideoOzet.Business.Events;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Worker.Consumers;

public class ExtractDokumanTextConsumer : IConsumer<DocumentUploadedEvent>
{
    private readonly ILogger<ExtractDokumanTextConsumer> _logger;
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly VideoOzet.Business.Interfaces.IGeminiProvider? _geminiProvider;

    public ExtractDokumanTextConsumer(
        ILogger<ExtractDokumanTextConsumer> logger,
        AppDbContext dbContext,
        IFileStorageService fileStorageService,
        IPublishEndpoint publishEndpoint,
        VideoOzet.Business.Interfaces.IGeminiProvider? geminiProvider = null)
    {
        _logger = logger;
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _publishEndpoint = publishEndpoint;
        _geminiProvider = geminiProvider;
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

        dokuman.IslemDurumu = VideoIslemDurumu.SttBasladi; // Ortak durumu kullanıyoruz
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        await _publishEndpoint.Publish(new PipelineProgressEvent
        {
            VideoId = evt.DokumanId, // UI aynı event modelini kullanıyor
            Asama = "Doküman Çıkarımı",
            Durum = VideoIslemDurumu.SttBasladi.ToString(),
            Mesaj = "Dokümandan metin çıkarılıyor..."
        }, context.CancellationToken);

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{evt.DokumanId}{evt.Uzanti}");

        try
        {
            // Dosyayı MinIO'dan indir
            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
            {
                var storageStream = await _fileStorageService.DownloadFileAsync(evt.DosyaYolu);
                await storageStream.CopyToAsync(fileStream, context.CancellationToken);
                storageStream.Dispose();
            }

            string extractedText = "";

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

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                _logger.LogWarning("Dokümandan metin çıkarılamadı veya doküman boş.");
                extractedText = "Boş Döküman";
            }

            var kelimeSayisi = extractedText.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

            string? damitilmisOzet = null;
            string? cevrilmisOzet = null;
            string orijinalDil = "tr";

            if (_geminiProvider != null && extractedText.Length > 100 && extractedText != "Boş Döküman")
            {
                try
                {
                    var sampleText = extractedText.Length > 20000 ? extractedText.Substring(0, 20000) : extractedText;
                    var summaryJson = await _geminiProvider.SummarizeAsync(sampleText);
                    var parsed = VideoOzet.Business.Helpers.LlmJson.Deserialize<SummaryDocLlmResponse>(summaryJson);
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

            var dokumanMetin = new DokumanMetin
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
            
            dokuman.IslemDurumu = VideoIslemDurumu.SttTamamlandi; 
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            await _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = evt.DokumanId,
                Asama = "Doküman Çıkarımı",
                Durum = VideoIslemDurumu.SttTamamlandi.ToString(),
                Mesaj = "Dokümandan metin çıkarıldı."
            }, context.CancellationToken);

            _logger.LogInformation("Text extracted and saved successfully for DokumanId: {DokumanId}", evt.DokumanId);

            // İndeksleme için event fırlat
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
            
            if (dokuman != null)
            {
                dokuman.IslemDurumu = VideoIslemDurumu.Hata;
                await _dbContext.SaveChangesAsync(context.CancellationToken);
            }

            await _publishEndpoint.Publish(new PipelineProgressEvent
            {
                VideoId = evt.DokumanId,
                Asama = "Doküman Çıkarımı",
                Durum = VideoIslemDurumu.Hata.ToString(),
                Mesaj = $"Doküman işlenirken hata oluştu: {ex.Message}"
            }, context.CancellationToken);

            throw;
        }
        finally
        {
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
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
