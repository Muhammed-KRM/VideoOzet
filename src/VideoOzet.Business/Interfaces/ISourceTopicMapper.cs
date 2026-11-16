using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Interfaces;

public interface ISourceTopicMapper
{
    /// <summary>
    /// Eğitimdeki tüm kaynakları (videolar ve dokümanlar) tek tek analiz ederek konularını çıkarır (Map) 
    /// ve genel analiz (Reduce) için özet bir konu haritası metni oluşturur.
    /// Sonuçları KaynakKonuCikarimi tablosunda önbelleğe alır.
    /// </summary>
    Task<string> BuildTopicDigestAsync(Guid egitimId, CancellationToken ct = default);

    /// <summary>
    /// Eğitimdeki tüm kaynakların yapılandırılmış konu ve meta veri kartlarını döner.
    /// </summary>
    Task<IReadOnlyList<CourseSourceCardDto>> GetStructuredSourceCardsAsync(Guid egitimId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CourseSourceCardDto>>(Array.Empty<CourseSourceCardDto>());
}
