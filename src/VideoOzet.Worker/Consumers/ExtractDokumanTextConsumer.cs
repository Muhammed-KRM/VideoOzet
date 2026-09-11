using DocumentFormat.OpenXml.Packaging;
using MassTransit;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using VideoOzet.Business.Events;
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

    public ExtractDokumanTextConsumer(
        ILogger<ExtractDokumanTextConsumer> logger,
        AppDbContext dbContext,
        IFileStorageService fileStorageService,
        IPublishEndpoint publishEndpoint)
    {
        _logger = logger;
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _publishEndpoint = publishEndpoint;
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
            else
            {
                _logger.LogWarning("Desteklenmeyen döküman formatı: {Uzanti}", evt.Uzanti);
                throw new NotSupportedException($"Format desteklenmiyor: {evt.Uzanti}");
            }

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                _logger.LogWarning("Dokümandan metin çıkarılamadı veya doküman boş.");
                extractedText = "Boş Döküman";
            }

            var kelimeSayisi = extractedText.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

            var dokumanMetin = new DokumanMetin
            {
                DokumanId = evt.DokumanId,
                HamMetin = extractedText,
                KelimeSayisi = kelimeSayisi,
                OlusturmaTarihi = DateTime.UtcNow
            };

            _dbContext.DokumanMetinleri.Add(dokumanMetin);
            
            dokuman.IslemDurumu = VideoIslemDurumu.SttTamamlandi; 
            await _dbContext.SaveChangesAsync(context.CancellationToken);

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
}
