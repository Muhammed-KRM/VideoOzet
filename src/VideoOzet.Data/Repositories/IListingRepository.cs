using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Repositories;

public interface IListingRepository : IRepository<Listing>
{
    Task<Listing?> GetBySlugWithDetailsAsync(string slug);
    Task<List<Listing>> GetActiveListingsByOwnerAsync(Guid ownerId);
    Task<List<Listing>> GetAllListingsByOwnerAsync(Guid ownerId);
    Task<List<Listing>> SearchWithDetailsAsync();
    IQueryable<Listing> GetActiveWithDetailsQueryable();
}
