using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.DTOs.Egitim;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Repositories;

namespace VideoOzet.Business.Services;

public class EgitimManager : IEgitimService
{
    private readonly IRepository<Egitim> _egitimRepository;
    private readonly IMapper _mapper;
    private readonly ILogger<EgitimManager> _logger;

    public EgitimManager(IRepository<Egitim> egitimRepository, IMapper mapper, ILogger<EgitimManager> logger)
    {
        _egitimRepository = egitimRepository;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IEnumerable<EgitimListDto>> GetAllAsync()
    {
        var egitimler = await _egitimRepository.GetAllAsync();
        return _mapper.Map<IEnumerable<EgitimListDto>>(egitimler);
    }

    public async Task<EgitimDetailDto> GetByIdAsync(Guid id)
    {
        var egitim = await _egitimRepository.GetByIdAsync(id);
        if (egitim == null)
            throw new NotFoundException("Egitim", id);

        return _mapper.Map<EgitimDetailDto>(egitim);
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
