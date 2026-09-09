using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Interfaces;

public interface IAuthService
{
    Task<AuthResultDto> RegisterAsync(UserRegisterDto dto);
    Task<AuthResultDto> LoginAsync(UserLoginDto dto);
    Task<UserDto?> GetCurrentUserAsync(Guid userId);
    Task<UserDto> UpdateProfileAsync(Guid userId, UserProfileUpdateDto dto);
    Task<AuthResultDto> RefreshTokenAsync(string refreshToken);
    Task UpdateRefreshTokenAsync(Guid userId, string refreshToken, DateTime expiryTime);
    Task SetUserStatusAsync(Guid userId, bool isActive);
}
