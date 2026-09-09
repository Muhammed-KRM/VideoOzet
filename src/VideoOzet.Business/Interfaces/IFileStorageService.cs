using System.IO;
using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public interface IFileStorageService
{
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType);
    Task DeleteFileAsync(string fileName);
    Task<string> GetFileUrlAsync(string fileName);
    Task<Stream> DownloadFileAsync(string fileName);
}
