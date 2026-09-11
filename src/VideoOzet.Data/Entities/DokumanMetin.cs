using System;

namespace VideoOzet.Data.Entities;

public class DokumanMetin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DokumanId { get; set; }
    public string HamMetin { get; set; } = string.Empty;
    public int KelimeSayisi { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    public Dokuman Dokuman { get; set; } = null!;
}
