using FluentValidation;
using Ganss.Xss;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Events;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Data.Repositories;

namespace VideoOzet.Business.Services;

public class ListingManager : IListingService
{
    // ═══════════════════════════════════════════════
    // HATA KODLARI — ListingManager (Prefix: LM)
    // ═══════════════════════════════════════════════
    private const string EC_CREATE   = "LM-001"; // CreateAsync
    private const string EC_GETBYID  = "LM-002"; // GetByIdAsync
    private const string EC_GETSLUG  = "LM-003"; // GetBySlugAsync
    private const string EC_VITRIN   = "LM-004"; // GetVitrinListingsAsync
    private const string EC_MYLIST   = "LM-005"; // GetMyListingsAsync
    private const string EC_UPDATE   = "LM-006"; // UpdateAsync
    private const string EC_DELETE   = "LM-007"; // DeleteAsync
    private const string EC_SEARCH   = "LM-008"; // SearchAsync
    // ═══════════════════════════════════════════════

    private readonly IListingRepository _listingRepo;
    private readonly IRepository<Branch> _branchRepo;
    private readonly IRepository<District> _districtRepo;
    private readonly IValidator<ListingCreateDto> _createValidator;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ITokenService _tokenService;
    private readonly ISettingService _settingService;
    private readonly ILogService _logService;
    private readonly IModerationService _moderationService;

    public ListingManager(
        IListingRepository listingRepo,
        IRepository<Branch> branchRepo,
        IRepository<District> districtRepo,
        IValidator<ListingCreateDto> createValidator,
        IPublishEndpoint publishEndpoint,
        ITokenService tokenService,
        ISettingService settingService,
        ILogService logService,
        IModerationService moderationService)
    {
        _listingRepo = listingRepo;
        _branchRepo = branchRepo;
        _districtRepo = districtRepo;
        _createValidator = createValidator;
        _publishEndpoint = publishEndpoint;
        _tokenService = tokenService;
        _settingService = settingService;
        _logService = logService;
        _moderationService = moderationService;
    }

    public async Task<ListingDto> CreateAsync(ListingCreateDto dto, Guid userId)
    {
        try
        {
        // 1. FluentValidation ile doğrula
        var validationResult = await _createValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
            throw new BusinessException(string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage)));

        // 2. Slug oluştur
        var slug = SlugHelper.GenerateSlug(dto.Title);

        // 3. XSS Koruması ve Entity'ye map et
        var sanitizer = new HtmlSanitizer();
        string sanitizedDescription = sanitizer.Sanitize(dto.Description);

        // Moderasyon kontrolü — kullanıcıya açıklama yap
        var modResult = _moderationService.CheckContent(dto.Title, sanitizedDescription);
        var listingStatus = modResult.IsViolation ? ListingStatus.Pending : ListingStatus.Active;

        var listing = new Listing
        {
            OwnerId = userId,
            Type = dto.Type,
            Title = dto.Title,
            Slug = slug,
            Description = sanitizedDescription,
            HourlyPrice = dto.HourlyPrice,
            LessonType = dto.LessonType,
            BranchId = dto.BranchId,
            DistrictId = dto.DistrictId,
            Status = listingStatus,
            // Madde 7 — Yeni alanlar
            EducationLevel = dto.EducationLevel,
            ExperienceYears = dto.ExperienceYears,
            LessonDurationMinutes = dto.LessonDurationMinutes,
            IsGroupLesson = dto.IsGroupLesson,
            MaxGroupSize = dto.IsGroupLesson ? dto.MaxGroupSize : null,
            HasTrialLesson = dto.HasTrialLesson,
            EducationBackground = dto.EducationBackground,
            GradeMin = dto.GradeMin,
            GradeMax = dto.GradeMax,
        };

        // 4. Jeton Harcaması
        var cost = await _settingService.GetIntSettingAsync("ListingCreationCost", 5);
        await _tokenService.SpendTokenAsync(userId, cost, "Yeni ilan oluşturuldu");

