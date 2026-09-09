using System.Net.Http;
using System.Threading.Tasks;
using VideoOzet.IntegrationTests.Setup;
using Xunit;

namespace VideoOzet.IntegrationTests.Endpoints;

public class AdminEndpointTests : BaseEndpointTest
{
    private const string GroupName = "Admin Endpoints";

    public AdminEndpointTests(CustomWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Test_GetDashboard_AsAdmin()
    {
        await AuthenticateAdminAsync();
        await ExecuteAndLogAsync(GroupName, "/api/admin/dashboard", HttpMethod.Get);
    }

    [Fact]
    public async Task Test_GetUsers_AsAdmin()
    {
        await AuthenticateAdminAsync();
        await ExecuteAndLogAsync(GroupName, "/api/admin/users", HttpMethod.Get);
    }

    [Fact]
    public async Task Test_GetListings_AsAdmin()
    {
        await AuthenticateAdminAsync();
        await ExecuteAndLogAsync(GroupName, "/api/admin/listings", HttpMethod.Get);
    }

    [Fact]
    public async Task Test_AdminEndpoints_AsNormalUser_ShouldFail()
    {
        // Normal user yetkisiyle dashboard görmeye çalış (403 Forbidden dönmeli)
        await AuthenticateUserAsync("notadmin@test.com", "UserPass123!", "Not Admin");
        await ExecuteAndLogAsync(GroupName, "/api/admin/dashboard [Forbidden Check]", HttpMethod.Get, null, async response => 
        {
             return (response.StatusCode == System.Net.HttpStatusCode.Forbidden, "403 Forbidden", "Not admin role");
        });
    }
}
