using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Interfaces;

public interface IReviewService
{
    Task<List<ReviewDto>> GetByListingAsync(Guid listingId);
    Task<ReviewDto> CreateAsync(ReviewCreateDto dto, Guid reviewerId);
    Task ApproveReviewAsync(Guid reviewId);
}
