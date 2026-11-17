using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public class StudioQaAskRequestDto
{
    public Guid? EgitimId { get; set; }
    public Guid? ContentRequestId { get; set; }
    public int? BolumNo { get; set; }
    public Guid? KaynakId { get; set; }
    public string Kapsam { get; set; } = "Bolum"; // "Bolum" | "Plan" | "Kaynak"
    public string Soru { get; set; } = string.Empty;
}

public class StudioQaSuggestionDto
{
    public int? HedefBolumNo { get; set; }
    public string HedefAlan { get; set; } = "VideoPlani"; // "VideoPlani" | "ArastirmaOzeti"
    public string Aciklama { get; set; } = string.Empty;
    public string EskiMetin { get; set; } = string.Empty;
    public string YeniMetin { get; set; } = string.Empty;
    public bool Uygulandi { get; set; }
}

public class StudioQaMessageDto
{
    public Guid Id { get; set; }
    public string Kapsam { get; set; } = string.Empty;
    public int? BolumNo { get; set; }
    public Guid? KaynakId { get; set; }
    public string Rol { get; set; } = string.Empty;
    public string Mesaj { get; set; } = string.Empty;
    public StudioQaSuggestionDto? Oneri { get; set; }
    public string? KullanilanBaglamOzeti { get; set; }
    public DateTime OlusturmaTarihi { get; set; }
}

public class StudioQaApplySuggestionDto
{
    public Guid? MesajId { get; set; }
    public Guid ContentRequestId { get; set; }
    public int HedefBolumNo { get; set; }
    public string HedefAlan { get; set; } = "VideoPlani";
    public string EskiMetin { get; set; } = string.Empty;
    public string YeniMetin { get; set; } = string.Empty;
}

public interface IStudioQaService
{
    Task<IReadOnlyList<StudioQaMessageDto>> GetHistoryAsync(
        Guid? egitimId,
        Guid? contentRequestId,
        int? bolumNo,
        Guid? kaynakId,
        string kapsam,
        CancellationToken ct = default);

    Task<StudioQaMessageDto> AskAsync(
        StudioQaAskRequestDto request,
        CancellationToken ct = default);

    Task<(bool Basarili, string Mesaj, int? HedefBolumNo)> ApplySuggestionAsync(
        StudioQaApplySuggestionDto dto,
        CancellationToken ct = default);

    Task ClearHistoryAsync(
        Guid? egitimId,
        Guid? contentRequestId,
        int? bolumNo,
        Guid? kaynakId,
        string kapsam,
        CancellationToken ct = default);

    bool IsThreadProcessing(
        Guid? egitimId,
        Guid? contentRequestId,
        int? bolumNo,
        Guid? kaynakId,
        string kapsam);
}
