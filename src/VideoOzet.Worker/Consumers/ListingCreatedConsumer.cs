using MassTransit;
using VideoOzet.Business.Events;
using VideoOzet.Business.Interfaces;
using VideoOzet.Worker.Services;

namespace VideoOzet.Worker.Consumers;

public class ListingCreatedConsumer : IConsumer<ListingCreatedEvent>
{
    private readonly ILogger<ListingCreatedConsumer> _logger;
    private readonly IListingService _listingService;
    private readonly ISearchService _searchService;
    private readonly ICacheService _cacheService;
    private readonly OllamaService _ollamaService;
    private readonly IModerationService _moderationService;

    public ListingCreatedConsumer(
        ILogger<ListingCreatedConsumer> logger,
        IListingService listingService,
        ISearchService searchService,
        ICacheService cacheService,
        OllamaService ollamaService,
        IModerationService moderationService)
    {
        _logger = logger;
        _listingService = listingService;
        _searchService = searchService;
        _cacheService = cacheService;
        _ollamaService = ollamaService;
        _moderationService = moderationService;
    }

    public async Task Consume(ConsumeContext<ListingCreatedEvent> context)
    {
        _logger.LogInformation("ListingCreatedEvent alındı: {ListingId}", context.Message.ListingId);
        try
        {
            var listing = await _listingService.GetByIdAsync(context.Message.ListingId);
            if (listing == null) return;

            // Katman 2: Ollama analizi (Regex'ten kaçan bypass'lar için)
            var ollamaResult = await _ollamaService.AnalyzeAsync(
                listing.Title, listing.Description, context.CancellationToken);

            if (ollamaResult.IsViolation)
            {
                _logger.LogWarning("Ollama ihlal tespit etti: {ListingId}, Tür: {Type}",
                    listing.Id, ollamaResult.ViolationType);

                await _moderationService.AddStrikeAsync(
                    listing.OwnerId,
                    listing.Id,
                    listing.Title,
                    ollamaResult.ViolationType ?? "Unknown",
                    $"{listing.Title} {listing.Description}",
                    "Ollama");
            }

            // Mevcut işlemler devam eder
            await _searchService.IndexListingAsync(listing);
            await _cacheService.RemoveByPatternAsync("search:*");
            _logger.LogInformation("İlan işlendi: {ListingId}", listing.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListingCreatedEvent işlenirken hata: {ListingId}", context.Message.ListingId);
            throw;
        }
    }
}
