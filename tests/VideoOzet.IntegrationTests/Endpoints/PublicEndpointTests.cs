using System.Net.Http;
using System.Threading.Tasks;
using VideoOzet.IntegrationTests.Setup;
using Xunit;

namespace VideoOzet.IntegrationTests.Endpoints;

public class PublicEndpointTests : BaseEndpointTest
{
    private const string GroupName = "Public Endpoints";

    public PublicEndpointTests(CustomWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Test_GetBranches()
    {
        await ExecuteAndLogAsync(GroupName, "/api/branches", HttpMethod.Get);
    }

    [Fact]
    public async Task Test_GetCities()
    {
        await ExecuteAndLogAsync(GroupName, "/api/cities", HttpMethod.Get);
    }

    [Fact]
    public async Task Test_GetSitemap()
    {
         await ExecuteAndLogAsync(GroupName, "/api/sitemap", HttpMethod.Get);
    }

    [Fact]
    public async Task Test_SearchListings()
    {
         await ExecuteAndLogAsync(GroupName, "/api/listings/search", HttpMethod.Get);
    }

    [Fact]
    public async Task Test_GetVitrinPackages()
    {
         await ExecuteAndLogAsync(GroupName, "/api/vitrin/packages", HttpMethod.Get);
    }
    
    [Fact]
    public async Task Test_GetTokenPackages()
    {
         await ExecuteAndLogAsync(GroupName, "/api/tokens/packages", HttpMethod.Get);
    }
}
