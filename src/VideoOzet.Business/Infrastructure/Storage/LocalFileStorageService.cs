using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.Storage;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _uploadPath;
    private readonly ILogger<LocalFileStorageService> _logger;

    // İzin verilen dosya türleri ve maksimum boyut
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public LocalFileStorageService(IConfiguration config, ILogger<LocalFileStorageService> logger)
    {
        _logger = logger;
        _uploadPath = config["FileStorage:UploadPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        
        // Klasör yoksa oluştur
        if (!Directory.Exists(_uploadPath))
        {
            Directory.CreateDirectory(_uploadPath);
            _logger.LogInformation("Upload dizini oluşturuldu: {Path}", _uploadPath);
        }
    }

    public async Task<string> UploadAsync(Stream fileStream, string fileName, string contentType)
    {
        // ── Güvenlik Kontrolleri ──
        
        // 1. Dosya boyutu kontrolü
        if (fileStream.Length > MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"Dosya boyutu çok büyük. Maksimum {MaxFileSizeBytes / (1024 * 1024)} MB yüklenebilir.");
        }

        // 2. Dosya uzantısı kontrolü
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"Desteklenmeyen dosya türü: {extension}. İzin verilenler: {string.Join(", ", AllowedExtensions)}");
        }

        // 3. Content-Type kontrolü
        var allowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png", "image/webp"
        };
        if (!allowedContentTypes.Contains(contentType))
        {
            throw new InvalidOperationException($"Desteklenmeyen içerik türü: {contentType}");
        }

        // ── Dosya Kaydetme ──
        
        // Benzersiz dosya adı oluştur (GUID bazlı, path traversal engellenir)
        var safeFileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(_uploadPath, safeFileName);

        await using var outputStream = new FileStream(filePath, FileMode.Create);
        await fileStream.CopyToAsync(outputStream);

        _logger.LogInformation("Dosya yüklendi: {FileName} → {FilePath} ({Size} bytes)", fileName, safeFileName, fileStream.Length);

        // Dosyanın URL'ini döndür (relative path)
        return $"/uploads/{safeFileName}";
    }

    public Task DeleteAsync(string fileUrl)
    {
        if (string.IsNullOrEmpty(fileUrl)) return Task.CompletedTask;

        // URL'den dosya adını çıkar
        var fileName = Path.GetFileName(fileUrl);
        var filePath = Path.Combine(_uploadPath, fileName);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("Dosya silindi: {FilePath}", filePath);
        }

        return Task.CompletedTask;
    }
}
