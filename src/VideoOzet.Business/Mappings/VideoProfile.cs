using AutoMapper;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Data.Entities;

namespace VideoOzet.Business.Mappings;

public class VideoProfile : Profile
{
    public VideoProfile()
    {
        CreateMap<Video, VideoListDto>()
            .ForMember(dest => dest.OrijinalDil, opt => opt.MapFrom(src => 
                src.Summary != null && !string.IsNullOrWhiteSpace(src.Summary.OrijinalDil) ? src.Summary.OrijinalDil :
                (src.Transcript != null && !string.IsNullOrWhiteSpace(src.Transcript.Dil) ? src.Transcript.Dil : "tr")));
    }
}
