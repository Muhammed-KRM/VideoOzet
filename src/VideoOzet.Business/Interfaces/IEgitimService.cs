using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs.Egitim;

namespace VideoOzet.Business.Interfaces;

public interface IEgitimService
{
    Task<EgitimDetailDto> GetByIdAsync(Guid id);
    Task<IEnumerable<EgitimListDto>> GetAllAsync();
    Task<EgitimDetailDto> CreateAsync(EgitimCreateDto dto);
    Task<EgitimDetailDto> UpdateAsync(EgitimUpdateDto dto);
    Task DeleteAsync(Guid id);
}
