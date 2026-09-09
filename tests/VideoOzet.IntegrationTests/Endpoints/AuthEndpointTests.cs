using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs;
using VideoOzet.IntegrationTests.Setup;
using Xunit;
using System;

namespace VideoOzet.IntegrationTests.Endpoints;

public class AuthEndpointTests : BaseEndpointTest
{
    private const string GroupName = "Auth Endpoints";

    public AuthEndpointTests(CustomWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Test_RegisterEndpoint()
    {
        var dto = new UserRegisterDto { Email = "register_test@mail.com", Password = "Password123!", FullName = "Register Test" };
        var content = JsonContent.Create(dto);

        await ExecuteAndLogAsync(GroupName, "/api/auth/register", HttpMethod.Post, content, async response => 
        {
            return (response.IsSuccessStatusCode, "200 OK", ""); 
        });

        // Hatalı kayıt denemesi (Aynı şifre aynı mail)
        await ExecuteAndLogAsync(GroupName, "/api/auth/register [Aynı E-posta Error Check]", HttpMethod.Post, content, async response => 
        {
            bool isBadRequest = response.StatusCode == System.Net.HttpStatusCode.BadRequest;
            return (isBadRequest, "400 Bad Request", "Aynı mail ile kayıt olunduğu için 400 dönmeliydi.");
        });
    }

    [Fact]
    public async Task Test_LoginEndpoint()
    {
        // Login için kullanıcıyı önceden hazırla
        await AuthenticateUserAsync("login_tester@mail.com", "Password123!", "Login Tester");

        // Gerçek testi yap
        var dto = new UserLoginDto { Email = "login_tester@mail.com", Password = "Password123!" };
        var content = JsonContent.Create(dto);

        await ExecuteAndLogAsync(GroupName, "/api/auth/login", HttpMethod.Post, content);
    }

    [Fact]
    public async Task Test_GetMeEndpoint()
    {
        await AuthenticateUserAsync("me_tester@mail.com", "Password123!", "Me Tester");
        await ExecuteAndLogAsync(GroupName, "/api/auth/me", HttpMethod.Get);
        
        // Yetkisiz erişim denemesi (Token sil)
        _client.DefaultRequestHeaders.Authorization = null;
        await ExecuteAndLogAsync(GroupName, "/api/auth/me [Unauthorized Check]", HttpMethod.Get, null, async response => 
        {
             return (response.StatusCode == System.Net.HttpStatusCode.Unauthorized, "401 Unauthorized", "");
        });
    }

    [Fact]
    public async Task Test_UpdateProfileEndpoint()
    {
        await AuthenticateUserAsync("profile_tester@mail.com", "Password123!", "Profile Tester");
        
        var dto = new UserProfileUpdateDto { FullName = "Updated Profile Tester", Bio = "My new bio" };
        var content = JsonContent.Create(dto);
        
        await ExecuteAndLogAsync(GroupName, "/api/auth/profile", HttpMethod.Put, content);
    }
}
