using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Interfaces;

public interface IDokumanService
{
    Task<Guid> UploadDokumanAsync(Guid egitimId, string fileName, string contentType, System.IO.Stream fileStream, KaynakKullanimTuru kullanimTuru = KaynakKullanimTuru.DogrudanReferans);
    Task<IEnumerable<object>> GetDokumanlarByEgitimIdAsync(Guid egitimId);
    Task DeleteDokumanAsync(Guid dokumanId);
    Task UpdateKullanimTuruAsync(Guid dokumanId, KaynakKullanimTuru kullanimTuru);
    Task<KaynakIcerikDetayDto?> GetDokumanIcerikDetayAsync(Guid dokumanId);
}
