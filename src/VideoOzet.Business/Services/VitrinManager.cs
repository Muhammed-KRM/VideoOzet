using VideoOzet.Business.DTOs;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Repositories;

namespace VideoOzet.Business.Services;

public class VitrinManager : IVitrinService
{
    // ═══════════════════════════════════════════════
    // HATA KODLARI — VitrinManager (Prefix: VM)
    // ═══════════════════════════════════════════════
    private const string EC_GETPACKAGES = "VM-001"; // GetPackagesAsync
    private const string EC_PURCHASE    = "VM-002"; // PurchaseVitrinAsync
    // ═══════════════════════════════════════════════

    private readonly IRepository<VitrinPackage> _packageRepo;
    private readonly IListingRepository _listingRepo;
    private readonly ISearchService _searchService;
    private readonly IListingService _listingService;
    private readonly ILogService _logService;

    public VitrinManager(
        IRepository<VitrinPackage> packageRepo,
        IListingRepository listingRepo,
        ISearchService searchService,
        IListingService listingService,
        ILogService logService)
    {
        _packageRepo = packageRepo;
        _listingRepo = listingRepo;
        _searchService = searchService;
        _listingService = listingService;
        _logService = logService;
    }

    public async Task<List<VitrinPackageDto>> GetPackagesAsync()
    {
        try
        {
            var packages = await _packageRepo.GetAllAsync();
            return packages.Select(p => new VitrinPackageDto
            {
                Id = p.Id, Name = p.Name, DurationInDays = p.DurationInDays, Price = p.Price,
                IncludesAmberGlow = p.IncludesAmberGlow, IncludesTopRanking = p.IncludesTopRanking, IncludesHomeCarousel = p.IncludesHomeCarousel
            }).ToList();
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_GETPACKAGES, ex); throw; }
    }

    public async Task PurchaseVitrinAsync(Guid listingId, int packageId, Guid userId)
    {
        try
        {
        var listing = await _listingRepo.GetByIdAsync(listingId) ?? throw new NotFoundException("İlan", listingId);
        if (listing.OwnerId != userId) throw new UnauthorizedException("Bu ilana vitrin paketi alma yetkiniz yok.");

        var package = (await _packageRepo.FindAsync(p => p.Id == packageId)).FirstOrDefault()
            ?? throw new NotFoundException("Vitrin Paketi", packageId);

        var now = DateTime.UtcNow;
        if (listing.IsVitrin && listing.VitrinExpiresAt.HasValue && listing.VitrinExpiresAt.Value > now)
            listing.VitrinExpiresAt = listing.VitrinExpiresAt.Value.AddDays(package.DurationInDays);
        else
        {
            listing.IsVitrin = true;
            listing.VitrinExpiresAt = now.AddDays(package.DurationInDays);
        }

        _listingRepo.Update(listing);
        await _listingRepo.SaveChangesAsync();

        var listingDto = await _listingService.GetByIdAsync(listingId);
        if (listingDto != null) await _searchService.IndexListingAsync(listingDto);
        }
        catch (NotFoundException) { throw; }
        catch (UnauthorizedException) { throw; }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_PURCHASE, ex, new { listingId, packageId }, userId); throw; }
    }
}
