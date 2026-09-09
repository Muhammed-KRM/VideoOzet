namespace VideoOzet.Business.DTOs;

// Arama filtreleri
public class SearchFilterDto
{
    public string? Query { get; set; }
    public string? Branch { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public int? MinPrice { get; set; }
    public int? MaxPrice { get; set; }
    public string? LessonType { get; set; }
    public string? ListingType { get; set; }
    public string? SortBy { get; set; } // "price_asc", "price_desc", "rating", "newest"
    // Madde 7.4 — Yeni filtre alanları
    public string? EducationLevel { get; set; }
    public bool? HasTrialLesson { get; set; }
    public bool? IsGroupLesson { get; set; }
    public int? MinExperienceYears { get; set; }
    // Sınıf filtresi — aranacak sınıf numarası (0=Anasınıfı, 1-12=Sınıf, 13=Üniversite, 14=Mezun)
    public int? GradeLevel { get; set; }
    // Kategori filtresi — kategori slug'ı (akademik, sinav, yazilim, muzik, spor, dil)
    public string? CategorySlug { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}

// Arama sonucu
public class SearchResultDto
{
    public List<ListingDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
