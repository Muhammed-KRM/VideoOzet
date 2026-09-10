using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.DTOs.Egitim;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using Xunit;

namespace VideoOzet.IntegrationTests;

public class EndToEndPipelineTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public EndToEndPipelineTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-API-Key", "test-valid-key");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Full_MVP_EndToEnd_Lifecycle_ShouldSucceed()
    {
        // =========================================================================
        // ADIM 1: Yeni Bir Eğitim (Course) Oluşturma (POST /api/egitimler)
        // =========================================================================
        var createEgitimDto = new EgitimCreateDto
        {
            Ad = $"E2E Otomatik Test Eğitimi - {Guid.NewGuid():N}",
            Aciklama = "E2E Kabul Testi Kapsamında Otomatik Oluşturulan Eğitim"
        };

        var createEgitimResponse = await _client.PostAsJsonAsync("/api/egitimler", createEgitimDto);
        createEgitimResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdEgitim = await createEgitimResponse.Content.ReadFromJsonAsync<EgitimDetailDto>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        createdEgitim.Should().NotBeNull();
        createdEgitim!.Id.Should().NotBeEmpty();
        createdEgitim.Ad.Should().Be(createEgitimDto.Ad);

        var egitimId = createdEgitim.Id;

        // =========================================================================
        // ADIM 2: Eğitime Video Yükleme (POST /api/egitimler/{egitimId}/videolar/upload)
        // =========================================================================
        var dummyVideoBytes = new byte[2048];
        new Random().NextBytes(dummyVideoBytes);
        var testVideoFileName = "e2e_ders_01.mp4";

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(dummyVideoBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("video/mp4");
        content.Add(fileContent, "file", testVideoFileName);

        var uploadResponse = await _client.PostAsync($"/api/egitimler/{egitimId}/videolar/upload", content);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var uploadedVideo = await uploadResponse.Content.ReadFromJsonAsync<VideoListDto>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        uploadedVideo.Should().NotBeNull();
        uploadedVideo!.Id.Should().NotBeEmpty();
        uploadedVideo.DosyaYolu.Should().NotBeNullOrEmpty();

        // =========================================================================
        // ADIM 3: MinIO S3 & PostgreSQL Entegrasyon Doğrulaması
        // =========================================================================
        using (var scope = _factory.Services.CreateScope())
        {
            // MinIO'da dosyanın fiziksel varlığı
            var storageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            using var downloadedStream = await storageService.DownloadFileAsync(uploadedVideo.DosyaYolu);
            downloadedStream.Length.Should().Be(dummyVideoBytes.Length);

            // PostgreSQL'de Video kaydının varlığı
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dbVideo = await db.Videolar.FindAsync(uploadedVideo.Id);
            dbVideo.Should().NotBeNull();
            dbVideo!.EgitimId.Should().Be(egitimId);
        }

        // =========================================================================
        // ADIM 4: RAG İçerik Talebi Oluşturma (POST /api/egitimler/{egitimId}/content-requests)
        // =========================================================================
        var contentRequestDto = new CreateContentRequestDto
        {
            Konu = "Eğitimin Temel Prensipleri ve Çıkarımları",
            HedefUzunluk = "Orta",
            HedefKitle = "Genel İzleyici"
        };

        var contentRequestResponse = await _client.PostAsJsonAsync($"/api/egitimler/{egitimId}/content-requests", contentRequestDto);
        contentRequestResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // =========================================================================
        // ADIM 5: Eğitimin Videolarını Sorgulama (GET /api/egitimler/{egitimId}/videolar)
        // =========================================================================
        var getVideosResponse = await _client.GetAsync($"/api/egitimler/{egitimId}/videolar");
        getVideosResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var videosList = await getVideosResponse.Content.ReadFromJsonAsync<System.Collections.Generic.List<VideoListDto>>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        videosList.Should().NotBeNull();
        videosList.Should().NotBeEmpty();
        videosList.Should().Contain(v => v.Id == uploadedVideo.Id);

        // =========================================================================
        // ADIM 6: İçerik Taleplerini Listeleme (GET /api/egitimler/{egitimId}/content-requests)
        // =========================================================================
        var listRequestsResponse = await _client.GetAsync($"/api/egitimler/{egitimId}/content-requests");
        listRequestsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
