namespace VideoOzet.Data.Enums;

/// <summary>
/// Vektör indeksindeki metin parçasının (chunk) kaynağını ve işlevsel rolünü belirtir.
/// </summary>
public enum ChunkTipi
{
    /// <summary>Doküman ham tam metninden üretilen parça.</summary>
    HamMetin = 0,

    /// <summary>Video ham transkriptinden üretilen zaman damgalı parça.</summary>
    Transkript = 1,

    /// <summary>Hafif kimlik/yönlendirme özeti parçası.</summary>
    OzetKart = 2,

    /// <summary>Akademik PDF sonundaki Kaynakça/References bölümü (varsayılan aramada filtrelenir).</summary>
    Kaynakca = 3
}
