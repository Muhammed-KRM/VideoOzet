using Microsoft.EntityFrameworkCore;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Repositories;

namespace VideoOzet.Business.Services;

public class ReviewManager : IReviewService
{
    private const string EC_GETBYLISTING = "RM-001";
    private const string EC_CREATE       = "RM-002";
    private const string EC_APPROVE      = "RM-003";

    private readonly AppDbContext _context;
    private readonly IListingRepository _listingRepo;
    private readonly ILogService _logService;

    public ReviewManager(AppDbContext context, IListingRepository listingRepo, ILogService logService)
    {
        _context = context;
        _listingRepo = listingRepo;
        _logService = logService;
    }

    public async Task<List<ReviewDto>> GetByListingAsync(Guid listingId)
    {
        try
        {
            return await _context.Reviews
                .Include(r => r.Reviewer)
                .Where(r => r.ListingId == listingId && r.IsApproved)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => MapToDto(r))
                .ToListAsync();
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_GETBYLISTING, ex, listingId); throw; }
    }

    public async Task<ReviewDto> CreateAsync(ReviewCreateDto dto, Guid reviewerId)
    {
        try
        {
            var listing = await _listingRepo.GetByIdAsync(dto.ListingId)
                ?? throw new NotFoundException("İlan", dto.ListingId);

            if (listing.OwnerId == reviewerId)
                throw new BusinessException("Kendi ilanınıza yorum yapamazsınız.");

            var review = new Review
            {
                ReviewerId = reviewerId,
                ReviewedId = listing.OwnerId,
                ListingId = dto.ListingId,
                ProfessionalismRating = dto.ProfessionalismRating,
                CommunicationRating = dto.CommunicationRating,
                ValueRating = dto.ValueRating,
                Content = dto.ReviewText,
                IsApproved = true
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            // Listing AverageRating ve ReviewCount güncelle
            var allReviews = await _context.Reviews
                .Where(r => r.ListingId == dto.ListingId && r.IsApproved)
                .ToListAsync();

            listing.AverageRating = allReviews.Any() ? allReviews.Average(r => r.AverageRating) : 0;
            listing.ReviewCount = allReviews.Count;
            _listingRepo.Update(listing);
            await _listingRepo.SaveChangesAsync();

            // Reviewer bilgisiyle birlikte döndür
            var saved = await _context.Reviews
                .Include(r => r.Reviewer)
                .FirstOrDefaultAsync(r => r.Id == review.Id);

            return MapToDto(saved ?? review);
        }
        catch (BusinessException) { throw; }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_CREATE, ex, dto, reviewerId); throw; }
    }

    public async Task ApproveReviewAsync(Guid reviewId)
    {
        try
        {
            var review = await _context.Reviews.FindAsync(reviewId)
                ?? throw new NotFoundException("Yorum", reviewId);
            review.IsApproved = true;
            await _context.SaveChangesAsync();
        }
        catch (NotFoundException) { throw; }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_APPROVE, ex, reviewId); throw; }
    }

    private static ReviewDto MapToDto(Review r) => new()
    {
        Id = r.Id,
        ReviewerId = r.ReviewerId,
        ReviewerName = r.Reviewer?.FullName ?? "Kullanıcı",
        ReviewerImageUrl = r.Reviewer?.ProfileImageUrl,
        ProfessionalismRating = r.ProfessionalismRating,
        CommunicationRating = r.CommunicationRating,
        ValueRating = r.ValueRating,
        AverageRating = r.AverageRating,
        Content = r.Content,
        CreatedAt = r.CreatedAt
    };
}