        // 5. Repository ile kaydet
        await _listingRepo.AddAsync(listing);
        await _listingRepo.SaveChangesAsync();

        // 5. Event fırlat (Consumer ES'e indexleyecek + cache'i temizleyecek)
        await _publishEndpoint.Publish(new ListingCreatedEvent { ListingId = listing.Id });

        // 6. Bildirim gönder
        if (modResult.IsViolation)
        {
            // Moderasyon ihlali — uyarı bildirimi
            await _publishEndpoint.Publish(new SendNotificationEvent
            {
                UserId = userId,
                Type = "Warning",
                Title = "İlan İçeriği Uyarısı",
                Message = $"İlanınız incelemeye alındı. Sebep: {modResult.Message}",
                ActionUrl = "/panel/ilanlarim",
                SendEmail = true,
                UserEmail = listing.Owner?.Email
            });
        }
        else
        {
            // Normal ilan oluşturma bildirimi
            await _publishEndpoint.Publish(new SendNotificationEvent
            {
                UserId = userId,
                Type = "ListingPending",
                Title = "İlanınız Oluşturuldu",
                Message = $"\"{listing.Title}\" başlıklı ilanınız başarıyla oluşturuldu ve yayında.",
                ActionUrl = $"/ilan/{listing.Slug}",
                SendEmail = true,
                UserEmail = listing.Owner?.Email
            });
        }

