using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Interfaces;

public interface IDokumanService
{
    Task<Guid> UploadDokumanAsync(Guid egitimId, string fileName, string contentType, Stream fileStream, KaynakKullanimTuru kullanimTuru = KaynakKullanimTuru.DogrudanReferans);
    Task<List<DokumanListDto>> GetDokumanlarByEgitimIdAsync(Guid egitimId);
    Task DeleteDokumanAsync(Guid dokumanId);
    Task RetryDokumanAsync(Guid dokumanId);
    Task ResetDokumanQueueForEgitimAsync(Guid egitimId);
    Task UpdateKullanimTuruAsync(Guid dokumanId, KaynakKullanimTuru kullanimTuru);
    Task<KaynakIcerikDetayDto?> GetDokumanIcerikDetayAsync(Guid dokumanId);
}
