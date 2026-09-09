using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.IntegrationTests.Setup;
using Xunit;

namespace VideoOzet.IntegrationTests.Endpoints;

public class ListingsEndpointTests : BaseEndpointTest
{
    private const string GroupName = "Listings Endpoints";

    public ListingsEndpointTests(CustomWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Test_CreateListing()
    {
        await AuthenticateUserAsync("listing_creator@test.com", "Password123!", "Listing Creator");

        var dto = new ListingCreateDto
        {
            Title = "Gitar Dersleri Başlangıç",
            Description = "En az yirmi karakter uzunluğunda bir açıklama...",
            HourlyPrice = 400,
            BranchId = 1, 
            DistrictId = 1, 
            Type = VideoOzet.Data.Enums.ListingType.TeacherOffering,
            LessonType = VideoOzet.Data.Enums.LessonType.Online
        };

        var content = JsonContent.Create(dto);
        await ExecuteAndLogAsync(GroupName, "/api/listings", HttpMethod.Post, content); // 200 OK beklenir
    }

    [Fact]
    public async Task Test_GetMyListings()
    {
        await AuthenticateUserAsync("listing_reader@test.com", "Password123!", "Listing Reader");
        await ExecuteAndLogAsync(GroupName, "/api/listings/my", HttpMethod.Get);
    }
}
