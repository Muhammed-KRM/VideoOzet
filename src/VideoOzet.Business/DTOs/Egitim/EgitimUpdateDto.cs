using System;

namespace VideoOzet.Business.DTOs.Egitim;

public class EgitimUpdateDto
{
    public Guid Id { get; set; }
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
}
