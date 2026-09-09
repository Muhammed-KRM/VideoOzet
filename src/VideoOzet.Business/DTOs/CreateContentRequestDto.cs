using System;

namespace VideoOzet.Business.DTOs;

public class CreateContentRequestDto
{
    public string Konu { get; set; } = string.Empty;
    public string? HedefUzunluk { get; set; }
    public string? HedefKitle { get; set; }
}
