using VideoOzet.IntegrationTests.Setup;
using System.Net.Http.Json;
using System.Net.Http;
using Xunit;
using VideoOzet.Business.DTOs;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace VideoOzet.IntegrationTests.Endpoints;

[CollectionDefinition("EndpointTests")]
public class EndpointTestCollection : ICollectionFixture<CustomWebApplicationFactory>
{
    // Bu class xUnit collection fixture için işaretçi görevi görür.
}

[Collection("EndpointTests")]
public abstract class BaseEndpointTest : IClassFixture<CustomWebApplicationFactory>
{
    protected readonly CustomWebApplicationFactory _factory;
    protected readonly HttpClient _client;

    public BaseEndpointTest(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Bir kullanıcı kaydeder (veya varsa çeker) ve db uzerinden direkt oluşturarak JWT token'ı manuel atar. Rate limit aşımını (429) engeller.
    /// </summary>
    protected async Task AuthenticateUserAsync(string email = "testuser@endpointe2e.com", string password = "Password123!", string fullName = "Endpoint Tester")
    {
        Guid userId;
        var role = VideoOzet.Data.Enums.UserRole.User;

        using (var scope = _factory.Services.CreateScope())
        {
             var db = scope.ServiceProvider.GetRequiredService<VideoOzet.Data.Context.AppDbContext>();
             var user = db.Users.FirstOrDefault(u => u.Email == email);
             if (user == null) 
             {
                 user = new VideoOzet.Data.Entities.User
                 {
                     Id = Guid.NewGuid(),
                     Email = email,
                     FullName = fullName,
                     PasswordHash = VideoOzet.Business.Helpers.PasswordHasher.Hash(password),
                     Role = role,
                     IsActive = true
                 };
                 db.Users.Add(user);
                 db.SaveChanges();
             }
             userId = user.Id;
             role = user.Role;
        }

        var config = _factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var token = VideoOzet.API.Helpers.JwtHelper.GenerateToken(userId, email, fullName, role.ToString(), config);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Admin kullanıcısı oluşturarak veya veritabanından yetki vererek lokal JWT üretir. 
    /// </summary>
    protected async Task AuthenticateAdminAsync()
    {
        string email = "admin_tester_e2e@VideoOzet.com";
        string password = "AdminPassword123!";
        Guid userId;
        
        using (var scope = _factory.Services.CreateScope())
        {
             var db = scope.ServiceProvider.GetRequiredService<VideoOzet.Data.Context.AppDbContext>();
             var user = db.Users.FirstOrDefault(u => u.Email == email);
             if (user == null) 
             {
                 user = new VideoOzet.Data.Entities.User
                 {
                     Id = Guid.NewGuid(),
                     Email = email,
                     FullName = "Admin Tester",
                     PasswordHash = VideoOzet.Business.Helpers.PasswordHasher.Hash(password),
                     Role = VideoOzet.Data.Enums.UserRole.Admin,
                     IsActive = true
                 };
                 db.Users.Add(user);
             }
             else 
             {
                 user.Role = VideoOzet.Data.Enums.UserRole.Admin;
             }
             db.SaveChanges();
             userId = user.Id;
        }
        
        var config = _factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var token = VideoOzet.API.Helpers.JwtHelper.GenerateToken(userId, email, "Admin Tester", VideoOzet.Data.Enums.UserRole.Admin.ToString(), config);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// HTTP İsteğini çalıştırır ve sonucunu otomatik olarak rapora (EndpointTestReporter) yazar.
    /// </summary>
    protected async Task ExecuteAndLogAsync(
        string group, 
        string path, 
        HttpMethod method, 
        HttpContent? content = null, 
        Func<HttpResponseMessage, Task<(bool isSuccess, string expectedInfo, string errorMessage)>>? assertAction = null)
    {
        var requestUrl = path.Split('[')[0].Trim();
        var request = new HttpRequestMessage(method, requestUrl);
        if (content != null)
        {
            request.Content = content;
        }

        bool isSuccess = false;
        string expectedStatus = "Success StatusCode (2xx)";
        string actualStatus = "N/A";
        string errorMessage = string.Empty;

        try
        {
            var response = await _client.SendAsync(request);
            actualStatus = ((int)response.StatusCode).ToString() + " " + response.StatusCode.ToString();

            if (assertAction != null)
            {
               var assertResult = await assertAction(response);
               isSuccess = assertResult.isSuccess;
               expectedStatus = assertResult.expectedInfo;
               
               if (!isSuccess && string.IsNullOrEmpty(assertResult.errorMessage))
               {
                   errorMessage = await response.Content.ReadAsStringAsync();
               }
               else
               {
                   errorMessage = assertResult.errorMessage;
               }
            } 
            else 
            {
                if (!response.IsSuccessStatusCode) {
                    errorMessage = await response.Content.ReadAsStringAsync();
                }
                response.EnsureSuccessStatusCode();
                isSuccess = true;
            }
        }
        catch (Exception ex)
        {
            isSuccess = false;
            errorMessage = string.IsNullOrEmpty(errorMessage) ? ex.Message : errorMessage;
        }

        if (errorMessage.Length > 200) errorMessage = errorMessage.Substring(0, 200) + "...";

        EndpointTestReporter.RecordResult(new EndpointTestResult
        {
            Group = group,
            HttpMethod = method.ToString(),
            Path = path,
            IsSuccess = isSuccess,
            ExpectedStatus = expectedStatus,
            ActualStatus = actualStatus,
            ErrorMessage = errorMessage
        });

        // XUnit Test Explorer'da da görünsün diye
        Assert.True(isSuccess, $"[Endpoint failed] {method} {path} => {errorMessage}");
    }
}
