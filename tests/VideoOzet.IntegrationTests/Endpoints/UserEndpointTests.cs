using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using VideoOzet.IntegrationTests.Setup;
using Xunit;
using VideoOzet.Business.DTOs;
using System;

namespace VideoOzet.IntegrationTests.Endpoints;

public class UserEndpointTests : BaseEndpointTest
{
    private const string GroupName = "User Features Endpoints";

    public UserEndpointTests(CustomWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Test_GetTokenBalance()
    {
        await AuthenticateUserAsync("token_user@test.com", "Password123!", "Token User");
        await ExecuteAndLogAsync(GroupName, "/api/tokens/balance", HttpMethod.Get);
    }
    
    [Fact]
    public async Task Test_GetTokenHistory()
    {
        await AuthenticateUserAsync("history_user@test.com", "Password123!", "History User");
        await ExecuteAndLogAsync(GroupName, "/api/tokens/history", HttpMethod.Get);
    }

    [Fact]
    public async Task Test_GetInbox()
    {
        await AuthenticateUserAsync("inbox_user@test.com", "Password123!", "Inbox User");
        await ExecuteAndLogAsync(GroupName, "/api/messages/inbox", HttpMethod.Get);
    }

    [Fact]
    public async Task Test_UpdatePersonalInfo()
    {
        await AuthenticateUserAsync("personal_user@test.com", "Password123!", "Personal User");
        var dto = new PersonalInfoDto { FullName = "New Name", Email = "personal_user@test.com", Phone = "5551234567", BirthDate = new DateTime(1995,1,1, 0, 0, 0, DateTimeKind.Utc) };
        var content = JsonContent.Create(dto);
        await ExecuteAndLogAsync(GroupName, "/api/users/personal-info", HttpMethod.Put, content);
    }
}
