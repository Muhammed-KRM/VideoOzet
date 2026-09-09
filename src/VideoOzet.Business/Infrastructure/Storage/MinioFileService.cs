using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.Storage;

public class MinioFileService : IFileStorageService
{
    private readonly IMinioClient _minioClient;
    private readonly ILogger<MinioFileService> _logger;
    private readonly string _bucketName;

    public MinioFileService(IMinioClient minioClient, IConfiguration configuration, ILogger<MinioFileService> logger)
    {
        _minioClient = minioClient;
        _logger = logger;
        _bucketName = configuration.GetValue<string>("Minio:BucketName") ?? "videolar";
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
    {
        try
        {
            var bucketExistsArgs = new BucketExistsArgs().WithBucket(_bucketName);
            bool found = await _minioClient.BucketExistsAsync(bucketExistsArgs);
            if (!found)
            {
                var makeBucketArgs = new MakeBucketArgs().WithBucket(_bucketName);
                await _minioClient.MakeBucketAsync(makeBucketArgs);
            }

            var putObjectArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(contentType);

            await _minioClient.PutObjectAsync(putObjectArgs);
            _logger.LogInformation("File {FileName} uploaded successfully to MinIO bucket {BucketName}.", fileName, _bucketName);

            return fileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName} to MinIO.", fileName);
            throw new Exception("File upload failed", ex);
        }
    }

    public async Task DeleteFileAsync(string fileName)
    {
        try
        {
            var removeObjectArgs = new RemoveObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName);
            
            await _minioClient.RemoveObjectAsync(removeObjectArgs);
            _logger.LogInformation("File {FileName} deleted successfully from MinIO bucket {BucketName}.", fileName, _bucketName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileName} from MinIO.", fileName);
            throw new Exception("File deletion failed", ex);
        }
    }

    public async Task<string> GetFileUrlAsync(string fileName)
    {
        try
        {
            var presignedGetObjectArgs = new PresignedGetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithExpiry(60 * 60); // 1 hour
            
            var url = await _minioClient.PresignedGetObjectAsync(presignedGetObjectArgs);
            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating URL for file {FileName} from MinIO.", fileName);
            throw new Exception("URL generation failed", ex);
        }
    }

    public async Task<Stream> DownloadFileAsync(string fileName)
    {
        try
        {
            var memoryStream = new MemoryStream();
            var getObjectArgs = new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithCallbackStream(stream =>
                {
                    stream.CopyTo(memoryStream);
                });

            await _minioClient.GetObjectAsync(getObjectArgs);
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileName} from MinIO.", fileName);
            throw new Exception("File download failed", ex);
        }
    }
}
