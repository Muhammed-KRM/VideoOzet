using System;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VideoOzet.Business.DTOs.Egitim;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Mappings;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using Xunit;

namespace VideoOzet.UnitTests.Business.Mappings;

public class AutoMapperProfileTests
{
    private readonly IMapper _mapper;

    public AutoMapperProfileTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutoMapper(cfg =>
        {
            cfg.AddProfile<EgitimProfile>();
            cfg.AddProfile<VideoProfile>();
        });

        var provider = services.BuildServiceProvider();
        _mapper = provider.GetRequiredService<IMapper>();
    }

    [Fact]
    public void EgitimProfile_ShouldMapEgitimCreateDtoToEgitim()
    {
        // Arrange
        var dto = new EgitimCreateDto
        {
            Ad = "C# ve .NET 10 Eğitimi",
            Aciklama = "Detaylı eğitim serisi"
        };

        // Act
        var entity = _mapper.Map<Egitim>(dto);

        // Assert
        entity.Should().NotBeNull();
        entity.Ad.Should().Be(dto.Ad);
        entity.Aciklama.Should().Be(dto.Aciklama);
    }

    [Fact]
    public void EgitimProfile_ShouldMapEgitimUpdateDtoToEgitim()
    {
        // Arrange
        var dto = new EgitimUpdateDto
        {
            Id = Guid.NewGuid(),
            Ad = "Güncellenmiş Başlık",
            Aciklama = "Güncellenmiş Açıklama"
        };

        // Act
        var entity = _mapper.Map<Egitim>(dto);

        // Assert
        entity.Should().NotBeNull();
        entity.Id.Should().Be(dto.Id);
        entity.Ad.Should().Be(dto.Ad);
        entity.Aciklama.Should().Be(dto.Aciklama);
    }

    [Fact]
    public void EgitimProfile_ShouldMapEgitimToEgitimListDto()
    {
        // Arrange
        var entity = new Egitim
        {
            Id = Guid.NewGuid(),
            Ad = "Test Eğitimi",
            Aciklama = "Test Açıklaması",
            Durum = EgitimDurumu.Tamamlandi,
            OlusturmaTarihi = DateTime.UtcNow
        };

        // Act
        var dto = _mapper.Map<EgitimListDto>(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(entity.Id);
        dto.Ad.Should().Be(entity.Ad);
        dto.Durum.Should().Be(entity.Durum);
    }

    [Fact]
    public void EgitimProfile_ShouldMapEgitimToEgitimDetailDto()
    {
        // Arrange
        var entity = new Egitim
        {
            Id = Guid.NewGuid(),
            Ad = "Detaylı Eğitim",
            Aciklama = "Açıklama",
            Durum = EgitimDurumu.Tamamlandi,
            OlusturmaTarihi = DateTime.UtcNow
        };

        // Act
        var dto = _mapper.Map<EgitimDetailDto>(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(entity.Id);
        dto.Ad.Should().Be(entity.Ad);
        dto.Aciklama.Should().Be(entity.Aciklama);
    }

    [Fact]
    public void VideoProfile_ShouldMapVideoToVideoListDto()
    {
        // Arrange
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var entity = new Video
        {
            Id = videoId,
            EgitimId = egitimId,
            Baslik = "1. Ders: Giriş",
            DosyaYolu = "/videos/intro.mp4",
            Sira = 1,
            IslemDurumu = VideoIslemDurumu.Bekliyor,
            DosyaBoyutu = 1024,
            OlusturmaTarihi = DateTime.UtcNow
        };

        // Act
        var dto = _mapper.Map<VideoListDto>(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(videoId);
        dto.EgitimId.Should().Be(egitimId);
        dto.Baslik.Should().Be("1. Ders: Giriş");
        dto.DosyaYolu.Should().Be("/videos/intro.mp4");
        dto.IslemDurumu.Should().Be(VideoIslemDurumu.Bekliyor);
    }
}
