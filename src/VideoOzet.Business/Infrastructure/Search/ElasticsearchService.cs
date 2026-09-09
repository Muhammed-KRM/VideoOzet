using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Configuration;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Infrastructure.Search.Models;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.Search;

public class ElasticsearchService : ISearchService
{
    private readonly ElasticsearchClient _client;
    private readonly string _indexName;

    public ElasticsearchService(ElasticsearchClient client, IConfiguration config)
    {
        _client = client;
        _indexName = config["Elasticsearch:DefaultIndex"] ?? "listings";
    }

    public async Task IndexListingAsync(ListingDto listing)
    {
        var doc = new ListingDocument
        {
            Id = listing.Id.ToString(),
            Title = listing.Title,
            Description = listing.Description ?? "",
            TeacherName = listing.OwnerName,
            BranchSlug = listing.BranchName.ToLowerInvariant(), // basitleştirilmiş
            BranchName = listing.BranchName,
            CitySlug = listing.CityName.ToLowerInvariant(),
            DistrictSlug = listing.DistrictName.ToLowerInvariant(),
            HourlyPrice = listing.HourlyPrice,
            LessonType = "Online", // default
            IsVitrin = listing.IsVitrin,
            AverageRating = (float)listing.AverageRating,
            ReviewCount = listing.ReviewCount,
            Status = "Active",
            CreatedAt = listing.CreatedAt
        };

        await _client.IndexAsync(doc, idx => idx.Index(_indexName).Id(doc.Id));
    }

    public async Task DeleteListingIndexAsync(Guid listingId)
    {
        await _client.DeleteAsync<ListingDocument>(listingId.ToString(), d => d.Index(_indexName));
    }

    public async Task<SearchResultDto> SearchAsync(SearchFilterDto filters)
    {
        var mustQueries = new List<Action<QueryDescriptor<ListingDocument>>>();

        if (!string.IsNullOrWhiteSpace(filters.Query))
        {
            mustQueries.Add(q => q.QueryString(qs => qs
                .Fields(new[] { "title^3", "description", "teacherName^2" })
                .Query($"*{filters.Query}*")
            ));
        }

        if (!string.IsNullOrWhiteSpace(filters.Branch))
        {
            mustQueries.Add(q => q.Term(t => t.Field(f => f.BranchSlug).Value(filters.Branch)));
        }

        if (!string.IsNullOrWhiteSpace(filters.City))
        {
            mustQueries.Add(q => q.Term(t => t.Field(f => f.CitySlug).Value(filters.City)));
        }

        var searchResponse = await _client.SearchAsync<ListingDocument>(s => s
            .Indices(_indexName)
            .From((filters.Page - 1) * filters.PageSize)
            .Size(filters.PageSize)
            .Query(q => q.Bool(b => b.Must(mustQueries.ToArray())))
            .Sort(srt => srt
                .Field(f => f.IsVitrin, sort => sort.Order(SortOrder.Desc))
                .Field(f => f.AverageRating, sort => sort.Order(SortOrder.Desc))
                .Field(f => f.CreatedAt, sort => sort.Order(SortOrder.Desc))
            )
        );

        var result = new SearchResultDto
        {
            Page = filters.Page,
            PageSize = filters.PageSize,
            TotalCount = (int)searchResponse.Total,
            Items = searchResponse.Documents.Select(d => new ListingDto
            {
                Id = Guid.Parse(d.Id),
                Title = d.Title,
                OwnerName = d.TeacherName,
                BranchName = d.BranchName,
                CityName = d.CitySlug,
                DistrictName = d.DistrictSlug,
                HourlyPrice = d.HourlyPrice,
                IsVitrin = d.IsVitrin,
                AverageRating = d.AverageRating,
                ReviewCount = d.ReviewCount,
                CreatedAt = d.CreatedAt
            }).ToList()
        };

        return result;
    }
}
