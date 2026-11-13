using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Events;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Helpers;
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
    private readonly IPipelineStateTracker? _stateTracker;

    public DokumanService(
        AppDbContext context,
        IFileStorageService fileStorageService,
        IPublishEndpoint publishEndpoint,
        ILogger<DokumanService> logger,
        IPipelineStateTracker? stateTracker = null)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
        _stateTracker = stateTracker;
    }

    public async Task<Guid> UploadDokumanAsync(Guid egitimId, string fileName, string contentType, Stream fileStream, KaynakKullanimTuru kullanimTuru = KaynakKullanimTuru.DogrudanReferans)
    {
        var egitimExists = await _context.Egitimler.AnyAsync(e => e.Id == egitimId);
        if (!egitimExists)
            throw new NotFoundException("Egitim", egitimId);

        var uzanti = Path.GetExtension(fileName).ToLowerInvariant();
        var dokumanId = Guid.NewGuid();
        var objectName = $"dokumanlar/{egitimId}/{dokumanId}{uzanti}";

        await _fileStorageService.UploadFileAsync(fileStream, objectName, contentType);

        var dokuman = new Dokuman
        {
            Id = dokumanId,
            EgitimId = egitimId,
            DosyaAdi = fileName,
            DosyaYolu = objectName,
            Uzanti = uzanti,
            DosyaBoyutu = fileStream.Length,
            IslemDurumu = VideoIslemDurumu.Bekliyor,
            KullanimTuru = kullanimTuru
        };

        _context.Dokumanlar.Add(dokuman);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Doküman uploaded and saved to DB. DokumanId: {DokumanId}", dokuman.Id);

        if (_stateTracker != null)
        {
            await _stateTracker.RecordQueuedAsync(dokuman.Id, egitimId, "Bekliyor", "Kuyruğa alındı, metin çıkarımı bekleniyor...");
        }

        await _publishEndpoint.Publish(new DocumentUploadedEvent
        {
            DokumanId = dokuman.Id,
            EgitimId = egitimId,
            DosyaYolu = dokuman.DosyaYolu,
            Uzanti = dokuman.Uzanti
        });

        return dokuman.Id;
    }

    public async Task<List<DokumanListDto>> GetDokumanlarByEgitimIdAsync(Guid egitimId)
    {
        var docs = await _context.Dokumanlar
            .AsNoTracking()
            .Include(d => d.DokumanMetin)
            .Where(d => d.EgitimId == egitimId)
            .OrderByDescending(d => d.OlusturmaTarihi)
            .ToListAsync();

        var result = new List<DokumanListDto>(docs.Count);
        foreach (var d in docs)
        {
            var dto = new DokumanListDto
            {
                Id = d.Id,
                EgitimId = d.EgitimId,
                DosyaAdi = d.DosyaAdi,
                Uzanti = d.Uzanti,
                DosyaBoyutu = d.DosyaBoyutu,
                IslemDurumu = d.IslemDurumu,
                KullanimTuru = d.KullanimTuru,
                OrijinalDil = d.DokumanMetin != null && !string.IsNullOrWhiteSpace(d.DokumanMetin.OrijinalDil)
                    ? d.DokumanMetin.OrijinalDil
                    : "tr",
                OlusturmaTarihi = d.OlusturmaTarihi
            };

            if (_stateTracker != null)
            {
                var health = await _stateTracker.EvaluateItemHealthAsync(d.Id, "dokuman", d.IslemDurumu, d.OlusturmaTarihi);
                dto.SonAsama = health.SonAsama;
                dto.Mesaj = health.Mesaj;
                dto.HataMesaji = health.HataMesaji;
                dto.Yuzde = health.Yuzde;
                dto.MevcutAdim = health.MevcutAdim;
                dto.ToplamAdim = health.ToplamAdim;
                dto.TakiliMi = health.TakiliMi;
                dto.TakilmaNedeni = health.TakilmaNedeni;
                dto.SonGuncellemeTarihi = health.UpdatedAtUtc;
            }

            result.Add(dto);
        }

        return result;
    }

    public async Task DeleteDokumanAsync(Guid dokumanId)
    {
        var dokuman = await _context.Dokumanlar.FindAsync(dokumanId);
        if (dokuman != null)
        {
            try
            {
                await _fileStorageService.DeleteFileAsync(dokuman.DosyaYolu);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete file from storage: {DosyaYolu}", dokuman.DosyaYolu);
            }

            var orphanChunks = await _context.VideoChunkDocuments
                .Where(c => c.DokumanId == dokumanId)
                .ToListAsync();
            if (orphanChunks.Count > 0)
            {
                _context.VideoChunkDocuments.RemoveRange(orphanChunks);
            }

            var orphanTopics = await _context.KaynakKonuCikarimlari
                .Where(k => k.KaynakId == dokumanId)
                .ToListAsync();
            if (orphanTopics.Count > 0)
            {
                _context.KaynakKonuCikarimlari.RemoveRange(orphanTopics);
            }

            _context.Dokumanlar.Remove(dokuman);
            await _context.SaveChangesAsync();

            if (_stateTracker != null)
            {
                await _stateTracker.ClearStateAsync(dokumanId);
            }
        }
    }

    public async Task RetryDokumanAsync(Guid dokumanId)
    {
        var dokuman = await _context.Dokumanlar
            .Include(d => d.DokumanMetin)
            .FirstOrDefaultAsync(d => d.Id == dokumanId);

        if (dokuman == null)
            throw new NotFoundException("Dokuman", dokumanId);

        if (dokuman.DokumanMetin != null &&
            !DocumentContentValidator.IsBlockedOrInvalidContent(dokuman.DokumanMetin.HamMetin, out _))
        {
            dokuman.IslemDurumu = VideoIslemDurumu.SttTamamlandi;
            await _context.SaveChangesAsync();

            if (_stateTracker != null)
            {
                await _stateTracker.RecordQueuedAsync(
                    dokuman.Id,
                    dokuman.EgitimId,
                    "Doküman İndeksleme",
                    "Metin hazır, vektör indeksleme kuyruğuna alındı...");
            }

            await _publishEndpoint.Publish(new DocumentTextReadyEvent
            {
                DokumanId = dokuman.Id,
                EgitimId = dokuman.EgitimId,
                DokumanMetinId = dokuman.DokumanMetin.Id
            });

            _logger.LogInformation("Retry triggered from Index stage (DokumanMetin exists) for Dokuman {DokumanId}", dokuman.Id);
        }
        else
        {
            if (dokuman.DokumanMetin != null)
            {
                _context.DokumanMetinleri.Remove(dokuman.DokumanMetin);
            }

            dokuman.IslemDurumu = VideoIslemDurumu.Bekliyor;
            await _context.SaveChangesAsync();

            if (_stateTracker != null)
            {
                await _stateTracker.RecordQueuedAsync(
                    dokuman.Id,
                    dokuman.EgitimId,
                    "Bekliyor",
                    "Yeniden başlatıldı, metin çıkarımı kuyruğuna alındı...");
            }

            await _publishEndpoint.Publish(new DocumentUploadedEvent
            {
                DokumanId = dokuman.Id,
                EgitimId = dokuman.EgitimId,
                DosyaYolu = dokuman.DosyaYolu,
                Uzanti = dokuman.Uzanti
            });

            _logger.LogInformation("Retry triggered from beginning for Dokuman {DokumanId}", dokuman.Id);
        }
    }

    public async Task ResetDokumanQueueForEgitimAsync(Guid egitimId)
    {
        var incompleteDocIds = await _context.Dokumanlar
            .Where(d => d.EgitimId == egitimId && d.IslemDurumu != VideoIslemDurumu.Tamamlandi)
            .Select(d => d.Id)
            .ToListAsync();

        foreach (var docId in incompleteDocIds)
        {
            await RetryDokumanAsync(docId);
        }
    }

    public async Task UpdateKullanimTuruAsync(Guid dokumanId, KaynakKullanimTuru kullanimTuru)
    {
        var dokuman = await _context.Dokumanlar.FindAsync(dokumanId);
        if (dokuman == null)
            throw new NotFoundException("Dokuman", dokumanId);

        dokuman.KullanimTuru = kullanimTuru;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Doküman {DokumanId} kullanım türü güncellendi: {KullanimTuru}", dokumanId, kullanimTuru);
    }

    public async Task<KaynakIcerikDetayDto?> GetDokumanIcerikDetayAsync(Guid dokumanId)
    {
        var dokuman = await _context.Dokumanlar
            .Include(d => d.DokumanMetin)
            .FirstOrDefaultAsync(d => d.Id == dokumanId);

        if (dokuman == null) return null;

        var dto = new KaynakIcerikDetayDto
        {
            Id = dokuman.Id,
            Tip = "dokuman",
            Baslik = dokuman.DosyaAdi,
            KullanimTuru = dokuman.KullanimTuru,
            OrijinalDil = dokuman.DokumanMetin?.OrijinalDil ?? "tr",
            KelimeSayisi = dokuman.DokumanMetin?.KelimeSayisi ?? 0,
            SttModel = null,
            LlmModel = "gemini-1.5-flash",
            OlusturmaTarihi = dokuman.OlusturmaTarihi,
            HamMetin = dokuman.DokumanMetin?.HamMetin ?? string.Empty,
            DamitilmisOzetMetni = dokuman.DokumanMetin?.DamitilmisOzetMetni,
            CevrilmisOzetMetni = dokuman.DokumanMetin?.CevrilmisOzetMetni
        };

        return dto;
    }
}
