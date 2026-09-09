using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using VideoOzet.API.Helpers;
using Xunit;

namespace VideoOzet.UnitTests.API.Helpers;

public class JwtHelperTests
{
    private readonly IConfiguration _config;
    private const string SecretKey = "this_is_a_very_secret_key_used_for_jwt_unit_tests_1234567890!";

    public JwtHelperTests()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Jwt:Key", SecretKey },
            { "Jwt:Issuer", "VideoOzetApp" },
            { "Jwt:Audience", "VideoOzetUsers" },
            { "Jwt:AccessTokenExpirationMinutes", "30" }
        };

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public void GenerateToken_ShouldReturnValidJwtToken_WithCorrectClaimsAndExpiration()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";
        var fullName = "Test User";
        var role = "Admin";

        // Act
        var tokenString = JwtHelper.GenerateToken(userId, email, fullName, role, _config);

        // Assert
        tokenString.Should().NotBeNullOrWhiteSpace();

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(tokenString);

        token.Issuer.Should().Be("VideoOzetApp");
        token.Audiences.Should().Contain("VideoOzetUsers");

        token.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value.Should().Be(userId.ToString());
        token.Claims.First(c => c.Type == ClaimTypes.Email).Value.Should().Be(email);
        token.Claims.First(c => c.Type == ClaimTypes.Name).Value.Should().Be(fullName);
        token.Claims.First(c => c.Type == ClaimTypes.Role).Value.Should().Be(role);
        token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti).Should().NotBeNull();

        token.ValidTo.Should().BeAfter(DateTime.UtcNow.AddMinutes(25));
        token.ValidTo.Should().BeBefore(DateTime.UtcNow.AddMinutes(35));
    }

    [Fact]
    public void GenerateToken_ShouldUseDefault15Minutes_WhenExpirationNotConfigured()
    {
        // Arrange
        var minimalConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Jwt:Key", SecretKey }
            })
            .Build();

        var userId = Guid.NewGuid();

        // Act
        var tokenString = JwtHelper.GenerateToken(userId, "u@example.com", "Name", "User", minimalConfig);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(tokenString);

        token.ValidTo.Should().BeAfter(DateTime.UtcNow.AddMinutes(10));
        token.ValidTo.Should().BeBefore(DateTime.UtcNow.AddMinutes(20));
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnBase64String_WithExpectedLength()
    {
        // Act
        var refreshToken = JwtHelper.GenerateRefreshToken();

        // Assert
        refreshToken.Should().NotBeNullOrWhiteSpace();
        var bytes = Convert.FromBase64String(refreshToken);
        bytes.Length.Should().Be(64);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnUniqueValues()
    {
        // Act
        var token1 = JwtHelper.GenerateRefreshToken();
        var token2 = JwtHelper.GenerateRefreshToken();

        // Assert
        token1.Should().NotBe(token2);
    }
}
