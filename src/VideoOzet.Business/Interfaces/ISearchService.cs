using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Interfaces;

public interface ISearchService
{
    Task<SearchResultDto> SearchAsync(SearchFilterDto filters);
    Task IndexListingAsync(ListingDto listing);
    Task DeleteListingIndexAsync(Guid listingId);
}
