using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.Storage;

public class LocalFileStorageService : IFileStorageService
{
    private readonly ILogger<LocalFileStorageService> _logger;
    private readonly string _storageRoot;

    public LocalFileStorageService(ILogger<LocalFileStorageService> logger)
    {
        _logger = logger;
        _storageRoot = Path.Combine(AppContext.BaseDirectory, "Storage", "videolar");
        Directory.CreateDirectory(_storageRoot);
    }

    private string GetSafeFilePath(string fileName)
    {
        var normalized = fileName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_storageRoot, normalized));
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return fullPath;
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
    {
        var fullPath = GetSafeFilePath(fileName);
        using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await fileStream.CopyToAsync(fs);
        }
        _logger.LogInformation("Dosya yerel depolamaya kaydedildi: {FullPath}", fullPath);
        return fileName;
    }

    public Task DeleteFileAsync(string fileName)
    {
        var fullPath = GetSafeFilePath(fileName);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
        return Task.CompletedTask;
    }

    public Task<string> GetFileUrlAsync(string fileName)
    {
        var fullPath = GetSafeFilePath(fileName);
        return Task.FromResult(fullPath);
    }

    public async Task<Stream> DownloadFileAsync(string fileName)
    {
        var fullPath = GetSafeFilePath(fileName);
        var ms = new MemoryStream();
        using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await fs.CopyToAsync(ms);
        }
        ms.Position = 0;
        return ms;
    }
}
