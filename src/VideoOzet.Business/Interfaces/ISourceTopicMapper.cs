using System;
using System.Threading;
using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public interface ISourceTopicMapper
{
    /// <summary>
    /// Eğitimdeki tüm kaynakları (videolar ve dokümanlar) tek tek analiz ederek konularını çıkarır (Map) 
    /// ve genel analiz (Reduce) için özet bir konu haritası metni oluşturur.
    /// Sonuçları KaynakKonuCikarimi tablosunda önbelleğe alır.
    /// </summary>
    Task<string> BuildTopicDigestAsync(Guid egitimId, CancellationToken ct = default);
}
