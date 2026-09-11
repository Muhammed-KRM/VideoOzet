using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Services;

public class DokumanService : IDokumanService
{
    private readonly AppDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<DokumanService> _logger;

    public DokumanService(
        AppDbContext context,
        IFileStorageService fileStorageService,
        IPublishEndpoint publishEndpoint,
        ILogger<DokumanService> logger)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<Guid> UploadDokumanAsync(Guid egitimId, string fileName, string contentType, Stream fileStream)
    {
        var egitimExists = await _context.Egitimler.AnyAsync(e => e.Id == egitimId);
        if (!egitimExists)
            throw new Exception("Eğitim bulunamadı.");

        var uzanti = Path.GetExtension(fileName).ToLowerInvariant();
        var dokumanId = Guid.NewGuid();
        var objectName = $"dokumanlar/{egitimId}/{dokumanId}{uzanti}";

        // Upload to storage
        var uploadedUrl = await _fileStorageService.UploadFileAsync(fileStream, objectName, contentType);

        var dokuman = new Dokuman
        {
            Id = dokumanId,
            EgitimId = egitimId,
            DosyaAdi = fileName,
            DosyaYolu = objectName, // We store the object name to fetch later
            Uzanti = uzanti,
            DosyaBoyutu = fileStream.Length,
            IslemDurumu = VideoIslemDurumu.Bekliyor
        };

        _context.Dokumanlar.Add(dokuman);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Doküman uploaded and saved to DB. DokumanId: {DokumanId}", dokuman.Id);

        // Publish event for Worker
        await _publishEndpoint.Publish(new DocumentUploadedEvent
        {
            DokumanId = dokuman.Id,
            EgitimId = egitimId,
            DosyaYolu = dokuman.DosyaYolu,
            Uzanti = dokuman.Uzanti
        });

        return dokuman.Id;
    }

    public async Task<IEnumerable<object>> GetDokumanlarByEgitimIdAsync(Guid egitimId)
    {
        return await _context.Dokumanlar
            .Where(d => d.EgitimId == egitimId)
            .Select(d => new
            {
                d.Id,
                d.DosyaAdi,
                d.Uzanti,
                d.DosyaBoyutu,
                d.IslemDurumu,
                d.OlusturmaTarihi
            })
            .ToListAsync();
    }

    public async Task DeleteDokumanAsync(Guid dokumanId)
    {
        var dokuman = await _context.Dokumanlar.FindAsync(dokumanId);
        if (dokuman != null)
        {
            // Try to delete from storage
            try
            {
                await _fileStorageService.DeleteFileAsync(dokuman.DosyaYolu);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete file from storage: {DosyaYolu}", dokuman.DosyaYolu);
            }

            _context.Dokumanlar.Remove(dokuman);
            await _context.SaveChangesAsync();
        }
    }
}
