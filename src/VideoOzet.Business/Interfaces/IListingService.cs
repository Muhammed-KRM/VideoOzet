using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Interfaces;

public interface IListingService
{
    Task<SearchResultDto> SearchAsync(SearchFilterDto filters, CancellationToken cancellationToken = default);
    Task<ListingDto?> GetByIdAsync(Guid id);
    Task<ListingDto?> GetBySlugAsync(string slug);
    Task<List<ListingDto>> GetVitrinListingsAsync();
    Task<ListingDto> CreateAsync(ListingCreateDto dto, Guid userId);
    Task<ListingDto> UpdateAsync(Guid id, ListingUpdateDto dto, Guid userId);
    Task DeleteAsync(Guid id, Guid userId);
    Task<List<ListingDto>> GetMyListingsAsync(Guid userId);
}
