using AutoMapper;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Data.Entities;

namespace VideoOzet.Business.Mappings;

public class VideoProfile : Profile
{
    public VideoProfile()
    {
        CreateMap<Video, VideoListDto>();
    }
}