        // 7. DTO olarak döndür — moderasyon mesajını da ekle
        var resultDto = MapToDto(listing);
        if (modResult.IsViolation)
            resultDto.ModerationMessage = modResult.Message;
        return resultDto;
        }
        catch (BusinessException) { throw; }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync(EC_CREATE, ex, dto, userId);
            throw;
        }
    }

    public async Task<ListingDto?> GetByIdAsync(Guid id)
    {
        try
        {
            var listing = await _listingRepo.GetByIdAsync(id);
            return listing is null ? null : MapToDto(listing);
        }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync(EC_GETBYID, ex, id);
            throw;
        }
    }

    public async Task<ListingDto?> GetBySlugAsync(string slug)
    {
        try
        {
            var listing = await _listingRepo.GetBySlugWithDetailsAsync(slug);
            return listing is null ? null : MapToDto(listing);
        }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync(EC_GETSLUG, ex, slug);
            throw;
        }
    }

    public async Task<List<ListingDto>> GetVitrinListingsAsync()
    {
        try
        {
            var listings = await _listingRepo.FindAsync(l => l.IsVitrin && l.Status == ListingStatus.Active);
            return listings.Select(MapToDto).ToList();
        }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync(EC_VITRIN, ex);
            throw;
        }
    }

    public async Task<List<ListingDto>> GetMyListingsAsync(Guid userId)
    {
        try
        {
            var listings = await _listingRepo.GetAllListingsByOwnerAsync(userId);
            return listings.Select(MapToDto).ToList();
        }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync(EC_MYLIST, ex, userId, userId);
            throw;
        }
    }

    public async Task<ListingDto> UpdateAsync(Guid id, ListingUpdateDto dto, Guid userId)
    {
        try
        {
        var listing = await _listingRepo.GetByIdAsync(id)
            ?? throw new NotFoundException("İlan", id);

        if (listing.OwnerId != userId)
            throw new UnauthorizedException("Bu ilanı düzenleme yetkiniz yok.");

        var sanitizer = new HtmlSanitizer();
        string sanitizedDescription = sanitizer.Sanitize(dto.Description);

        listing.Type = dto.Type;
        listing.Title = dto.Title;
        listing.Slug = SlugHelper.GenerateSlug(dto.Title);
        listing.Description = sanitizedDescription;
        listing.HourlyPrice = dto.HourlyPrice;
        listing.LessonType = dto.LessonType;
        listing.BranchId = dto.BranchId;
        listing.DistrictId = dto.DistrictId;
        // Madde 7 — Yeni alanlar
        listing.EducationLevel = dto.EducationLevel;
        listing.ExperienceYears = dto.ExperienceYears;
        listing.LessonDurationMinutes = dto.LessonDurationMinutes;
        listing.IsGroupLesson = dto.IsGroupLesson;
        listing.MaxGroupSize = dto.IsGroupLesson ? dto.MaxGroupSize : null;
        listing.HasTrialLesson = dto.HasTrialLesson;
        listing.EducationBackground = dto.EducationBackground;
        listing.GradeMin = dto.GradeMin;
        listing.GradeMax = dto.GradeMax;
        
        var modResult = _moderationService.CheckContent(dto.Title, sanitizedDescription);
        listing.Status = modResult.IsViolation
            ? ListingStatus.Pending
            : (dto.IsActive ? ListingStatus.Active : ListingStatus.Suspended);

        _listingRepo.Update(listing);
        await _listingRepo.SaveChangesAsync();

        await _publishEndpoint.Publish(new ListingUpdatedEvent { ListingId = listing.Id });

        return MapToDto(listing);
        }
        catch (BusinessException) { throw; }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync(EC_UPDATE, ex, new { id, dto }, userId);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id, Guid userId)
    {
        try
        {
        var listing = await _listingRepo.GetByIdAsync(id)
            ?? throw new NotFoundException("İlan", id);

        if (listing.OwnerId != userId)
            throw new UnauthorizedException("Bu ilanı silme yetkiniz yok.");

        listing.Status = ListingStatus.Closed;
        _listingRepo.Update(listing);
        await _listingRepo.SaveChangesAsync();

        await _publishEndpoint.Publish(new ListingDeletedEvent { ListingId = listing.Id });
        }
        catch (BusinessException) { throw; }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync(EC_DELETE, ex, new { id }, userId);
            throw;
        }
    }

    public async Task<SearchResultDto> SearchAsync(SearchFilterDto filters, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
        // IQueryable — filtreler veritabanında uygulanır, tüm veri belleğe çekilmez
        var query = _listingRepo.GetActiveWithDetailsQueryable();

        // Metin araması
        if (!string.IsNullOrWhiteSpace(filters.Query))
        {
            var q = filters.Query.ToLower();
            query = query.Where(l =>
                l.Title.ToLower().Contains(q) ||
                l.Description.ToLower().Contains(q) ||
                (l.Branch != null && l.Branch.Name.ToLower().Contains(q)));
        }

        // Branş filtresi
        if (!string.IsNullOrWhiteSpace(filters.Branch))
            query = query.Where(l => l.Branch != null && l.Branch.Name.ToLower().Contains(filters.Branch.ToLower()));

        // Şehir filtresi
        if (!string.IsNullOrWhiteSpace(filters.City))
            query = query.Where(l => l.District != null && l.District.City != null &&
                l.District.City.Name.ToLower().Contains(filters.City.ToLower()));

        // İlçe filtresi
        if (!string.IsNullOrWhiteSpace(filters.District))
            query = query.Where(l => l.District != null && l.District.Name.ToLower().Contains(filters.District.ToLower()));

        // Fiyat filtresi
        if (filters.MinPrice.HasValue)
            query = query.Where(l => l.HourlyPrice >= filters.MinPrice.Value);
        if (filters.MaxPrice.HasValue)
            query = query.Where(l => l.HourlyPrice <= filters.MaxPrice.Value);

        // Ders türü filtresi
        if (!string.IsNullOrWhiteSpace(filters.LessonType) &&
            Enum.TryParse<LessonType>(filters.LessonType, true, out var lessonType))
            query = query.Where(l => l.LessonType == lessonType || l.LessonType == LessonType.Both);

        // İlan türü filtresi
        if (!string.IsNullOrWhiteSpace(filters.ListingType) &&
            Enum.TryParse<ListingType>(filters.ListingType, true, out var listingType))
            query = query.Where(l => l.Type == listingType);

        // Eğitim seviyesi filtresi
        if (!string.IsNullOrWhiteSpace(filters.EducationLevel))
            query = query.Where(l => l.EducationLevel == filters.EducationLevel);

        // Deneme dersi filtresi
        if (filters.HasTrialLesson == true)
            query = query.Where(l => l.HasTrialLesson);

        // Grup dersi filtresi
        if (filters.IsGroupLesson == true)
            query = query.Where(l => l.IsGroupLesson);

        // Min deneyim filtresi
        if (filters.MinExperienceYears.HasValue)
            query = query.Where(l => l.ExperienceYears >= filters.MinExperienceYears.Value);

        // Sınıf filtresi
        if (filters.GradeLevel.HasValue)
            query = query.Where(l =>
                l.GradeMin.HasValue && l.GradeMax.HasValue &&
                l.GradeMin.Value <= filters.GradeLevel.Value &&
                l.GradeMax.Value >= filters.GradeLevel.Value);

        // Kategori filtresi — Branch.Category ile
        if (!string.IsNullOrWhiteSpace(filters.CategorySlug))
        {
            var categoryName = filters.CategorySlug.ToLower() switch
            {
                "akademik" => "Akademik",
                "sinav"    => "Sınav Hazırlık",
                "yazilim"  => "Yazılım",
                "muzik"    => "Müzik",
                "spor"     => "Spor",
                "dil"      => "Dil",
                _          => null
            };
            if (categoryName != null)
                query = query.Where(l => l.Branch != null && l.Branch.Category == categoryName);
        }

        // Sıralama
        query = filters.SortBy switch
        {
            "price_asc"  => query.OrderBy(l => l.HourlyPrice),
            "price_desc" => query.OrderByDescending(l => l.HourlyPrice),
            "rating"     => query.OrderByDescending(l => l.AverageRating),
            "newest"     => query.OrderByDescending(l => l.CreatedAt),
            _            => query.OrderByDescending(l => l.IsVitrin).ThenByDescending(l => l.CreatedAt)
        };

        // Toplam sayı — Include olmadan count (daha hızlı, navigation property gerekmez)
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((filters.Page - 1) * filters.PageSize)
            .Take(filters.PageSize)
            .ToListAsync(cancellationToken);

        sw.Stop();
        // Performans logu — sadece yavaş sorgular için (>500ms)
        if (sw.ElapsedMilliseconds > 500)
            await _logService.LogFunctionErrorAsync("SEARCH_SLOW",
                new Exception($"Yavaş arama: {sw.ElapsedMilliseconds}ms, filters={System.Text.Json.JsonSerializer.Serialize(filters)}"),
                filters);

        return new SearchResultDto
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = filters.Page,
            PageSize = filters.PageSize
        };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            // İptal edildi — normal durum, loglama yapma
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _logService.LogFunctionErrorAsync(EC_SEARCH, ex, filters);
            throw;
        }
    }

    private static ListingDto MapToDto(Listing l) => new()
    {
        Id = l.Id,
        OwnerId = l.OwnerId,
        OwnerName = l.Owner?.FullName ?? "",
        OwnerImageUrl = l.Owner?.ProfileImageUrl,
        Type = l.Type,
        Title = l.Title,
        Slug = l.Slug,
        Description = l.Description,
        HourlyPrice = l.HourlyPrice,
        LessonType = l.LessonType,
        BranchName = l.Branch?.Name ?? "",
        CityName = l.District?.City?.Name ?? "",
        DistrictName = l.District?.Name ?? "",
        IsVitrin = l.IsVitrin,
        AverageRating = l.AverageRating,
        ReviewCount = l.ReviewCount,
        Status = l.Status,
        CreatedAt = l.CreatedAt,
        ImageUrls = l.Images?.Select(i => i.ImageUrl).ToList() ?? [],
        // Madde 7 — Yeni alanlar
        EducationLevel = l.EducationLevel,
        ExperienceYears = l.ExperienceYears,
        LessonDurationMinutes = l.LessonDurationMinutes,
        IsGroupLesson = l.IsGroupLesson,
        MaxGroupSize = l.MaxGroupSize,
        HasTrialLesson = l.HasTrialLesson,
        EducationBackground = l.EducationBackground,
        GradeMin = l.GradeMin,
        GradeMax = l.GradeMax,
    };
}
