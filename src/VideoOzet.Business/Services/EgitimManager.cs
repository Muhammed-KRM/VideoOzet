using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.DTOs.Egitim;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Data.Repositories;

namespace VideoOzet.Business.Services;

public class EgitimManager : IEgitimService
{
    private readonly AppDbContext? _context;
    private readonly IRepository<Egitim> _egitimRepository;
    private readonly IMapper _mapper;
    private readonly ILogger<EgitimManager> _logger;
    private readonly IFileStorageService? _fileStorageService;

    public EgitimManager(
        IRepository<Egitim> egitimRepository, 
        IMapper mapper, 
        ILogger<EgitimManager> logger,
        AppDbContext? context = null,
        IFileStorageService? fileStorageService = null)
    {
        _context = context;
        _egitimRepository = egitimRepository;
        _mapper = mapper;
        _logger = logger;
        _fileStorageService = fileStorageService;
    }

    public async Task<IEnumerable<EgitimListDto>> GetAllAsync()
    {
        if (_context == null)
        {
            var fallbackEgitimler = await _egitimRepository.GetAllAsync();
            return _mapper.Map<IEnumerable<EgitimListDto>>(fallbackEgitimler);
        }

        var egitimler = await _context.Egitimler
            .Include(e => e.Videolar)
            .Include(e => e.Dokumanlar)
            .OrderByDescending(e => e.OlusturmaTarihi)
            .ToListAsync();

        var list = new List<EgitimListDto>();
        foreach (var egitim in egitimler)
        {
            var dto = _mapper.Map<EgitimListDto>(egitim);

            var totalFiles = (egitim.Videolar?.Count ?? 0) + (egitim.Dokumanlar?.Count ?? 0);
            var completedFiles = (egitim.Videolar?.Count(v => v.IslemDurumu == VideoIslemDurumu.Tamamlandi) ?? 0) +
                                 (egitim.Dokumanlar?.Count(d => d.IslemDurumu == VideoIslemDurumu.Tamamlandi) ?? 0);

            dto.ToplamVideoSayisi = totalFiles;
            dto.IslenmiVideoSayisi = completedFiles;

            if (totalFiles > 0 && completedFiles >= totalFiles)
            {
                dto.Durum = EgitimDurumu.Tamamlandi;
            }
            else if (completedFiles > 0)
            {
                dto.Durum = EgitimDurumu.Isleniyor;
            }

            list.Add(dto);
        }

        return list;
    }

    public async Task<EgitimDetailDto> GetByIdAsync(Guid id)
    {
        if (_context == null)
        {
            var fallback = await _egitimRepository.GetByIdAsync(id);
            if (fallback == null)
                throw new NotFoundException("Egitim", id);
            return _mapper.Map<EgitimDetailDto>(fallback);
        }

        var egitim = await _context.Egitimler
            .Include(e => e.Videolar)
            .Include(e => e.Dokumanlar)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (egitim == null)
            throw new NotFoundException("Egitim", id);

        var dto = _mapper.Map<EgitimDetailDto>(egitim);

        var totalFiles = (egitim.Videolar?.Count ?? 0) + (egitim.Dokumanlar?.Count ?? 0);
        var completedFiles = (egitim.Videolar?.Count(v => v.IslemDurumu == VideoIslemDurumu.Tamamlandi) ?? 0) +
                             (egitim.Dokumanlar?.Count(d => d.IslemDurumu == VideoIslemDurumu.Tamamlandi) ?? 0);

        dto.ToplamVideoSayisi = totalFiles;
        dto.IslenmiVideoSayisi = completedFiles;

        if (totalFiles > 0 && completedFiles >= totalFiles)
        {
            dto.Durum = EgitimDurumu.Tamamlandi;
        }
        else if (completedFiles > 0)
        {
            dto.Durum = EgitimDurumu.Isleniyor;
        }

        return dto;
    }

