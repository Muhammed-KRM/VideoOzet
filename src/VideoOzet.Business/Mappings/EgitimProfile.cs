using AutoMapper;
using VideoOzet.Business.DTOs.Egitim;
using VideoOzet.Data.Entities;

namespace VideoOzet.Business.Mappings;

public class EgitimProfile : Profile
{
    public EgitimProfile()
    {
        CreateMap<EgitimCreateDto, Egitim>();
        CreateMap<EgitimUpdateDto, Egitim>();
        CreateMap<Egitim, EgitimListDto>();
        CreateMap<Egitim, EgitimDetailDto>();
    }
}
