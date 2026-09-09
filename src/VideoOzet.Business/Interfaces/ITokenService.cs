using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Interfaces;

public interface ITokenService
{
    Task<int> GetBalanceAsync(Guid userId);
    Task<List<TokenPackageDto>> GetPackagesAsync();
    Task<List<TokenTransactionDto>> GetTransactionHistoryAsync(Guid userId);
    Task SpendTokenAsync(Guid userId, int amount, string reason);
    Task AddTokenAsync(Guid userId, int amount, string reason);
}
