using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public interface IDokumanService
{
    Task<Guid> UploadDokumanAsync(Guid egitimId, string fileName, string contentType, System.IO.Stream fileStream);
    Task<IEnumerable<object>> GetDokumanlarByEgitimIdAsync(Guid egitimId);
    Task DeleteDokumanAsync(Guid dokumanId);
}
