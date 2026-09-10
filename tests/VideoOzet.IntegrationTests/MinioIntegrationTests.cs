using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using VideoOzet.Business.Interfaces;
using Xunit;

namespace VideoOzet.IntegrationTests;

public class MinioIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public MinioIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Upload_Download_And_Delete_File_With_Minio_Should_Succeed()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var storageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        var testContent = "Test MinIO Video or Audio Content File Payload";
        var fileName = $"test_{Guid.NewGuid():N}.mp4";
        var contentType = "video/mp4";
        var contentBytes = Encoding.UTF8.GetBytes(testContent);

        // Act 1: Upload
        using (var uploadStream = new MemoryStream(contentBytes))
        {
            var uploadedName = await storageService.UploadFileAsync(uploadStream, fileName, contentType);
            Assert.Equal(fileName, uploadedName);
        }

        // Act 2: Download & Verify Content
        using (var downloadStream = await storageService.DownloadFileAsync(fileName))
        {
            using var reader = new StreamReader(downloadStream, Encoding.UTF8);
            var downloadedContent = await reader.ReadToEndAsync();
            Assert.Equal(testContent, downloadedContent);
        }

        // Act 3: Get Presigned URL
        var presignedUrl = await storageService.GetFileUrlAsync(fileName);
        Assert.NotNull(presignedUrl);
        Assert.Contains(fileName, presignedUrl);

        // Act 4: Delete
        await storageService.DeleteFileAsync(fileName);
    }
}