    public async Task<EgitimDetailDto> CreateAsync(EgitimCreateDto dto)
    {
        var egitim = _mapper.Map<Egitim>(dto);
        egitim.Durum = Data.Enums.EgitimDurumu.Taslak; // Default state

        await _egitimRepository.AddAsync(egitim);
        await _egitimRepository.SaveChangesAsync();

        _logger.LogInformation("Egitim created with id {EgitimId}", egitim.Id);

        return _mapper.Map<EgitimDetailDto>(egitim);
    }

    public async Task<EgitimDetailDto> UpdateAsync(EgitimUpdateDto dto)
    {
        var egitim = await _egitimRepository.GetByIdAsync(dto.Id);
        if (egitim == null)
            throw new NotFoundException("Egitim", dto.Id);

        _mapper.Map(dto, egitim);
        egitim.GuncellemeTarihi = DateTime.UtcNow;

        _egitimRepository.Update(egitim);
        await _egitimRepository.SaveChangesAsync();

        _logger.LogInformation("Egitim updated with id {EgitimId}", egitim.Id);

        return _mapper.Map<EgitimDetailDto>(egitim);
    }

    public async Task<EgitimDetailDto> CloneAsync(Guid id)
    {
        if (_context == null) throw new InvalidOperationException("DbContext required for cloning.");

        // Asıl eğitimi ve ilişkili verileri çekiyoruz
        var egitim = await _context.Egitimler
            .Include(e => e.Videolar)
                .ThenInclude(v => v.Transcript)
            .Include(e => e.Videolar)
                .ThenInclude(v => v.Summary)
            .Include(e => e.Dokumanlar)
                .ThenInclude(d => d.DokumanMetin)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (egitim == null)
            throw new NotFoundException("Egitim", id);

        var newEgitimId = Guid.NewGuid();
        
        var cloneEgitim = new Egitim
        {
            Id = newEgitimId,
            Ad = egitim.Ad + " (Kopya)",
            Aciklama = egitim.Aciklama,
            Durum = egitim.Durum,
            ToplamVideoSayisi = egitim.ToplamVideoSayisi,
            IslenmiVideoSayisi = egitim.IslenmiVideoSayisi,
            OlusturmaTarihi = DateTime.UtcNow,
            GuncellemeTarihi = DateTime.UtcNow
        };
        _context.Egitimler.Add(cloneEgitim);

        var videoIdMap = new Dictionary<Guid, Guid>();
        var dokumanIdMap = new Dictionary<Guid, Guid>();

        // Videoları Kopyala
        foreach (var v in egitim.Videolar ?? new List<Video>())
        {
            var newVideoId = Guid.NewGuid();
            videoIdMap[v.Id] = newVideoId;

            var cloneVideo = new Video
            {
                Id = newVideoId,
                EgitimId = newEgitimId,
                Baslik = v.Baslik,
                DosyaYolu = v.DosyaYolu,
                SesYolu = v.SesYolu,
                Sure = v.Sure,
                Sira = v.Sira,
                DosyaBoyutu = v.DosyaBoyutu,
                IslemDurumu = v.IslemDurumu,
                KullanimTuru = v.KullanimTuru,
                IndekslemeTuru = v.IndekslemeTuru,
                OlusturmaTarihi = DateTime.UtcNow,
                IslemTamamlanmaTarihi = v.IslemTamamlanmaTarihi
            };
            _context.Videolar.Add(cloneVideo);

            if (v.Transcript != null)
            {
                var cloneTranscript = new VideoTranscript
                {
                    Id = Guid.NewGuid(),
                    VideoId = newVideoId,
                    HamMetin = v.Transcript.HamMetin,
                    ZamanDamgalari = v.Transcript.ZamanDamgalari,
                    Dil = v.Transcript.Dil,
                    KelimeSayisi = v.Transcript.KelimeSayisi,
                    SttModel = v.Transcript.SttModel,
                    SttSuresiMs = v.Transcript.SttSuresiMs,
                    OlusturmaTarihi = DateTime.UtcNow
                };
                _context.VideoTranscripts.Add(cloneTranscript);
            }

            if (v.Summary != null)
            {
                var cloneSummary = new VideoSummary
                {
                    Id = Guid.NewGuid(),
                    VideoId = newVideoId,
                    OzetMetni = v.Summary.OzetMetni,
                    CevrilmisOzetMetni = v.Summary.CevrilmisOzetMetni,
                    OrijinalDil = v.Summary.OrijinalDil,
                    KonuBasliklari = v.Summary.KonuBasliklari,
                    KonuEtiketleri = v.Summary.KonuEtiketleri,
                    LlmModel = v.Summary.LlmModel,
                    TokenKullanimi = v.Summary.TokenKullanimi,
                    OlusturmaTarihi = DateTime.UtcNow
                };
                _context.VideoSummaries.Add(cloneSummary);
            }
        }

        // Dökümanları Kopyala
        foreach (var d in egitim.Dokumanlar ?? new List<Dokuman>())
        {
            var newDokumanId = Guid.NewGuid();
            dokumanIdMap[d.Id] = newDokumanId;

            var cloneDokuman = new Dokuman
            {
                Id = newDokumanId,
                EgitimId = newEgitimId,
                DosyaAdi = d.DosyaAdi,
                DosyaYolu = d.DosyaYolu,
                Uzanti = d.Uzanti,
                DosyaBoyutu = d.DosyaBoyutu,
                IslemDurumu = d.IslemDurumu,
                KullanimTuru = d.KullanimTuru,
                IndekslemeTuru = d.IndekslemeTuru,
                OlusturmaTarihi = DateTime.UtcNow,
                IslemTamamlanmaTarihi = d.IslemTamamlanmaTarihi
            };
            _context.Dokumanlar.Add(cloneDokuman);

            if (d.DokumanMetin != null)
            {
                var cloneMetin = new DokumanMetin
                {
                    Id = Guid.NewGuid(),
                    DokumanId = newDokumanId,
                    HamMetin = d.DokumanMetin.HamMetin,
                    DamitilmisOzetMetni = d.DokumanMetin.DamitilmisOzetMetni,
                    CevrilmisOzetMetni = d.DokumanMetin.CevrilmisOzetMetni,
                    OrijinalDil = d.DokumanMetin.OrijinalDil,
                    LlmModel = d.DokumanMetin.LlmModel,
                    KelimeSayisi = d.DokumanMetin.KelimeSayisi,
                    OlusturmaTarihi = DateTime.UtcNow
                };
                _context.DokumanMetinleri.Add(cloneMetin);
            }
        }

        // Vektör/Chunk verilerini kopyala (RAG için)
        var chunks = await _context.VideoChunkDocuments
            .Where(c => c.EgitimId == id)
            .AsNoTracking()
            .ToListAsync();

        foreach (var c in chunks)
        {
            var cloneChunk = new VideoChunkDocument
            {
                Id = Guid.NewGuid(),
                EgitimId = newEgitimId,
                VideoId = c.VideoId.HasValue && videoIdMap.ContainsKey(c.VideoId.Value) ? videoIdMap[c.VideoId.Value] : null,
                DokumanId = c.DokumanId.HasValue && dokumanIdMap.ContainsKey(c.DokumanId.Value) ? dokumanIdMap[c.DokumanId.Value] : null,
                StartTimeMs = c.StartTimeMs,
                EndTimeMs = c.EndTimeMs,
                ChunkIndex = c.ChunkIndex,
                ChunkTipi = c.ChunkTipi,
                KaynakBaslik = c.KaynakBaslik,
                BolumBasligi = c.BolumBasligi,
                KaynakDil = c.KaynakDil,
                KullanimTuru = c.KullanimTuru,
                Text = c.Text,
                Embedding = c.Embedding,
                CreatedAt = DateTime.UtcNow
            };
            _context.VideoChunkDocuments.Add(cloneChunk);
        }

        // Kaynak konu çıkarımı önbelleğini kopyala
        var allOldSourceIds = videoIdMap.Keys.Concat(dokumanIdMap.Keys).ToList();
        if (allOldSourceIds.Count > 0)
        {
            var topicExtractions = await _context.KaynakKonuCikarimlari
                .Where(k => allOldSourceIds.Contains(k.KaynakId))
                .AsNoTracking()
                .ToListAsync();

            foreach (var k in topicExtractions)
            {
                Guid? newKaynakId = null;
                if (videoIdMap.TryGetValue(k.KaynakId, out var mappedVideoId))
                    newKaynakId = mappedVideoId;
                else if (dokumanIdMap.TryGetValue(k.KaynakId, out var mappedDocId))
                    newKaynakId = mappedDocId;

                if (newKaynakId.HasValue)
                {
                    _context.KaynakKonuCikarimlari.Add(new KaynakKonuCikarimi
                    {
                        Id = Guid.NewGuid(),
                        KaynakId = newKaynakId.Value,
                        KaynakTuru = k.KaynakTuru,
                        KonularJson = k.KonularJson,
                        PromptVersiyonu = k.PromptVersiyonu,
                        IcerikHash = k.IcerikHash
                    });
                }
            }
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Egitim cloned from {OldId} to {NewId}", id, newEgitimId);

        return await GetByIdAsync(newEgitimId);
    }

    public async Task DeleteAsync(Guid id)
    {
        var egitim = await _egitimRepository.GetByIdAsync(id);
        if (egitim == null)
            throw new NotFoundException("Egitim", id);

        if (_context != null)
        {
            var videos = await _context.Videolar
                .AsNoTracking()
                .Where(v => v.EgitimId == id)
                .ToListAsync();

            var dokumanlar = await _context.Dokumanlar
                .AsNoTracking()
                .Where(d => d.EgitimId == id)
                .ToListAsync();

            if (_fileStorageService != null)
            {
                foreach (var video in videos)
                {
                    if (!string.IsNullOrWhiteSpace(video.DosyaYolu))
                    {
                        var sharedByOtherVideo = await _context.Videolar
                            .AnyAsync(v => v.EgitimId != id && v.DosyaYolu == video.DosyaYolu);
                        if (!sharedByOtherVideo)
                        {
                            try
                            {
                                await _fileStorageService.DeleteFileAsync(video.DosyaYolu);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "MinIO video dosyası silinemedi: {DosyaYolu}", video.DosyaYolu);
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(video.SesYolu))
                    {
                        var sharedByOtherAudio = await _context.Videolar
                            .AnyAsync(v => v.EgitimId != id && v.SesYolu == video.SesYolu);
                        if (!sharedByOtherAudio)
                        {
                            try
                            {
                                await _fileStorageService.DeleteFileAsync(video.SesYolu);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "MinIO ses dosyası silinemedi: {SesYolu}", video.SesYolu);
                            }
                        }
                    }
                }

                foreach (var dokuman in dokumanlar)
                {
                    if (!string.IsNullOrWhiteSpace(dokuman.DosyaYolu))
                    {
                        var sharedByOtherDoc = await _context.Dokumanlar
                            .AnyAsync(d => d.EgitimId != id && d.DosyaYolu == dokuman.DosyaYolu);
                        if (!sharedByOtherDoc)
                        {
                            try
                            {
                                await _fileStorageService.DeleteFileAsync(dokuman.DosyaYolu);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "MinIO doküman dosyası silinemedi: {DosyaYolu}", dokuman.DosyaYolu);
                            }
                        }
                    }
                }
            }

            var sourceIds = videos.Select(v => v.Id).Concat(dokumanlar.Select(d => d.Id)).ToList();
            if (sourceIds.Count > 0)
            {
                var orphanTopics = await _context.KaynakKonuCikarimlari
                    .Where(k => sourceIds.Contains(k.KaynakId))
                    .ToListAsync();
                if (orphanTopics.Count > 0)
                {
                    _context.KaynakKonuCikarimlari.RemoveRange(orphanTopics);
                }
            }

            var orphanChunks = await _context.VideoChunkDocuments
                .Where(c => c.EgitimId == id)
                .ToListAsync();
            if (orphanChunks.Count > 0)
            {
                _context.VideoChunkDocuments.RemoveRange(orphanChunks);
            }
        }

        _egitimRepository.Delete(egitim);
        await _egitimRepository.SaveChangesAsync();

        _logger.LogInformation("Egitim deleted with id {EgitimId}", id);
    }
}
