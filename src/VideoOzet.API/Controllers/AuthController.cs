using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VideoOzet.API.Helpers;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IConfiguration _config;

    public AuthController(IAuthService authService, IConfiguration config)
    {
        _authService = authService;
        _config = config;
    }

    [HttpPost("register")]
    [EnableRateLimiting("RegisterPolicy")]
    public async Task<ActionResult<AuthResultDto>> Register(UserRegisterDto dto)
    {
        var result = await _authService.RegisterAsync(dto);
        if (!result.Success) return BadRequest(result);

        // JWT token üret
        result.Token = JwtHelper.GenerateToken(result.User!.Id, result.User.Email, result.User.FullName, result.User.Role, _config);
        result.RefreshToken = JwtHelper.GenerateRefreshToken();
        
        // Refresh token'ı DB'ye kaydet (7 gün geçerli)
        await _authService.UpdateRefreshTokenAsync(result.User.Id, result.RefreshToken, DateTime.UtcNow.AddDays(7));

        return Ok(result);
    }

    [HttpPost("login")]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<ActionResult<AuthResultDto>> Login(UserLoginDto dto)
    {
        var result = await _authService.LoginAsync(dto);
        if (!result.Success) return Unauthorized(result);

        result.Token = JwtHelper.GenerateToken(result.User!.Id, result.User.Email, result.User.FullName, result.User.Role, _config);
        result.RefreshToken = JwtHelper.GenerateRefreshToken();

        // Refresh token'ı DB'ye kaydet (7 gün geçerli)
        await _authService.UpdateRefreshTokenAsync(result.User.Id, result.RefreshToken, DateTime.UtcNow.AddDays(7));

        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> GetCurrentUser()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _authService.GetCurrentUserAsync(userId);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<ActionResult<UserDto>> UpdateProfile(UserProfileUpdateDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.UpdateProfileAsync(userId, dto);
        return Ok(result);
    }

    [HttpPost("refresh-token")]
    public async Task<ActionResult<AuthResultDto>> RefreshToken(RefreshTokenRequestDto dto)
    {
        var result = await _authService.RefreshTokenAsync(dto.RefreshToken);
        if (!result.Success) return Unauthorized(result);

        // Yeni tokenleri üret
        result.Token = JwtHelper.GenerateToken(result.User!.Id, result.User.Email, result.User.FullName, result.User.Role, _config);
        result.RefreshToken = JwtHelper.GenerateRefreshToken();

        // Yeni refresh token'ı DB'ye kaydet
        await _authService.UpdateRefreshTokenAsync(result.User.Id, result.RefreshToken, DateTime.UtcNow.AddDays(7));

        return Ok(result);
    }
}
