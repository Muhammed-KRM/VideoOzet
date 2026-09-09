namespace VideoOzet.Business.Interfaces;

public interface IFileStorageService
{
    Task<string> UploadAsync(Stream fileStream, string fileName, string contentType);
    Task DeleteAsync(string fileUrl);
}
