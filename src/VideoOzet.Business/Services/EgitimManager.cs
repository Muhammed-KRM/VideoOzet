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

    public EgitimManager(
        IRepository<Egitim> egitimRepository, 
        IMapper mapper, 
        ILogger<EgitimManager> logger,
        AppDbContext? context = null)
    {
        _context = context;
        _egitimRepository = egitimRepository;
        _mapper = mapper;
        _logger = logger;
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

    public async Task DeleteAsync(Guid id)
    {
        var egitim = await _egitimRepository.GetByIdAsync(id);
        if (egitim == null)
            throw new NotFoundException("Egitim", id);

        _egitimRepository.Delete(egitim);
        await _egitimRepository.SaveChangesAsync();

        _logger.LogInformation("Egitim deleted with id {EgitimId}", id);
    }
}
