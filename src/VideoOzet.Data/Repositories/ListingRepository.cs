using Microsoft.EntityFrameworkCore;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Data.Repositories;

public class ListingRepository : GenericRepository<Listing>, IListingRepository
{
    public ListingRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Listing?> GetBySlugWithDetailsAsync(string slug)
    {
        return await _dbSet
            .Include(l => l.Owner)
            .Include(l => l.Branch)
            .Include(l => l.District)
                .ThenInclude(d => d.City)
            .Include(l => l.Images)
            .FirstOrDefaultAsync(l => l.Slug == slug && l.Status == ListingStatus.Active);
    }

    public async Task<List<Listing>> GetActiveListingsByOwnerAsync(Guid ownerId)
    {
        return await _dbSet
            .Include(l => l.Branch)
            .Where(l => l.OwnerId == ownerId && l.Status == ListingStatus.Active)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Listing>> GetAllListingsByOwnerAsync(Guid ownerId)
    {
        // Yalnızca kalıcı olarak silinen (Closed) hariç tüm ilanları getir
        return await _dbSet
            .Include(l => l.Branch)
            .Where(l => l.OwnerId == ownerId && l.Status != ListingStatus.Closed)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Listing>> SearchWithDetailsAsync()
    {
        return await _dbSet
            .Include(l => l.Owner)
            .Include(l => l.Branch)
            .Include(l => l.District)
                .ThenInclude(d => d.City)
            .Include(l => l.Images)
            .Where(l => l.Status == ListingStatus.Active)
            .ToListAsync();
    }

    public IQueryable<Listing> GetActiveWithDetailsQueryable()
    {
        return _dbSet
            .Include(l => l.Owner)
            .Include(l => l.Branch)
            .Include(l => l.District)
                .ThenInclude(d => d.City)
            .Include(l => l.Images)
            .Where(l => l.Status == ListingStatus.Active)
            .AsSplitQuery()
            .AsNoTracking();
    }
}
